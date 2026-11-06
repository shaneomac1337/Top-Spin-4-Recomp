using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class GameExitReportNativeTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), "ts4-exit-" + Guid.NewGuid().ToString("N") + ".log");

    public void Dispose() => File.Delete(_log);

    [Fact]
    public void The_exit_code_matches_the_game()
    {
        Assert.Equal(0x4E415456, GameExitReport.NativeUnavailableExitCode);
    }

    [Fact]
    public void When_Native_cannot_start_the_message_is_the_one_the_game_logged()
    {
        File.WriteAllText(_log,
            "[2026-11-06 12:09:24.359] [error] [core] [t1] native renderer: cannot start: Top Spin 4 can't run on Intel(R) UHD Graphics yet.\n");

        var report = GameExitReport.FromExit(GameExitReport.NativeUnavailableExitCode, _log);

        Assert.Equal(GameExitKind.NativeUnavailable, report.Kind);
        Assert.Equal("Top Spin 4 can't run on Intel(R) UHD Graphics yet.", report.Message);
    }

    [Fact]
    public void Without_a_log_line_the_message_is_still_about_the_graphics_card()
    {
        Assert.Contains("graphics card", GameExitReport.FromExit(GameExitReport.NativeUnavailableExitCode, null).Message);
    }

    [Fact]
    public void A_missing_DLL_is_named_as_the_Visual_Cpp_runtime()
    {
        var report = GameExitReport.FromExit(GameExitReport.DllNotFoundExitCode, null);

        Assert.Equal(GameExitKind.Crashed, report.Kind);
        Assert.Contains("Visual C++", report.Message);
    }

    [Fact]
    public void A_saved_Classic_becomes_Native_when_the_file_is_migrated()
    {
        var migrated = SettingsFile.Migrate("version = 2\nts4_renderer = \"classic\"\n", 3);

        Assert.Equal("version = 3\nts4_renderer = \"native\"\n", migrated);
    }
}
