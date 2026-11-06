using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    // The shape of `topspin4.exe --print-settings-schema` (src/settings/schema.h), cut down; no file version unless given.
    private static string Export(long? fileVersion = null) => ExportFixture.Json(
    [
        ExportFixture.Setting("ts4_renderer", "choice", "classic", ["classic", "native"], "next_start", "both", "Graphics", "Renderer", 10, "Renderer help \"quoted\""),
        ExportFixture.Setting("ts4_msaa", "choice", "original", ["original", "4", "8"], "now", "native", "Graphics", "MSAA", 30, "MSAA help"),
        ExportFixture.Setting("ts4_gpu", "choice", "auto", ["auto", "1", "2"], "next_start", "native", "Graphics", "GPU", 15, "GPU help"),
        ExportFixture.Setting("ts4_pad_a", "choice", "a", ["a", "b"], "next_start", "both", "Controls", "Pad: A", 100, "Pad help", null, ("pad", "A")),
        ExportFixture.Setting("mnk_mode", "bool", "false", [], "now", "both", "Controls", "Keyboard and mouse", 10, "Mnk help"),
    ], fileVersion: fileVersion);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ts4-launcher-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => SettingsFile.PathIn(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private SettingsStore Settings() => new(SettingsSchema.Parse(Export()), SettingsPath);

    [Fact]
    public void Reading_the_export_gives_every_setting_with_its_fields()
    {
        var schema = SettingsSchema.Parse(Export());

        Assert.Equal(5, schema.Settings.Count);
        var renderer = schema.Settings[0];
        Assert.Equal(("ts4_renderer", "choice", "classic", "next_start", "Graphics", "Renderer", 10),
            (renderer.Key, renderer.Kind, renderer.Default, renderer.AppliesWhen, renderer.Section, renderer.Label, renderer.Order));
        Assert.Equal(["classic", "native"], renderer.Choices);
        Assert.Equal(["Classic", "Native"], renderer.ChoiceLabels);
        Assert.Equal("Renderer help \"quoted\"", renderer.Help);
        Assert.True(schema.Settings[4].IsBool);
        Assert.Equal((BindingDevice.Pad, "A"), (schema.Settings[3].Binding!.Device, schema.Settings[3].Binding!.Action));
        Assert.Null(renderer.Binding);
    }

    [Fact]
    public void Reading_the_export_gives_the_sections_and_the_presets_with_their_covered_values()
    {
        var schema = SettingsSchema.Parse(Export());

        Assert.Equal(["Graphics", "Controls"], schema.Sections);
        Assert.Equal(("Graphics", "Graphics preset"), (schema.PresetSection, schema.PresetLabel));
        Assert.Equal(["original", "enhanced"], schema.Presets.Select(preset => preset.Value));
        Assert.Equal(["Original", "Enhanced"], schema.Presets.Select(preset => preset.Label));
        Assert.Equal(
            new KeyValuePair<string, string>[]
            {
                new("ts4_resolution_scale", "auto"), new("ts4_msaa", "4"), new("ts4_anisotropic_filtering", "16"), new("ts4_sharpening", "off"),
            },
            schema.Presets[1].Values);
    }

    [Theory]
    [InlineData("""{"format": 2, "settings": []}""")]
    [InlineData("""{"format": 1, "sections": [], "settings": []}""")]
    [InlineData("""{"format": 3}""")]
    [InlineData("not json")]
    public void An_export_in_another_format_or_not_json_or_missing_format_2_fields_is_refused(string export) =>
        Assert.Throws<SettingsExportException>(() => SettingsSchema.Parse(export));

    [Fact]
    public void Writing_a_setting_into_an_existing_file_leaves_the_other_lines_untouched()
    {
        Directory.CreateDirectory(_folder);
        const string Before = "version = 1\r\n# mine\r\nmnk_mode = true   # keep\r\nts4_renderer = \"classic\"\r\nts4_msaa = \"4\"\r\n\r\n[table]\r\nx = 1";
        File.WriteAllText(SettingsPath, Before);
        var settings = Settings();
        var renderer = settings.Settings.Single(setting => setting.Key == "ts4_renderer");

        Assert.Equal(SettingChange.Saved, settings.Set(renderer, "native"));

        Assert.Equal(Before.Replace("ts4_renderer = \"classic\"", "ts4_renderer = \"native\""), File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void A_setting_missing_from_the_file_is_appended_and_an_unversioned_file_gets_its_version_first()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "mnk_mode = true");
        var renderer = Settings().Settings.Single(setting => setting.Key == "ts4_renderer");

        Settings().Set(renderer, "native");

        Assert.Equal("version = 1\nmnk_mode = true\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void A_new_file_is_created_at_the_current_version()
    {
        var renderer = Settings().Settings.Single(setting => setting.Key == "ts4_renderer");

        Settings().Set(renderer, "native");

        Assert.Equal("version = 1\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void A_saved_Classic_reads_as_Native_before_the_file_is_migrated()
    {
        const string Before = "version = 2\nts4_renderer = \"classic\"\n";
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, Before);
        var store = new SettingsStore(SettingsSchema.Parse(Export(3)), SettingsPath);

        Assert.Equal("native", store.Values()["ts4_renderer"]);
        Assert.Equal("native", store.ValueOf(store.Settings[0]));
        Assert.Equal(Before, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void The_version_written_and_the_newer_check_come_from_the_export()
    {
        var schema = SettingsSchema.Parse(Export(3));
        var renderer = schema.Settings[0];
        Assert.Equal(3, schema.FileVersion);
        Assert.Equal(SettingsFile.FallbackVersion, SettingsSchema.Parse(Export()).FileVersion);

        new SettingsStore(schema, SettingsPath).Set(renderer, "native");
        Assert.Equal("version = 3\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));

        File.WriteAllText(SettingsPath, "version = 4\n");
        Assert.Equal(SettingChange.FileIsNewer, new SettingsStore(schema, SettingsPath).Set(renderer, "classic"));
    }

    [Fact]
    public void Saving_into_a_version_1_file_migrates_a_resolution_scale_of_4_to_3_as_the_game_does()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "version = 1\r\n# mine\r\nts4_resolution_scale = \"4\"  # big\r\nts4_msaa = \"4\"\r\n");
        var schema = SettingsSchema.Parse(Export(2));

        new SettingsStore(schema, SettingsPath).Set(schema.Settings[0], "native");

        Assert.Equal(
            "version = 2\r\n# mine\r\nts4_resolution_scale = \"3\"  # big\r\nts4_msaa = \"4\"\r\nts4_renderer = \"native\"\n",
            File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Saving_into_a_version_1_file_keeps_any_other_resolution_scale()
    {
        var schema = SettingsSchema.Parse(Export(2));
        foreach (var scale in new[] { "1", "auto", "2", "3" })
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(SettingsPath, $"version = 1\nts4_resolution_scale = \"{scale}\"\n");

            new SettingsStore(schema, SettingsPath).Set(schema.Settings[0], "native");

            Assert.Equal($"version = 2\nts4_resolution_scale = \"{scale}\"\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));
        }
    }

    [Fact]
    public void A_version_2_file_is_not_migrated_again_and_an_export_without_a_version_runs_no_migration()
    {
        Directory.CreateDirectory(_folder);
        var schema = SettingsSchema.Parse(Export(2));
        File.WriteAllText(SettingsPath, "version = 2\nts4_resolution_scale = \"4\"\n");
        new SettingsStore(schema, SettingsPath).Set(schema.Settings[0], "native");
        Assert.Equal("version = 2\nts4_resolution_scale = \"4\"\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));

        // The game's version decides how many migrations run: at version 1 there are none.
        File.WriteAllText(SettingsPath, "version = 1\nts4_resolution_scale = \"4\"\n");
        Settings().Set(Settings().Settings[0], "native");
        Assert.Equal("version = 1\nts4_resolution_scale = \"4\"\nts4_renderer = \"native\"\n", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("ts4_resolution_scale = \"4\"\n", "ts4_resolution_scale = \"3\"\n")]
    [InlineData("  ts4_resolution_scale='4' # x\r\n", "  ts4_resolution_scale='3' # x\r\n")]
    [InlineData("ts4_resolution_scale = 4\n", "ts4_resolution_scale = 3\n")]
    [InlineData("ts4_resolution_scale = 4 # x", "ts4_resolution_scale = 3 # x")]
    [InlineData("ts4_resolution_scale = \"2\"\n", "ts4_resolution_scale = \"2\"\n")]
    [InlineData("ts4_resolution_scale = \"40\"\n", "ts4_resolution_scale = \"40\"\n")]
    [InlineData("ts4_resolution_scale = \"4\n", "ts4_resolution_scale = \"4\n")]
    [InlineData("ts4_resolution_scale_old = \"4\"\nts4_msaa = \"4\"\n", "ts4_resolution_scale_old = \"4\"\nts4_msaa = \"4\"\n")]
    [InlineData("ts4_resolution_scale =\n", "ts4_resolution_scale =\n")]
    public void Replacing_a_value_changes_only_that_keys_value_and_keeps_the_line(string text, string expected) =>
        Assert.Equal(expected, SettingsFile.ReplaceValue(text, "ts4_resolution_scale", "4", "3"));

    [Theory]
    [InlineData("version = 3\n", 3L)]
    [InlineData("version = -2 # old\n", -2L)]
    [InlineData("version = 4 \t\r\n", 4L)]
    [InlineData("version = \"2\"\n", null)]
    [InlineData("version = 2x\n", null)]
    [InlineData("version = 99999999999999999999\n", null)]
    [InlineData("version_old = 2\n", null)]
    public void The_file_version_is_read_as_the_game_reads_it(string text, long? expected) =>
        Assert.Equal(expected, SettingsFile.Version(text));

    [Fact]
    public void A_file_from_a_newer_build_is_not_rewritten()
    {
        Directory.CreateDirectory(_folder);
        const string Newer = "version = 9\nts4_renderer = \"classic\"\n";
        File.WriteAllText(SettingsPath, Newer);
        var settings = Settings();

        Assert.Equal(SettingChange.FileIsNewer, settings.Set(settings.Settings[0], "native"));
        Assert.Equal(Newer, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void A_value_the_game_would_refuse_is_not_written()
    {
        var settings = Settings();

        Assert.Equal(SettingChange.Refused, settings.Set(settings.Settings[0], "vulkan"));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void The_value_shown_is_the_files_or_the_default()
    {
        var settings = Settings();
        var renderer = settings.Settings.Single(setting => setting.Key == "ts4_renderer");
        Assert.Equal("classic", settings.ValueOf(renderer));

        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "version = 1\nts4_renderer = \"native\" # why\n");
        Assert.Equal("native", settings.ValueOf(renderer));

        File.WriteAllText(SettingsPath, "ts4_renderer = \"vulkan\"\n");
        Assert.Equal("classic", settings.ValueOf(renderer));
    }

    [Fact]
    public void A_bool_is_written_bare()
    {
        var mnk = SettingsSchema.Parse(Export()).Settings.Single(setting => setting.Key == "mnk_mode");

        Assert.Equal("true", SettingsFile.Literal(mnk, "true"));
        Assert.Equal("\"native\"", SettingsFile.Literal(SettingsSchema.Parse(Export()).Settings[0], "native"));
    }

    [Fact]
    public void The_gpu_failure_exit_code_maps_to_the_update_driver_message()
    {
        Assert.Equal(0x47505546, GameExitReport.GpuFailureExitCode);
        Assert.Contains("Update the graphics driver", GameExitReport.FromExit(0x47505546, null).Message);
        Assert.Equal(GameExitKind.Closed, GameExitReport.FromExit(0, null).Kind);
    }

    [Fact]
    public void Safe_start_arguments_are_plays_plus_safe_and_no_setting()
    {
        var prepared = new PreparedData(@"C:\data\gamedata", @"C:\data\updatedata", Reused: true);

        Assert.Equal(["--game_data_root=" + prepared.GameDataRoot, "--update_data_root=" + prepared.UpdateDataRoot, "--safe"],
            LaunchArguments.SafeStart(prepared));
        Assert.Equal(LaunchArguments.Play(prepared), LaunchArguments.SafeStart(prepared).Where(argument => argument != "--safe"));
        Assert.DoesNotContain(LaunchArguments.Play(prepared), argument => argument == "--safe");
    }

    [Fact]
    public async Task Reading_the_export_from_a_gui_exe_through_a_pipe_works()
    {
        // Needs a built game: set TOPSPIN4_EXE to its path to run this against the real exe.
        var exe = System.Environment.GetEnvironmentVariable("TOPSPIN4_EXE");
        Assert.SkipWhen(string.IsNullOrEmpty(exe) || !File.Exists(exe), "TOPSPIN4_EXE is not set to a built topspin4.exe.");

        var schema = await SettingsExport.ReadAsync(exe!, cancel: TestContext.Current.CancellationToken);

        Assert.Contains(schema.Settings, setting => setting.Key == "ts4_renderer");
        Assert.Contains(schema.Presets, preset => preset.Value == "enhanced");
        Assert.Contains(schema.Settings, setting => setting.Binding is { Device: BindingDevice.Pad });
    }
}
