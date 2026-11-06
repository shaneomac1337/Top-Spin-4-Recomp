namespace TopSpin4.Launcher.Core;

/// <summary>What the home screen's status line reports.</summary>
public enum HomeStatus
{
    Ready,
    /// <summary>The game was started from this window and has not exited yet.</summary>
    GameRunning,
    /// <summary>The game exe is not beside the Launcher, so PLAY cannot start anything.</summary>
    GameExeMissing,
    /// <summary>The prepared folders are gone or incomplete; setup has to run again.</summary>
    GameFilesMissing,
    /// <summary>The last session ended with the GPU or driver failing.</summary>
    LastSessionGpuFailure,
    /// <summary>The Native renderer could not start last session; the status line says why.</summary>
    NativeUnavailable,
    /// <summary>The last session crashed or was ended from outside.</summary>
    LastSessionCrashed,
}

/// <summary>The labelled actions of the home screen. PLAY is not one: it is the screen's main button.</summary>
public enum HomeAction
{
    /// <summary>Takes the player to the setup page; offered when the game files are missing.</summary>
    SetUpAgain,
    Settings,
    /// <summary>Creates or replaces the Direct shortcut on the Desktop and in the Start menu.</summary>
    CreateShortcut,
    Help,
    OpenLogs,
    /// <summary>Opens the crash report the game saved; offered only after a crash that left one.</summary>
    OpenCrashReport,
    SafeStart,
}

/// <summary>
/// The home screen as a model with no UI: the status line and which actions are offered. The
/// status is read from the disk each time it is asked for, so the window shows the missing exe the
/// moment it opens, not when PLAY is pressed, and notices prepared folders that went away.
/// </summary>
public sealed class HomeModel(Preparation preparation, string? gameExe)
{
    public const string TroubleStartingHeading = "Trouble starting?";

    /// <summary>The game exe PLAY starts, or null when there is none beside the Launcher.</summary>
    public string? GameExe { get; } = gameExe;

    private GameExitReport? _lastExit;
    private bool _running;
    private bool _startFailed;

    public HomeStatus Status =>
        _running ? HomeStatus.GameRunning
        : GameExe is null || !File.Exists(GameExe) ? HomeStatus.GameExeMissing
        : preparation.Existing() is null ? HomeStatus.GameFilesMissing
        : _lastExit?.Kind switch
        {
            GameExitKind.GpuFailure => HomeStatus.LastSessionGpuFailure,
            GameExitKind.NativeUnavailable => HomeStatus.NativeUnavailable,
            GameExitKind.Crashed => HomeStatus.LastSessionCrashed,
            _ => HomeStatus.Ready,
        };

    /// <summary>PLAY works whenever the exe and the prepared folders are there, also after a failed session.</summary>
    public bool CanPlay => Status is HomeStatus.Ready or HomeStatus.LastSessionGpuFailure or HomeStatus.NativeUnavailable or HomeStatus.LastSessionCrashed;

    /// <summary>
    /// Safe start is offered prominently, not only under "Trouble starting?", after a GPU failure, a
    /// crash or a game that could not be started at all.
    /// </summary>
    public bool SafeStartProminent =>
        Status is HomeStatus.LastSessionGpuFailure or HomeStatus.LastSessionCrashed || _startFailed && CanPlay;

    /// <summary>The crash report the game saved for the last session, when it crashed and one is on disk.</summary>
    public string? CrashReportPath => _lastExit?.CrashReportPath;

    /// <summary>The prepared folders PLAY points the game at, or null when they are not all there.</summary>
    public PreparedData? Prepared => preparation.Existing();

    /// <summary>One line under PLAY, in the player's terms.</summary>
    public string StatusLine => Status switch
    {
        HomeStatus.Ready => "Ready to play.",
        HomeStatus.LastSessionGpuFailure => "The last session stopped on the graphics card. You can play again.",
        HomeStatus.NativeUnavailable => "The game could not start.",
        HomeStatus.LastSessionCrashed => "The last session crashed. You can play again.",
        HomeStatus.GameRunning => "The game is running.",
        HomeStatus.GameExeMissing => $"{LaunchArguments.GameExeNotFound}. Put the Launcher in the game's folder.",
        _ => "The game files are missing. Set up the game again.",
    };

    /// <summary>
    /// The actions shown as labelled buttons beside PLAY, in order. When the game files are missing
    /// "Set up again" comes first; after a crash that left a report, "Open crash report" comes last.
    /// </summary>
    public IReadOnlyList<HomeAction> Actions
    {
        get
        {
            List<HomeAction> actions = [HomeAction.Settings, HomeAction.CreateShortcut, HomeAction.Help, HomeAction.OpenLogs];
            if (Status == HomeStatus.GameFilesMissing) actions.Insert(0, HomeAction.SetUpAgain);
            if (CrashReportPath is not null) actions.Add(HomeAction.OpenCrashReport);
            return actions;
        }
    }

    /// <summary>The recovery actions kept in the small "Trouble starting?" area.</summary>
    public IReadOnlyList<HomeAction> TroubleStartingActions { get; } = [HomeAction.SafeStart];

    /// <summary>The folder Open logs shows: the game's, which the Launcher writes beside (src/crash_report.cpp).</summary>
    public string? LogsFolder => GameExe is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(GameExe))!, "logs");

    /// <summary>How the last session ended, when the player should hear about it; null after a normal exit.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// Records how a game session ended: a normal exit clears the last failure, a GPU failure or a
    /// crash sets it, with the crash report the game saved in its logs folder when there is one.
    /// </summary>
    public void SessionEnded(int exitCode)
    {
        _running = false;
        _startFailed = false;
        var log = LogsFolder is { } folder ? GameExitReport.NewestSessionLog(folder) : null;
        _lastExit = GameExitReport.FromExit(exitCode, log);
        Note = _lastExit.Message;
    }

    /// <summary>The game was started from this window: PLAY is off until <see cref="SessionEnded"/>.</summary>
    public void GameStarted()
    {
        _running = true;
        _startFailed = false;
    }

    /// <summary>
    /// The game could not be started at all (the system refused to run it). That counts as a failed
    /// start: <paramref name="reason"/> goes in the note and Safe start moves up front.
    /// </summary>
    public void StartFailed(string reason)
    {
        _running = false;
        _startFailed = true;
        Note = "The game could not be started: " + reason;
    }

    /// <summary>Shows a line under the status, e.g. when an action could not be carried out.</summary>
    public void Say(string note) => Note = note;
}
