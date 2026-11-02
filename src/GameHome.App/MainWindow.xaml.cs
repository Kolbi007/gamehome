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
using GameHome.Metadata;
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
    private bool _waking;
    private bool _lastWakeNoNetwork;
    private string? _statusBeforeSlow;
    private bool _loadingCovered;
    private bool _windowActive;
    private Game? _sessionGame;
    private DateTime _sessionStarted;
    private string? _focusGameId;
    private string? _rememberedTile;
    private readonly Dictionary<string, TextBlock> _tileTitles = new(StringComparer.Ordinal);
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

        // Pakkeversjonen er den som installeres og oppgraderes (0.1.N.0); assembly-versjonen er alltid 1.0.0.0.
        var version = global::Windows.ApplicationModel.Package.Current.Id.Version;
        var versionText = string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
        InfoText.Text = $"Versjon {versionText}";
        HostTile.Content = $"Spill fra {HostLabel}";
        var variant = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT";
        _log.Write("start", $"Vindu klart (versjon {versionText}, {variant})");
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
                SetTileContent(existing, visible[i]);
            }
        }
    }

    private Button CreateTile(Game game)
    {
        var tile = new Button
        {
            Tag = game.Id,
            Width = 280,
            Height = 160,
            FontSize = 22,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        SetTileContent(tile, game);
        tile.Click += GameTile_Click;
        tile.GotFocus += (_, _) => RememberTile(game.Id);
        return tile;
    }

    /// <summary>
    /// Flisens innhold: bilde (Steams lokale bilder eller omslag fra hosten) med tittelen nederst over et mørkt slør,
    /// eller bare tittelen når spillet ikke har bilde.
    /// </summary>
    private void SetTileContent(Button tile, Game game)
    {
        var image = TileImageFor(game);
        if (image is null)
        {
            tile.Content = TileText(game);
            _tileTitles.Remove(game.Id);
            return;
        }

        var title = new TextBlock
        {
            Text = TileText(game),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 12, 10),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var picture = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        var scrim = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Fill = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.45),
                EndPoint = new global::Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = global::Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 0 },
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = global::Windows.UI.Color.FromArgb(220, 0, 0, 0), Offset = 1 },
                },
            },
        };
        var grid = new Grid { CornerRadius = new CornerRadius(4) };
        grid.Children.Add(picture);
        grid.Children.Add(scrim);
        grid.Children.Add(title);
        tile.Content = grid;
        _tileTitles[game.Id] = title;
        _ = LoadTileImageAsync(picture, image);
    }

    private async Task LoadTileImageAsync(Image target, string path)
    {
        if (await LoadImageAsync(path, 560) is { } bitmap)
        {
            target.Source = bitmap;
        }
    }

    /// <summary>Bilde for flisen: Steams brede bakgrunnsbilde for Steam-spill på Legion Go, ellers lagret omslag.</summary>
    private string? TileImageFor(Game game)
    {
        var art = ArtFor(game);
        var path = art.Hero ?? art.Cover;
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Bildene for et spill: Steams lokale bildecache for Steam-spill på Legion Go, ellers det som er lagret.</summary>
    private ArtSet ArtFor(Game game)
    {
        var steam = _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.SteamRunnerId);
        if (steam is not null && SteamClient.FindRoot() is { } root)
        {
            var local = SteamArt.Find(root, steam.Target);
            return new ArtSet(local.Cover ?? game.Art.Cover, local.Hero ?? game.Art.Hero, local.Logo ?? game.Art.Logo);
        }

        return game.Art;
    }

    private string TileText(Game game) =>
        SessionActive && _sessionGame?.Id == game.Id ? $"▶ {GameLibrary.DisplayTitle(game)}" : GameLibrary.DisplayTitle(game);

    private void RefreshTileTexts()
    {
        foreach (var tile in GameTiles.Children.OfType<Button>())
        {
            if (tile.Tag is string id && _library.Find(id) is { } game)
            {
                if (_tileTitles.TryGetValue(id, out var title))
                {
                    title.Text = TileText(game);
                }
                else
                {
                    tile.Content = TileText(game);
                }
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

    /// <summary>
    /// Starter et spill. Feil underveis logges og gir en melding med valg; de skal aldri forsvinne stille og etterlate
    /// en lasteskjerm som henger (0.1.68.0).
    /// </summary>
    private async Task LaunchAsync(Game game, PlayAction? chosen = null)
    {
        try
        {
            await LaunchCoreAsync(game, chosen);
        }
        catch (Exception ex)
        {
            _log.Write("krasj", $"Start av «{GameLibrary.DisplayTitle(game)}» feilet: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            HideLoading("feil under start");
            ShowMessage($"{GameLibrary.DisplayTitle(game)} startet ikke", ex.Message,
                "Prøv igjen", () => _ = LaunchAsync(game, chosen), "Lukk", () => FocusHome(game.Id));
        }
    }

    private async Task LaunchCoreAsync(Game game, PlayAction? chosen)
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

        // Stream: sover gaming-PC-en, vekkes den først (Wake-on-LAN), så Moonlight ikke gir opp etter 30 s.
        if (action.IsRemote && !await WakeHostAsync(action.HostId!))
        {
            HideLoading(_lastWakeNoNetwork ? "Legion Go har ikke nettverk" : "PC-en våknet ikke");
            ShowMessage(_lastWakeNoNetwork ? "Legion Go har ikke nettverk" : $"{HostLabel} svarer ikke",
                _lastWakeNoNetwork
                    ? "Legion Go når ikke ruteren. Koble til Wi-Fi på nytt (Innstillinger → Nettverk), og prøv igjen."
                    : $"GameHome prøvde å vekke {HostLabel}. Sjekk at den er på og har nettverkskabel, og prøv igjen.",
                "Prøv igjen", () => _ = LaunchAsync(game, action), "Lukk", () => FocusHome(game.Id));
            return;
        }

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
        else if (session is SteamSession steam)
        {
            StartSteamCover(steam);
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
        LoadingIntro.Begin();
        LoadingPulse.Begin();
        HeroDrift.Begin();
        _loadingShown = DateTime.UtcNow;
        _loadingCovered = false;

        _loadingTimer?.Stop();
        _loadingTimer = DispatcherQueue.CreateTimer();
        _loadingTimer.Interval = LoadingPatience;
        _loadingTimer.IsRepeating = false;
        _loadingTimer.Tick += (_, _) => OnLoadingSlow();
        _loadingTimer.Start();

        var art = ArtFor(game);
        _log.Write("laster", $"Lasteskjerm for «{title}» (logo: {(art.Logo is null ? "nei" : "ja")}, bakgrunn: {(art.Hero is not null ? "ja" : art.Cover is not null ? "omslag" : "nei")})");
        _ = LoadArtAsync(art);
    }

    private async Task LoadArtAsync(ArtSet art)
    {
        var shownAt = _loadingShown;
        var logo = await LoadImageAsync(art.Logo, 720);

        // Uten eget bakgrunnsbilde brukes omslaget (som Nuvio: bakgrunn, ellers plakat).
        var hero = await LoadImageAsync(art.Hero ?? art.Cover, 1280);

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

        // Under vekking av gaming-PC-en har vekkingen sin egen tidsgrense og melding; ikke spør i tillegg.
        if (_waking)
        {
            _loadingTimer?.Start();
            return;
        }

        _statusBeforeSlow = LoadingStatus.Text;
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

    /// <summary>
    /// Som <see cref="StartStreamCover"/>, men for Steam-spill på Legion Go: Steams lille startvindu legger seg ellers
    /// over lasteskjermen. Fjernes når spillets eget vindu finnes, når økten slutter, eller etter 30 s, så en dialog
    /// fra Steam (for eksempel sky-lagring) aldri skjules lenge.
    /// </summary>
    private void StartSteamCover(SteamSession steam)
    {
        Foreground.SetTopmost(WinRT.Interop.WindowNative.GetWindowHandle(this), true);
        _streamCover?.Stop();
        _streamCover = DispatcherQueue.CreateTimer();
        _streamCover.Interval = TimeSpan.FromMilliseconds(500);
        var started = DateTime.UtcNow;
        _streamCover.Tick += (_, _) =>
        {
            if (!ReferenceEquals(steam, _session) || steam.Completion.IsCompleted)
            {
                EndStreamCover("økten sluttet");
            }
            else if (steam.State == SteamSessionState.Running && steam.MainWindowHandle != 0)
            {
                EndStreamCover("spillets vindu finnes");
            }
            else if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
            {
                EndStreamCover("spillvindu ikke funnet etter 30 s");
            }
        };
        _streamCover.Start();
        _log.Write("laster", "Lasteskjermen ligger over Steam til spillets vindu finnes");
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
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Foreground.SetTopmost(window, false);

        // Kjører spillet og har et annet vindu forgrunnen, skal GameHome ligge bak det, ikke foran.
        if (SessionActive && !Foreground.IsForeground(window))
        {
            Foreground.SendToBack(window);
            _log.Write("laster", "GameHome lagt bakerst; spillet ligger foran");
        }

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
        LoadingIntro.Stop();
        LoadingPulse.Stop();
        HeroDrift.Stop();
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

        // Vis hva vi fortsatt venter på (for eksempel «Kobler til Gaming Laptop …»), ikke bare «Venter».
        LoadingStatus.Text = string.IsNullOrEmpty(_statusBeforeSlow) ? "Venter på spillet …" : _statusBeforeSlow;
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
        var wake = await HostWaker.EnsureAwakeAsync(host, TimeSpan.FromSeconds(90), onWaking: () => DispatcherQueue.TryEnqueue(() =>
        {
            HostStatus.Text = $"Vekker {host.DisplayName} …";
            _log.Write("vekking", $"{host.DisplayName} svarer ikke; sender Wake-on-LAN før listen hentes");
        }), progress: p => DispatcherQueue.TryEnqueue(() => HostStatus.Text = WakeStatusText(host, p)));
        if (wake.NoLocalNetwork)
        {
            HostStatus.Text = "Legion Go har mistet nettverket (Wi-Fi). Koble til på nytt og prøv igjen.";
            _log.Write("vekking", "Legion Go når ikke ruteren; viser nettverksfeil i stedet for å vekke");
            return;
        }

        if (wake.WasAsleep)
        {
            _log.Write("vekking", string.Create(CultureInfo.InvariantCulture,
                $"{host.DisplayName} {(wake.Awake ? "våknet" : "våknet ikke")} etter {wake.Elapsed.TotalSeconds:F1} s"));
            HostStatus.Text = wake.Awake ? $"Henter spill fra {host.DisplayName} …" : $"{host.DisplayName} våknet ikke. Er den slått på?";
            if (!wake.Awake)
            {
                return;
            }
        }
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
            button.Click += (_, _) => AddHostGame(host, app, button);
            HostGames.Children.Add(button);
        }

        if (HostGames.Children.Count > 0 && HostGames.Children[0] is Button first)
        {
            first.Focus(FocusState.Keyboard);
        }

        _hostApps = apps;
        ApplyHostArt(host, apps);
    }

    private string HostGameText(string hostId, string name) => _library.HasStream(hostId, name) ? $"✓  {name}   (i Home)" : name;

    private void AddHostGame(MoonlightHost host, HostApp app, Button button)
    {
        var (game, newTile) = _library.AddStream(host.Uuid, app.Name);
        SaveLibrary();
        ApplyHostArt(host, _hostApps ?? [app]);
        BuildGameTiles();
        button.Content = HostGameText(host.Uuid, app.Name);
        _focusGameId = game.Id;
        _log.Write("host", newTile ? $"La til «{app.Name}» fra {host.Name} i Home" : $"«{app.Name}» fra {host.Name} er spillemåte på «{GameLibrary.DisplayTitle(game)}»");
        _ = FetchMissingArtAsync();
    }

    // ---- Omslag for spill fra gaming-PC-en ----

    private IReadOnlyList<HostApp>? _hostApps;
    private bool _hostArtRunning;

    /// <summary>
    /// Kopierer omslag Moonlight har i bufferen (hentet fra Vibepollo, altså fra Playnite på PC-en) til GameHomes egen
    /// mappe for spill som er lagt til i Home. Ingen nettverk her; bare lokale filer.
    /// </summary>
    private void ApplyHostArt(MoonlightHost host, IReadOnlyList<HostApp> apps)
    {
        var changed = 0;
        foreach (var app in apps)
        {
            if (app.BoxArtFile is not { } source || !File.Exists(source)
                || _library.FindStreamGame(host.Uuid, app.Name) is not { } game
                || (game.Art.Cover is { } existing && File.Exists(existing)))
            {
                continue;
            }

            try
            {
                var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, $"{game.Id}-cover{Path.GetExtension(source)}");
                File.Copy(source, target, overwrite: true);
                _library.SetArt(game.Id, game.Art with { Cover = target });
                changed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Write("bilder", $"Kunne ikke kopiere omslag for «{app.Name}»: {ex.Message}");
            }
        }

        if (changed > 0)
        {
            SaveLibrary();
            BuildGameTiles();
            _log.Write("bilder", $"Omslag fra {host.DisplayName} lagt til for {changed} spill");
        }
    }

    // Moonlights egen buffer ble ikke fylt av «list» (målt 2026-11-02: 0 av 33 omslag etter 75 s), så bilder hentes fra
    // Steams butikk i stedet (ADR 0003, tillegg).

    private static readonly HttpClient ArtHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _artTried = new(StringComparer.Ordinal);

    /// <summary>
    /// Henter omslag, bakgrunn og logo fra Steams butikk for spill i Home som mangler bilder: Steam-appid fra butikksøket
    /// (bare samme tittel), så de offentlige butikkbildene. Viser fremdrift øverst. Aldri mens et spill kjører, og hvert
    /// spill prøves bare én gang per kjøring.
    /// </summary>
    private async Task FetchMissingArtAsync()
    {
        if (_hostArtRunning || SessionActive)
        {
            return;
        }

        var missing = _library.VisibleGames.Where(g => TileImageFor(g) is null && !_artTried.Contains(g.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        _hostArtRunning = true;
        ArtProgress.Maximum = missing.Count;
        ArtProgress.Value = 0;
        ArtProgressPanel.Visibility = Visibility.Visible;
        var store = new SteamStoreClient(ArtHttp);
        var found = 0;
        try
        {
            for (var i = 0; i < missing.Count; i++)
            {
                if (SessionActive)
                {
                    _log.Write("bilder", "Henting av bilder stoppet: et spill startet");
                    break;
                }

                var game = missing[i];
                _artTried.Add(game.Id);
                var title = GameLibrary.DisplayTitle(game);
                ArtProgressText.Text = string.Create(CultureInfo.InvariantCulture, $"Henter bilder {i + 1}/{missing.Count}: {title}");
                try
                {
                    var steam = _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.SteamRunnerId);
                    var appId = steam is not null && int.TryParse(steam.Target, CultureInfo.InvariantCulture, out var id)
                        ? id
                        : await store.FindAppIdAsync(title);
                    if (appId is not { } steamId)
                    {
                        _log.Write("bilder", $"Fant ikke «{title}» på Steam");
                    }
                    else if (await DownloadArtAsync(game, SteamStoreClient.ImageUrls(steamId)) is { } art)
                    {
                        _library.SetArt(game.Id, art);
                        SaveLibrary();
                        if (TileFor(game.Id) is { } tile && _library.Find(game.Id) is { } updated)
                        {
                            SetTileContent(tile, updated);
                        }

                        found++;
                        _log.Write("bilder", string.Create(CultureInfo.InvariantCulture,
                            $"Bilder for «{title}» fra Steam (appid {steamId}): omslag {(art.Cover is null ? "nei" : "ja")}, bakgrunn {(art.Hero is null ? "nei" : "ja")}, logo {(art.Logo is null ? "nei" : "ja")}"));
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
                {
                    // Nettverksfeil (for eksempel Wi-Fi som har falt ut): prøv igjen neste gang Home blir aktiv.
                    _artTried.Remove(game.Id);
                    _log.Write("bilder", $"Henting av bilder for «{title}» feilet, prøver igjen senere: {ex.Message}");
                }

                ArtProgress.Value = i + 1;
            }
        }
        finally
        {
            _hostArtRunning = false;
            ArtProgressText.Text = string.Create(CultureInfo.InvariantCulture, $"Bilder hentet for {found} av {missing.Count} spill");
            var hide = DispatcherQueue.CreateTimer();
            hide.Interval = TimeSpan.FromSeconds(4);
            hide.IsRepeating = false;
            hide.Tick += (_, _) => ArtProgressPanel.Visibility = Visibility.Collapsed;
            hide.Start();
        }
    }

    /// <summary>Laster ned bildene som finnes (Steam har ikke alltid logo eller bakgrunn), til GameHomes bildemappe.</summary>
    private static async Task<ArtSet?> DownloadArtAsync(Game game, SteamImageUrls urls)
    {
        var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
        Directory.CreateDirectory(folder);

        async Task<string?> Get(string url, string kind)
        {
            using var response = await ArtHttp.GetAsync(new Uri(url));
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var target = Path.Combine(folder, $"{game.Id}-{kind}{Path.GetExtension(new Uri(url).AbsolutePath)}");
            await using (var file = File.Create(target))
            {
                await response.Content.CopyToAsync(file);
            }

            return target;
        }

        var cover = await Get(urls.Cover, "cover");
        var hero = await Get(urls.Hero, "hero");
        var logo = await Get(urls.Logo, "logo");
        return cover is null && hero is null && logo is null
            ? null
            : new ArtSet(cover ?? game.Art.Cover, hero ?? game.Art.Hero, logo ?? game.Art.Logo);
    }

    private void HostCloseButton_Click(object sender, RoutedEventArgs e) => CloseHostPanel();

    private void CloseHostPanel()
    {
        HostPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        FocusHome(_focusGameId);
    }

    /// <summary>
    /// Sørger for at gaming-PC-en svarer. Sover den, sendes Wake-on-LAN og lasteskjermen viser «Vekker …» til den
    /// svarer, høyst 90 s. Returnerer false hvis den ikke våknet.
    /// </summary>
    private async Task<bool> WakeHostAsync(string hostId)
    {
        var host = MoonlightClient.FindHosts().FirstOrDefault(h => h.Uuid == hostId);
        if (host is null)
        {
            _log.Write("vekking", "Fant ikke hosten i Moonlights liste; lar Moonlight prøve selv");
            return true;
        }

        WakeResult result;
        try
        {
            result = await HostWaker.EnsureAwakeAsync(host, TimeSpan.FromSeconds(90), onWaking: () => DispatcherQueue.TryEnqueue(() =>
            {
                _waking = true;
                LoadingStatus.Text = $"Vekker {host.DisplayName} …";
                _log.Write("vekking", $"{host.DisplayName} svarer ikke; sender Wake-on-LAN (MAC {(host.Mac is null ? "ukjent" : "kjent")})");
            }), progress: p => DispatcherQueue.TryEnqueue(() => LoadingStatus.Text = WakeStatusText(host, p)));
        }
        finally
        {
            _waking = false;
        }

        _lastWakeNoNetwork = result.NoLocalNetwork;
        if (result.NoLocalNetwork)
        {
            _log.Write("vekking", "Legion Go når ikke ruteren; viser nettverksfeil i stedet for å vekke");
            return false;
        }

        if (result.WasAsleep)
        {
            _log.Write("vekking", string.Create(CultureInfo.InvariantCulture,
                $"{host.DisplayName} {(result.Awake ? "våknet" : "våknet ikke")} etter {result.Elapsed.TotalSeconds:F1} s, {result.PacketsSent} pakker"));
            if (result.Awake)
            {
                LoadingStatus.Text = $"Kobler til {host.DisplayName} …";
            }
        }

        return result.Awake || host.Address is null;
    }

    /// <summary>Live status under vekking, for eksempel «Vekker Gaming Laptop · 3 vekkesignaler · ingen svar · 21/90 s».</summary>
    private static string WakeStatusText(MoonlightHost host, WakeProgress p) => string.Create(CultureInfo.InvariantCulture,
        $"Vekker {host.DisplayName} · {p.PacketsSent / 4} vekkesignal{(p.PacketsSent / 4 == 1 ? "" : "er")} · ingen svar ennå · {p.Elapsed.TotalSeconds:F0}/90 s");

    // ---- Test av tilkobling til gaming-PC-en ----

    /// <summary>
    /// Vekker og sjekker gaming-PC-en uten å starte Moonlight eller et spill, med live status. For rask feilsøking av
    /// Wake-on-LAN og nettverk.
    /// </summary>
    private async void TestHostTile_Click(object sender, RoutedEventArgs e)
    {
        LogChoice(TestHostTile);
        var hosts = MoonlightClient.FindHosts();
        var host = hosts.Count > 0 ? hosts[0] : null;
        HostGames.Children.Clear();
        HostTitle.Text = host is null ? "Test tilkobling" : $"Test {host.DisplayName}";
        HostPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        HostCloseButton.Focus(FocusState.Keyboard);
        if (host?.Address is null)
        {
            HostStatus.Text = "Moonlight kjenner ingen PC med adresse ennå.";
            return;
        }

        var lines = new List<string>
        {
            $"Adresse: {host.Address} · vekkeadresse (MAC): {(host.Mac is null ? "mangler" : "kjent")}",
        };
        void Show(string line)
        {
            lines.Add(line);
            HostStatus.Text = string.Join('\n', lines);
        }

        Show("Sjekker om den svarer …");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await HostWaker.EnsureAwakeAsync(host, TimeSpan.FromSeconds(90),
            onWaking: () => DispatcherQueue.TryEnqueue(() => Show("Svarer ikke. Sender vekkesignal (Wake-on-LAN) …")),
            progress: p => DispatcherQueue.TryEnqueue(() =>
            {
                lines[^1] = WakeStatusText(host, p);
                HostStatus.Text = string.Join('\n', lines);
            }));

        var summary = result switch
        {
            { NoLocalNetwork: true } => "✗ Legion Go når ikke ruteren. Wi-Fi er nede eller uten tilkobling.",
            { Awake: true, WasAsleep: false } => string.Create(CultureInfo.InvariantCulture, $"✓ {host.DisplayName} er våken og svarer ({watch.Elapsed.TotalSeconds:F1} s)."),
            { Awake: true } => string.Create(CultureInfo.InvariantCulture, $"✓ {host.DisplayName} våknet etter {result.Elapsed.TotalSeconds:F0} s ({result.PacketsSent / 4} vekkesignaler)."),
            _ => string.Create(CultureInfo.InvariantCulture, $"✗ {host.DisplayName} svarte ikke på {result.Elapsed.TotalSeconds:F0} s ({result.PacketsSent / 4} vekkesignaler)."),
        };
        Show(summary);
        _log.Write("vekking", $"Test: {summary}");
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

        // Spill som fortsatt mangler bilder (for eksempel etter en nettverksfeil), prøves igjen når Home blir aktiv.
        if (_firstSteamSync is not null)
        {
            _ = FetchMissingArtAsync();
        }

        // Første skanning venter til Home er tegnet fra cache, så oppstarten ikke endrer flisene midt i første visning.
        if (_firstSteamSync is null)
        {
            _firstSteamSync = DispatcherQueue.CreateTimer();
            _firstSteamSync.Interval = TimeSpan.FromSeconds(1);
            _firstSteamSync.IsRepeating = false;
            _firstSteamSync.Tick += (_, _) =>
            {
                SyncSteamInBackground();
                _ = FetchMissingArtAsync();
            };
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
