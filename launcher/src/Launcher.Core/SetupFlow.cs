namespace TopSpin4.Launcher.Core;

public enum SetupPhase
{
    /// <summary>The prepared files lack the disc, Title Update 1 or both; the player chooses what is missing.</summary>
    NeedsInputs,
    Preparing,
    /// <summary>The last preparation was refused or stopped; <see cref="SetupFlow.Message"/> says why.</summary>
    Failed,
    /// <summary>The prepared files hold everything; Play is available.</summary>
    Ready,
    /// <summary>
    /// The folder the install lives in can't be written to (ADR-0009); <see cref="SetupFlow.Message"/> says so
    /// and nothing can be set up or played from here.
    /// </summary>
    Refused,
}

/// <summary>
/// The Launcher's first-run setup as a model with no UI: two rows ("1 Disc", "2 Title Update 1"), each
/// holding the one file the player chose and the result of checking it, and how preparation went. Preparation ends
/// with the shaders band (<see cref="ShaderPreparation"/>) and the pipelines band (<see cref="PipelinePreparation"/>)
/// when the flow has them: "set up" then means the disc, Title Update 1, a usable shader pack and a pipeline store that
/// is not stale. Neither step disables Play when it fails: without a pack the game says so and ends, and without a store
/// the pipelines build during play.
/// A file is checked the moment it is chosen, and nothing is chosen for the player. The window only
/// draws it. It asks only for the input the prepared files lack, so a setup that stopped part way resumes.
/// </summary>
public sealed class SetupFlow
{
    public const string SetUpLabel = "Set up the game";
    public const string ResumeLabel = "Resume setup";

    private readonly Preparation _preparation;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly Action<string>? _readDiscArt;
    private readonly Action<string>? _readPreparedArt;
    private readonly ShaderPreparation? _shaders;
    private readonly PipelinePreparation? _pipelines;
    private readonly RowCheck _discCheck = new();
    private readonly RowCheck _titleUpdateCheck = new();
    private OverallProgress? _overall;
    private string? _stepLabel;
    private ShaderPackProblem _packProblem = ShaderPackProblem.None;
    private bool _stoppedAtShaders;
    private PipelineRebuild _pipelineRebuild = PipelineRebuild.None;
    private bool _repairPipelines;

    /// <param name="readDiscArt">
    /// Called with the disc image's path the moment the disc row accepts it, before the row reports
    /// the change, so that the art is there when the window redraws (<see cref="DiscArt.FromIso"/>). It
    /// can fail or throw without consequence: the failure is logged and the row stays accepted.
    /// </param>
    /// <param name="readPreparedArt">
    /// Called with the prepared disc folder once setup has finished and by <see cref="RefreshArt"/>, so the art is
    /// read again from there when the art folder is missing or incomplete (<see cref="DiscArt.RestoreFromFolder"/>).
    /// Like <paramref name="readDiscArt"/> it can fail or throw without consequence.
    /// </param>
    /// <param name="refusal">
    /// Why the install folder can't be used (<see cref="UserDirectories.Refusal"/>), or null. The flow then
    /// stays <see cref="SetupPhase.Refused"/> with this as its message: setup is disabled and the setup page
    /// shows, even when the prepared files are there.
    /// </param>
    /// <param name="shaders">
    /// The shader step that ends setup, or null for a flow without one (the disc and Title Update 1 alone are then
    /// "set up"). With it, prepared files that lack a usable shader pack are not done yet: the flow shows the setup page
    /// until the pack is built (<see cref="WantsRebuild"/> says when that needs no click).
    /// </param>
    public SetupFlow(Preparation preparation, TimeProvider? time = null, Action<string>? log = null, Action<string>? readDiscArt = null,
        Action<string>? readPreparedArt = null, string? refusal = null, ShaderPreparation? shaders = null,
        PipelinePreparation? pipelines = null)
    {
        _preparation = preparation;
        _time = time ?? TimeProvider.System;
        _log = log;
        _readDiscArt = readDiscArt;
        _readPreparedArt = readPreparedArt;
        _shaders = shaders;
        _pipelines = pipelines;
        Prepared = refusal is null ? preparation.Existing() : null;
        _packProblem = PackProblem();
        _pipelineRebuild = PipelineCheck();
        Phase = refusal is not null ? SetupPhase.Refused
            : Prepared is null || _packProblem != ShaderPackProblem.None || _pipelineRebuild.Rebuilds() ? SetupPhase.NeedsInputs
            : SetupPhase.Ready;
        Message = refusal;
        Disc = Fresh(preparation.NeedsDisc);
        TitleUpdate = Fresh(preparation.NeedsTitleUpdate);
    }

    public SetupPhase Phase { get; private set; }
    /// <summary>Row 1: the disc image.</summary>
    public SetupRow Disc { get; private set; }
    /// <summary>Row 2: Title Update 1.</summary>
    public SetupRow TitleUpdate { get; private set; }
    public PreparedData? Prepared { get; private set; }
    /// <summary>Why the last preparation failed or stopped, in the player's terms.</summary>
    public string? Message { get; private set; }
    /// <summary>The technical text behind <see cref="Message"/>, shown only behind "Details".</summary>
    public string? MessageDetails { get; private set; }
    public SetupError? Error { get; private set; }

    /// <summary>
    /// Why the shader step failed on the last preparation, in the player's terms (it also says how to try again);
    /// null when it ran well or did not run. A failure here does not disable Play.
    /// </summary>
    public string? ShaderFailure { get; private set; }

    /// <summary>
    /// Why the pipeline step failed on the last preparation, in the player's terms; null when it ran well or did not run.
    /// A failure here does not stop Play: the pipelines build during play.
    /// </summary>
    public string? PipelineFailure { get; private set; }

    /// <summary>The shaders XenosRecomp cannot convert, left out of the pack on the last preparation (the game skips draws that need one).</summary>
    public IReadOnlyList<string> ShadersSkipped { get; private set; } = [];

    /// <summary>
    /// The prepared files are all there but the shader pack is missing, damaged or was built by another XenosRecomp patch
    /// series, or the pipelines need a rebuild (<see cref="PipelinePreparation.Check"/>): the window starts
    /// <see cref="PrepareAsync"/> by itself when it opens, so no click is needed. A step stopped or failed last time is
    /// tried once per start; if it fails again, the flow ends Ready with the failure shown, so Play is never lost to it.
    /// </summary>
    public bool WantsRebuild =>
        Phase == SetupPhase.NeedsInputs && Prepared is not null && CanPrepare
        && (_shaders is not null && _packProblem != ShaderPackProblem.None || _pipelineRebuild.Rebuilds());
    public SetupProgress? Progress { get; private set; }

    /// <summary>One value for all of setup, 0 to 1, that never goes back.</summary>
    public double ProgressFraction => _overall?.Fraction ?? 0;

    /// <summary>The progress bar can't say how far along it is (a check, or starting).</summary>
    public bool ProgressIsIndeterminate => _overall?.IsIndeterminate ?? true;

    /// <summary>One line under the progress bar: the step, how far all of setup has come and the time left.</summary>
    public string ProgressText
    {
        get
        {
            var step = _stepLabel ?? "Starting";
            if (ProgressIsIndeterminate) return step;
            var line = $"{step}  {(int)(ProgressFraction * 100)}%";
            if (_overall?.TimeLeft is { } left) line += ", " + SetupProgress.TimeLeftText(left);
            return line;
        }
    }

    /// <summary>Raised on whichever thread changed the flow; the window marshals it.</summary>
    public event Action? Changed;

    /// <summary>Setup can start: the flow is idle and both rows are accepted.</summary>
    public bool CanPrepare =>
        Phase is SetupPhase.NeedsInputs or SetupPhase.Failed && Disc.Accepted && TitleUpdate.Accepted;

    /// <summary>A stopped or failed setup left files that setup resumes from.</summary>
    public bool CanStartOver => Phase is not (SetupPhase.Preparing or SetupPhase.Ready or SetupPhase.Refused) && _preparation.HasPartial;

    /// <summary>"Resume setup" while earlier files are waiting or a setup stopped at the shaders or pipelines, else "Set up the game".</summary>
    public string PrimaryLabel => CanStartOver || _stoppedAtShaders ? ResumeLabel : SetUpLabel;

    /// <summary>
    /// Setup is running, so the rows can't be changed. A file that is only being checked does not
    /// make the flow busy: choosing again cancels that check.
    /// </summary>
    public bool Busy => Phase == SetupPhase.Preparing;

    /// <summary>Checks the disc image the player chose and shows the result on row 1; null empties the row.</summary>
    public Task ChooseDisc(string? path) =>
        Choose(path, _discCheck, row => Disc = row, Disc.Needed, CheckDiscAndReadArt, "Disc image");

    // The disc is checked, and once it is accepted its art is read from the image. The art is a nicety:
    // whatever goes wrong there is logged and never turns an accepted disc away.
    private InputCheck CheckDiscAndReadArt(string? path, CancellationToken cancel)
    {
        var result = _preparation.CheckDisc(path, cancel);
        if (!result.Accepted || _readDiscArt is null || path is null || cancel.IsCancellationRequested) return result;
        try
        {
            _readDiscArt(path);
        }
        catch (Exception problem)
        {
            _log?.Invoke($"Disc art: could not read the art from {path}: {problem.Message}");
        }
        return result;
    }

    /// <summary>Checks the Title Update 1 file the player chose and shows the result on row 2; null empties the row.</summary>
    public Task ChooseTitleUpdate(string? path) =>
        Choose(path, _titleUpdateCheck, row => TitleUpdate = row, TitleUpdate.Needed, _preparation.CheckTitleUpdate, "Title Update 1");

    private async Task Choose(string? path, RowCheck running, Action<SetupRow> set, bool needed,
        Func<string?, CancellationToken, InputCheck> check, string name)
    {
        if (Phase == SetupPhase.Preparing || !needed) return;
        // A newer choice cancels the check still running for this row; that check's own call disposes its source.
        running.Source?.Cancel();
        if (string.IsNullOrWhiteSpace(path))
        {
            set(new SetupRow());
            Changed?.Invoke();
            return;
        }
        using var mine = new CancellationTokenSource();
        running.Source = mine;
        set(new SetupRow(RowState.Checking, path));
        Changed?.Invoke();
        InputCheck result;
        try
        {
            result = await Task.Run(() => check(path, mine.Token), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (running.Source == mine) running.Source = null;
        }
        if (mine.IsCancellationRequested) return;
        set(Row(path, result));
        if (!result.Accepted) _log?.Invoke($"{name}: {path} was refused: {result.Reason} {result.Details}");
        Changed?.Invoke();
    }

    private static SetupRow Row(string path, InputCheck result) =>
        result.Accepted
            ? new SetupRow(RowState.Accepted, path, result.Summary)
            : new SetupRow(RowState.Rejected, path, null, result.Reason, result.Details);

    private static SetupRow Fresh(bool needed) =>
        needed ? new SetupRow() : new SetupRow(RowState.Accepted, null, "Already set up", Needed: false);

    /// <summary>Prepares what is missing; ends Ready, Failed or, when stopped, NeedsInputs with the files kept for a resume.</summary>
    public async Task PrepareAsync(CancellationToken cancel)
    {
        if (!CanPrepare) return;
        Phase = SetupPhase.Preparing;
        Message = null;
        MessageDetails = null;
        Error = null;
        ShaderFailure = null;
        PipelineFailure = null;
        ShadersSkipped = [];
        _stoppedAtShaders = false;
        Progress = null;
        _stepLabel = null;
        var shadersWanted = _shaders is not null && PackProblem() != ShaderPackProblem.None;
        // A pack built now changes what the pipelines are built from, so the pipelines band follows it.
        var pipelinesWanted = _pipelines is not null && (shadersWanted || PipelineCheck().Rebuilds());
        _overall = new OverallProgress(_preparation, Disc.Needed, TitleUpdate.Needed, _time, shadersWanted, pipelinesWanted);
        Changed?.Invoke();
        var progress = new SyncProgress(report =>
        {
            Progress = report;
            _stepLabel = StepLabel(report.Step);
            _overall.Update(report);
            Changed?.Invoke();
        });
        try
        {
            var prepared = await Task.Run(() => _preparation.Prepare(Disc.Path, TitleUpdate.Path, progress, cancel), CancellationToken.None);
            Prepared = prepared;
            if (shadersWanted)
            {
                // Stopping here keeps the disc and Title Update 1; resuming starts at the shaders.
                _stoppedAtShaders = true;
                await Task.Run(() => RunShaders(prepared, progress, cancel), CancellationToken.None);
                _stoppedAtShaders = false;
            }
            if (pipelinesWanted && PipelineCheck().Rebuilds())
            {
                _stoppedAtShaders = true;
                await RunPipelines(progress, cancel);
                _stoppedAtShaders = false;
            }
            if (shadersWanted || pipelinesWanted)
            {
                Message = ShaderFailure ?? PipelineFailure ?? (ShadersSkipped.Count > 0
                    ? $"Set up. {ShadersSkipped.Count} shaders could not be converted and are left out of the shader pack; " +
                      "on the Native renderer, what they draw may be missing."
                    : null);
            }
            Phase = SetupPhase.Ready;
            await Task.Run(RefreshArt, CancellationToken.None);
        }
        catch (SetupException problem)
        {
            Phase = SetupPhase.Failed;
            Error = problem.Error;
            Message = problem.Reason;
            MessageDetails = problem.Details;
            _log?.Invoke("Setup failed: " + problem.Message);
            MarkRow(problem);
        }
        catch (OperationCanceledException)
        {
            Phase = SetupPhase.NeedsInputs;
            Message = "Stopped. Resume setup to carry on where it left off, or start over.";
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Phase = SetupPhase.Failed;
            Message = "Setup failed: " + problem.Message;
            MessageDetails = problem.GetType().Name;
            _log?.Invoke("Setup failed: " + problem);
        }
        _overall = null;
        Progress = null;
        _packProblem = PackProblem();
        _pipelineRebuild = PipelineCheck();
        RefreshNeeded();
        Changed?.Invoke();
    }

    /// <summary>
    /// After a play session: when the game left shaders the pack lacks in the top-up folder (and did not convert them live),
    /// the pack is built again with them, then the pipelines, with the progress bar. A shader is then converted live at most
    /// once per install. A failure is shown like setup's and Play stays available; nothing happens otherwise.
    /// </summary>
    public async Task AddTopUpsAsync(CancellationToken cancel)
    {
        if (_shaders is null || Prepared is not { } prepared || Phase != SetupPhase.Ready ||
            !_shaders.StatusOf(_preparation.ShaderCacheDirectory).AddsAfterSession)
            return;
        Phase = SetupPhase.Preparing;
        Message = null;
        MessageDetails = null;
        ShaderFailure = null;
        PipelineFailure = null;
        ShadersSkipped = [];
        _stepLabel = null;
        _overall = new OverallProgress(_preparation, false, false, _time, needShaders: true, needPipelines: _pipelines is not null);
        Changed?.Invoke();
        var progress = new SyncProgress(report =>
        {
            Progress = report;
            _stepLabel = StepLabel(report.Step);
            _overall.Update(report);
            Changed?.Invoke();
        });
        try
        {
            await Task.Run(() => RunShaders(prepared, progress, cancel), CancellationToken.None);
            if (_pipelines is not null && PipelineCheck().Rebuilds()) await RunPipelines(progress, cancel);
        }
        catch (OperationCanceledException)
        {
            // The top-ups stay in their folder; the next session's end tries again.
        }
        Message = ShaderFailure ?? PipelineFailure;
        Phase = SetupPhase.Ready;
        _overall = null;
        Progress = null;
        _packProblem = PackProblem();
        _pipelineRebuild = PipelineCheck();
        Changed?.Invoke();
    }

    private PipelineRebuild PipelineCheck() =>
        _pipelines?.Check(PackProblem() == ShaderPackProblem.None, _repairPipelines) ?? PipelineRebuild.None;

    // The pipeline step, as one band of the overall progress. A failure is shown (PipelineFailure) and never throws; a cancel
    // is the setup's own cancel.
    private async Task RunPipelines(IProgress<SetupProgress> progress, CancellationToken cancel)
    {
        const long Scale = 1000;
        void Report(double fraction) =>
            progress.Report(new SetupProgress(SetupStep.PreparingPipelines, (long)(Math.Clamp(fraction, 0, 1) * Scale), Scale, null, null));
        Report(0);
        var result = await Task.Run(() => _pipelines!.RunAsync(new ProgressRelay<PipelineProgress>(report => Report(report.Fraction)), cancel),
            CancellationToken.None);
        if (result.Usable)
        {
            _repairPipelines = false;
            if (result.Outcome == PipelineOutcome.Partial) _log?.Invoke("Pipelines: " + result.Message);
        }
        else
        {
            PipelineFailure = result.Message + " Play still starts the game; the pipelines build while it runs.";
            _log?.Invoke("Pipeline step failed: " + result.Message);
        }
        Report(1);
    }

    private ShaderPackProblem PackProblem() =>
        _shaders is null ? ShaderPackProblem.None : _shaders.StatusOf(_preparation.ShaderCacheDirectory).Pack.Problem;

    // The shader step, as one band of the overall progress. A failure is shown (ShaderFailure) and never throws: the files are
    // set up, and the next Launcher start tries again. A cancel is the setup's own cancel.
    private void RunShaders(PreparedData prepared, IProgress<SetupProgress> progress, CancellationToken cancel)
    {
        const long Scale = 1000;
        void Report(double fraction) =>
            progress.Report(new SetupProgress(SetupStep.PreparingShaders, (long)(Math.Clamp(fraction, 0, 1) * Scale), Scale, null, null));
        Report(0);
        try
        {
            if (_preparation.FreeBytes < Preparation.ShaderPackBytes)
                throw new ShaderPreparationException(ShaderPreparation.Failed(
                    $"there is not enough disk space for the shader pack: it needs about {Preparation.ShaderPackBytes >> 20} MB free on " +
                    $"{Path.GetPathRoot(_preparation.ShaderCacheDirectory)}. Free some space, then set up again"));
            var result = _shaders!.Run(prepared.GameDataRoot, _preparation.ShaderCacheDirectory,
                new ProgressRelay<ShaderProgress>(report => Report(report.Fraction)), cancel);
            ShadersSkipped = result.Skipped;
            if (result.Skipped.Count > 0)
                _log?.Invoke($"Shaders: the pack holds {result.Converted}; XenosRecomp cannot convert {result.Skipped.Count}: {string.Join(", ", result.Skipped)}");
        }
        catch (ShaderPreparationException problem)
        {
            ShaderFailure = problem.Message;
            MessageDetails = problem.InnerException?.Message;
            _log?.Invoke("Shader step failed: " + problem.Message + (problem.InnerException is { } inner ? " " + inner.Message : ""));
        }
        Report(1);
    }

    /// <summary>
    /// Throws away the shader pack and the pipeline record so the next preparation builds both again (the Launcher's
    /// Repair). The flow goes back to the setup page with <see cref="WantsRebuild"/> set, so the window rebuilds without a click.
    /// Does nothing without a shader step, without prepared files or while setup runs.
    /// </summary>
    public void Repair()
    {
        if (_shaders is null || Prepared is null || Phase is SetupPhase.Preparing or SetupPhase.Refused) return;
        try
        {
            File.Delete(Path.Combine(_preparation.ShaderCacheDirectory, ShaderPackFile.FileName));
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke("Repair: could not remove the shader pack: " + problem.Message);
        }
        _pipelines?.Forget();
        _repairPipelines = _pipelines is not null;
        _packProblem = ShaderPackProblem.Damaged;
        _pipelineRebuild = PipelineCheck();
        Phase = SetupPhase.NeedsInputs;
        Message = null;
        MessageDetails = null;
        ShaderFailure = null;
        PipelineFailure = null;
        Changed?.Invoke();
    }

    // An input that failed its check again at setup (it changed, moved or went away since it was accepted) marks its own row.
    private void MarkRow(SetupException problem)
    {
        SetupRow Rejected(SetupRow row) =>
            row with { State = RowState.Rejected, Summary = null, Reason = problem.Reason, Details = problem.Details };
        switch (problem.Error)
        {
            case SetupError.ImageNotFound or SetupError.NotAnXboxImage or SetupError.WrongDisc:
                Disc = Rejected(Disc);
                break;
            case SetupError.NoTitleUpdate or SetupError.WrongTitleUpdate:
                TitleUpdate = Rejected(TitleUpdate);
                break;
        }
    }

    // After a preparation, an input the prepared files now hold needs no choice any more.
    private void RefreshNeeded()
    {
        if (!_preparation.NeedsDisc && Disc.Needed) Disc = Fresh(false);
        if (!_preparation.NeedsTitleUpdate && TitleUpdate.Needed) TitleUpdate = Fresh(false);
    }

    /// <summary>
    /// Brings a flow that finished setup back to its input rows because the prepared files went away
    /// (Home's "Set up again"). Only what the prepared files now lack is asked for; the rest shows as
    /// already set up. Does nothing while the files are all there or setup is running.
    /// </summary>
    public void SetUpAgain()
    {
        if (Phase is SetupPhase.Preparing or SetupPhase.Refused || _preparation.Existing() is not null) return;
        Prepared = null;
        Phase = SetupPhase.NeedsInputs;
        Message = null;
        MessageDetails = null;
        Error = null;
        Disc = Fresh(_preparation.NeedsDisc);
        TitleUpdate = Fresh(_preparation.NeedsTitleUpdate);
        Changed?.Invoke();
    }

    /// <summary>
    /// Reads the art again from the prepared disc folder if the art folder is missing or incomplete. Setup does it when it
    /// has finished, and the window does it at start. Without prepared files it does nothing; a failure is logged, never thrown.
    /// </summary>
    public void RefreshArt()
    {
        if (_readPreparedArt is null || Prepared is not { } prepared) return;
        try
        {
            _readPreparedArt(prepared.GameDataRoot);
        }
        catch (Exception problem)
        {
            _log?.Invoke($"Disc art: could not read the art from {prepared.GameDataRoot}: {problem.Message}");
        }
    }

    /// <summary>Shows a line on the setup page, e.g. when an action outside setup could not be carried out.</summary>
    public void Notice(string note)
    {
        Message = note;
        MessageDetails = null;
        Changed?.Invoke();
    }

    /// <summary>Throws away what a stopped or failed setup wrote, so the next setup starts from nothing. The chosen files stay.</summary>
    public void StartOver()
    {
        if (!CanStartOver) return;
        _preparation.DiscardPartial();
        Message = null;
        MessageDetails = null;
        Error = null;
        Phase = SetupPhase.NeedsInputs;
        Changed?.Invoke();
    }

    private static string StepLabel(SetupStep step) => step switch
    {
        SetupStep.CheckingDisc => "Checking the disc image",
        SetupStep.CheckingTitleUpdate => "Checking Title Update 1",
        SetupStep.CheckingExistingFiles => "Checking the files of an earlier setup",
        SetupStep.Extracting => "Extracting the disc",
        SetupStep.UnpackingTitleUpdate => "Unpacking Title Update 1",
        SetupStep.PreparingShaders => "Preparing shaders",
        SetupStep.PreparingPipelines => "Preparing pipelines",
        _ => "Done",
    };

    /// <summary>The check still running for one row, so a newer choice can cancel it.</summary>
    private sealed class RowCheck
    {
        public CancellationTokenSource? Source;
    }
}
