namespace TopSpin4.Launcher.Core;

/// <summary>One choice of a setting, with the label the player sees.</summary>
public sealed record SettingOption(string Value, string Label);

public enum SettingRowKind { Setting, Preset, Action }

/// <summary>
/// One row of the Settings page: a setting, the Graphics Preset row or the Reset controls action,
/// with its label, current value, help and choices. A row that does nothing on the chosen renderer
/// is not available, and <see cref="Note"/> says why. A binding row has no choices to pick from;
/// it is <see cref="Remappable"/>: the player presses the key or pad button to use (BindingCapture). A text row that is not a
/// binding (the gamertag) is <see cref="TextEntry"/>: the player types its value.
/// </summary>
public sealed record SettingRow(string Key, SettingRowKind Kind, string Label, string Help, string Value, string ValueLabel,
    IReadOnlyList<SettingOption> Options, bool Available, bool Editable, bool Remappable, string? Note, BindingDescription? Binding,
    bool TextEntry = false);

/// <summary>
/// What a remap did: the write, and one line for each other binding of the same device it clashed
/// with (a pad button another binding used swaps with it; a key another binding used is taken from it).
/// </summary>
public sealed record BindingResult(SettingChange Change, IReadOnlyList<string> Clashes)
{
    public static BindingResult Of(SettingChange change) => new(change, []);
}

/// <summary>One tab of the Settings page: a section of the game's Settings page and its rows.</summary>
public sealed record SettingsTab(string Name, IReadOnlyList<SettingRow> Rows);

/// <summary>
/// The Settings page as a model with no UI. The tabs and rows come from the game's settings export
/// (the Launcher keeps no list of its own) and the values from the Settings file the game reads, so
/// the page and the in-game Settings page agree. A change is written to the Settings file at once.
/// </summary>
public sealed class SettingsPageModel
{
    /// <summary>The key of the Preset row (the game's <c>kPresetKey</c>). It names no setting and is never saved.</summary>
    public const string PresetKey = "graphics_preset";

    /// <summary>The key of the Reset controls row (the game's <c>kResetControlsKey</c>). It names no setting.</summary>
    public const string ResetControlsKey = "reset_controls";

    private const string CustomPreset = "custom";

    /// <summary>Plain words for the choices whose game labels mean little to a player.</summary>
    private static readonly Dictionary<(string Key, string Value), string> ChoiceLabels = new()
    {
        [(SettingsSchema.RendererKey, "native")] = "Native (recommended)",
        [(SettingsSchema.RendererKey, "classic")] = "Classic (compatibility)",
    };

    private readonly SettingsSchema? _schema;
    private readonly SettingsStore? _store;

    private SettingsPageModel(SettingsSchema? schema, string settingsPath, string? unavailableReason)
    {
        _schema = schema;
        _store = schema is null ? null : new SettingsStore(schema, settingsPath);
        UnavailableReason = unavailableReason;
    }

    /// <summary>The page for a settings export read from the game.</summary>
    public static SettingsPageModel From(SettingsSchema schema, string settingsPath) => new(schema, settingsPath, null);

    /// <summary>The page for the text of an export: the reason instead of rows when the Launcher cannot read it.</summary>
    public static SettingsPageModel FromExport(string exportJson, string settingsPath)
    {
        try
        {
            return From(SettingsSchema.Parse(exportJson), settingsPath);
        }
        catch (SettingsExportException error)
        {
            return Unavailable(error.Message);
        }
    }

    /// <summary>The page when the game's settings could not be read: no rows, and the reason.</summary>
    public static SettingsPageModel Unavailable(string reason) => new(null, "", reason);

    /// <summary>Why the settings cannot be shown, or null when they can.</summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Said under the rows: every setting is on this page, and the same ones can also be changed in
    /// game, and how to open that page (ADR-0008; Glossary: Settings page).
    /// </summary>
    public string InGameNote =>
        "Every setting is on this page. You can also change them while playing: choose PC SETTINGS in the main menu, press F4, or hold Back and Start on a controller.";

    /// <summary>One tab per section of the export, in its order; the Graphics tab opens with the Preset row. Empty when unavailable.</summary>
    public IReadOnlyList<SettingsTab> Tabs
    {
        get
        {
            if (_schema is null || _store is null) return [];
            var values = _store.Values();
            var renderer = RendererIn(values);
            return _schema.Sections.Select(section =>
            {
                var rows = new List<SettingRow>();
                if (section == _schema.PresetSection && _schema.Presets.Count > 0) rows.Add(PresetRow(values, renderer));
                // Players get Native, so the renderer has no row (ShownOnSettingsPage in src/settings/registry.h).
                rows.AddRange(_schema.Settings.Where(setting => setting.Section == section && setting.Key != SettingsSchema.RendererKey)
                    .OrderBy(setting => setting.Order)
                    .Select(setting => Row(setting, values[setting.Key], renderer)));
                if (rows.Any(row => row.Binding is not null)) rows.Add(ResetControlsRow());
                return new SettingsTab(section, rows);
            }).ToList();
        }
    }

    /// <summary>Writes the chosen value of a row into the Settings file; on the Preset row, every setting the Preset covers.</summary>
    public SettingChange Choose(SettingRow row, string value)
    {
        if (_schema is null || _store is null) return SettingChange.NotWritten;
        if (row.Kind == SettingRowKind.Action) return row.Key == ResetControlsKey ? ResetControls() : SettingChange.Refused;
        if (row.Binding is not null) return Bind(row, value).Change;
        if (row.Kind == SettingRowKind.Setting)
            return _schema.Settings.FirstOrDefault(setting => setting.Key == row.Key) is { } setting
                ? _store.Set(setting, value)
                : SettingChange.Refused;
        var preset = _schema.Presets.FirstOrDefault(candidate => candidate.Value == value);
        if (preset is null) return SettingChange.Refused;   // Custom is shown, never chosen
        foreach (var (key, covered) in preset.Values)
        {
            if (_schema.Settings.FirstOrDefault(setting => setting.Key == key) is not { } member) continue;
            var change = _store.Set(member, covered);
            if (change != SettingChange.Saved) return change;
        }
        return SettingChange.Saved;
    }

    /// <summary>Writes every setting's default into the Settings file.</summary>
    public SettingChange ResetToDefaults() => _store?.ResetToDefaults() ?? SettingChange.NotWritten;

    /// <summary>
    /// Writes the default of every pad and key binding, and nothing else: keyboard and mouse on or
    /// off is not a binding and stays (SettingsModel::ResetControls in src/settings/page_model.h).
    /// </summary>
    public SettingChange ResetControls()
    {
        if (_schema is null || _store is null) return SettingChange.NotWritten;
        foreach (var setting in _schema.Settings.Where(setting => setting.Binding is not null))
        {
            var change = _store.Set(setting, setting.Default);
            if (change != SettingChange.Saved) return change;
        }
        return SettingChange.Saved;
    }

    /// <summary>
    /// Binds a binding row to a pad input (its name as the game's choices have it: "a", "lt") or to one
    /// key ("Shift+W", as the game's key names go, replacing the keys the row had), writing it to the
    /// Settings file. The game's clash rules apply (SettingsModel::Change and BindKey in
    /// src/settings/page_model.h), and each clash is reported: a pad button another binding uses swaps
    /// with it, so nothing is left unbound; a key another binding uses is taken from it, and a binding
    /// left with no key takes this row's old keys.
    /// </summary>
    public BindingResult Bind(SettingRow row, string value)
    {
        if (_schema is null || _store is null) return BindingResult.Of(SettingChange.NotWritten);
        if (row.Binding is not { } binding || _schema.Settings.FirstOrDefault(candidate => candidate.Key == row.Key) is not { } setting
            || !setting.Allows(value) || (binding.Device == BindingDevice.Keyboard && value.Length == 0))
            return BindingResult.Of(SettingChange.Refused);
        var values = _store.Values();
        var old = values[setting.Key];
        if (old == value) return BindingResult.Of(SettingChange.Saved);   // already so: nothing to write, no clash
        var change = _store.Set(setting, value);
        if (change != SettingChange.Saved) return BindingResult.Of(change);
        var clashes = new List<string>();
        foreach (var other in _schema.Settings.Where(other => other.Key != setting.Key && other.Binding?.Device == binding.Device))
        {
            var (replacement, said) = binding.Device == BindingDevice.Pad
                ? PadClash(other, values[other.Key], value, old)
                : KeyClash(other, values[other.Key], value, old);
            if (replacement is null) continue;
            change = _store.Set(other, replacement);
            if (change != SettingChange.Saved) return new BindingResult(change, clashes);
            clashes.Add(said!);
        }
        return new BindingResult(SettingChange.Saved, clashes);
    }

    // Another pad binding on the same button takes the old button of the one that was just bound.
    private static (string? Replacement, string? Said) PadClash(SettingDescription other, string current, string value, string old) =>
        current != value ? (null, null)
            : (old, $"{other.Label} had {other.LabelOf(value)}; it now has {other.LabelOf(old)}.");

    // Another key binding with the same key loses it; with no key left it takes the old keys of the one just bound.
    private static (string? Replacement, string? Said) KeyClash(SettingDescription other, string current, string value, string old)
    {
        var keys = current.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!keys.Remove(value)) return (null, null);
        var rest = string.Join(',', keys);
        var replacement = rest.Length == 0 ? old : rest;
        return (replacement, $"{other.Label} had {value}; it now has {replacement.Replace(",", ", ")}.");
    }

    // The renderer the values choose; a file without one runs the schema's default.
    private string RendererIn(IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue(SettingsSchema.RendererKey, out var renderer) ? renderer
            : _schema!.Settings.FirstOrDefault(setting => setting.Key == SettingsSchema.RendererKey)?.Default ?? "native";

    private SettingRow Row(SettingDescription setting, string value, string renderer)
    {
        var available = setting.AvailableOn(renderer);
        var options = (setting.IsBool ? new[] { "false", "true" } : setting.Choices.ToArray())
            .Select(choice => new SettingOption(choice, LabelOf(setting, choice))).ToList();
        return new SettingRow(setting.Key, SettingRowKind.Setting, setting.Label, setting.Help, value, LabelOf(setting, value), options,
            available, Editable: available && setting.Binding is null, Remappable: available && setting.Binding is not null,
            Note: available ? null : "Only the Native renderer uses this setting; the renderer is set to Classic.",
            setting.Binding, TextEntry: setting.IsText && setting.Binding is null);
    }

    // The Preset the values match, else custom. A Preset whose every covered value matches wins. Else a setting that
    // does nothing on this renderer is left out of the match, so it cannot make the row read Custom (ActivePreset in
    // src/settings/presets.h).
    private string ActivePreset(IReadOnlyDictionary<string, string> values, string renderer)
    {
        foreach (var allSettings in new[] { true, false })
        {
            foreach (var preset in _schema!.Presets)
            {
                var all = true;
                foreach (var (key, covered) in preset.Values)
                {
                    var setting = _schema.Settings.FirstOrDefault(candidate => candidate.Key == key);
                    if (setting is null || (!allSettings && !setting.AvailableOn(renderer))) continue;
                    all &= (values.TryGetValue(key, out var current) ? current : setting.Default) == covered;
                }
                if (all) return preset.Value;
            }
        }
        return CustomPreset;
    }

    private SettingRow PresetRow(IReadOnlyDictionary<string, string> values, string renderer)
    {
        var schema = _schema!;
        var active = ActivePreset(values, renderer);
        var options = schema.Presets.Select(preset => new SettingOption(preset.Value, preset.Label)).ToList();
        // The settings the Presets set to different values that this renderer cannot use.
        var unavailable = schema.Presets[0].Values
            .Where(member => schema.Presets.Any(preset => preset.Values.Any(other => other.Key == member.Key && other.Value != member.Value)))
            .Select(member => schema.Settings.FirstOrDefault(setting => setting.Key == member.Key))
            .OfType<SettingDescription>().Where(setting => !setting.AvailableOn(renderer)).Select(setting => setting.Label).ToList();
        var note = unavailable.Count == 0 ? null
            : string.Join(" and ", unavailable) + (renderer == SettingsSchema.NativeRenderer
                ? " do not apply on this renderer."
                : " need the Native renderer and do not count on Classic.");
        return new SettingRow(PresetKey, SettingRowKind.Preset, schema.PresetLabel, schema.PresetHelp, active,
            options.FirstOrDefault(option => option.Value == active)?.Label ?? "Custom", options,
            Available: true, Editable: true, Remappable: false, Note: note, Binding: null);
    }

    // The game's Reset controls row (ResetRow in src/settings/page_model.h), worded the same.
    private static SettingRow ResetControlsRow() =>
        new(ResetControlsKey, SettingRowKind.Action, "Reset controls",
            "Put every controller and keyboard binding back to the original layout.", "", "", [],
            Available: true, Editable: false, Remappable: false, Note: null, Binding: null);

    private static string LabelOf(SettingDescription setting, string value) =>
        ChoiceLabels.TryGetValue((setting.Key, value), out var label) ? label : setting.LabelOf(value);
}
