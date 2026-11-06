using System.Globalization;
using System.Text.RegularExpressions;

namespace TopSpin4.Launcher.Core;

/// <summary>
/// The shaders in the Loader and Swing Modules, read by the game exe's headless Module mode
/// (<c>--ts4_prepare_modules</c>, src/module_shaders.cpp): it loads both Modules as play does (Title Update 1
/// applied) without running any guest code, writes every shader container in their data into the shader top-up
/// folder under its dump name, prints a header and one <c>result</c> line, and exits (0 ok, 2 failed). The
/// Launcher reads those lines the way it reads the prepare-pipelines mode's. The folders are the preparation's at the time of
/// the call, so a move of the game files in between is followed.
/// </summary>
public sealed partial class GameModuleShaders(string gameExe, Preparation preparation, IPrepareRunner runner) : IModuleShaders
{
    public const string Flag = "--ts4_prepare_modules";

    public int Write(string cacheDirectory, CancellationToken cancel)
    {
        const string Stopped = "The game stopped before it had read the shaders in its Modules.";
        var gameFolder = Path.GetDirectoryName(Path.GetFullPath(gameExe))!;
        var start = new ProcessStart(Path.GetFullPath(gameExe), gameFolder,
        [
            Flag + "=true",
            "--game_data_root=" + preparation.GameDataRoot,
            "--update_data_root=" + preparation.UpdateDataRoot,
            "--cache_root=" + cacheDirectory,
            // Never a window or a prompt: the mode runs unattended inside setup.
            "--headless",
            "--window_hidden=true",
        ]);
        Match? result = null;
        int exitCode;
        try
        {
            exitCode = runner.RunAsync(start, line =>
            {
                if (ResultLine().Match(line) is { Success: true } match) result = match;
            }, cancel).GetAwaiter().GetResult();
        }
        catch (LauncherException error)
        {
            throw new ShaderPreparationException(error.Message, error);
        }
        cancel.ThrowIfCancellationRequested();
        if (exitCode == GameExitReport.DllNotFoundExitCode)
            throw new ShaderPreparationException($"The game could not start: {GameExitReport.DllNotFoundWords} (0xC0000135)");
        if (result is null) throw new ShaderPreparationException($"{Stopped} (exit code {exitCode})");
        var fields = result.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(field => field.Split('=', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);
        if (fields.GetValueOrDefault("status") != "ok")
            throw new ShaderPreparationException(
                $"The game could not read the shaders in its Modules ({fields.GetValueOrDefault("reason") ?? $"exit code {exitCode}"}).");
        return int.Parse(fields.GetValueOrDefault("containers") ?? "0", CultureInfo.InvariantCulture);
    }

    // "result status=ok containers=91 ms=2400" or "result status=failed reason=module_load ms=40".
    [GeneratedRegex(@"^result (.+)$")]
    private static partial Regex ResultLine();
}
