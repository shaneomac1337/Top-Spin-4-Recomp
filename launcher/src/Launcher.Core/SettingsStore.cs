namespace TopSpin4.Launcher.Core;

/// <summary>
/// Every setting the game's export lists, over the Settings file. The Launcher has no list of
/// settings of its own: the rows, labels, choices, defaults and help text all come from the
/// export, and the values from the file the game itself reads, so the Launcher and the in-game
/// Settings page agree.
/// </summary>
public sealed class SettingsStore(SettingsSchema schema, string settingsPath)
{
    public IReadOnlyList<SettingDescription> Settings { get; } = schema.Settings;

    public string SettingsPath { get; } = settingsPath;

    private string ReadFile() => File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "";

    // What the game reads: it migrates the file before applying it, so a value a migration replaces counts as replaced.
    private string ReadMigrated() => SettingsFile.Migrate(ReadFile(), schema.FileVersion);

    private static string ValueIn(string text, SettingDescription setting) =>
        SettingsFile.ValueOf(text, setting.Key) is { } value && setting.Allows(value) ? value : setting.Default;

    /// <summary>The value the game will read: the file's, or the default when it has none or the game would refuse it.</summary>
    public string ValueOf(SettingDescription setting) => ValueIn(ReadMigrated(), setting);

    /// <summary>Every setting's value by key, from one read of the file.</summary>
    public IReadOnlyDictionary<string, string> Values()
    {
        var text = ReadMigrated();
        return Settings.ToDictionary(setting => setting.Key, setting => ValueIn(text, setting));
    }

    /// <summary>Writes the value into the Settings file; a value the game would refuse is not written.</summary>
    public SettingChange Set(SettingDescription setting, string value)
    {
        if (!setting.Allows(value)) return SettingChange.Refused;
        if (SettingsFile.IsNewerThanBuild(ReadFile(), schema.FileVersion)) return SettingChange.FileIsNewer;
        return SettingsFile.Save(SettingsPath, setting.Key, SettingsFile.Literal(setting, value), schema.FileVersion)
            ? SettingChange.Saved
            : SettingChange.NotWritten;
    }

    /// <summary>Writes each setting's default, every one, stopping at the first that cannot be written.</summary>
    public SettingChange ResetToDefaults()
    {
        foreach (var setting in Settings)
        {
            var change = Set(setting, setting.Default);
            if (change != SettingChange.Saved) return change;
        }
        return SettingChange.Saved;
    }

    public static string Describe(SettingChange change) => change switch
    {
        SettingChange.Saved => "",
        SettingChange.FileIsNewer => "Your Settings file was written by a newer version of the game, so the Launcher left it alone. Change this setting in the game.",
        SettingChange.Refused => "The game does not allow that value.",
        _ => "The Settings file could not be written.",
    };
}

public enum SettingChange { Saved, Refused, FileIsNewer, NotWritten }
