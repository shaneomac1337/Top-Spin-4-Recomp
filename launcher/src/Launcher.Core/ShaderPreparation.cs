using System.Text.RegularExpressions;

namespace TopSpin4.Launcher.Core;

/// <summary>Converts Xenos shader containers to SPIR-V; the real one runs XenosRecomp.</summary>
public interface IShaderConverter
{
    /// <summary>
    /// Converts every container file in <paramref name="inputDirectory"/>, writing
    /// <c>shader_cache.cpp</c> and an <c>hlsl</c> folder into <paramref name="outputDirectory"/>.
    /// Progress runs from 0 to 1.
    /// </summary>
    /// <exception cref="ShaderConverterException">The conversion failed.</exception>
    void Convert(string inputDirectory, string outputDirectory, IProgress<double>? progress, CancellationToken cancel);
}

/// <param name="unusable">
/// The converter itself cannot run (missing, or it did not start), as against failing on its input: trying its shaders one
/// by one would only fail the same way.
/// </param>
public sealed class ShaderConverterException(string message, Exception? inner = null, bool unusable = false) : Exception(message, inner)
{
    public bool Unusable { get; } = unusable;
}

/// <summary>What the shader step made: how many shaders the pack holds, and the names of those XenosRecomp cannot convert.</summary>
public sealed record ShaderResult(int Converted, IReadOnlyList<string> Skipped);

/// <summary>
/// The shaders in the read-only data of the Loader and Swing Modules, which no archive holds. The real one
/// runs the game's headless Module mode (<see cref="GameModuleShaders"/>); a test puts a fake behind it.
/// </summary>
public interface IModuleShaders
{
    /// <summary>
    /// Writes every shader container of the Modules into the shader top-up folder under
    /// <paramref name="cacheDirectory"/>, each as <c>ps_&lt;fnv&gt;.bin</c> or <c>vs_&lt;fnv&gt;.bin</c> (the folder's names),
    /// and returns how many.
    /// </summary>
    /// <exception cref="ShaderPreparationException">The Modules could not be read; the message is the player's.</exception>
    int Write(string cacheDirectory, CancellationToken cancel);
}

/// <summary>The shader step failed; the message says why and how to try again.</summary>
public sealed class ShaderPreparationException(string message, Exception? inner = null) : Exception(message, inner);

public enum ShaderStep
{
    ReadingGameFiles,
    ReadingModules,
    Converting,
    /// <summary>Trying each shader alone, after XenosRecomp failed on the whole set.</summary>
    Screening,
    WritingPack,
    Done,
}

/// <summary>The shader step's progress: the step, and the fraction of the whole step done.</summary>
public sealed record ShaderProgress(ShaderStep Step, double Fraction);

/// <summary>
/// Where the shaders stand: the pack's state, and how many containers the game left in the
/// top-up folder that the pack does not hold yet.
/// </summary>
public sealed record ShaderStatus(ShaderPackCheck Pack, int WaitingShaders)
{
    public bool NeedsPreparing => !Pack.Ready || WaitingShaders > 0;

    /// <summary>
    /// A usable pack lacks only shaders the game found: the Launcher adds them by itself once the session
    /// ends. A missing or stale pack is rebuilt instead, before the next Play.
    /// </summary>
    public bool AddsAfterSession => Pack.Ready && WaitingShaders > 0;

    /// <summary>What the Launcher tells the player, or empty when nothing is needed.</summary>
    public string Message => Pack.Problem switch
    {
        ShaderPackProblem.None when WaitingShaders > 0 =>
            $"The game found {WaitingShaders} new shader{(WaitingShaders == 1 ? "" : "s")}. They are added to the shader pack after the session.",
        ShaderPackProblem.None => "",
        ShaderPackProblem.Stale =>
            "The Native shaders were prepared for another version of the game. They are prepared again.",
        ShaderPackProblem.Damaged =>
            "The Native shaders are damaged. They are prepared again.",
        _ => "The Native shaders are not prepared.",
    };
}

/// <summary>
/// Setup's "Preparing shaders" step (spec, "Prepare shaders"): read every shader container from the prepared
/// game files (the archives, then the Modules through <see cref="IModuleShaders"/>), add any the game left in
/// the top-up folder, convert them with XenosRecomp and write the shader pack into the cache folder. A failure or a
/// cancel leaves any earlier pack in place; without a usable pack the game says so and ends.
/// </summary>
public sealed partial class ShaderPreparation(IShaderConverter converter, ulong converterId, IModuleShaders? modules = null)
{
    /// <summary>Where the game leaves containers it met that the pack lacks (<c>ShaderTopUp::kFolderName</c> in the game).</summary>
    public const string TopUpFolderName = "shader-topup";

    /// <summary>
    /// A top-up the game converted live: a one-shader pack beside its container, <c>ps_&lt;fnv&gt;.shaderpack</c>.
    /// The game merges it into the pack at startup.
    /// </summary>
    public const string ConvertedExtension = ".shaderpack";

    private const string WorkFolderName = "shader-build";

    // Progress: the archives, the Modules, then XenosRecomp (the long part), then the pack.
    private const double ArchiveShare = 0.2;
    private const double ModuleShare = 0.1;
    private const double ConvertShare = 0.65;

    public ShaderStatus StatusOf(string cacheDirectory)
    {
        var path = Path.Combine(cacheDirectory, ShaderPackFile.FileName);
        var check = ShaderPackFile.Check(path, converterId);
        var topUps = TopUpContainers(cacheDirectory);
        if (!check.Ready || topUps.Count == 0) return new ShaderStatus(check, check.Ready ? 0 : topUps.Count);
        var packed = ShaderPackFile.ConvertedShaders(path);
        return new ShaderStatus(check,
            topUps.Count(file => !packed.Contains(ContainerHash(file)) && !ShaderPackFile.Check(ConvertedPath(file), converterId).Ready));
    }

    /// <summary>Where the game puts a top-up container it converted live.</summary>
    public static string ConvertedPath(string containerFile) => Path.ChangeExtension(containerFile, ConvertedExtension);

    /// <exception cref="ShaderPreparationException">The step failed; the game can't start until it succeeds.</exception>
    /// <exception cref="OperationCanceledException">The step was cancelled.</exception>
    public ShaderResult Run(string gameDataDirectory, string cacheDirectory, IProgress<ShaderProgress>? progress, CancellationToken cancel)
    {
        var work = Path.Combine(cacheDirectory, WorkFolderName);
        var pack = Path.Combine(cacheDirectory, ShaderPackFile.FileName);
        var temp = pack + ".tmp";
        try
        {
            progress?.Report(new ShaderProgress(ShaderStep.ReadingGameFiles, 0));
            var containers = ArchiveShaders.Read(gameDataDirectory,
                Scaled<(int Done, int Total)>(progress, ShaderStep.ReadingGameFiles, 0, ArchiveShare, count => count.Total == 0 ? 1 : (double)count.Done / count.Total),
                cancel);
            if (containers.Count == 0)
                throw new ShaderPreparationException(Failed("no shaders were found in the game files"));

            cancel.ThrowIfCancellationRequested();
            if (modules is not null)
            {
                progress?.Report(new ShaderProgress(ShaderStep.ReadingModules, ArchiveShare));
                Directory.CreateDirectory(Path.Combine(cacheDirectory, TopUpFolderName));
                modules.Write(cacheDirectory, cancel);
                cancel.ThrowIfCancellationRequested();
            }

            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            var input = Path.Combine(work, "containers");
            var output = Path.Combine(work, "converted");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);
            foreach (var container in containers)
                File.WriteAllBytes(Path.Combine(input, container.Name + ".bin"), container.Bytes);
            // The Modules' shaders (and any the game met in a session) sit in the top-up folder; a name the archives have is one file.
            foreach (var file in TopUpContainers(cacheDirectory))
                File.Copy(file, Path.Combine(input, Path.GetFileName(file)), overwrite: true);

            cancel.ThrowIfCancellationRequested();
            const double Start = ArchiveShare + ModuleShare;
            var skipped = ConvertAll(input, output, progress, Start, cancel);
            cancel.ThrowIfCancellationRequested();

            progress?.Report(new ShaderProgress(ShaderStep.WritingPack, Start + ConvertShare));
            File.WriteAllBytes(temp, ShaderPackFile.Build(output, converterId));
            File.Move(temp, pack, overwrite: true);
            // Not waiting for the pack, since XenosRecomp cannot convert them: the game meets them again only if it draws with them.
            foreach (var name in skipped) TryDelete(Path.Combine(cacheDirectory, TopUpFolderName, name + ".bin"));
            // The top-ups (the Modules' shaders, and those the game met in play) are folded into the pack now: the folder is
            // emptied of them, with any one-shader pack the game converted live beside them.
            var packed = ShaderPackFile.ConvertedShaders(pack);
            foreach (var file in TopUpContainers(cacheDirectory).Where(file => packed.Contains(ContainerHash(file))))
            {
                TryDelete(ConvertedPath(file));
                TryDelete(file);
            }
            progress?.Report(new ShaderProgress(ShaderStep.Done, 1));
            return new ShaderResult(Directory.GetFiles(input).Length, skipped);
        }
        catch (ShaderConverterException error)
        {
            throw new ShaderPreparationException(Failed(error.Message), error);
        }
        catch (ShaderPreparationException error) when (!error.Message.StartsWith(FailedPrefix, StringComparison.Ordinal))
        {
            throw new ShaderPreparationException(Failed(error.Message), error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ShaderPreparationException(Failed(error.Message), error);
        }
        finally
        {
            TryDelete(temp);
            if (Directory.Exists(work))
            {
                try { Directory.Delete(work, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Converts every container in <paramref name="input"/> into <paramref name="output"/>. XenosRecomp stops on any shader it
    /// cannot convert (it fails the run, or crashes) and then writes no cache, and some of the game's shaders are such (fetches
    /// it does not translate). So when the whole folder fails, each container is tried alone (several processes at once), those that
    /// fail are removed from <paramref name="input"/>, and the rest are converted together. Returns the removed names, sorted.
    /// A converter that fails on most shaders, or cannot run, fails the step as before.
    /// </summary>
    private IReadOnlyList<string> ConvertAll(string input, string output, IProgress<ShaderProgress>? progress, double start,
        CancellationToken cancel)
    {
        // The whole folder takes the first half of the converting share, screening the next 40%, the final run the rest.
        ShaderConverterException firstFailure;
        try
        {
            converter.Convert(input, output, Scaled<double>(progress, ShaderStep.Converting, start, ConvertShare * 0.5, fraction => fraction), cancel);
            progress?.Report(new ShaderProgress(ShaderStep.Converting, start + ConvertShare * 0.5));
            return [];
        }
        catch (ShaderConverterException error) when (!error.Unusable)
        {
            firstFailure = error;
        }

        var files = Directory.GetFiles(input).Order(StringComparer.Ordinal).ToList();
        var bad = new System.Collections.Concurrent.ConcurrentBag<string>();
        var done = 0;
        var screen = Path.Combine(Path.GetDirectoryName(input)!, "screen");
        try
        {
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 16), CancellationToken = cancel },
                file =>
                {
                    var folder = Path.Combine(screen, Path.GetFileNameWithoutExtension(file));
                    Directory.CreateDirectory(Path.Combine(folder, "in"));
                    File.Copy(file, Path.Combine(folder, "in", Path.GetFileName(file)), overwrite: true);
                    try
                    {
                        converter.Convert(Path.Combine(folder, "in"), Path.Combine(folder, "out"), null, cancel);
                    }
                    catch (ShaderConverterException error) when (!error.Unusable)
                    {
                        bad.Add(Path.GetFileNameWithoutExtension(file));
                    }
                    finally
                    {
                        TryDeleteFolder(folder);
                    }
                    progress?.Report(new ShaderProgress(ShaderStep.Screening,
                        start + ConvertShare * (0.5 + 0.4 * Interlocked.Increment(ref done) / files.Count)));
                });
        }
        catch (AggregateException error) when (error.InnerExceptions.OfType<ShaderConverterException>().FirstOrDefault() is { } unusable)
        {
            throw unusable;
        }
        if (bad.Count == 0 || bad.Count > files.Count / 2) throw firstFailure;

        foreach (var name in bad) File.Delete(Path.Combine(input, name + ".bin"));
        // The failed run left its HLSL behind, and the pack lists a fetch slot per HLSL file.
        Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        converter.Convert(input, output,
            Scaled<double>(progress, ShaderStep.Converting, start + ConvertShare * 0.9, ConvertShare * 0.1, fraction => fraction), cancel);
        return [.. bad.Order(StringComparer.Ordinal)];
    }

    private static void TryDeleteFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private const string FailedPrefix = "The Native shaders could not be prepared";

    internal static string Failed(string why) =>
        $"{FailedPrefix}: {why.TrimEnd('.')}. The game can't start until they are. Close and reopen the Launcher to try again.";

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The top-up folder's container files, named like <c>--ts4_shader_dump_dir</c>.</summary>
    private static List<string> TopUpContainers(string cacheDirectory)
    {
        var folder = Path.Combine(cacheDirectory, TopUpFolderName);
        if (!Directory.Exists(folder)) return [];
        return [.. Directory.EnumerateFiles(folder, "*.bin").Where(file => ContainerName().IsMatch(Path.GetFileName(file)))];
    }

    private static ulong ContainerHash(string file) =>
        ulong.Parse(ContainerName().Match(Path.GetFileName(file)).Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);

    private static IProgress<T>? Scaled<T>(IProgress<ShaderProgress>? progress, ShaderStep step, double start, double share,
        Func<T, double> fraction) =>
        progress is null ? null : new ProgressRelay<T>(value => progress.Report(new ShaderProgress(step, start + share * Math.Clamp(fraction(value), 0, 1))));

    [GeneratedRegex(@"^(?:ps|vs)_([0-9a-f]{16})\.bin$")]
    private static partial Regex ContainerName();
}
