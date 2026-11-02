// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameHome.Core.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using GameHome.Data;
using GameHome.Sources.Moonlight;
using GameHome.Sources.Steam;
using GameHome.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Gaming.Input;
using Windows.Storage;
using VirtualKey = Windows.System.VirtualKey;

namespace GameHome.App;

/// <summary>
/// Home for fase 1: spill lagt til manuelt, start, «Kjører nå» og retur til samme flis. Kontrolleren leses med
/// Windows.Gaming.Input bare mens vinduet har fokus. Hurtigtasten for øktmenyen er registrert bare under en økt.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MaxVisibleEvents = 10;
    private const double StickThreshold = 0.6;
    private const double TriggerThreshold = 0.5;
    private const string LegionGoController = "vid_17ef&pid_61eb";
    private static readonly TimeSpan HidCaptureDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(10);

    // M2 er lagt om til Ctrl+Alt+Shift+F12 i Legion Space. De andre er reserver.
    private static readonly (int Id, HotkeyModifiers Modifiers, uint Key, string Name)[] SessionHotkeys =
    [
        (1, HotkeyModifiers.None, 0x7C, "F13"),
        (2, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7B, "Ctrl+Alt+Shift+F12"),
        (3, HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B, "Ctrl+Shift+F12"),
    ];

    private readonly FileLog _log;
    private readonly string _libraryPath;
    private readonly GameLibrary _library;
    private static readonly TimeSpan SteamSyncInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LoadingPatience = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CoveredLoadingLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StreamCoverLimit = TimeSpan.FromSeconds(45);

    private readonly Dictionary<string, IRunner> _runners = new()
    {
        [GameLibrary.ExecutableRunnerId] = new ExecutableRunner(),
        [GameLibrary.SteamRunnerId] = new SteamRunner(),
        [GameLibrary.MoonlightRunnerId] = new MoonlightRunner(),
    };
    private readonly List<string> _events = [];
    private readonly DispatcherQueueTimer _gamepadTimer;
    private readonly DispatcherQueueTimer _metricsTimer;
    private readonly HotkeyListener _hotkeys;
    private readonly List<HidReportListener> _hidListeners = [];

    private IForegroundSession? _session;
    private DateTime _lastSteamSync = DateTime.MinValue;
    private bool _steamSyncRunning;
    private DispatcherQueueTimer? _firstSteamSync;
    private DispatcherQueueTimer? _loadingTimer;
    private DispatcherQueueTimer? _streamCover;
    private DateTime _loadingShown;
    private bool _loadingCovered;
    private bool _windowActive;
    private Game? _sessionGame;
    private DateTime _sessionStarted;
    private string? _focusGameId;
    private string? _rememberedTile;
    private Action? _messagePrimary;
    private Action? _messageSecondary;
    private Action? _messageCancel;

    private GamepadButtons _previousButtons;
    private bool _previousLeftTrigger;
    private bool _previousRightTrigger;
    private FocusNavigationDirection? _previousStick;
    private string _lastSource = "ukjent";
    private bool _xamlGamepadKeysSeen;
    private bool _startupLogged;

    public MainWindow(FileLog log)
    {
        _log = log;
        InitializeComponent();
        _log.Write("start", "XAML lastet");
        ExtendsContentIntoTitleBar = true;

        _log.Written += OnLogWritten;

        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        Activated += OnWindowActivated;
        Closed += (_, _) => StopEverything();

        _gamepadTimer = DispatcherQueue.CreateTimer();
        _gamepadTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gamepadTimer.Tick += (_, _) => PollGamepad();

        _metricsTimer = DispatcherQueue.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(2);
        _metricsTimer.Tick += (_, _) => UpdateMemory();

        _hotkeys = new HotkeyListener(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _hotkeys.Pressed += OnSessionHotkey;

        Gamepad.GamepadAdded += (_, _) => _log.Write("kontroller", "Kontroller tilkoblet");
        Gamepad.GamepadRemoved += (_, _) => _log.Write("kontroller", "Kontroller frakoblet");

        _libraryPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "library.json");
        _library = LoadLibrary();
        BuildGameTiles();

        var variant = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT";
        InfoText.Text = $"Versjon {typeof(MainWindow).Assembly.GetName().Version} · {variant}";
        HostTile.Content = $"Spill fra {HostLabel}";
        _log.Write("start", "Vindu klart");
    }

    private bool SessionActive => _session is not null && !_session.Completion.IsCompleted;

    /// <summary>Navnet på gaming-PC-en slik GameHome viser det, for eksempel «Gaming Laptop».</summary>
    private static string HostLabel => MoonlightClient.FindHosts() is [var host, ..] ? host.DisplayName : "gaming-PC-en";

    /// <summary>
    /// Henter vinduet frem når en ny aktivering sendes hit, for eksempel Home i Game Bar eller <c>gamehome://session</c>.
    /// </summary>
    internal void BringToFront(string via)
    {
        Activate();
        var ok = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _log.Write("fokus", ok ? "Tok forgrunnen" : "Windows avslo forgrunnen");
        if (SessionActive)
        {
            ShowSessionPanel(via);
        }
    }

    // ---- Bibliotek ----

    private GameLibrary LoadLibrary()
    {
        try
        {
            var library = LibraryStore.Load(_libraryPath);
            _log.Write("bibliotek", $"Lest {library.Games.Count} spill fra library.json");
            return library;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Et ødelagt bibliotek skal aldri stoppe Home. Filen beholdes for feilsøking.
            _log.Write("bibliotek", $"Kunne ikke lese library.json: {ex.Message}");
            return new GameLibrary();
        }
    }

    private void SaveLibrary()
    {
        try
        {
            LibraryStore.Save(_library, _libraryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("bibliotek", $"Kunne ikke lagre library.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Bringer flisene i takt med biblioteket uten å fjerne fliser som fortsatt finnes. Å tømme og lage alle flisene
    /// på nytt mens en av dem hadde fokus, krasjet appen rett etter oppstart (0.1.27.0 og 0.1.28.0).
    /// </summary>
    private const string RememberedTileSetting = "home.lastTile";

    /// <summary>Husker flisen med fokus. Lagres i appens lokale innstillinger; skrives bare når fokus endres.</summary>
    private void RememberTile(string gameId)
    {
        // Mens et panel er åpent, kan XAML flytte fokus mellom flisene på egen hånd; det skal ikke huskes.
        if (MessagePanel.Visibility == Visibility.Visible || SessionPanel.Visibility == Visibility.Visible
            || HostPanel.Visibility == Visibility.Visible || PowerPanel.Visibility == Visibility.Visible
            || LoadingPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        _focusGameId = gameId;
        if (_rememberedTile == gameId)
        {
            return;
        }

        _rememberedTile = gameId;
        try
        {
            ApplicationData.Current.LocalSettings.Values[RememberedTileSetting] = gameId;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("fokus", $"Kunne ikke huske flisen: {ex.Message}");
        }
    }

    private string? LoadRememberedTile()
    {
        try
        {
            _rememberedTile = ApplicationData.Current.LocalSettings.Values[RememberedTileSetting] as string;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("fokus", $"Kunne ikke lese husket flis: {ex.Message}");
        }

        return _rememberedTile;
    }

    private void BuildGameTiles()
    {
        var visible = _library.VisibleGames;
        var ids = visible.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        for (var i = GameTiles.Children.Count - 1; i >= 0; i--)
        {
            if (GameTiles.Children[i] is not Button { Tag: string id } || !ids.Contains(id))
            {
                GameTiles.Children.RemoveAt(i);
            }
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var existing = TileFor(visible[i].Id);
            if (existing is null)
            {
                GameTiles.Children.Insert(i, CreateTile(visible[i]));
            }
            else
            {
                existing.Content = TileText(visible[i]);
            }
        }
    }

    private Button CreateTile(Game game)
    {
        var tile = new Button
        {
            Content = TileText(game),
            Tag = game.Id,
            Width = 280,
            Height = 160,
            FontSize = 22,
        };
        tile.Click += GameTile_Click;
        tile.GotFocus += (_, _) => RememberTile(game.Id);
        return tile;
    }

    private string TileText(Game game) =>
        SessionActive && _sessionGame?.Id == game.Id ? $"▶ {GameLibrary.DisplayTitle(game)}" : GameLibrary.DisplayTitle(game);

    private void RefreshTileTexts()
    {
        foreach (var tile in GameTiles.Children.OfType<Button>())
        {
            if (tile.Tag is string id && _library.Find(id) is { } game)
            {
                tile.Content = TileText(game);
            }
        }
    }

    private Button? TileFor(string? gameId) =>
        GameTiles.Children.OfType<Button>().FirstOrDefault(b => b.Tag is string id && id == gameId);

    /// <summary>
    /// Gir flisen fokus. En nylaget flis får fokus først når den er lastet: fokus og scrolling på et element som ikke
    /// er lagt ut ennå, krasjet appen ved oppstart (0.1.27.0, E_INVALIDARG i XAML). ScrollViewer scroller selv til
    /// elementet med fokus.
    /// </summary>
    private void FocusHome(string? gameId)
    {
        var target = TileFor(gameId) ?? GameTiles.Children.OfType<Button>().FirstOrDefault() ?? AddGameTile;
        if (target.IsLoaded)
        {
            target.Focus(FocusState.Keyboard);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= OnLoaded;
            if (MessagePanel.Visibility != Visibility.Visible && SessionPanel.Visibility != Visibility.Visible)
            {
                target.Focus(FocusState.Keyboard);
            }
        }

        target.Loaded += OnLoaded;
    }

    private async void AddGameTile_Click(object sender, RoutedEventArgs e)
    {
        LogChoice(AddGameTile);
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            ViewMode = global::Windows.Storage.Pickers.PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.Write("bibliotek", $"Filvelgeren feilet: {ex.Message}");
            return;
        }

        if (file is null)
        {
            _log.Write("bibliotek", "Ingen fil valgt");
            AddGameTile.Focus(FocusState.Keyboard);
            return;
        }

        var (game, added) = _library.AddExecutable(file.Path);
        if (added)
        {
            SaveLibrary();
            BuildGameTiles();
        }

        _log.Write("bibliotek", added ? $"La til «{game.Title}» ({file.Path})" : $"«{game.Title}» finnes allerede");
        LastInput.Text = added ? $"La til {game.Title}. Trykk A for å starte." : $"{game.Title} finnes allerede.";
        FocusHome(game.Id);
    }

    private void ConfirmRemove(Game game)
    {
        var title = GameLibrary.DisplayTitle(game);
        ShowMessage(
            $"Fjerne {title}?",
            "Spillet fjernes bare fra GameHome. Ingen filer slettes.",
            "Fjern fra GameHome", () =>
            {
                _library.Remove(game.Id);
                SaveLibrary();
                BuildGameTiles();
                _log.Write("bibliotek", $"Fjernet «{title}»");
                FocusHome(null);
            },
            "Avbryt", () => FocusHome(game.Id));
    }

    // ---- Start og økt ----

    private void GameTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } tile && _library.Find(id) is { } game)
        {
            LogChoice(tile);
            _ = LaunchAsync(game);
        }
    }

    private async Task LaunchAsync(Game game, PlayAction? chosen = null)
    {
        if (SessionActive)
        {
            ShowSessionPanel("flis (økt kjører allerede)");
            return;
        }

        var title = GameLibrary.DisplayTitle(game);

        // Flere spillemåter: brukeren velger. B avbryter, så et feiltrykk aldri starter en stream.
        var available = _library.AvailableActionsFor(game.Id);
        if (chosen is null && available.Count > 1)
        {
            var local = available.First(a => !a.IsRemote);
            var remote = available.First(a => a.IsRemote);
            ShowMessage($"Spill {title}", "Velg hvordan du vil spille.",
                "Spill her", () => _ = LaunchAsync(game, local),
                $"Stream fra {HostLabel}", () => _ = LaunchAsync(game, remote),
                onCancel: () => FocusHome(game.Id));
            return;
        }

        var action = chosen ?? _library.DefaultPlayAction(game.Id);
        if (action is null)
        {
            ShowMessage($"{title} kan ikke startes", "Spillet har ingen måte å startes på.",
                "Fjern fra GameHome", () => ConfirmRemove(game), "Lukk", () => FocusHome(game.Id));
            return;
        }

        if (!_runners.TryGetValue(action.RunnerId, out var runner))
        {
            ShowMessage($"{title} kan ikke startes", $"GameHome kan ikke starte spill fra «{action.RunnerId}» ennå.",
                "Fjern fra GameHome", () => ConfirmRemove(game), "Lukk", () => FocusHome(game.Id));
            return;
        }

        ShowLoading(game, action);
        try
        {
            _session = (IForegroundSession)await runner.LaunchAsync(action, Profile.Default, CancellationToken.None);
        }
        catch (LaunchException ex)
        {
            HideLoading("start feilet");
            _log.Write("økt", $"Kunne ikke starte «{title}»: {ex.Failure}: {ex.Message}");
            if (ex.Failure == LaunchFailure.FileMissing)
            {
                ShowMessage($"Finner ikke {title}", $"Filen finnes ikke lenger:\n{action.Target}",
                    "Fjern fra GameHome", () => ConfirmRemove(game), "Lukk", () => FocusHome(game.Id));
            }
            else
            {
                ShowMessage($"{title} startet ikke", ex.Message,
                    "Prøv igjen", () => _ = LaunchAsync(game), "Lukk", () => FocusHome(game.Id));
            }

            return;
        }

        _sessionGame = game;
        _sessionStarted = DateTime.UtcNow;
        _focusGameId = game.Id;
        var session = _session;
        session.Note += (_, note) => _log.Write("økt", note);
        session.StatusChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(session, _session) && SessionPanel.Visibility == Visibility.Visible)
            {
                SessionDetail.Text = session.Status;
            }
        });
        _ = session.Completion.ContinueWith(
            t => DispatcherQueue.TryEnqueue(() => OnSessionEnded(session, t.Result)),
            TaskScheduler.Default);

        if (session is StreamSession stream)
        {
            StartStreamCover(stream);
        }

        var registered = SessionHotkeys.Where(h => _hotkeys.Register(h.Id, h.Modifiers, h.Key)).Select(h => h.Name).ToList();
        _log.Write("økt", $"Startet «{title}» ({action.RunnerId} {action.Target}, {session.Status}). Hurtigtaster: {string.Join(", ", registered)}");
        RefreshTileTexts();
        LastInput.Text = $"{title} starter. M2 åpner menyen.";
    }

    private void OnSessionEnded(IForegroundSession session, SessionEnd end)
    {
        if (!ReferenceEquals(session, _session))
        {
            return;
        }

        _hotkeys.UnregisterAll();
        HideLoading("økten avsluttet");
        var game = _sessionGame;
        var title = game is null ? "spillet" : GameLibrary.DisplayTitle(game);
        var minutes = (DateTime.UtcNow - _sessionStarted).TotalMinutes;
        _log.Write("økt", string.Create(CultureInfo.InvariantCulture,
            $"«{title}» avsluttet: {end.Reason}, kode {end.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "–"}, {minutes:F1} min{(end.Detail is null ? "" : $" ({end.Detail})")}"));

        session.Dispose();
        _session = null;
        _sessionGame = null;
        SessionPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        RefreshTileTexts();

        // Tilbake til samme flis.
        Activate();
        var ok = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _log.Write("fokus", ok ? "Tilbake i Home etter spillet" : "Windows avslo forgrunnen etter spillet");

        // Alltid flisen til spillet som nettopp sluttet, ikke den som tilfeldigvis fikk fokus mens menyene var åpne.
        if (game is not null)
        {
            RememberTile(game.Id);
        }

        FocusHome(game?.Id ?? _focusGameId);
        LastInput.Text = $"Velkommen tilbake fra {title}.";

        if (end.Reason == SessionEndReason.LaunchFailed && game is not null)
        {
            ShowMessage($"{title} startet ikke", end.Detail ?? "Spillet startet ikke.",
                "Prøv igjen", () => _ = LaunchAsync(game), "Lukk", () => FocusHome(game.Id));
        }
    }

    // ---- Steam ----

    /// <summary>
    /// Leser Steams bibliotekfiler på en bakgrunnstråd og oppdaterer Home. Aldri mens et spill kjører, og høyst
    /// én gang i minuttet.
    /// </summary>
    private void SyncSteamInBackground()
    {
        if (SessionActive || _steamSyncRunning || DateTime.UtcNow - _lastSteamSync < SteamSyncInterval)
        {
            return;
        }

        _steamSyncRunning = true;
        _lastSteamSync = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            var root = SteamClient.FindRoot();
            IReadOnlyList<StoreEntry>? entries = null;
            if (root is not null)
            {
                entries = SteamLibraryReader.Scan(root, message => _log.Write("steam", message));
            }

            DispatcherQueue.TryEnqueue(() => ApplySteamScan(root, entries));
        });
    }

    private void ApplySteamScan(string? root, IReadOnlyList<StoreEntry>? entries)
    {
        _steamSyncRunning = false;
        if (root is null || entries is null)
        {
            _log.Write("steam", "Steam er ikke installert");
            return;
        }

        var result = _library.SyncStore(GameLibrary.SteamRunnerId, entries);
        _log.Write("steam", $"Fant {entries.Count} installerte spill; {result.Added.Count} nye, {result.MarkedMissing} borte");
        if (!result.Changed)
        {
            return;
        }

        SaveLibrary();
        BuildGameTiles();
        _log.Write("steam", "Flisene oppdatert");
        var enabled = MessagePanel.Visibility != Visibility.Visible && SessionPanel.Visibility != Visibility.Visible;
        SetHomeEnabled(enabled);

        // Flisen med fokus finnes fortsatt; fokus flyttes bare hvis ingenting har fokus.
        if (enabled && FocusManager.GetFocusedElement(Root.XamlRoot) is not Button)
        {
            FocusHome(result.Added.Count > 0 ? result.Added[0].Id : null);
        }
    }

    // ---- Lasteskjerm ----

    private void ShowLoading(Game game, PlayAction action)
    {
        var title = GameLibrary.DisplayTitle(game);
        LoadingTitle.Text = title;
        LoadingTitle.Visibility = Visibility.Visible;
        LoadingLogo.Source = null;
        LoadingHero.Source = null;
        LoadingStatus.Text = action.RunnerId switch
        {
            GameLibrary.SteamRunnerId => "Starter via Steam …",
            GameLibrary.MoonlightRunnerId => $"Kobler til {HostLabel} …",
            _ => "Starter …",
        };
        LoadingChoices.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        LoadingPulse.Begin();
        _loadingShown = DateTime.UtcNow;
        _loadingCovered = false;

        _loadingTimer?.Stop();
        _loadingTimer = DispatcherQueue.CreateTimer();
        _loadingTimer.Interval = LoadingPatience;
        _loadingTimer.IsRepeating = false;
        _loadingTimer.Tick += (_, _) => OnLoadingSlow();
        _loadingTimer.Start();

        var art = action.RunnerId == GameLibrary.SteamRunnerId && SteamClient.FindRoot() is { } root
            ? SteamArt.Find(root, action.Target)
            : game.Art;
        _log.Write("laster", $"Lasteskjerm for «{title}» (logo: {(art.Logo is null ? "nei" : "ja")}, bakgrunn: {(art.Hero is null ? "nei" : "ja")})");
        _ = LoadArtAsync(art);
    }

    private async Task LoadArtAsync(ArtSet art)
    {
        var shownAt = _loadingShown;
        var logo = await LoadImageAsync(art.Logo, 720);
        var hero = await LoadImageAsync(art.Hero, 1280);

        // Kom bildene etter at lasteskjermen var borte, eller for en ny start, brukes de ikke.
        if (LoadingPanel.Visibility != Visibility.Visible || shownAt != _loadingShown)
        {
            return;
        }

        if (logo is not null)
        {
            LoadingLogo.Source = logo;
            LoadingTitle.Visibility = Visibility.Collapsed;
        }

        LoadingHero.Source = hero;
    }

    private async Task<BitmapImage?> LoadImageAsync(string? path, int decodeWidth)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage { DecodePixelWidth = decodeWidth };
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.Write("laster", $"Kunne ikke lese bilde: {ex.Message}");
            return null;
        }
    }

    private void OnLoadingSlow()
    {
        if (LoadingPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        // Spillet ligger foran: lasteskjermen trengs ikke lenger.
        if (!_windowActive)
        {
            HideLoading("spillet er i forgrunnen");
            return;
        }

        LoadingStatus.Text = "Det tar lang tid …";
        LoadingChoices.Visibility = Visibility.Visible;
        LoadingWaitButton.Focus(FocusState.Keyboard);
        _log.Write("laster", "Tar lang tid; viser Vent og Tilbake til Home");
    }

    // ---- Lasteskjerm over Moonlight til streamen viser bilde ----

    /// <summary>
    /// Holder lasteskjermen over Moonlight mens den kobler til, så brukeren ikke ser «Establishing connection» og
    /// Moonlights egne skjermer. Lasteskjermen tar ikke forgrunnen; den ligger bare øverst. Den fjernes når Moonlights
    /// streamvindu vises, når økten slutter, eller etter 45 s, så en feilmelding fra Moonlight aldri skjules lenge.
    /// </summary>
    private void StartStreamCover(StreamSession stream)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Foreground.SetTopmost(window, true);
        _streamCover?.Stop();
        _streamCover = DispatcherQueue.CreateTimer();
        _streamCover.Interval = TimeSpan.FromMilliseconds(250);
        var started = DateTime.UtcNow;
        _streamCover.Tick += (_, _) =>
        {
            if (!ReferenceEquals(stream, _session) || stream.Completion.IsCompleted)
            {
                EndStreamCover("økten sluttet");
            }
            else if (stream.IsStreamVisible)
            {
                EndStreamCover("streamen viser bilde");
            }
            else if (DateTime.UtcNow - started > StreamCoverLimit)
            {
                EndStreamCover("ingen stream etter 45 s; viser Moonlight");
            }
        };
        _streamCover.Start();
        _log.Write("laster", "Lasteskjermen ligger over Moonlight til streamen viser bilde");
    }

    /// <summary>Som <see cref="StartStreamCover"/>, men mens spillet avsluttes: ligger øverst til økten er slutt, høyst 30 s.</summary>
    private void StartQuitCover(StreamSession stream)
    {
        Foreground.SetTopmost(WinRT.Interop.WindowNative.GetWindowHandle(this), true);
        _streamCover?.Stop();
        _streamCover = DispatcherQueue.CreateTimer();
        _streamCover.Interval = TimeSpan.FromMilliseconds(250);
        var started = DateTime.UtcNow;
        _streamCover.Tick += (_, _) =>
        {
            if (stream.Completion.IsCompleted)
            {
                EndStreamCover("spillet og Moonlight er avsluttet");
            }
            else if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
            {
                EndStreamCover("avslutning tok over 30 s; viser Moonlight");
            }
        };
        _streamCover.Start();
        _log.Write("laster", "Lasteskjermen dekker Moonlight mens spillet avsluttes");
    }

    private void EndStreamCover(string why)
    {
        if (_streamCover is null)
        {
            return;
        }

        _streamCover.Stop();
        _streamCover = null;
        Foreground.SetTopmost(WinRT.Interop.WindowNative.GetWindowHandle(this), false);
        HideLoading(why);
    }

    private void HideLoading(string why)
    {
        if (_streamCover is not null)
        {
            EndStreamCover(why);
            return;
        }

        if (LoadingPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _loadingTimer?.Stop();
        LoadingPulse.Stop();
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingLogo.Source = null;
        LoadingHero.Source = null;
        SetHomeEnabled(MessagePanel.Visibility != Visibility.Visible && SessionPanel.Visibility != Visibility.Visible);
        _log.Write("laster", string.Create(CultureInfo.InvariantCulture,
            $"Lasteskjerm skjult etter {(DateTime.UtcNow - _loadingShown).TotalSeconds:F1} s: {why}"));
    }

    private void LoadingWaitButton_Click(object sender, RoutedEventArgs e)
    {
        LoadingChoices.Visibility = Visibility.Collapsed;
        LoadingStatus.Text = "Venter …";
        if (_loadingTimer is not null)
        {
            _loadingTimer.Interval = LoadingPatience;
            _loadingTimer.Start();
        }
    }

    private void LoadingHomeButton_Click(object sender, RoutedEventArgs e)
    {
        HideLoading("tilbake til Home");
        FocusHome(_focusGameId);
    }

    private void OnSessionHotkey(object? sender, int id)
    {
        var name = SessionHotkeys.FirstOrDefault(h => h.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture);
        _log.Write("hurtigtast", $"Mottatt: {name}");
        HideLoading("M2");
        Activate();
        var ok = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _log.Write("fokus", ok ? "Tok forgrunnen etter hurtigtast" : "Windows avslo forgrunnen etter hurtigtast");
        ShowSessionPanel($"hurtigtast {name}");
    }

    private void ShowSessionPanel(string via)
    {
        if (!SessionActive)
        {
            return;
        }

        SessionTitle.Text = _sessionGame is null ? "Spill" : GameLibrary.DisplayTitle(_sessionGame);
        SessionDetail.Text = string.Create(CultureInfo.InvariantCulture,
            $"Startet for {(DateTime.UtcNow - _sessionStarted).TotalMinutes:F0} min siden");
        var streaming = _session is StreamSession;
        ResumeButton.Content = streaming ? "Tilbake til streamen" : "Tilbake til spillet";
        EndSessionButton.Content = streaming ? $"Koble fra (spillet fortsetter på {HostLabel})" : "Avslutt spillet";
        EndRemoteButton.Content = $"Avslutt spillet på {HostLabel}";
        EndRemoteButton.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
        if (streaming)
        {
            SessionDetail.Text = $"Strømmer fra {HostLabel}";
        }

        SessionPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        ResumeButton.Focus(FocusState.Keyboard);
        _log.Write("økt", $"«Kjører nå» vist via {via}");
    }

    private void HideSessionPanel()
    {
        SessionPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        FocusHome(_focusGameId);
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session || !SessionActive)
        {
            HideSessionPanel();
            return;
        }

        // Panelet skjules og fokus settes i Home før spillet får forgrunnen. Motsatt rekkefølge gjorde at fokus i
        // XAML aktiverte GameHome igjen, så spillet ble liggende bak (0.1.33.0).
        HideSessionPanel();
        var handle = session.MainWindowHandle;
        if (handle == 0)
        {
            _log.Write("fokus", "Tilbake til spillet: fant ikke spillets vindu");
            return;
        }

        var (ok, wasMinimized) = Foreground.BringGame(handle);
        _log.Write("fokus", (ok ? "Tilbake til spillet: spillet fikk forgrunnen" : "Tilbake til spillet: Windows avslo")
            + (wasMinimized ? " (vinduet var minimert og ble gjenopprettet)" : ""));
    }

    private void EndSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session || !SessionActive)
        {
            HideSessionPanel();
            return;
        }

        if (EndSessionButton.Content is "Tving avslutning")
        {
            _log.Write("økt", "Tvangsavslutter etter bekreftelse");
            _ = session.ForceTerminateAsync();
            return;
        }

        _ = session.RequestCloseAsync();
        _log.Write("økt", "«Avslutt spillet» valgt");
        SessionDetail.Text = "Venter på at spillet avslutter …";

        // Avslutter ikke spillet selv, tilbys tvangsavslutning. Aldri automatisk.
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = CloseGracePeriod;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (ReferenceEquals(session, _session) && SessionActive)
            {
                SessionDetail.Text = "Spillet svarte ikke. Tvangsavslutning kan miste ulagret fremgang.";
                EndSessionButton.Content = "Tving avslutning";
                _log.Write("økt", "Spillet avsluttet ikke innen 10 s; tilbyr tvangsavslutning");
            }
        };
        timer.Start();
    }

    private void ClosePanelButton_Click(object sender, RoutedEventArgs e) => HideSessionPanel();

    // ---- Meldinger med to valg ----

    /// <summary>
    /// Viser en melding med to valg. B kjører <paramref name="onCancel"/>, eller det andre valget hvis den mangler. Bruk
    /// <paramref name="onCancel"/> når begge valgene gjør noe, så B aldri starter noe.
    /// </summary>
    private void ShowMessage(string title, string text, string primary, Action onPrimary, string secondary, Action onSecondary,
        Action? onCancel = null)
    {
        MessageTitle.Text = title;
        MessageText.Text = text;
        MessagePrimary.Content = primary;
        MessageSecondary.Content = secondary;
        _messagePrimary = onPrimary;
        _messageSecondary = onSecondary;
        _messageCancel = onCancel ?? onSecondary;
        MessagePanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        (onCancel is null ? MessageSecondary : MessagePrimary).Focus(FocusState.Keyboard);
        _log.Write("melding", title);
    }

    private void CloseMessage(Action? then)
    {
        MessagePanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(SessionPanel.Visibility != Visibility.Visible && HostPanel.Visibility != Visibility.Visible);
        _messagePrimary = null;
        _messageSecondary = null;
        _messageCancel = null;
        then?.Invoke();
    }

    // ---- Spill på PC-en (Moonlight) ----

    private async void HostTile_Click(object sender, RoutedEventArgs e)
    {
        LogChoice(HostTile);
        HostGames.Children.Clear();
        HostPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        HostCloseButton.Focus(FocusState.Keyboard);

        var hosts = MoonlightClient.FindHosts();
        if (MoonlightClient.FindExe() is null || hosts.Count == 0)
        {
            HostStatus.Text = MoonlightClient.FindExe() is null
                ? "Moonlight er ikke installert på Legion Go."
                : "Moonlight er ikke paret med en PC ennå. Åpne Moonlight og legg til PC-en én gang.";
            _log.Write("host", HostStatus.Text);
            return;
        }

        var host = hosts[0];
        HostTitle.Text = $"Spill fra {host.DisplayName}";
        HostStatus.Text = $"Henter spill fra {host.DisplayName} …";
        MoonlightResult result;
        try
        {
            result = await MoonlightClient.ListAppsAsync(host.Uuid, TimeSpan.FromSeconds(70));
        }
        catch (LaunchException ex)
        {
            HostStatus.Text = ex.Message;
            _log.Write("host", $"list feilet: {ex.Message}");
            return;
        }

        var apps = MoonlightAppList.ParseCsv(result.Output);
        var games = apps.Where(a => a.IsGame).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var extra = (result.TimedOut ? ", tidsgrense nådd" : "")
            + (result.ExitCode != 0 && result.Errors.Length > 0 ? ", stderr: " + FirstLine(result.Errors) : "");
        _log.Write("host", string.Create(CultureInfo.InvariantCulture,
            $"list {host.Name}: exit {result.ExitCode}, {result.Elapsed.TotalSeconds:F1} s, {apps.Count} apper, {games.Count} spill{extra}"));

        if (HostPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (result.ExitCode != 0)
        {
            HostStatus.Text = result.TimedOut
                ? $"{host.DisplayName} svarte ikke. Er den slått på?"
                : $"Fikk ikke kontakt med {host.DisplayName}. {FirstLine(result.Errors)}";
            return;
        }

        HostStatus.Text = games.Count == 0
            ? $"Fant ingen installerte spill på {host.DisplayName}. Sjekk at Vibepollo synker alle installerte Playnite-spill."
            : $"{games.Count} installerte spill på {host.DisplayName}. Velg et spill for å legge det til i Home.";
        foreach (var app in games)
        {
            var button = new Button
            {
                Content = HostGameText(host.Uuid, app.Name),
                Tag = app.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 56,
                FontSize = 20,
            };
            button.Click += (_, _) => AddHostGame(host, app.Name, button);
            HostGames.Children.Add(button);
        }

        if (HostGames.Children.Count > 0 && HostGames.Children[0] is Button first)
        {
            first.Focus(FocusState.Keyboard);
        }
    }

    private string HostGameText(string hostId, string name) => _library.HasStream(hostId, name) ? $"✓  {name}   (i Home)" : name;

    private void AddHostGame(MoonlightHost host, string name, Button button)
    {
        var (game, newTile) = _library.AddStream(host.Uuid, name);
        SaveLibrary();
        BuildGameTiles();
        button.Content = HostGameText(host.Uuid, name);
        _focusGameId = game.Id;
        _log.Write("host", newTile ? $"La til «{name}» fra {host.Name} i Home" : $"«{name}» fra {host.Name} er spillemåte på «{GameLibrary.DisplayTitle(game)}»");
    }

    private void HostCloseButton_Click(object sender, RoutedEventArgs e) => CloseHostPanel();

    private void CloseHostPanel()
    {
        HostPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        FocusHome(_focusGameId);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    private void EndRemoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not StreamSession stream || !SessionActive)
        {
            HideSessionPanel();
            return;
        }

        var title = _sessionGame is null ? "spillet" : GameLibrary.DisplayTitle(_sessionGame);
        SessionPanel.Visibility = Visibility.Collapsed;
        ShowMessage($"Avslutte {title} på {HostLabel}?", $"Spillet lukkes på {HostLabel}. Ulagret fremgang kan gå tapt.",
            $"Avslutt på {HostLabel}", () =>
            {
                _log.Write("økt", "Avslutt fjernspill bekreftet");
                LastInput.Text = $"Avslutter {title} på {HostLabel} …";

                // Moonlight viser sitt eget «Quitting»-vindu mens spillet avsluttes; lasteskjermen dekker det til
                // Moonlight er lukket og økten er slutt.
                if (_sessionGame is { } game
                    && _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.MoonlightRunnerId) is { } streamAction)
                {
                    ShowLoading(game, streamAction);
                    LoadingStatus.Text = $"Avslutter på {HostLabel} …";
                    StartQuitCover(stream);
                }

                _ = stream.EndRemoteAppAsync();
            },
            "Avbryt", () => ShowSessionPanel("avslutt fjernspill avbrutt"));
    }

    private void MessagePrimary_Click(object sender, RoutedEventArgs e) => CloseMessage(_messagePrimary);

    private void MessageSecondary_Click(object sender, RoutedEventArgs e) => CloseMessage(_messageSecondary);

    private void SetHomeEnabled(bool enabled)
    {
        foreach (var child in GameTiles.Children.OfType<Button>()
            .Concat(ToolTiles.Children.OfType<Button>())
            .Append(PowerMenuButton))
        {
            child.IsEnabled = enabled;
        }
    }

    // ---- Strømvalg: ett ikon åpner menyen; valget i menyen er bekreftelsen ----

    private bool _powerFromSession;

    private void PowerMenuButton_Click(object sender, RoutedEventArgs e)
    {
        _powerFromSession = SessionPanel.Visibility == Visibility.Visible;
        if (_powerFromSession)
        {
            SessionPanel.Visibility = Visibility.Collapsed;
        }

        var game = SessionActive && _sessionGame is not null ? GameLibrary.DisplayTitle(_sessionGame) : null;
        PowerText.Text = game is null
            ? "Windows lukker programmene normalt ved omstart og avslutning."
            : $"{game} kjører. Dvale setter det på pause. Omstart og avslutning avslutter spillet, og ulagret fremgang kan gå tapt.";
        PowerPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        PowerCancelButton.Focus(FocusState.Keyboard);
        _log.Write("strøm", $"Strømmenyen åpnet via {_lastSource}");
    }

    private void PowerOption_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag as string;

        // Avslutter bare GameHome. Et spill som kjører, fortsetter; Windows starter GameHome igjen som hjem i Xbox mode.
        if (tag == "exit")
        {
            _log.Write("strøm", "Avslutt GameHome valgt");
            StopEverything();
            Application.Current.Exit();
            return;
        }

        // Ukjente valg gjør ingenting; bare de tre kjente kan slå av eller starte på nytt.
        PowerAction? choice = tag switch
        {
            "sleep" => PowerAction.Sleep,
            "restart" => PowerAction.Restart,
            "shutdown" => PowerAction.Shutdown,
            _ => null,
        };
        if (choice is not { } action)
        {
            _log.Write("strøm", $"Ukjent strømvalg «{tag}» ignorert");
            ClosePowerMenu(restoreSession: true);
            return;
        }
        _log.Write("strøm", $"{action} valgt");
        ClosePowerMenu(restoreSession: false);

        if (Power.Run(action) is { } error)
        {
            _log.Write("strøm", $"{action} feilet: {error}");
            ShowMessage("Det gikk ikke", error, "Lukk", () => FocusHome(_focusGameId), "Tilbake", () => FocusHome(_focusGameId));
        }
    }

    private void PowerCancel_Click(object sender, RoutedEventArgs e)
    {
        _log.Write("strøm", "Strømmenyen avbrutt");
        ClosePowerMenu(restoreSession: true);
    }

    private void ClosePowerMenu(bool restoreSession)
    {
        PowerPanel.Visibility = Visibility.Collapsed;
        if (restoreSession && _powerFromSession && SessionActive)
        {
            ShowSessionPanel("strømmenyen lukket");
            return;
        }

        SetHomeEnabled(true);
        if (restoreSession)
        {
            PowerMenuButton.Focus(FocusState.Keyboard);
        }
        else
        {
            FocusHome(_focusGameId);
        }
    }

    // ---- Legion-knapp via Lenovos HID-kanaler (diagnostikk) ----

    private void HidTile_Click(object sender, RoutedEventArgs e)
    {
        LogChoice(HidTile);
        StartHidCapture();
    }

    private void StartHidCapture()
    {
        if (_hidListeners.Count > 0)
        {
            return;
        }

        var interfaces = HidReportListener.FindInterfaces(LegionGoController);
        _log.Write("hid", string.Create(CultureInfo.InvariantCulture, $"Fant {interfaces.Count} HID-grensesnitt for Legion Go-kontrolleren"));
        foreach (var path in interfaces)
        {
            var label = ShortHidName(path);
            var listener = HidReportListener.TryOpen(path, out var error);
            if (listener is null)
            {
                _log.Write("hid", $"{label}: {error}");
                continue;
            }

            var previous = string.Empty;
            var lastLogged = DateTime.MinValue;
            var suppressed = 0;
            listener.ReportReceived += (_, hex) =>
            {
                // Logg bare endringer, og høyst fem per sekund per grensesnitt.
                if (hex == previous)
                {
                    return;
                }

                previous = hex;
                if ((DateTime.UtcNow - lastLogged).TotalMilliseconds < 200)
                {
                    suppressed++;
                    return;
                }

                lastLogged = DateTime.UtcNow;
                _log.Write("hid", string.Create(CultureInfo.InvariantCulture, $"{label} (+{suppressed}): {hex}"));
                suppressed = 0;
            };
            listener.Failed += (_, message) => _log.Write("hid", $"{label}: {message}");
            listener.Start();
            _hidListeners.Add(listener);
            _log.Write("hid", $"{label}: lytter");
        }

        LastInput.Text = "Lytter i 15 sekunder. Trykk venstre Legion-knapp nå.";
        var stopTimer = DispatcherQueue.CreateTimer();
        stopTimer.Interval = HidCaptureDuration;
        stopTimer.IsRepeating = false;
        stopTimer.Tick += (_, _) => StopHidCapture();
        stopTimer.Start();
    }

    private void StopHidCapture()
    {
        foreach (var listener in _hidListeners)
        {
            listener.Dispose();
        }

        if (_hidListeners.Count > 0)
        {
            _log.Write("hid", "Lytting stoppet");
        }

        _hidListeners.Clear();
    }

    private static string ShortHidName(string path)
    {
        var start = path.IndexOf("mi_", StringComparison.OrdinalIgnoreCase);
        var name = start >= 0 ? path.Substring(start, Math.Min(15, path.Length - start)) : path;
        return name.Split('#')[0];
    }

    private void StopEverything()
    {
        StopHidCapture();
        _hotkeys.UnregisterAll();

        // Spillet får leve videre selv om GameHome lukkes.
        _session?.Dispose();
        _session = null;
    }

    // ---- Fokus, input og målinger ----

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _gamepadTimer.Stop();
            _metricsTimer.Stop();
            _log.Write("fokus", "Mistet fokus (kontrollerlesing og målinger stoppet)");

            // Lasteskjermen blir liggende bak det som tok forgrunnen. Steam tar den ofte først med et lite vindu, og
            // spillet tegner seg over lasteskjermen når det er klart. Den skjules når GameHome blir aktiv igjen.
            _windowActive = false;
            if (LoadingPanel.Visibility == Visibility.Visible && !_loadingCovered)
            {
                _loadingCovered = true;
                _log.Write("laster", string.Create(CultureInfo.InvariantCulture,
                    $"Forgrunnen gikk til {Foreground.CurrentProcessName() ?? "ukjent"} etter {(DateTime.UtcNow - _loadingShown).TotalSeconds:F1} s; lasteskjermen ligger bak"));

                // Animasjonen skal ikke gå bak spillet lenge: lasteskjermen fjernes etter kort tid. Under tilkobling til
                // gaming-PC-en ligger den øverst og styres av streamvinduet i stedet.
                if (_loadingTimer is not null && _streamCover is null)
                {
                    _loadingTimer.Stop();
                    _loadingTimer.Interval = CoveredLoadingLimit;
                    _loadingTimer.Start();
                }
            }

            return;
        }

        _windowActive = true;
        if (LoadingPanel.Visibility == Visibility.Visible && _loadingCovered)
        {
            HideLoading("GameHome aktiv igjen");
        }

        if (!_startupLogged)
        {
            _startupLogged = true;
            var startup = DateTime.Now - Process.GetCurrentProcess().StartTime;
            var memoryMb = Environment.WorkingSet / (1024.0 * 1024.0);
            StartupText.Text = string.Create(CultureInfo.InvariantCulture, $"Oppstart: {startup.TotalMilliseconds:F0} ms");
            _log.Write("ytelse", string.Create(CultureInfo.InvariantCulture,
                $"Oppstart til første aktivering: {startup.TotalMilliseconds:F0} ms, minne: {memoryMb:F0} MB"));

            // Tilbake til flisen brukeren sto på sist, også etter omstart.
            var remembered = LoadRememberedTile();
            var found = remembered is not null && TileFor(remembered) is not null;
            _log.Write("fokus", remembered is null ? "Ingen husket flis; fokus på første"
                : found ? $"Husket flis: «{(_library.Find(remembered) is { } g ? GameLibrary.DisplayTitle(g) : remembered)}»"
                : "Husket flis finnes ikke lenger; fokus på første");
            FocusHome(found ? remembered : null);
        }

        UpdateMemory();
        _metricsTimer.Start();

        // Første skanning venter til Home er tegnet fra cache, så oppstarten ikke endrer flisene midt i første visning.
        if (_firstSteamSync is null)
        {
            _firstSteamSync = DispatcherQueue.CreateTimer();
            _firstSteamSync.Interval = TimeSpan.FromSeconds(1);
            _firstSteamSync.IsRepeating = false;
            _firstSteamSync.Tick += (_, _) => SyncSteamInBackground();
            _firstSteamSync.Start();
        }
        else
        {
            SyncSteamInBackground();
        }
        _gamepadTimer.Start();
        _log.Write("fokus", $"Fikk fokus ({args.WindowActivationState})");
    }

    private void PollGamepad()
    {
        var gamepads = Gamepad.Gamepads;
        if (gamepads.Count == 0)
        {
            return;
        }

        var reading = gamepads[0].GetCurrentReading();
        var pressed = reading.Buttons & ~_previousButtons;
        _previousButtons = reading.Buttons;

        var stick = StickDirection(reading.LeftThumbstickX, reading.LeftThumbstickY);
        var stickMoved = stick is not null && stick != _previousStick;
        _previousStick = stick;

        // Avtrekkerne (L2/R2) er analoge verdier 0–1, ikke knapper.
        var leftTrigger = reading.LeftTrigger > TriggerThreshold;
        var rightTrigger = reading.RightTrigger > TriggerThreshold;
        var leftTriggerPressed = leftTrigger && !_previousLeftTrigger;
        var rightTriggerPressed = rightTrigger && !_previousRightTrigger;
        _previousLeftTrigger = leftTrigger;
        _previousRightTrigger = rightTrigger;

        if (pressed == GamepadButtons.None && !stickMoved && !leftTriggerPressed && !rightTriggerPressed)
        {
            return;
        }

        _lastSource = "kontroller";
        var names = Enum.GetValues<GamepadButtons>()
            .Where(b => b != GamepadButtons.None && pressed.HasFlag(b))
            .Select(b => b.ToString())
            .ToList();
        if (leftTriggerPressed)
        {
            names.Add("L2");
        }

        if (rightTriggerPressed)
        {
            names.Add("R2");
        }

        if (stickMoved)
        {
            names.Add($"Venstre stikke {stick}");
        }

        _log.Write("input", $"Kontroller: {string.Join(", ", names)}");

        var direction = DPadDirection(pressed) ?? (stickMoved ? stick : null);
        if (direction is { } dir)
        {
            FocusManager.TryMoveFocus(dir, new FindNextElementOptions { SearchRoot = Root.XamlRoot.Content });
        }

        var focusedButton = FocusManager.GetFocusedElement(Root.XamlRoot) as Button;

        // Hvis XAML selv leverer kontrollertaster, gjør knappene jobben; ellers aktiverer vi med A.
        if (pressed.HasFlag(GamepadButtons.A) && !_xamlGamepadKeysSeen && focusedButton is not null)
        {
            new ButtonAutomationPeer(focusedButton).Invoke();
            return;
        }

        // Y på en spillflis: fjern fra GameHome (med bekreftelse).
        if (pressed.HasFlag(GamepadButtons.Y) && focusedButton is { Tag: string id } && _library.Find(id) is { } game
            && MessagePanel.Visibility != Visibility.Visible && SessionPanel.Visibility != Visibility.Visible)
        {
            ConfirmRemove(game);
            return;
        }

        // B lukker det øverste panelet.
        if (pressed.HasFlag(GamepadButtons.B))
        {
            if (MessagePanel.Visibility == Visibility.Visible)
            {
                CloseMessage(_messageCancel);
            }
            else if (HostPanel.Visibility == Visibility.Visible)
            {
                CloseHostPanel();
            }
            else if (PowerPanel.Visibility == Visibility.Visible)
            {
                _log.Write("strøm", "Strømmenyen lukket med B");
                ClosePowerMenu(restoreSession: true);
            }
            else if (LoadingPanel.Visibility == Visibility.Visible)
            {
                HideLoading("B");
                FocusHome(_focusGameId);
            }
            else if (SessionPanel.Visibility == Visibility.Visible)
            {
                HideSessionPanel();
            }
        }
    }

    private void UpdateMemory()
    {
        using var process = Process.GetCurrentProcess();
        MemoryText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Minne: {process.WorkingSet64 / 1048576.0:F0} MB (privat {process.PrivateMemorySize64 / 1048576.0:F0} MB)");
    }

    private static FocusNavigationDirection? DPadDirection(GamepadButtons pressed) =>
        pressed.HasFlag(GamepadButtons.DPadUp) ? FocusNavigationDirection.Up
        : pressed.HasFlag(GamepadButtons.DPadDown) ? FocusNavigationDirection.Down
        : pressed.HasFlag(GamepadButtons.DPadLeft) ? FocusNavigationDirection.Left
        : pressed.HasFlag(GamepadButtons.DPadRight) ? FocusNavigationDirection.Right
        : null;

    private static FocusNavigationDirection? StickDirection(double x, double y) =>
        y > StickThreshold ? FocusNavigationDirection.Up
        : y < -StickThreshold ? FocusNavigationDirection.Down
        : x < -StickThreshold ? FocusNavigationDirection.Left
        : x > StickThreshold ? FocusNavigationDirection.Right
        : null;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isGamepadKey = e.Key >= VirtualKey.GamepadA && e.Key <= VirtualKey.GamepadRightThumbstickLeft;
        if (isGamepadKey && !_xamlGamepadKeysSeen)
        {
            _xamlGamepadKeysSeen = true;
            _log.Write("input", "XAML leverer kontrollertaster selv (innebygd ruting finnes)");
        }

        _lastSource = isGamepadKey ? "kontroller (XAML)" : "tastatur";
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) =>
        _lastSource = e.Pointer.PointerDeviceType switch
        {
            PointerDeviceType.Touch => "berøring",
            PointerDeviceType.Pen => "penn",
            _ => "mus",
        };

    private void LogChoice(Button tile) => _log.Write("valg", $"{tile.Content} valgt via {_lastSource}");

    private void OnLogWritten(object? sender, string line) => DispatcherQueue.TryEnqueue(() =>
    {
        _events.Insert(0, line);
        while (_events.Count > MaxVisibleEvents)
        {
            _events.RemoveAt(_events.Count - 1);
        }

        EventText.Text = string.Join('\n', _events);
    });
}
