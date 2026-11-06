namespace TopSpin4.Launcher.Core;

/// <summary>The facts in the window's footer: the Launcher's version and commit, the renderer the settings choose, the GPU and the game files folder.</summary>
public static class FooterInfo
{
    /// <summary>
    /// "0.1.0 (cd2c6fc)" from an informational version such as "0.1.0+cd2c6fc…", the commit cut to seven characters; the
    /// version alone when there is no commit.
    /// </summary>
    public static string Version(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "dev";
        var plus = informational.IndexOf('+');
        if (plus < 0) return informational;
        var commit = informational[(plus + 1)..];
        return commit.Length == 0 ? informational[..plus] : $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    /// <summary>The renderer the settings file asks for: Native (the game's default) unless it names Classic.</summary>
    public static string Renderer(string? settingsText) =>
        settingsText is not null && SettingsFile.ValueOf(SettingsFile.MigrateToLatest(settingsText), SettingsSchema.RendererKey) is { } value
            && value.Equals("classic", StringComparison.OrdinalIgnoreCase) ? "Classic renderer" : "Native renderer";

    /// <summary>The footer line: the parts that are known, joined.</summary>
    public static string Line(string version, string renderer, string? gpu, string? gameFiles) =>
        string.Join("  ·  ", new[] { "Version " + version, renderer, gpu, gameFiles is null ? null : "Game files: " + gameFiles }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
