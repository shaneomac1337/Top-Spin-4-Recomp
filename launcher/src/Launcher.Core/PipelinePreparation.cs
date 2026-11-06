using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TopSpin4.Launcher.Core;

/// <summary>Why the Launcher rebuilds pipelines before the next Play, or why it does not.</summary>
public enum PipelineRebuild
{
    /// <summary>The last prepare still matches its inputs.</summary>
    None,
    /// <summary>No usable shader pack: there is nothing to build pipelines from (the game can't start).</summary>
    NoShaderPack,
    /// <summary>Repair rebuilds whatever the state.</summary>
    Repair,
    /// <summary>No pipeline store, or none the Launcher prepared.</summary>
    NoStore,
    /// <summary>The game met a store or driver cache from another GPU or driver and left its marker.</summary>
    Stale,
    PackChanged,
    TopUpsChanged,
    /// <summary>A new game build ships another key list (<c>pipeline-keys.txt</c>).</summary>
    KeyListChanged,
    /// <summary>The update folder moved or its files changed.</summary>
    UpdateFolderChanged,
}

public static class PipelineRebuildExtensions
{
    public static bool Rebuilds(this PipelineRebuild rebuild) => rebuild is not (PipelineRebuild.None or PipelineRebuild.NoShaderPack);
}

/// <summary>
/// Setup's pipeline step (copied from WWE All Stars, ADR-0004): runs the game's headless prepare-pipelines mode
/// (<c>--ts4_prepare_pipelines</c>, src/native/native_renderer.h), which writes the pipeline store and the driver cache
/// under <c>native-pipelines</c> in the cache folder. After a finished prepare the Launcher records what it was built
/// from (the pack, the top-ups, the shipped key list and the update folder with its files); a change to any of them, a
/// missing store, or the game's "pipelines stale" marker means a rebuild before the next Play, without a click. Repair
/// always rebuilds. The folders are the preparation's at the time of the call, so a move of the game files is followed.
/// </summary>
public sealed class PipelinePreparation(string gameExe, Preparation preparation, IPrepareRunner runner)
{
    public const string Flag = "--ts4_prepare_pipelines";
    public const string StoreFolderName = "native-pipelines";
    public const string KeyListFileName = "pipeline-keys.txt";
    /// <summary>What the store and the driver cache take, for the space check.</summary>
    public const long StoreBytes = 64L << 20;
    private const string StaleMarkerName = "pipelines-stale";
    private const string InputsFileName = "launcher-inputs";
    private const string InputsHeader = "topspin4-pipelines-inputs 1";
    private static readonly string[] StoreFiles = ["keys.bin", "driver-cache.bin"];

    private string CacheDirectory => preparation.ShaderCacheDirectory;
    private string Store => Path.Combine(CacheDirectory, StoreFolderName);
    private string GameDirectory => Path.GetDirectoryName(Path.GetFullPath(gameExe))!;

    /// <summary>Whether the next Play needs a prepare first, and why.</summary>
    public PipelineRebuild Check(bool shaderPackReady, bool repair = false)
    {
        if (!shaderPackReady) return PipelineRebuild.NoShaderPack;
        if (repair) return PipelineRebuild.Repair;
        if (StoreFiles.Any(file => !File.Exists(Path.Combine(Store, file)))) return PipelineRebuild.NoStore;
        if (File.Exists(Path.Combine(Store, StaleMarkerName))) return PipelineRebuild.Stale;
        var recorded = ReadInputs();
        if (recorded is null) return PipelineRebuild.NoStore;
        var now = CurrentInputs();
        if (now.Pack != recorded.Value.Pack) return PipelineRebuild.PackChanged;
        if (now.TopUps != recorded.Value.TopUps) return PipelineRebuild.TopUpsChanged;
        if (now.Keys != recorded.Value.Keys) return PipelineRebuild.KeyListChanged;
        if (now.Update != recorded.Value.Update) return PipelineRebuild.UpdateFolderChanged;
        return PipelineRebuild.None;
    }

    /// <summary>Throws the record away, so the next <see cref="Check"/> asks for a prepare (the Launcher's Repair).</summary>
    public void Forget()
    {
        try
        {
            File.Delete(Path.Combine(Store, InputsFileName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A record that stays only means no rebuild is forced; the window says Repair did not finish.
        }
    }

    /// <summary>
    /// Runs the prepare. A failure is a result, never an exception, except a cancel. A crash is run once more: AMD's
    /// driver has crashed now and then while many threads built pipeline libraries at once (0xC0000005 inside
    /// amdvlk64), and the same step then finished on the next run.
    /// </summary>
    public async Task<PipelineResult> RunAsync(IProgress<PipelineProgress>? progress, CancellationToken cancel)
    {
        var result = await RunOnceAsync(progress, cancel).ConfigureAwait(false);
        if (result.Outcome != PipelineOutcome.Crashed || result.StartError is not null) return result;
        return await RunOnceAsync(progress, cancel).ConfigureAwait(false);
    }

    private async Task<PipelineResult> RunOnceAsync(IProgress<PipelineProgress>? progress, CancellationToken cancel)
    {
        var inputs = CurrentInputs();
        var output = new PipelineOutput();
        int exitCode;
        try
        {
            exitCode = await runner.RunAsync(
                new ProcessStart(Path.GetFullPath(gameExe), GameDirectory,
                [
                    Flag + "=true",
                    "--game_data_root=" + preparation.GameDataRoot,
                    "--update_data_root=" + preparation.UpdateDataRoot,
                    "--cache_root=" + CacheDirectory,
                    // Never a window or a prompt: the mode runs unattended inside setup.
                    "--headless",
                    "--window_hidden=true",
                ]),
                line =>
                {
                    if (output.Read(line) is { } report) progress?.Report(report);
                }, cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is LauncherException or IOException or UnauthorizedAccessException)
        {
            return new PipelineResult(PipelineOutcome.Crashed, -1, 0, 0, null) { StartError = error.Message };
        }
        cancel.ThrowIfCancellationRequested();
        var result = output.Finish(exitCode);
        if (result.Usable) WriteInputs(inputs);
        return result;
    }

    private (string Pack, string TopUps, string Keys, string Update) CurrentInputs()
    {
        var topUps = Path.Combine(CacheDirectory, ShaderPreparation.TopUpFolderName);
        // A top-up the game converted live had its pipelines made in that session.
        var names = Directory.Exists(topUps)
            ? string.Join('\n', Directory.EnumerateFiles(topUps, "*.bin")
                .Where(file => !File.Exists(ShaderPreparation.ConvertedPath(file)))
                .Select(Path.GetFileName).Order(StringComparer.Ordinal))
            : "";
        return (FileHash(Path.Combine(CacheDirectory, ShaderPackFile.FileName)),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(names))),
            FileHash(Path.Combine(GameDirectory, KeyListFileName)),
            UpdateStamp(preparation.UpdateDataRoot));
    }

    /// <summary>The update folder the game plans from: its path and each file's name, size and time.</summary>
    private static string UpdateStamp(string folder)
    {
        var stamp = new StringBuilder(Path.GetFullPath(folder)).Append('\n');
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles().OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
                stamp.Append(CultureInfo.InvariantCulture, $"{file.Name} {file.Length} {file.LastWriteTimeUtc.Ticks}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A missing folder is stamped by its path alone.
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamp.ToString())));
    }

    private static string FileHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return "none";
        }
    }

    private (string Pack, string TopUps, string Keys, string Update)? ReadInputs()
    {
        try
        {
            var lines = File.ReadAllLines(Path.Combine(Store, InputsFileName));
            if (lines.Length < 5 || lines[0] != InputsHeader) return null;
            var fields = lines.Skip(1).Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);
            return fields.TryGetValue("pack", out var pack) && fields.TryGetValue("topups", out var topUps) &&
                   fields.TryGetValue("keys", out var keys) && fields.TryGetValue("update", out var update)
                ? (pack, topUps, keys, update)
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteInputs((string Pack, string TopUps, string Keys, string Update) inputs)
    {
        try
        {
            Directory.CreateDirectory(Store);
            File.WriteAllText(Path.Combine(Store, InputsFileName),
                $"{InputsHeader}\npack={inputs.Pack}\ntopups={inputs.TopUps}\nkeys={inputs.Keys}\nupdate={inputs.Update}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Without the record the next Play prepares again; nothing else is lost.
        }
    }
}
