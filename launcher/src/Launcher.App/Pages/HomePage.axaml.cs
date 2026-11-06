using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Pages;

/// <summary>The home page: draws <see cref="HomeModel"/> and the <see cref="Showcase"/> cards, and reports which button was pressed.</summary>
public partial class HomePage : UserControl
{
    private const int FacesShown = 5;
    private const int IconsShown = 16;
    private const double WideEnoughForNotes = 1000;
    private const double TallEnough = 620;

    private readonly HomeModel _home;
    private readonly Dictionary<HomeAction, Button> _buttons = [];
    private readonly DispatcherTimer _venueTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private IReadOnlyList<HomeAction>? _shownActions;
    private IReadOnlyList<(IImage Image, string Name)> _venues = [];
    private int _venue;

    public HomePage() : this(new HomeModel(new Preparation(Path.GetTempPath()), null)) { }

    public HomePage(HomeModel home)
    {
        InitializeComponent();
        _home = home;
        TroubleHeading.Text = HomeModel.TroubleStartingHeading;
        WhatsNewList.ItemsSource = Showcase.WhatsNew.Select(line => "•  " + line).ToList();
        PlayButton.Click += (_, _) => PlayPressed?.Invoke();
        AllAchievementsButton.Click += (_, _) => AchievementsPressed?.Invoke();
        foreach (var action in Enum.GetValues<HomeAction>()) Button(action);
        foreach (var action in home.TroubleStartingActions) TroubleHost.Children.Add(_buttons[action]);
        _venueTimer.Tick += (_, _) => NextVenue();
        SizeChanged += (_, _) => FitCards();
        AttachedToVisualTree += (_, _) => _venueTimer.Start();
        DetachedFromVisualTree += (_, _) => _venueTimer.Stop();
        ShowActions();
    }

    public event Action? PlayPressed;
    public event Action? AchievementsPressed;
    public event Action<HomeAction>? ActionPressed;

    /// <summary>The logo from the player's disc in the hero; null shows the Launcher's own wordmark.</summary>
    public void ShowLogo(IImage? logo)
    {
        LogoImage.Source = logo;
        LogoImage.IsVisible = logo is not null;
        Wordmark.IsVisible = logo is null;
    }

    /// <summary>The card row. <paramref name="picture"/> gives the drawn picture of an art file, or null when it cannot be drawn.</summary>
    public void ShowCards(Showcase showcase, Func<string, IImage?> picture)
    {
        PlayerFaces.Children.Clear();
        var faces = showcase.Players.Select(player => picture(player.ImagePath)).OfType<IImage>().Take(FacesShown).ToList();
        foreach (var face in faces) PlayerFaces.Children.Add(new Image { Source = face, Width = 96, Height = 96, Stretch = Stretch.Uniform });
        PlayersText.Text = showcase.Players.Count == 0 ? "" : string.Join(", ", showcase.Players.Take(4).Select(player => player.Title))
            + (showcase.Players.Count > 4 ? $" and {showcase.Players.Count - 4} more" : "");
        PlayersCard.IsVisible = faces.Count > 0;

        _venues = [.. showcase.Venues.Select(venue => (picture(venue.ImagePath), venue.Title)).Where(pair => pair.Item1 is not null).Select(pair => (pair.Item1!, pair.Title))];
        _venue = 0;
        VenuesCard.IsVisible = _venues.Count > 0;
        ShowVenue();

        AchievementIcons.Children.Clear();
        foreach (var achievement in showcase.Achievements.Take(IconsShown))
            if (picture(achievement.ImagePath) is { } icon)
                AchievementIcons.Children.Add(new Image { Source = icon, Width = 40, Height = 40, Margin = new(0, 0, 4, 4) });
        AchievementsText.Text = showcase.AchievementsSummary;
        AchievementsCard.IsVisible = showcase.Achievements.Count > 0;
        FitCards();
    }

    // The cards share the row's width; a card with nothing to show leaves no gap, and a narrow window drops the release
    // notes so the other cards keep room for their pictures.
    private void FitCards()
    {
        WhatsNewCard.IsVisible = Bounds.Width == 0 || Bounds.Width >= WideEnoughForNotes;
        CardsHost.Columns = Math.Max(1, CardsHost.Children.Count(card => card.IsVisible));
        // A short window draws the logo and the cards smaller, so PLAY and the cards both stay whole.
        var compact = Bounds.Height is > 0 and < TallEnough;
        LogoImage.Height = compact ? 76 : 120;
        Wordmark.FontSize = compact ? 52 : 76;
        Hero.Margin = compact ? new(44, 18, 44, 12) : new(44, 36, 44, 20);
        foreach (var card in CardsHost.Children.OfType<Border>()) card.Height = compact ? 168 : 196;
    }

    private void NextVenue()
    {
        if (_venues.Count < 2 || !IsVisible) return;
        VenueImage.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            _venue = (_venue + 1) % _venues.Count;
            ShowVenue();
            VenueImage.Opacity = 1;
        }, TimeSpan.FromMilliseconds(400));
    }

    private void ShowVenue()
    {
        if (_venues.Count == 0) return;
        VenueImage.Source = _venues[_venue].Image;
        VenueText.Text = _venues[_venue].Name;
    }

    public Control EntryControl => PlayButton.IsEnabled ? PlayButton : _buttons[_home.Actions[0]];

    private static (string Label, string Icon) Look(HomeAction action) => action switch
    {
        HomeAction.SetUpAgain => ("Set up again", Icons.Again),
        HomeAction.Settings => ("Settings", Icons.Settings),
        HomeAction.CreateShortcut => ("Shortcut", Icons.Shortcut),
        HomeAction.OpenCrashReport => ("Crash report", Icons.Warning),
        HomeAction.Help => ("Help", Icons.Help),
        HomeAction.OpenLogs => ("Logs", Icons.Folder),
        HomeAction.SafeStart => ("Safe start", Icons.Shield),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "A home action with no label"),
    };

    // A tool beside PLAY: an icon over its label. Safe start is a plain button: it sits in the trouble card or under the note.
    private Button Button(HomeAction action)
    {
        var (label, icon) = Look(action);
        var button = new Button();
        AutomationProperties.SetName(button, label);
        if (action == HomeAction.SafeStart)
        {
            button.Content = label;
            button.Classes.Add("secondary");
            ToolTip.SetTip(button, "Runs once on the original settings (original look and resolution) and leaves your Settings file unchanged.");
        }
        else
        {
            button.Content = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Icons.Make(icon, 26), new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center } },
            };
            button.Classes.Add("tool");
        }
        button.Click += (_, _) => ActionPressed?.Invoke(action);
        _buttons[action] = button;
        return button;
    }

    /// <summary>Draws the status; the status is read from the disk each time, so a missing exe shows when the window opens.</summary>
    public void Refresh()
    {
        PlayButton.IsEnabled = _home.CanPlay;
        StatusText.Text = _home.StatusLine;
        NoteText.Text = _home.Note;
        NoteText.IsVisible = _home.Note is not null;
        ShowActions();
        var safe = _buttons[HomeAction.SafeStart];
        safe.IsEnabled = _home.CanPlay;
        // Safe start is the way back into the game after a failure: it moves up under the note.
        var host = _home.SafeStartProminent ? ProminentHost : TroubleHost;
        if (safe.Parent != host)
        {
            (safe.Parent as Panel)?.Children.Remove(safe);
            host.Children.Add(safe);
            if (_home.SafeStartProminent) safe.Classes.Add("attention");
            else safe.Classes.Remove("attention");
        }
    }

    // The tools; the crash report joins them after a crash. Rebuilt only when the list changes, so focus stays put.
    private void ShowActions()
    {
        var wanted = _home.Actions;
        if (_shownActions is not null && _shownActions.SequenceEqual(wanted)) return;
        _shownActions = wanted;
        ActionsHost.Children.Clear();
        foreach (var action in wanted) ActionsHost.Children.Add(_buttons[action]);
    }

    /// <summary>PLAY answers Enter only while this page is the one on show.</summary>
    public void SetDefault(bool isDefault) => PlayButton.IsDefault = isDefault;

    public void FocusFirst() => EntryControl.Focus(NavigationMethod.Tab);
}
