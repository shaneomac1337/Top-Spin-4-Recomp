using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class HomeModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ts4-home-").FullName;
    private readonly TestDisc _disc = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Cache => Path.Combine(_root, "cache");

    private Preparation Preparation() =>
        new(Cache, FileManifest.Parse(_disc.ManifestText()), FileManifest.Parse(TestTitleUpdate.ManifestText()),
            new FakeDiskSpace(), new FakeTime());

    // A cache holding both prepared folders, made the way setup makes it.
    private Preparation PreparedCache()
    {
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(input);
        var disc = Path.Combine(input, "Top Spin 4.iso");
        _disc.WriteImage(disc);
        var titleUpdate = TestTitleUpdate.Package().WriteTo(Path.Combine(input, "TU_1A5822P_000000C000000.0000000000083"));
        var preparation = Preparation();
        preparation.Prepare(disc, titleUpdate, null, TestContext.Current.CancellationToken);
        return preparation;
    }

    private string GameExe(bool exists = true)
    {
        var path = Path.Combine(_root, "game", LaunchArguments.GameExeName);
        if (exists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "exe");
        }
        return path;
    }

    [Fact]
    public void Both_prepared_folders_and_the_game_exe_give_ready_before_any_play()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        Assert.Equal(HomeStatus.Ready, home.Status);
        Assert.True(home.CanPlay);
        Assert.Contains("ready", home.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_the_game_exe_the_status_says_so_at_once_and_play_is_not_offered()
    {
        var home = new HomeModel(PreparedCache(), GameExe(exists: false));

        Assert.Equal(HomeStatus.GameExeMissing, home.Status);
        Assert.False(home.CanPlay);
        Assert.Contains(LaunchArguments.GameExeName, home.StatusLine);
    }

    [Fact]
    public void Without_prepared_folders_the_status_says_the_game_files_are_missing()
    {
        var home = new HomeModel(Preparation(), GameExe());

        Assert.Equal(HomeStatus.GameFilesMissing, home.Status);
        Assert.False(home.CanPlay);
    }

    [Fact]
    public void A_prepared_folder_removed_later_is_noticed_without_building_a_new_model()
    {
        var preparation = PreparedCache();
        var home = new HomeModel(preparation, GameExe());
        Directory.Delete(preparation.GameDataRoot, recursive: true);

        Assert.Equal(HomeStatus.GameFilesMissing, home.Status);
    }

    [Fact]
    public void Home_offers_settings_help_and_logs_and_keeps_safe_start_under_trouble_starting()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        Assert.Equal([HomeAction.Settings, HomeAction.CreateShortcut, HomeAction.Help, HomeAction.OpenLogs], home.Actions);
        Assert.Equal([HomeAction.SafeStart], home.TroubleStartingActions);
        Assert.Equal("Trouble starting?", HomeModel.TroubleStartingHeading);
    }

    [Fact]
    public void The_logs_folder_is_the_logs_folder_beside_the_game_exe()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        Assert.Equal(Path.Combine(_root, "game", "logs"), home.LogsFolder);
    }

    [Fact]
    public void Help_opens_the_help_file_beside_the_launcher_when_it_is_there()
    {
        Assert.Null(HelpDocument.Find(_root));

        File.WriteAllText(Path.Combine(_root, "HELP.md"), "# Top Spin 4 Recomp");

        Assert.Equal(Path.Combine(_root, "HELP.md"), HelpDocument.Find(_root));
    }

    private string LogsFolder => Path.Combine(_root, "game", "logs");

    // A session log the game wrote, with the "crash report: saved" line and the zip it names.
    private string WriteCrashReport()
    {
        Directory.CreateDirectory(LogsFolder);
        var zip = Path.Combine(LogsFolder, "crash-20261028-120000.zip");
        File.WriteAllText(zip, "zip");
        File.WriteAllText(Path.Combine(LogsFolder, "topspin4_001.log"),
            $"[error] crash: unhandled exception 0xC0000005; writing a crash report{Environment.NewLine}" +
            $"[error] crash report: saved {zip}{Environment.NewLine}");
        return zip;
    }

    [Fact]
    public void After_a_normal_exit_the_home_screen_is_ready_with_no_note_and_safe_start_stays_small()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(0);

        Assert.Equal(HomeStatus.Ready, home.Status);
        Assert.Null(home.Note);
        Assert.False(home.SafeStartProminent);
        Assert.Null(home.CrashReportPath);
        Assert.DoesNotContain(HomeAction.OpenCrashReport, home.Actions);
    }

    [Fact]
    public void When_Native_cannot_start_the_note_says_so_and_safe_start_is_not_offered_as_the_way_out()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(GameExitReport.NativeUnavailableExitCode);

        Assert.Equal(HomeStatus.NativeUnavailable, home.Status);
        Assert.True(home.CanPlay);
        Assert.Contains("graphics card", home.Note);
        Assert.False(home.SafeStartProminent);
        Assert.Null(home.CrashReportPath);
    }

    [Fact]
    public void After_a_gpu_failure_exit_the_note_says_update_the_driver_and_safe_start_is_prominent()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(GameExitReport.GpuFailureExitCode);

        Assert.Equal(HomeStatus.LastSessionGpuFailure, home.Status);
        Assert.True(home.CanPlay);
        Assert.Contains("Update the graphics driver", home.Note);
        Assert.True(home.SafeStartProminent);
        Assert.Null(home.CrashReportPath);
        Assert.DoesNotContain(HomeAction.OpenCrashReport, home.Actions);
    }

    [Fact]
    public void After_a_crash_exit_the_status_says_so_safe_start_is_prominent_and_the_crash_report_is_offered()
    {
        var zip = WriteCrashReport();
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(unchecked((int)0xC0000005));

        Assert.Equal(HomeStatus.LastSessionCrashed, home.Status);
        Assert.True(home.CanPlay);
        Assert.Contains("crashed", home.Note);
        Assert.True(home.SafeStartProminent);
        Assert.Equal(zip, home.CrashReportPath);
        Assert.Contains(HomeAction.OpenCrashReport, home.Actions);
    }

    [Fact]
    public void A_crash_with_no_report_on_disk_is_still_a_crash_but_offers_no_report()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(unchecked((int)0xC0000005));

        Assert.Equal(HomeStatus.LastSessionCrashed, home.Status);
        Assert.True(home.SafeStartProminent);
        Assert.Null(home.CrashReportPath);
        Assert.DoesNotContain(HomeAction.OpenCrashReport, home.Actions);
    }

    [Fact]
    public void A_crash_report_the_log_names_but_that_is_gone_is_not_offered()
    {
        var zip = WriteCrashReport();
        File.Delete(zip);
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(unchecked((int)0xC0000005));

        Assert.Null(home.CrashReportPath);
    }

    [Fact]
    public void A_nonzero_exit_that_is_not_a_crash_code_is_still_reported_as_the_game_stopping()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.SessionEnded(1);

        Assert.Equal(HomeStatus.LastSessionCrashed, home.Status);
        Assert.Contains("exit code 1", home.Note);
    }

    [Fact]
    public void A_later_normal_exit_clears_the_failure()
    {
        WriteCrashReport();
        var home = new HomeModel(PreparedCache(), GameExe());
        home.SessionEnded(unchecked((int)0xC0000005));

        home.SessionEnded(0);

        Assert.Equal(HomeStatus.Ready, home.Status);
        Assert.Null(home.Note);
        Assert.False(home.SafeStartProminent);
        Assert.Null(home.CrashReportPath);
    }

    [Fact]
    public void While_the_game_runs_the_status_says_so_and_play_is_not_offered_until_it_exits()
    {
        var home = new HomeModel(PreparedCache(), GameExe());

        home.GameStarted();

        Assert.Equal(HomeStatus.GameRunning, home.Status);
        Assert.False(home.CanPlay);
        Assert.Contains("running", home.StatusLine);

        home.SessionEnded(0);

        Assert.Equal(HomeStatus.Ready, home.Status);
        Assert.True(home.CanPlay);
    }

    [Fact]
    public void A_game_that_could_not_be_started_says_why_and_puts_safe_start_up_front()
    {
        var home = new HomeModel(PreparedCache(), GameExe());
        home.GameStarted();

        home.StartFailed("Access is denied.");

        Assert.NotEqual(HomeStatus.GameRunning, home.Status);
        Assert.True(home.CanPlay);
        Assert.Contains("Access is denied.", home.Note);
        Assert.True(home.SafeStartProminent);

        home.GameStarted();
        home.SessionEnded(0);

        Assert.False(home.SafeStartProminent);
        Assert.Null(home.Note);
    }

    [Fact]
    public void Missing_game_files_offer_set_up_again_first_and_otherwise_it_is_not_offered()
    {
        var missing = new HomeModel(Preparation(), GameExe());

        Assert.Equal(HomeAction.SetUpAgain, missing.Actions[0]);
        Assert.Contains(HomeAction.Settings, missing.Actions);
        Assert.DoesNotContain(HomeAction.SetUpAgain, new HomeModel(PreparedCache(), GameExe()).Actions);
    }

    [Fact]
    public void A_missing_game_exe_outranks_the_last_session_in_the_status()
    {
        var home = new HomeModel(PreparedCache(), GameExe(exists: false));

        home.SessionEnded(GameExitReport.GpuFailureExitCode);

        Assert.Equal(HomeStatus.GameExeMissing, home.Status);
    }
}
