// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Vinduet lever like lenge som appen; Vibepollo-klienten frigjøres i Closed (StopEverything).")]
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

    // Alltid registrert (ikke bare under spill): Legion-knappen til venstre via Handheld Companion. Uten spill hentes Home fram,
    // under spill åpnes menyen som med M2.
    private static readonly (int Id, HotkeyModifiers Modifiers, uint Key, string Name) HomeHotkey =
        (4, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7A, "Ctrl+Alt+Shift+F11 (Home)");

    private void RegisterHomeHotkey()
    {
        if (!_hotkeys.Register(HomeHotkey.Id, HomeHotkey.Modifiers, HomeHotkey.Key))
        {
            _log.Write("hurtigtast", $"{HomeHotkey.Name} kunne ikke registreres (brukt av et annet program?)");
        }
    }

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
        [XboxRunner.Id] = new XboxRunner(),
        [BattleNetRunner.Id] = new BattleNetRunner(),
        [GogRunner.Id] = new GogRunner(),
        [EpicRunner.Id] = new EpicRunner(),
        [ChiakiRunner.Id] = new ChiakiRunner(),
        [RetroArchRunner.Id] = new RetroArchRunner(),
    };
    private readonly List<string> _events = [];
    private readonly DispatcherQueueTimer _gamepadTimer;
    private readonly DispatcherQueueTimer _metricsTimer;
    private readonly HotkeyListener _hotkeys;
    private readonly List<HidReportListener> _hidListeners = [];

    private IForegroundSession? _session;
    private string? _sessionRunnerId;
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
    private readonly HashSet<string> _logoTiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, global::Windows.UI.Color> _dominantColors = new(StringComparer.OrdinalIgnoreCase);
    private string? _ambientPath;
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

        // Fullskjerm uten oppgavelinje og vindusknapper. Uten Windows' fullskjermmodus (Gaming Home) sto GameHome som et
        // vanlig vindu med oppgavelinjen synlig (skjermbilder 2026-11-05).
        try
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _log.Write("start", $"Fikk ikke fullskjerm: {ex.Message}");
        }

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
        _metricsTimer.Tick += (_, _) =>
        {
            UpdateMemory();
            UpdateClock();
            _ = RefreshHostStatusAsync(); // Høyst hvert 10. sekund, bare mens Home er aktiv og ingen økt kjører.
        };

        _hotkeys = new HotkeyListener(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _hotkeys.Pressed += OnSessionHotkey;
        RegisterHomeHotkey();
        _hotkeys.Failed += (_, message) => _log.Write("krasj", $"Fanget i kall fra Windows: {message}");

        Gamepad.GamepadAdded += (_, _) => _log.Write("kontroller", string.Create(CultureInfo.InvariantCulture, $"Kontroller tilkoblet ({Gamepad.Gamepads.Count} i alt)"));
        Gamepad.GamepadRemoved += (_, _) => _log.Write("kontroller", "Kontroller frakoblet");

        _libraryPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "library.json");
        _library = LoadLibrary();
        _playStats = PlayStatsStore.Load(PlayStatsPath);
        _details = GameDetailsStore.Load(DetailsPath);
        MenuSounds.Enabled = ApplicationData.Current.LocalSettings.Values[SoundSetting] is not false;
        SoundToggleText.Text = MenuSounds.Enabled ? "Menylyder: på" : "Menylyder: av";
        UpdateClock();
        ShowM2Fallback();
        ShowSteamCloseSetting();
        ShowSteamLibraryTile();
        BuildGameTiles();

        // Pakkeversjonen er den som installeres og oppgraderes (0.1.N.0); assembly-versjonen er alltid 1.0.0.0.
        var version = global::Windows.ApplicationModel.Package.Current.Id.Version;
        var versionText = string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
        InfoText.Text = $"Versjon {versionText}";

        var variant = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT";
        _log.Write("start", $"Vindu klart (versjon {versionText}, {variant})");
    }

    private bool SessionActive => _session is not null && !_session.Completion.IsCompleted;

    // ---- Spilletid, spillinfo, klokke og lyd ----

    private const string SoundSetting = "home.sounds";
    private readonly Dictionary<string, PlayStat> _playStats;
    private readonly Dictionary<string, GameDetails> _details;

    private static string PlayStatsPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "playstats.json");

    private static string DetailsPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "details.json");

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Legger økten til spillets spilletid (målt av GameHome, lagres bare lokalt).</summary>
    private void RecordPlayTime(Game game, double minutes)
    {
        _playStats[game.Id] = PlayStatsStore.Add(_playStats.GetValueOrDefault(game.Id), DateTime.UtcNow, minutes);
        try
        {
            PlayStatsStore.Save(PlayStatsPath, _playStats);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("bibliotek", $"Kunne ikke lagre spilletid: {ex.Message}");
        }

        BuildGameTiles(); // «Fortsett» følger sist spilt.
    }

    private void SaveDetails()
    {
        try
        {
            GameDetailsStore.Save(DetailsPath, _details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("info", $"Kunne ikke lagre spillinfo: {ex.Message}");
        }
    }

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        MenuSounds.Enabled = !MenuSounds.Enabled;
        ApplicationData.Current.LocalSettings.Values[SoundSetting] = MenuSounds.Enabled;
        SoundToggleText.Text = MenuSounds.Enabled ? "Menylyder: på" : "Menylyder: av";
        MenuSounds.Select();
        _log.Write("innstillinger", MenuSounds.Enabled ? "Menylyder slått på" : "Menylyder slått av");
    }

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

    /// <summary>Så mange spill viser Home i «Fortsett». Resten ligger i Biblioteket.</summary>
    private const int HomeRowLimit = 10;

    private const string PinnedSetting = "home.pinned";

    /// <summary>Spill brukeren har festet i Home, i rekkefølgen de ble festet.</summary>
    private static List<string> LoadPinned()
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values[PinnedSetting] is string list
                ? [.. list.Split('|', StringSplitOptions.RemoveEmptyEntries)]
                : [];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SavePinned(List<string> pinned)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[PinnedSetting] = string.Join('|', pinned);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("home", $"Kunne ikke lagre festede spill: {ex.Message}");
        }
    }

    private DateTime LastPlayed(Game game) =>
        _playStats.TryGetValue(game.Id, out var s) && s.Minutes >= 1 ? s.LastPlayedUtc : DateTime.MinValue;

    private double MinutesPlayed(Game game) => _playStats.TryGetValue(game.Id, out var s) ? s.Minutes : 0;

    /// <summary>
    /// «Fortsett» i Home: festede spill først, så sist spilte, så nylig lagt til (nyeste først), høyst
    /// <see cref="HomeRowLimit"/>. Det første spillet er det du mest sannsynlig vil starte.
    /// </summary>
    private List<Game> HomeGames()
    {
        var pinned = LoadPinned();
        var all = _library.VisibleGames.Select((g, index) => (Game: g, Index: index)).ToList();
        return
        [
            .. all.Where(x => pinned.Contains(x.Game.Id))
                .OrderBy(x => pinned.IndexOf(x.Game.Id))
                .Concat(all.Where(x => !pinned.Contains(x.Game.Id))
                    .OrderByDescending(x => LastPlayed(x.Game))
                    .ThenByDescending(x => x.Index))
                .Select(x => x.Game)
                .Take(HomeRowLimit),
        ];
    }

    private void BuildGameTiles()
    {
        var visible = HomeGames();
        var ids = visible.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        for (var i = GameTiles.Children.Count - 1; i >= 0; i--)
        {
            if (GameTiles.Children[i] is not Button { Tag: string id } || !ids.Contains(id))
            {
                GameTiles.Children.RemoveAt(i);
            }
        }

        // XamlRoot finnes ikke før vinduet er vist (første oppbygging skjer i konstruktøren; 0.1.134.0 krasjet ved oppstart).
        var focused = Root.XamlRoot is { } xamlRoot ? FocusManager.GetFocusedElement(xamlRoot) : null;
        for (var i = 0; i < visible.Count; i++)
        {
            var existing = TileFor(visible[i].Id);
            if (existing is null)
            {
                GameTiles.Children.Insert(Math.Min(i, GameTiles.Children.Count), CreateTile(visible[i]));
                continue;
            }

            SetTileContent(existing, visible[i]);

            // Riktig rekkefølge (sist spilt først). Flisen som har fokus, flyttes ikke: å fjerne den mens den har fokus
            // krasjet appen tidligere (0.1.27.0).
            var at = GameTiles.Children.IndexOf(existing);
            if (at != i && !ReferenceEquals(existing, focused))
            {
                GameTiles.Children.RemoveAt(at);
                GameTiles.Children.Insert(Math.Min(i, GameTiles.Children.Count), existing);
            }
        }

        // Flisen med fokus kan ha flyttet seg; sørg for at den er synlig i raden.
        if (focused is Button { Tag: string } focusedTile && GameTiles.Children.Contains(focusedTile))
        {
            focusedTile.StartBringIntoView();
        }

        var hidden = _library.VisibleGames.Count - visible.Count;
        LibraryTileText.Text = hidden > 0 ? $"Bibliotek ({_library.VisibleGames.Count})" : "Bibliotek";
        if (LibraryPanel.Visibility == Visibility.Visible)
        {
            BuildLibrary();
        }
    }

    // Flisene er 16:9, som spillenes brede bilder. Flisen i fokus forstørres litt og får kant i bildets egen farge; de
    // andre dempes, så blikket går dit fokus er (docs/research/2026-11-02-designsprak.md).
    private const double TileWidth = 320;
    private const double TileHeight = 180;
    private const double FocusScale = 1.06;
    private const double DimmedOpacity = 0.62;
    private static readonly TimeSpan FocusDuration = TimeSpan.FromMilliseconds(130);

    private Button CreateTile(Game game)
    {
        var tile = new Button
        {
            Tag = game.Id,
            Width = TileWidth,
            Height = TileHeight,
            FontSize = 22,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform(),
            Opacity = DimmedOpacity,
        };
        SetTileContent(tile, game);
        tile.Click += GameTile_Click;
        tile.GotFocus += (_, _) =>
        {
            RememberTile(game.Id);
            OnTileFocused(tile, game.Id);
        };
        tile.LostFocus += (_, _) => OnTileUnfocused(tile);
        return tile;
    }

    /// <summary>Forstørrer flisen, gir den kant i bildets dominerende farge og bytter bakgrunnen («ambilight»).</summary>
    private void OnTileFocused(Button tile, string gameId)
    {
        AnimateFocus(tile, FocusScale, 1.0);
        if (_library.Find(gameId) is not { } game)
        {
            return;
        }

        var inLibrary = LibraryPanel.Visibility == Visibility.Visible;
        if (!inLibrary)
        {
            UpdateHero(game);
        }

        if (TileImageFor(game) is not { } image)
        {
            tile.BorderThickness = new Thickness(0);
            return;
        }

        _ = ApplyFocusColorsAsync(tile, gameId, image);
        if (inLibrary)
        {
            return; // Bakgrunnen bak Home synes ikke mens biblioteket er åpent.
        }

        var hero = ArtFor(game).Hero is { } heroFile && File.Exists(heroFile) ? heroFile : null;
        var backdrop = hero ?? image;
        if (!string.Equals(_ambientPath, backdrop, StringComparison.OrdinalIgnoreCase))
        {
            _ambientPath = backdrop;
            _ = LoadAmbientAsync(backdrop, sharp: hero is not null);
        }
    }

    private static void OnTileUnfocused(Button tile)
    {
        AnimateFocus(tile, 1.0, DimmedOpacity);
        tile.BorderThickness = new Thickness(0);
    }

    /// <summary>Kort og myk: forstørring og lysstyrke på 130 ms med ease-out, ingen sprett.</summary>
    private static void AnimateFocus(Button tile, double scaleTo, double opacityTo)
    {
        if (tile.RenderTransform is not Microsoft.UI.Xaml.Media.ScaleTransform scale)
        {
            return;
        }

        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        void Add(DependencyObject target, string property, double to)
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = to,
                Duration = new Duration(FocusDuration),
                EasingFunction = ease,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }

        Add(scale, "ScaleX", scaleTo);
        Add(scale, "ScaleY", scaleTo);
        Add(tile, "Opacity", opacityTo);
        story.Begin();
    }

    /// <summary>Animerer en egenskap kort (paneler inn på 200 ms).</summary>
    private static void FadeIn(UIElement element, double durationMs = 200)
    {
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
        story.Children.Add(animation);
        story.Begin();
    }

    private async Task ApplyFocusColorsAsync(Button tile, string gameId, string image)
    {
        var color = await DominantColorAsync(image);
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is Button { Tag: string focused } && focused == gameId)
        {
            tile.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            tile.BorderThickness = new Thickness(3);
        }
    }

    /// <summary>
    /// Bakgrunnen: spillets hero-bilde i full størrelse, eller (uten hero) omslaget dekodet svært lite (48 px) og strukket
    /// ut, som gir en myk uskarphet. Krysstoning på 380 ms: det nye bildet toner inn over det gamle, som toner ut, så
    /// skjermen aldri blinker mørk mellom to spill. Det gamle bildet slippes etterpå (bare to bilder i minnet).
    /// </summary>
    private async Task LoadAmbientAsync(string path, bool sharp)
    {
        if (await LoadImageAsync(path, sharp ? ScreenPx(0.85) : 48) is not { } bitmap
            || !string.Equals(_ambientPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var incoming = _ambientFrontIsA ? AmbientImageB : AmbientImage;
        var outgoing = _ambientFrontIsA ? AmbientImage : AmbientImageB;
        _ambientFrontIsA = !_ambientFrontIsA;
        incoming.Source = bitmap;
        // Bakgrunnslagene ligger alltid under slør, tekst og fliser (som har Z 0); bare innbyrdes byttes de.
        Canvas.SetZIndex(incoming, -1);
        Canvas.SetZIndex(outgoing, -2);

        var ease = new Microsoft.UI.Xaml.Media.Animation.SineEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(380));
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        void Fade(Image target, double to)
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            story.Children.Add(animation);
        }

        incoming.Opacity = 0;
        Fade(incoming, sharp ? 0.9 : 0.5);
        Fade(outgoing, 0);
        story.Completed += (_, _) =>
        {
            if (outgoing.Opacity == 0)
            {
                outgoing.Source = null;
            }
        };
        story.Begin();
    }

    private bool _ambientFrontIsA = true;

    // ---- Hovedfeltet: spillet i fokus ----

    private string? _heroGameId;
    private string? _heroLogoPath;

    /// <summary>Logo eller tittel, én linje sanne fakta og hva knappene gjør, for spillet i fokus.</summary>
    private void UpdateHero(Game game)
    {
        var changed = _heroGameId != game.Id;
        _heroGameId = game.Id;
        var logo = ArtFor(game).Logo is { } file && File.Exists(file) ? file : null;
        if (logo != _heroLogoPath || changed)
        {
            _heroLogoPath = logo;
            HeroLogo.Source = null;
            if (logo is not null)
            {
                _ = LoadTileImageAsync(HeroLogo, logo, Px(520));
            }
        }

        HeroTitle.Text = logo is null ? GameLibrary.DisplayTitle(game) : "";
        HeroFacts.Text = HeroFactsFor(game);
        HeroHint.Text = (SessionActive && _sessionGame?.Id == game.Id) || IsRunningOnHost(game)
            ? "A  Fortsett      Y  Om spillet      ☰  Valg"
            : "A  Spill      Y  Om spillet      ☰  Valg";
        if (changed)
        {
            HeroFadeIn.Begin();
        }
    }

    /// <summary>Kort og sann: kjører nå, sist spilt og spilletid, hvor spillet spilles, sjanger og år.</summary>
    private string HeroFactsFor(Game game)
    {
        var parts = new List<string>();
        if (SessionActive && _sessionGame?.Id == game.Id)
        {
            parts.Add("▶ Kjører nå");
        }
        else if (IsRunningOnHost(game))
        {
            parts.Add($"▶ Kjører på {HostLabel}");
        }
        else if (_playStats.TryGetValue(game.Id, out var stat) && stat.Minutes >= 1)
        {
            parts.Add($"Sist spilt {RelativeDay(stat.LastPlayedUtc)}");
            parts.Add(PlayTimeText(stat.Minutes));
        }

        parts.Add(ModesText(game));
        if (_details.TryGetValue(game.Id, out var details))
        {
            if (details.Genres.Count > 0)
            {
                parts.Add(details.Genres[0]);
            }

            if (YearOf(details.Released) is { } year)
            {
                parts.Add(year);
            }
        }

        return string.Join("  ·  ", parts);
    }

    private string ModesText(Game game)
    {
        var actions = _library.AvailableActionsFor(game.Id);
        var local = actions.Any(a => !a.IsRemote);
        var remote = actions.Any(a => a.IsRemote);
        return local && remote ? $"Her og på {HostLabel}" : remote ? $"Fra {HostLabel}" : "På Legion Go";
    }

    private static string PlayTimeText(double minutes) =>
        minutes < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes:F0} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:0.#} t").Replace('.', ',');

    private static string RelativeDay(DateTime utc)
    {
        var days = (DateTime.Now.Date - utc.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "i dag",
            1 => "i går",
            < 7 => string.Create(CultureInfo.InvariantCulture, $"for {days} dager siden"),
            < 60 => string.Create(CultureInfo.InvariantCulture, $"for {days / 7} uker siden"),
            _ => utc.ToLocalTime().ToString("d. MMM yyyy", CultureInfo.GetCultureInfo("nb-NO")),
        };
    }

    private static string? YearOf(string? released) =>
        released is null ? null
        : System.Text.RegularExpressions.Regex.Match(released, @"(19|20)\d\d") is { Success: true } match ? match.Value : null;

    /// <summary>
    /// Bildets dominerende farge: snitt av et 16 × 16-utsnitt, vektet mot fargerike piksler, og løftet så den lyser som
    /// en kant. Bufres per bilde.
    /// </summary>
    private async Task<global::Windows.UI.Color> DominantColorAsync(string path)
    {
        if (_dominantColors.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var fallback = global::Windows.UI.Color.FromArgb(255, 120, 170, 255);
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            var data = await decoder.GetPixelDataAsync(
                global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
                new global::Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = 16, ScaledHeight = 16 },
                global::Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                global::Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var pixels = data.DetachPixelData();
            double r = 0, g = 0, b = 0, weight = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var w = 0.05 + ((max - min) / 255.0 * (max / 255.0));
                r += pr * w;
                g += pg * w;
                b += pb * w;
                weight += w;
            }

            if (weight <= 0)
            {
                return fallback;
            }

            r /= weight;
            g /= weight;
            b /= weight;
            var lift = 230 / Math.Max(1, Math.Max(r, Math.Max(g, b)));
            var color = global::Windows.UI.Color.FromArgb(255,
                (byte)Math.Min(255, r * lift), (byte)Math.Min(255, g * lift), (byte)Math.Min(255, b * lift));
            _dominantColors[path] = color;
            return color;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Flisens innhold: bilde (Steams lokale bilder, Steams butikkbilder eller omslag) under et mørkt slør, med spillets
    /// logo nederst til venstre, eller tittelen når logo mangler. Uten bilde vises bare tittelen.
    /// </summary>
    private void SetTileContent(Button tile, Game game)
    {
        // Navn for skjermlesere og for testskriptet (scripts/Test-LegionGo.ps1), som finner flisen med UI Automation.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, GameLibrary.DisplayTitle(game));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "game:" + game.Id);
        var image = TileImageFor(game);
        if (image is null)
        {
            tile.Content = TileText(game);
            _tileTitles.Remove(game.Id);
            return;
        }

        var logoPath = ArtFor(game).Logo is { } logoFile && File.Exists(logoFile) ? logoFile : null;
        var running = SessionActive && _sessionGame?.Id == game.Id;
        var title = new TextBlock
        {
            Text = logoPath is null ? TileText(game) : "▶ Kjører",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(14, 0, 14, 12),
            VerticalAlignment = logoPath is null ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            HorizontalAlignment = logoPath is null ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Visibility = logoPath is null || running ? Visibility.Visible : Visibility.Collapsed,
        };
        if (logoPath is not null)
        {
            title.Margin = new Thickness(12, 10, 12, 0);
        }

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
        var grid = new Grid { CornerRadius = new CornerRadius(8) };
        grid.Children.Add(picture);
        grid.Children.Add(scrim);
        if (logoPath is not null)
        {
            var logo = new Image
            {
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                MaxHeight = 70,
                MaxWidth = 210,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 14, 12),
            };
            grid.Children.Add(logo);
            _ = LoadTileImageAsync(logo, logoPath, Px(210));
            _logoTiles.Add(game.Id);
        }
        else
        {
            _logoTiles.Remove(game.Id);
        }

        grid.Children.Add(title);
        tile.Content = grid;
        _tileTitles[game.Id] = title;
        _ = LoadTileImageAsync(picture, image, Px(TileWidth * FocusScale));
    }

    private async Task LoadTileImageAsync(Image target, string path, int decodeWidth)
    {
        if (await LoadImageAsync(path, decodeWidth) is { } bitmap)
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

    /// <summary>
    /// Bildene for et spill: brukerens egne valg (Velg bilder) først, så Steams lokale bildecache for Steam-spill på
    /// Legion Go, så det som er hentet.
    /// </summary>
    private ArtSet ArtFor(Game game)
    {
        var baseArt = game.Art;
        var steam = _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.SteamRunnerId);
        if (steam is not null && SteamClient.FindRoot() is { } root)
        {
            var local = SteamArt.Find(root, steam.Target);
            baseArt = new ArtSet(local.Cover ?? game.Art.Cover, local.Hero ?? game.Art.Hero, local.Logo ?? game.Art.Logo);
        }

        static string? Pick(string? chosen, string? fallback) => chosen is not null && File.Exists(chosen) ? chosen : fallback;
        return game.Overrides.Art is { } mine
            ? new ArtSet(Pick(mine.Cover, baseArt.Cover), Pick(mine.Hero, baseArt.Hero), Pick(mine.Logo, baseArt.Logo))
            : baseArt;
    }

    private string TileText(Game game) =>
        SessionActive && _sessionGame?.Id == game.Id ? $"▶ {GameLibrary.DisplayTitle(game)}"
        : IsRunningOnHost(game) ? $"▶ {GameLibrary.DisplayTitle(game)} · på {HostLabel}"
        : GameLibrary.DisplayTitle(game);

    private void RefreshTileTexts()
    {
        foreach (var tile in GameTiles.Children.OfType<Button>())
        {
            if (tile.Tag is string id && _library.Find(id) is { } game)
            {
                if (_tileTitles.TryGetValue(id, out var title) && _logoTiles.Contains(id))
                {
                    // Fliser med logo viser bare «▶ Kjører» når spillet kjører.
                    var onHost = IsRunningOnHost(game);
                    title.Text = onHost ? $"▶ På {HostLabel}" : "▶ Kjører";
                    title.Visibility = (SessionActive && _sessionGame?.Id == id) || onHost ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (title is not null)
                {
                    title.Text = TileText(game);
                }
                else
                {
                    tile.Content = TileText(game);
                }
            }
        }

        if (_heroGameId is not null && _library.Find(_heroGameId) is { } hero)
        {
            UpdateHero(hero);
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
        if (LibraryPanel.Visibility == Visibility.Visible)
        {
            FocusLibrary(gameId);
            return;
        }

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

    /// <summary>Ett sted å legge til spill: her på Legion Go (programfil) eller fra gaming-PC-en (installerte spill der).</summary>
    private void AddGameTile_Click(object sender, RoutedEventArgs e)
    {
        _log.Write("valg", $"Legg til spill valgt via {_lastSource}");
        ShowMessage("Legg til spill", $"Hvor er spillet installert?",
            "Her på Legion Go", () => _ = AddLocalGameAsync(),
            $"På {HostLabel}", () => _ = OpenHostPanelAsync(),
            onCancel: () => AddGameTile.Focus(FocusState.Keyboard));
    }

    private async Task AddLocalGameAsync()
    {
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
            _pickerOpen = true;
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.Write("bibliotek", $"Filvelgeren feilet: {ex.Message}");
            return;
        }
        finally
        {
            _pickerOpen = false;
        }

        if (file is null)
        {
            _log.Write("bibliotek", "Ingen fil valgt");
            AddGameTile.Focus(FocusState.Keyboard);
            return;
        }

        // Navnet: produktnavnet i filen, filnavnet, eller mappene over (ACC.exe i «Assassin's Creed Rogue-273»).
        string? product = null;
        try
        {
            product = FileVersionInfo.GetVersionInfo(file.Path).ProductName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("bibliotek", $"Kunne ikke lese produktnavnet: {ex.Message}");
        }

        var candidates = TitleGuess.Candidates(file.Path, product);
        var (game, added) = _library.AddExecutable(file.Path, candidates[0].Title);
        if (added)
        {
            SaveLibrary();
            BuildGameTiles();
        }

        _log.Write("bibliotek", added
            ? $"La til «{game.Title}» ({file.Path}); forslag: {string.Join(", ", candidates.Select(c => $"{c.Title} ({c.Origin})"))}"
            : $"«{game.Title}» finnes allerede");
        LastInput.Text = added ? $"La til {game.Title}. Trykk A for å starte." : $"{game.Title} finnes allerede.";
        FocusHome(game.Id);
        if (added)
        {
            _ = IdentifyNewGameAsync(game, candidates);
        }
    }

    // ---- Riktig spill: Steam-treff for spill lagt til som programfil ----

    /// <summary>Steam-appid for bilder og info: Steam-spillet selv, eller treffet brukeren eller GameHome har valgt.</summary>
    private int? SteamAppIdFor(Game game)
    {
        var steam = _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.SteamRunnerId);
        var value = steam?.Target ?? _library.Find(game.Id)?.Identities.FirstOrDefault(i => i.Namespace == GameLibrary.SteamStoreIdentity)?.Value;
        return int.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static string OriginText(TitleOrigin origin) => origin switch
    {
        TitleOrigin.ProductName => "fra programfilen",
        TitleOrigin.Folder => "fra mappenavnet",
        _ => "fra filnavnet",
    };

    /// <summary>
    /// Prøver forslagene mot Steam (tilgivende, alle ord må stemme). Treff: Steams navn og appid lagres, bilder og info
    /// hentes. Så spørres brukeren om det stemmer, med «Finn riktig spill» som alternativ.
    /// </summary>
    private async Task IdentifyNewGameAsync(Game game, IReadOnlyList<TitleCandidate> candidates)
    {
        ArtProgressPanel.Visibility = Visibility.Visible;
        ArtProgress.IsIndeterminate = true;
        ArtProgressText.Text = $"Finner «{candidates[0].Title}» på Steam …";
        SteamSearchHit? hit = null;
        TitleCandidate? used = null;
        try
        {
            var store = new SteamStoreClient(ArtHttp);
            foreach (var candidate in candidates.Take(3))
            {
                if ((hit = await store.FindMatchAsync(candidate.Title)) is not null)
                {
                    used = candidate;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.Write("bibliotek", $"Søk på Steam feilet: {ex.Message}");
        }
        finally
        {
            ArtProgress.IsIndeterminate = false;
            ArtProgressPanel.Visibility = Visibility.Collapsed;
        }

        if (hit is not null && used is not null)
        {
            await ApplySteamHitAsync(game, hit);
            var title = hit.Name;
            _log.Write("bibliotek", $"«{used.Title}» ({used.Origin}) funnet på Steam som «{title}» (appid {hit.Id})");
            if (!AnyOverlayOpen)
            {
                ShowMessage($"La til {title}", $"Navnet er hentet {OriginText(used.Origin)} og funnet på Steam. Stemmer det?",
                    "Ja, det stemmer", () => FocusHome(game.Id),
                    "Finn riktig spill", () => ShowFind(game),
                    onCancel: () => FocusHome(game.Id));
            }

            return;
        }

        _log.Write("bibliotek", $"Fant ikke «{candidates[0].Title}» på Steam");
        AddNotice("art:" + game.Id, game.Id, $"Fant ikke «{candidates[0].Title}» på Steam. Riktig navn gir bilder og info.");
        if (!AnyOverlayOpen)
        {
            ShowMessage($"La til {candidates[0].Title}", $"Navnet er hentet {OriginText(candidates[0].Origin)}, men GameHome fant ikke spillet på Steam.",
                "Finn riktig spill", () => ShowFind(game),
                "Behold navnet", () => FocusHome(game.Id),
                onCancel: () => FocusHome(game.Id));
        }
    }

    /// <summary>Bruker et Steam-treff: navn og appid lagres, bildene og info hentes på nytt.</summary>
    private async Task ApplySteamHitAsync(Game game, SteamSearchHit hit)
    {
        RemoveNoticesFor(game.Id);
        _library.SetTitle(game.Id, hit.Name);
        _library.SetIdentity(game.Id, GameLibrary.SteamStoreIdentity, hit.Id.ToString(CultureInfo.InvariantCulture));
        if (_library.Find(game.Id) is { Overrides.Title: not null } renamed)
        {
            _library.SetOverrides(game.Id, renamed.Overrides with { Title = null });
        }

        SaveLibrary();
        _details.Remove(game.Id);
        try
        {
            if (await DownloadArtAsync(game, SteamStoreClient.ImageUrls(hit.Id)) is { } art)
            {
                _library.SetArt(game.Id, art);
                SaveLibrary();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            _log.Write("bilder", $"Bilder for «{hit.Name}» feilet: {ex.Message}");
        }

        _ambientPath = null;
        _heroLogoPath = null;
        if (TileFor(game.Id) is { } tile && _library.Find(game.Id) is { } updated)
        {
            SetTileContent(tile, updated);
            UpdateHero(updated);
        }

        if (_library.Find(game.Id) is { } current)
        {
            _ = FetchDetailsAsync(current, force: true);
        }
    }

    private Game? _findGame;

    private void ShowFind(Game game)
    {
        _findGame = _library.Find(game.Id) ?? game;
        FindTitle.Text = GameLibrary.DisplayTitle(_findGame);
        FindQuery.Text = GameLibrary.DisplayTitle(_findGame);
        FindResults.Children.Clear();
        FindStatus.Text = "Søker …";
        FindPanel.Visibility = Visibility.Visible;
        FadeIn(FindPanel);
        SetHomeEnabled(false);
        FindSearchButton.Focus(FocusState.Keyboard);
        _ = RunFindAsync();
    }

    private void FindSearchButton_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync();

    private void FindQuery_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = RunFindAsync();
        }
    }

    /// <summary>Fem beste treff på Steam for teksten i søkefeltet, som knapper med lite bilde.</summary>
    private async Task RunFindAsync()
    {
        if (_findLibrary)
        {
            await RunLibraryAsync();
            return;
        }

        var term = FindQuery.Text.Trim();
        if (term.Length < 2)
        {
            FindStatus.Text = "Skriv navnet på spillet.";
            return;
        }

        FindStatus.Text = $"Søker etter «{term}» på Steam …";
        FindResults.Children.Clear();
        IReadOnlyList<SteamSearchHit> hits;
        try
        {
            hits = await new SteamStoreClient(ArtHttp).SearchAsync(term, 5);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            FindStatus.Text = $"Søket feilet (ingen nettverk?): {ex.Message}";
            return;
        }

        if (hits.Count == 0)
        {
            FindStatus.Text = $"Ingen treff på «{term}». Prøv et annet navn, eller bruk navnet uten Steam.";
            return;
        }

        FindStatus.Text = "Velg riktig spill:";
        foreach (var hit in hits)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            if (hit.TinyImage is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Små bilder (120 × 45) rett fra Steam; slippes når panelet lukkes.
                row.Children.Add(new Image
                {
                    Width = 120,
                    Height = 45,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new BitmapImage(uri) { DecodePixelWidth = Px(120) },
                });
            }

            row.Children.Add(new TextBlock { Text = hit.Name, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 64,
                Padding = new Thickness(12, 0, 12, 0),
            };
            button.Click += async (_, _) =>
            {
                if (_findGame is not { } game)
                {
                    return;
                }

                FindStatus.Text = $"Bruker «{hit.Name}» …";
                _log.Write("bibliotek", $"«{GameLibrary.DisplayTitle(game)}» satt til «{hit.Name}» (appid {hit.Id}) fra Finn riktig spill");
                await ApplySteamHitAsync(game, hit);
                CloseFind();
            };
            FindResults.Children.Add(button);
        }

        if (FindResults.Children[0] is Button first)
        {
            first.Focus(FocusState.Keyboard);
        }
    }

    /// <summary>Bare navnet, uten Steam: lagres som brukerens eget navn (vinner over alt).</summary>
    private void FindUseNameButton_Click(object sender, RoutedEventArgs e)
    {
        var name = FindQuery.Text.Trim();
        if (_findGame is not { } game || name.Length < 1)
        {
            return;
        }

        _library.SetOverrides(game.Id, game.Overrides with { Title = name });
        SaveLibrary();
        RefreshTileTexts();
        if (TileFor(game.Id) is { } tile && _library.Find(game.Id) is { } updated)
        {
            SetTileContent(tile, updated);
        }

        _log.Write("bibliotek", $"«{game.Title}» fikk navnet «{name}»");
        CloseFind();
    }

    private void FindCloseButton_Click(object sender, RoutedEventArgs e) => CloseFind();

    // ---- Steam-biblioteket: eide spill som ikke er installert, installeres med Steams egen steam://install (nøkkel kreves) ----

    private SteamWebApiClient? _steamWebApi;
    private bool _steamWebApiChecked;
    private bool _findLibrary;
    private IReadOnlyList<SteamOwnedGame>? _ownedGames;

    private SteamWebApiClient? SteamWebApi
    {
        get
        {
            if (!_steamWebApiChecked)
            {
                _steamWebApiChecked = true;
                if (Secrets.Load(Secrets.SteamWebApiKey, KeyLog) is { } key && Secrets.Load(Secrets.SteamId, KeyLog) is not null)
                {
                    _steamWebApi = new SteamWebApiClient(ArtHttp, key);
                    _log.Write("steam", "Steam Web API-nøkkel funnet; «Installer fra Steam» er på");
                }
            }

            return _steamWebApi;
        }
    }

    private void ShowSteamLibraryTile() => SteamLibraryTile.Visibility = SteamWebApi is null ? Visibility.Collapsed : Visibility.Visible;

    private void SteamLibraryTile_Click(object sender, RoutedEventArgs e)
    {
        _log.Write("valg", "Installer fra Steam valgt");
        _findLibrary = true;
        _findGame = null;
        FindHeading.Text = "Steam-biblioteket";
        FindTitle.Text = "Spill du eier som ikke er installert";
        FindQuery.Text = "";
        FindUseNameButton.Visibility = Visibility.Collapsed;
        FindResults.Children.Clear();
        FindStatus.Text = "Henter biblioteket …";
        FindPanel.Visibility = Visibility.Visible;
        FadeIn(FindPanel);
        SetHomeEnabled(false);
        FindSearchButton.Focus(FocusState.Keyboard);
        _ = RunLibraryAsync();
    }

    /// <summary>Eide Steam-spill som ikke er installert, filtrert på søketeksten, mest spilt først (høyst 30).</summary>
    private async Task RunLibraryAsync()
    {
        if (SteamWebApi is not { } api || Secrets.Load(Secrets.SteamId, KeyLog) is not { } who)
        {
            FindStatus.Text = "Steam-nøkkelen er ikke lagt inn.";
            return;
        }

        if (_ownedGames is null)
        {
            var id = ulong.TryParse(who, CultureInfo.InvariantCulture, out var parsed) ? parsed : await api.ResolveVanityUrlAsync(who);
            if (id is not { } steamId)
            {
                FindStatus.Text = "Fant ikke Steam-profilen. Sjekk Steam-ID-en.";
                return;
            }

            _ownedGames = await api.GetOwnedGamesAsync(steamId);
            if (_ownedGames is null)
            {
                FindStatus.Text = "Fikk ikke hentet biblioteket fra Steam (nettverk eller nøkkel).";
                return;
            }

            _log.Write("steam", string.Create(CultureInfo.InvariantCulture, $"Steam-biblioteket: {_ownedGames.Count} spill"));
        }

        var installed = _library.Games
            .SelectMany(g => _library.PlayActionsFor(g.Id))
            .Where(a => a.RunnerId == GameLibrary.SteamRunnerId && a.Availability == Availability.Installed)
            .Select(a => a.Target)
            .ToHashSet(StringComparer.Ordinal);
        var term = FindQuery.Text.Trim();
        var candidates = _ownedGames
            .Where(g => !installed.Contains(g.AppId.ToString(CultureInfo.InvariantCulture)))
            .Where(g => term.Length == 0 || (g.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(g => g.PlaytimeForeverMinutes)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(30)
            .ToList();

        FindResults.Children.Clear();
        if (candidates.Count == 0)
        {
            FindStatus.Text = term.Length == 0 ? "Alle spillene dine er installert, eller profilen er privat." : $"Ingen ikke-installerte spill med «{term}».";
            return;
        }

        FindStatus.Text = "Velg et spill for å installere det med Steam:";
        foreach (var owned in candidates)
        {
            var name = owned.Name ?? owned.AppId.ToString(CultureInfo.InvariantCulture);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var capsule = SteamStoreClient.ImageUrls(owned.AppId).Hero.Replace("library_hero.jpg", "capsule_184x69.jpg", StringComparison.Ordinal);
            row.Children.Add(new Image
            {
                Width = 120,
                Height = 45,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(capsule)) { DecodePixelWidth = Px(120) },
            });
            row.Children.Add(new TextBlock { Text = name, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 64,
                Padding = new Thickness(12, 0, 12, 0),
            };
            button.Click += (_, _) =>
            {
                try
                {
                    using var _ = Process.Start(new ProcessStartInfo($"steam://install/{owned.AppId.ToString(CultureInfo.InvariantCulture)}") { UseShellExecute = true });
                    _log.Write("steam", $"Installasjon av «{name}» (appid {owned.AppId}) startet i Steam");
                    FindStatus.Text = $"Steam viser installasjonen av «{name}». Når den er ferdig, dukker spillet opp i Home.";
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    FindStatus.Text = $"Steam kunne ikke åpnes: {ex.Message}";
                }
            };
            FindResults.Children.Add(button);
        }

        if (FindResults.Children[0] is Button first)
        {
            first.Focus(FocusState.Keyboard);
        }
    }

    private void CloseFind()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        FindResults.Children.Clear();
        if (_findLibrary)
        {
            _findLibrary = false;
            FindHeading.Text = "Finn riktig spill";
            FindUseNameButton.Visibility = Visibility.Visible;
            SetHomeEnabled(true);
            SteamLibraryTile.Focus(FocusState.Keyboard);
            return;
        }

        SetHomeEnabled(true);
        FocusHome(_findGame?.Id ?? _focusGameId);
    }

    private void OptionFind_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is { } game)
        {
            CloseOptions(focusHome: false);
            ShowFind(game);
        }
    }

    // ---- Velg bilder: én fane per kilde, bakgrunn/logo/omslag i hver ----

    private const string ArtHero = "hero";
    private const string ArtLogo = "logo";
    private const string ArtCover = "cover";

    /// <summary>Et bildevalg: type, forhåndsbilde og selve bildet (adresse på nett eller fil på disk).</summary>
    private sealed record ArtOption(string Kind, string Preview, string Full);

    private Game? _artGame;
    private string _artTab = "steam-store";

    private void OptionArt_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is { } game)
        {
            CloseOptions(focusHome: false);
            ShowArtPicker(game);
        }
    }

    /// <summary>Kildene som gir mening for spillet: Steams butikk, Steams lokale bilder, gaming-PC-en og SteamGridDB.</summary>
    private List<(string Id, string Name)> ArtSources(Game game)
    {
        var sources = new List<(string, string)> { ("steam-store", "Steam Store") };
        if (_library.PlayActionsFor(game.Id).Any(a => a.RunnerId == GameLibrary.SteamRunnerId))
        {
            sources.Add(("steam-local", "Steam (lokalt)"));
        }

        if (_library.PlayActionsFor(game.Id).Any(a => a.IsRemote))
        {
            sources.Add(("host", HostLabel));
        }

        sources.Add(("steamgriddb", "SteamGridDB"));
        return sources;
    }

    private void ShowArtPicker(Game game)
    {
        _artGame = _library.Find(game.Id) ?? game;
        ArtTitle.Text = GameLibrary.DisplayTitle(_artGame);
        _artTab = "steam-store";
        BuildArtTabs();
        ArtPanel.Visibility = Visibility.Visible;
        FadeIn(ArtPanel);
        SetHomeEnabled(false);
        _ = FillArtTabAsync();
    }

    private void BuildArtTabs()
    {
        ArtTabs.Children.Clear();
        if (_artGame is not { } game)
        {
            return;
        }

        foreach (var (id, name) in ArtSources(game))
        {
            var selected = id == _artTab;
            var tab = new Button
            {
                Content = name,
                Height = 44,
                Padding = new Thickness(18, 0, 18, 0),
                CornerRadius = new CornerRadius(22),
                FontSize = 16,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                BorderThickness = new Thickness(selected ? 2 : 0),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhTextPrimaryBrush"],
            };
            tab.Click += (_, _) =>
            {
                _artTab = id;
                BuildArtTabs();
                _ = FillArtTabAsync();
            };
            ArtTabs.Children.Add(tab);
            if (selected)
            {
                tab.Focus(FocusState.Keyboard);
            }
        }
    }

    /// <summary>Henter valgene for fanen og viser dem som tre rader: bakgrunn, logo og omslag.</summary>
    private async Task FillArtTabAsync()
    {
        if (_artGame is not { } game)
        {
            return;
        }

        var tab = _artTab;
        ArtSections.Children.Clear();
        ArtStatus.Text = "Henter bilder …";
        List<ArtOption> options = [];
        try
        {
            options = await ArtOptionsAsync(game, tab);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
        {
            ArtStatus.Text = $"Fikk ikke hentet bilder: {ex.Message}";
            return;
        }

        if (tab != _artTab || ArtPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        ArtStatus.Text = tab switch
        {
            "steamgriddb" when SteamGridDb is null => "SteamGridDB har mange bilder laget av brukere, men krever en gratis nøkkel som ikke er lagt inn ennå.",
            _ when options.Count == 0 => "Ingen bilder fra denne kilden.",
            _ => "Velg et bilde med A. Valget ditt vinner over automatiske bilder.",
        };

        var current = ArtFor(game);
        foreach (var (kind, label) in new[] { (ArtHero, "Bakgrunn"), (ArtLogo, "Logo"), (ArtCover, "Omslag") })
        {
            var ofKind = options.Where(o => o.Kind == kind).ToList();
            if (ofKind.Count == 0)
            {
                continue;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(4, 6, 4, 6) };
            foreach (var option in ofKind)
            {
                row.Children.Add(ArtOptionButton(game, option, IsChosen(game, option, current)));
            }

            var section = new StackPanel { Spacing = 6 };
            section.Children.Add(new TextBlock { Text = label, FontSize = 16, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhTextSecondaryBrush"] });
            section.Children.Add(new ScrollViewer
            {
                Content = row,
                HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            ArtSections.Children.Add(section);
        }
    }

    private async Task<List<ArtOption>> ArtOptionsAsync(Game game, string tab)
    {
        var options = new List<ArtOption>();
        switch (tab)
        {
            case "steam-store":
            {
                var store = new SteamStoreClient(ArtHttp);
                if ((SteamAppIdFor(game) ?? await store.FindAppIdAsync(GameLibrary.DisplayTitle(game))) is not { } appId)
                {
                    ArtStatus.Text = "Spillet er ikke funnet på Steam. Bruk «Finn riktig spill» under Valg først.";
                    return options;
                }

                var urls = SteamStoreClient.ImageUrls(appId);
                options.Add(new ArtOption(ArtHero, urls.Hero, urls.Hero));
                options.AddRange((await store.GetScreenshotsAsync(appId)).Select(s => new ArtOption(ArtHero, s.Preview, s.Full)));
                options.Add(new ArtOption(ArtLogo, urls.Logo, urls.Logo));
                options.AddRange(SteamStoreClient.ExtraCoverUrls(appId).Select(u => new ArtOption(ArtCover, u, u)));
                break;
            }

            case "steam-local":
            {
                var steam = _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.SteamRunnerId);
                if (steam is not null && SteamClient.FindRoot() is { } root)
                {
                    var local = SteamArt.Find(root, steam.Target);
                    if (local.Hero is { } h)
                    {
                        options.Add(new ArtOption(ArtHero, h, h));
                    }

                    if (local.Logo is { } l)
                    {
                        options.Add(new ArtOption(ArtLogo, l, l));
                    }

                    if (local.Cover is { } c)
                    {
                        options.Add(new ArtOption(ArtCover, c, c));
                    }
                }

                break;
            }

            case "steamgriddb" when SteamGridDb is { } sgdb:
            {
                var appId = SteamAppIdFor(game);
                var match = appId is { } id ? await sgdb.GetGameBySteamAppIdAsync(id) : null;
                match ??= First(await sgdb.SearchAsync(GameLibrary.DisplayTitle(game)));
                if (match is null)
                {
                    break;
                }

                options.AddRange((await sgdb.GetHeroesAsync(match.Id)).Take(12).Select(i => new ArtOption(ArtHero, i.Thumb ?? i.Url, i.Url)));
                options.AddRange((await sgdb.GetLogosAsync(match.Id)).Take(12).Select(i => new ArtOption(ArtLogo, i.Thumb ?? i.Url, i.Url)));
                options.AddRange((await sgdb.GetGridsAsync(match.Id, SteamGridDbGridShape.Portrait)).Take(12).Select(i => new ArtOption(ArtCover, i.Thumb ?? i.Url, i.Url)));
                break;
            }

            case "host":
            {
                _vibepolloApps ??= Vibepollo is { } api ? await api.GetAppsAsync() : [];
                if (await DownloadVibepolloCoverAsync(game, _vibepolloApps) is { } cover)
                {
                    options.Add(new ArtOption(ArtCover, cover, cover));
                    options.Add(new ArtOption(ArtHero, cover, cover));
                }

                break;
            }
        }

        return options;
    }

    private static string ArtSourceKey(Game game, string kind) => $"artsrc.{game.Id}.{kind}";

    private static bool IsChosen(Game game, ArtOption option, ArtSet current)
    {
        var chosenSource = ApplicationData.Current.LocalSettings.Values[ArtSourceKey(game, option.Kind)] as string;
        if (chosenSource is not null)
        {
            return string.Equals(chosenSource, option.Full, StringComparison.OrdinalIgnoreCase);
        }

        var path = option.Kind switch { ArtHero => current.Hero, ArtLogo => current.Logo, _ => current.Cover };
        return path is not null && string.Equals(path, option.Full, StringComparison.OrdinalIgnoreCase);
    }

    private Button ArtOptionButton(Game game, ArtOption option, bool chosen)
    {
        var (width, height) = option.Kind switch
        {
            ArtLogo => (260.0, 110.0),
            ArtCover => (200.0, 150.0),
            _ => (288.0, 162.0),
        };
        var image = new Image
        {
            Width = width,
            Height = height,
            Stretch = option.Kind == ArtHero ? Microsoft.UI.Xaml.Media.Stretch.UniformToFill : Microsoft.UI.Xaml.Media.Stretch.Uniform,
        };
        if (Uri.TryCreate(option.Preview, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.Ordinal))
        {
            image.Source = new BitmapImage(uri) { DecodePixelWidth = Px(width) };
        }
        else
        {
            _ = LoadTileImageAsync(image, option.Preview, Px(width));
        }

        var accent = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhTextPrimaryBrush"];
        var button = new Button
        {
            Content = new Grid { Children = { image }, Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhSurfaceBrush"] },
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(chosen ? 3 : 0),
            BorderBrush = accent,
        };
        if (chosen)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Valgt");
        }

        button.Click += async (_, _) => await ChooseArtAsync(game, option);
        return button;
    }

    /// <summary>Lagrer valget: lastes ned ved behov og settes som brukerens eget bilde for typen.</summary>
    private async Task ChooseArtAsync(Game game, ArtOption option)
    {
        ArtStatus.Text = "Bruker bildet …";
        string? path = option.Full;
        try
        {
            if (Uri.TryCreate(option.Full, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.Ordinal))
            {
                var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
                Directory.CreateDirectory(folder);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(option.Full)))[..10];
                path = Path.Combine(folder, $"{game.Id}-{option.Kind}-mine-{hash}{Path.GetExtension(uri.AbsolutePath)}");
                if (!File.Exists(path))
                {
                    using var response = await ArtHttp.GetAsync(uri);
                    if (!response.IsSuccessStatusCode)
                    {
                        ArtStatus.Text = "Bildet finnes ikke på Steam for dette spillet.";
                        return;
                    }

                    await using var file = File.Create(path);
                    await response.Content.CopyToAsync(file);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            ArtStatus.Text = $"Fikk ikke hentet bildet: {ex.Message}";
            return;
        }

        if (_library.Find(game.Id) is not { } current)
        {
            return;
        }

        var mine = current.Overrides.Art ?? ArtSet.Empty;
        mine = option.Kind switch
        {
            ArtHero => mine with { Hero = path },
            ArtLogo => mine with { Logo = path },
            _ => mine with { Cover = path },
        };
        _library.SetOverrides(game.Id, current.Overrides with { Art = mine });
        SaveLibrary();
        ApplicationData.Current.LocalSettings.Values[ArtSourceKey(game, option.Kind)] = option.Full;
        _log.Write("bilder", $"«{GameLibrary.DisplayTitle(game)}»: eget bilde for {option.Kind} ({option.Full})");
        RefreshGameArt(game.Id);
        _artGame = _library.Find(game.Id);
        await FillArtTabAsync();
        ArtStatus.Text = "Bildet er i bruk.";
    }

    private void RefreshGameArt(string gameId)
    {
        _ambientPath = null;
        _heroLogoPath = null;
        if (TileFor(gameId) is { } tile && _library.Find(gameId) is { } updated)
        {
            SetTileContent(tile, updated);
            UpdateHero(updated);
            if (TileImageFor(updated) is { } image)
            {
                var hero = ArtFor(updated).Hero is { } h && File.Exists(h) ? h : null;
                _ambientPath = hero ?? image;
                _ = LoadAmbientAsync(_ambientPath, sharp: hero is not null);
            }
        }
    }

    private void ArtResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_artGame is not { } game || _library.Find(game.Id) is not { } current)
        {
            return;
        }

        _library.SetOverrides(game.Id, current.Overrides with { Art = null });
        SaveLibrary();
        foreach (var kind in new[] { ArtHero, ArtLogo, ArtCover })
        {
            ApplicationData.Current.LocalSettings.Values.Remove(ArtSourceKey(game, kind));
        }

        _log.Write("bilder", $"«{GameLibrary.DisplayTitle(game)}»: tilbake til automatiske bilder");
        RefreshGameArt(game.Id);
        _artGame = _library.Find(game.Id);
        _ = FillArtTabAsync();
        ArtStatus.Text = "Tilbake til automatiske bilder.";
    }

    private void ArtCloseButton_Click(object sender, RoutedEventArgs e) => CloseArtPicker();

    private void CloseArtPicker()
    {
        ArtPanel.Visibility = Visibility.Collapsed;
        ArtSections.Children.Clear(); // Forhåndsbildene slippes.
        ArtTabs.Children.Clear();
        SetHomeEnabled(true);
        FocusHome(_artGame?.Id ?? _focusGameId);
    }

    // ---- Om spillet (Y) ----

    private Game? _detailsGame;
    private bool _pickerOpen;

    private bool AnyOverlayOpen => PanelOverLibraryOpen || LibraryPanel.Visibility == Visibility.Visible;

    /// <summary>Et panel er åpent over Home og biblioteket (melding, «Om spillet», valg …).</summary>
    private bool PanelOverLibraryOpen =>
        MessagePanel.Visibility == Visibility.Visible || SessionPanel.Visibility == Visibility.Visible
        || HostPanel.Visibility == Visibility.Visible || PowerPanel.Visibility == Visibility.Visible
        || LoadingPanel.Visibility == Visibility.Visible || DetailsPanel.Visibility == Visibility.Visible
        || OptionsPanel.Visibility == Visibility.Visible || DebugPanel.Visibility == Visibility.Visible
        || FindPanel.Visibility == Visibility.Visible || NoticePanel.Visibility == Visibility.Visible
        || ArtPanel.Visibility == Visibility.Visible;

    private void ShowDetails(Game game)
    {
        _detailsGame = game;
        var art = ArtFor(game);
        DetailsHero.Source = null;
        if ((art.Hero ?? TileImageFor(game)) is { } heroPath && File.Exists(heroPath))
        {
            _ = LoadTileImageAsync(DetailsHero, heroPath, ScreenPx(0.85));
        }

        DetailsLogo.Source = null;
        var logo = art.Logo is { } l && File.Exists(l) ? l : null;
        if (logo is not null)
        {
            _ = LoadTileImageAsync(DetailsLogo, logo, Px(480));
        }

        DetailsTitle.Text = logo is null ? GameLibrary.DisplayTitle(game) : "";
        FillDetails(game);
        DetailsPanel.Visibility = Visibility.Visible;
        FadeIn(DetailsContent);
        SetHomeEnabled(false);
        DetailsPlayButton.Focus(FocusState.Keyboard);
        _log.Write("info", $"Om spillet: «{GameLibrary.DisplayTitle(game)}»");

        if (!_details.ContainsKey(game.Id))
        {
            _ = FetchDetailsAsync(game, force: false);
        }
    }

    private void FillDetails(Game game)
    {
        _details.TryGetValue(game.Id, out var details);
        var facts = new List<string>();
        if (details is not null)
        {
            if (details.Genres.Count > 0)
            {
                facts.Add(string.Join(", ", details.Genres));
            }

            if (details.Released is { } released)
            {
                facts.Add(released);
            }

            if (details.Developer is { } developer)
            {
                facts.Add(details.Publisher is { } publisher && publisher != developer ? $"{developer} / {publisher}" : developer);
            }

            if (details.HoursMain is { } main)
            {
                facts.Add(details.HoursComplete is { } all && all > main
                    ? string.Create(CultureInfo.InvariantCulture, $"ca. {main} t (alt: {all} t)")
                    : string.Create(CultureInfo.InvariantCulture, $"ca. {main} t"));
            }
        }

        DetailsFacts.Text = facts.Count > 0 ? string.Join("  ·  ", facts) : "";
        DetailsFacts.Visibility = facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsDescription.Text = details?.Description ?? "";
        DetailsDescription.Visibility = details?.Description is null ? Visibility.Collapsed : Visibility.Visible;

        DetailsScoreBox.Visibility = details?.Metacritic is null ? Visibility.Collapsed : Visibility.Visible;
        DetailsScore.Text = details?.Metacritic?.ToString(CultureInfo.InvariantCulture) ?? "";

        if (_playStats.TryGetValue(game.Id, out var stat) && stat.Minutes >= 1)
        {
            DetailsPlayed.Text = PlayTimeText(stat.Minutes);
            DetailsPlayedLabel.Text = $"Spilt i GameHome · sist {RelativeDay(stat.LastPlayedUtc)}";
        }
        else
        {
            DetailsPlayed.Text = "Ikke spilt";
            DetailsPlayedLabel.Text = "i GameHome ennå";
        }

        DetailsModes.Text = ModesText(game);
        var resume = (SessionActive && _sessionGame?.Id == game.Id) || IsRunningOnHost(game);
        DetailsPlayText.Text = resume ? "Fortsett" : "Spill";
        DetailsSource.Text = details is null
            ? "Ingen info lagret ennå."
            : $"Info fra {details.Source}, hentet {details.FetchedUtc.ToLocalTime():d. MMM yyyy}."
                + (details.HoursMain is null ? (Igdb is null ? " Spilletid kommer fra IGDB når nøkkelen er lagt inn." : " IGDB har ikke spilletid for dette spillet.") : " Spilletid fra IGDB.");
    }

    /// <summary>Henter spillinfo fra Steam Store (appid fra Steam-spillet, ellers butikksøk på tittelen).</summary>
    private async Task FetchDetailsAsync(Game game, bool force)
    {
        if (!force && _details.ContainsKey(game.Id))
        {
            return;
        }

        var title = GameLibrary.DisplayTitle(game);
        if (SourceFor(game) == SourceHost)
        {
            // Playnite på PC-en gir bare omslag via Vibepollo, ikke tekst; info hentes da ikke.
            if (_detailsGame?.Id == game.Id)
            {
                DetailsSource.Text = $"Kilden er satt til {HostLabel}, som bare gir omslag. Velg Steam Store eller Automatisk under Valg for info.";
            }

            return;
        }

        if (_detailsGame?.Id == game.Id)
        {
            DetailsSource.Text = "Henter info fra Steam Store …";
        }

        try
        {
            var store = new SteamStoreClient(ArtHttp);
            var appId = SteamAppIdFor(game) ?? await store.FindAppIdAsync(title);
            var details = appId is { } steamId ? await store.GetDetailsAsync(steamId) : null;

            // IGDB (når nøklene finnes): spilletid for alle spill, og info for spill Steam ikke har.
            if (await IgdbDetailsAsync(game, appId) is { } igdb)
            {
                details = (details ?? igdb.Details) with
                {
                    HoursMain = Hours(igdb.Time?.HastilySeconds ?? igdb.Time?.NormallySeconds),
                    HoursComplete = Hours(igdb.Time?.CompletelySeconds),
                };
            }

            if (details is null)
            {
                _log.Write("info", $"Fant ikke info om «{title}» på Steam{(Igdb is null ? "" : " eller IGDB")}");
                AddNotice("info:" + game.Id, game.Id, $"Fant ikke info om «{title}».");
                if (_detailsGame?.Id == game.Id)
                {
                    DetailsSource.Text = $"Fant ikke «{title}» på Steam Store{(Igdb is null ? "" : " eller IGDB")}.";
                }

                return;
            }

            _details[game.Id] = details;
            SaveDetails();
            _log.Write("info", $"Info om «{title}» hentet fra {details.Source}{(details.HoursMain is null ? "" : ", med spilletid fra IGDB")}");
            if (_detailsGame?.Id == game.Id && DetailsPanel.Visibility == Visibility.Visible)
            {
                FillDetails(game);
            }

            if (_heroGameId == game.Id)
            {
                UpdateHero(game);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.Write("info", $"Henting av info om «{title}» feilet: {ex.Message}");
            if (_detailsGame?.Id == game.Id)
            {
                DetailsSource.Text = "Fikk ikke hentet info (ingen nettverk?). Prøv igjen fra Valg.";
            }
        }
    }

    /// <summary>Bildebredde i fysiske piksler for en bredde i XAML-enheter (skjermskalering tas med).</summary>
    private int Px(double logical) =>
        (int)Math.Ceiling(Math.Max(1, logical) * (Root.XamlRoot?.RasterizationScale ?? 1.5));

    /// <summary>Bredde for bilder som fyller (en del av) skjermen, i fysiske piksler.</summary>
    private int ScreenPx(double fraction) => Px((Root.ActualWidth > 0 ? Root.ActualWidth : 1280) * fraction);

    private void CloseDetails(bool focusHome = true)
    {
        DetailsPanel.Visibility = Visibility.Collapsed;
        DetailsHero.Source = null; // Det store bildet trengs ikke når panelet er lukket.
        DetailsLogo.Source = null;
        SetHomeEnabled(true);
        if (focusHome)
        {
            FocusHome(_detailsGame?.Id ?? _focusGameId);
        }
    }

    private void DetailsPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsGame is not { } game)
        {
            return;
        }

        CloseDetails(focusHome: false);
        MenuSounds.Select();
        if (SessionActive && _sessionGame?.Id == game.Id)
        {
            ResumeButton_Click(sender, e);
            return;
        }

        _ = LaunchAsync(game);
    }

    private void DetailsOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsGame is { } game)
        {
            CloseDetails(focusHome: false);
            ShowOptions(game);
        }
    }

    private void DetailsCloseButton_Click(object sender, RoutedEventArgs e) => CloseDetails();

    // ---- Valg for spillet (☰/Start) ----

    private Game? _optionsGame;

    private void ShowOptions(Game game)
    {
        _optionsGame = game;
        OptionsTitle.Text = GameLibrary.DisplayTitle(game);
        OptionsStatus.Text = "";
        var actions = _library.AvailableActionsFor(game.Id);
        var hasBoth = actions.Any(a => a.IsRemote) && actions.Any(a => !a.IsRemote);
        OptionDefaultMode.Visibility = hasBoth ? Visibility.Visible : Visibility.Collapsed;
        OptionDefaultMode.Content = DefaultModeText(game);
        OptionPowerProfileText.Text = HandheldCompanionStatus(game);
        OptionPin.Content = PinText(game);
        ShowSource(game);
        OptionsPanel.Visibility = Visibility.Visible;
        FadeIn(OptionsPanel);
        SetHomeEnabled(false);
        (hasBoth ? OptionDefaultMode : OptionRefreshArt).Focus(FocusState.Keyboard);
        _log.Write("valg", $"Valg for «{GameLibrary.DisplayTitle(game)}»");
    }

    private string DefaultModeText(Game game)
    {
        var preferred = game.Overrides.DefaultPlayActionId is { } id
            ? _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.Id == id)
            : null;
        return preferred is null ? "Spillemåte: spør hver gang"
            : preferred.IsRemote ? $"Spillemåte: alltid fra {HostLabel}" : "Spillemåte: alltid her";
    }

    /// <summary>Bytter mellom spør hver gang → alltid her → alltid fra PC-en.</summary>
    private void OptionDefaultMode_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is not { } game || _library.Find(game.Id) is not { } current)
        {
            return;
        }

        var actions = _library.AvailableActionsFor(game.Id);
        var local = actions.FirstOrDefault(a => !a.IsRemote);
        var remote = actions.FirstOrDefault(a => a.IsRemote);
        var now = current.Overrides.DefaultPlayActionId;
        var next = now is null ? local?.Id : now == local?.Id ? remote?.Id : null;
        _library.SetOverrides(game.Id, current.Overrides with { DefaultPlayActionId = next });
        SaveLibrary();
        _optionsGame = _library.Find(game.Id);
        OptionDefaultMode.Content = DefaultModeText(_optionsGame!);
        MenuSounds.Move();
        _log.Write("valg", $"«{GameLibrary.DisplayTitle(game)}»: {OptionDefaultMode.Content}");
    }

    // ---- Kilde for bilder og info, per spill ----

    private const string SourceAuto = "auto";
    private const string SourceSteam = "steam";
    private const string SourceHost = "host";
    private const string SourceIgdb = "igdb";

    private static string SourceFor(Game game) =>
        ApplicationData.Current.LocalSettings.Values["source." + game.Id] as string ?? SourceAuto;

    private static string SourceName(string source) => source switch
    {
        SourceSteam => "Steam Store",
        SourceHost => $"{HostLabel} (Playnite)",
        SourceIgdb => "IGDB",
        _ => $"Steam Store eller {HostLabel}",
    };

    private void ShowSource(Game game)
    {
        var source = SourceFor(game);
        OptionSourceText.Text = source == SourceAuto ? "Kilde: automatisk" : $"Kilde: {SourceName(source)}";
        OptionSourceHint.Text = source switch
        {
            SourceSteam => "Bilder og info fra Steams butikk.",
            SourceHost => $"Omslaget Playnite har på {HostLabel}. Ingen tekst.",
            SourceIgdb => "Krever en gratis IGDB-nøkkel, som ikke er satt opp ennå. Bruker automatisk så lenge.",
            _ => $"Steam Store først, så omslaget fra {HostLabel}.",
        };
    }

    /// <summary>Bytter kilde: automatisk → Steam Store → Gaming Laptop → IGDB (ikke satt opp) → automatisk.</summary>
    private void OptionSource_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is not { } game)
        {
            return;
        }

        var next = SourceFor(game) switch
        {
            SourceAuto => SourceSteam,
            SourceSteam => SourceHost,
            SourceHost => SourceIgdb,
            _ => SourceAuto,
        };
        ApplicationData.Current.LocalSettings.Values["source." + game.Id] = next;
        ShowSource(game);
        OptionsStatus.Text = next == SourceIgdb ? "" : "Trykk «Hent bilder på nytt» eller «Hent info på nytt» for å bruke kilden.";
        MenuSounds.Move();
        _log.Write("valg", $"Kilde for «{GameLibrary.DisplayTitle(game)}»: {next}");
    }

    private async void OptionRefreshArt_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is not { } game)
        {
            return;
        }

        OptionsStatus.Text = "Henter bilder …";
        _artTried.Remove(game.Id);
        var title = GameLibrary.DisplayTitle(game);
        try
        {
            var source = SourceFor(game);
            ArtSet? art = null;
            if (source != SourceHost)
            {
                var store = new SteamStoreClient(ArtHttp);
                var appId = SteamAppIdFor(game) ?? await store.FindAppIdAsync(title);
                art = appId is { } steamId ? await DownloadArtAsync(game, SteamStoreClient.ImageUrls(steamId)) : null;
            }

            if (art is null && source != SourceSteam)
            {
                _vibepolloApps ??= Vibepollo is { } api ? await api.GetAppsAsync() : [];
                art = await DownloadVibepolloCoverAsync(game, _vibepolloApps) is { } cover ? game.Art with { Cover = cover } : null;
            }

            if (art is null)
            {
                OptionsStatus.Text = $"Fant ingen bilder for «{title}» hos {SourceName(source)}.";
                return;
            }

            _library.SetArt(game.Id, art);
            SaveLibrary();
            _ambientPath = null;
            _heroLogoPath = null;
            if (TileFor(game.Id) is { } tile && _library.Find(game.Id) is { } updated)
            {
                SetTileContent(tile, updated);
            }

            OptionsStatus.Text = "Bildene er oppdatert.";
            _log.Write("bilder", $"Bilder for «{title}» hentet på nytt fra Valg");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
        {
            OptionsStatus.Text = $"Fikk ikke hentet bilder: {ex.Message}";
        }
    }

    private async void OptionRefreshInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is not { } game)
        {
            return;
        }

        OptionsStatus.Text = "Henter info …";
        await FetchDetailsAsync(game, force: true);
        OptionsStatus.Text = _details.ContainsKey(game.Id) ? "Info er oppdatert. Trykk Y på spillet for å se den." : "Fant ingen info på Steam Store.";
    }

    /// <summary>
    /// Handheld Companion har egne profiler per spill (strøm/TDP) i %LOCALAPPDATA%\HandheldCompanion. GameHome skriver
    /// ikke dit før formatet er sjekket på Legion Go med HC installert (docs/roadmap.md, hypotese).
    /// </summary>
    private static string HandheldCompanionStatus(Game game)
    {
        _ = game;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion");
        if (!Directory.Exists(root))
        {
            return "Krever Handheld Companion, som ikke er installert på denne maskinen.";
        }

        var profiles = Directory.Exists(Path.Combine(root, "powerprofiles"))
            ? Directory.GetFiles(Path.Combine(root, "powerprofiles"), "*.json").Length
            : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"Handheld Companion funnet ({profiles} strømprofiler). Valg per spill kommer i neste runde.");
    }

    private void OptionPowerProfile_Click(object sender, RoutedEventArgs e) =>
        OptionsStatus.Text = OptionPowerProfileText.Text;

    private void OptionRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is { } game)
        {
            CloseOptions(focusHome: false);
            ConfirmRemove(game);
        }
    }

    private void OptionsCloseButton_Click(object sender, RoutedEventArgs e) => CloseOptions();

    private void CloseOptions(bool focusHome = true)
    {
        OptionsPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        if (focusHome)
        {
            FocusHome(_optionsGame?.Id ?? _focusGameId);
        }
    }

    // ---- Feilsøking ----

    private const string WifiNetworkSetting = "wifi.network";

    /// <summary>Husker hjemmenettet mens det virker, så GameHome vet hva det skal koble til igjen.</summary>
    private static void RememberWifiNetwork()
    {
        if (WifiReconnect.CurrentNetwork() is { } network)
        {
            ApplicationData.Current.LocalSettings.Values[WifiNetworkSetting] = network;
        }
    }

    /// <summary>Kobler Wi-Fi fra og til det lagrede nettverket (etter trykk fra brukeren), og kjører <paramref name="then"/> hvis det virket.</summary>
    private async Task ReconnectWifiAsync(Action? then = null)
    {
        var network = WifiReconnect.CurrentNetwork() ?? ApplicationData.Current.LocalSettings.Values[WifiNetworkSetting] as string;
        if (network is null)
        {
            ShowMessage("Fant ikke hjemmenettet", "GameHome vet ikke hvilket Wi-Fi-nett den skal koble til. Koble til i Windows' innstillinger én gang, så husker GameHome det.",
                "OK", () => FocusHome(_focusGameId), "Lukk", () => FocusHome(_focusGameId));
            return;
        }

        ArtProgressPanel.Visibility = Visibility.Visible;
        ArtProgress.IsIndeterminate = true;
        ArtProgressText.Text = $"Kobler til «{network}» på nytt …";
        DebugResultText.Visibility = Visibility.Visible;
        DebugResultText.Text = $"Kobler til «{network}» på nytt …";
        _log.Write("nettverk", $"Kobler Wi-Fi til «{network}» på nytt (valgt av brukeren)");
        var ok = await WifiReconnect.ReconnectAsync(network, line => _log.Write("nettverk", line));
        ArtProgress.IsIndeterminate = false;
        ArtProgressPanel.Visibility = Visibility.Collapsed;
        var result = ok ? $"Tilkoblet «{network}» igjen; ruteren svarer." : $"Fikk ikke kontakt med ruteren etter ny tilkobling til «{network}».";
        DebugResultText.Text = result;
        _log.Write("nettverk", result);
        if (ok)
        {
            then?.Invoke();
        }
        else
        {
            ShowMessage("Fortsatt ikke nettverk", $"{result} Prøv å slå Wi-Fi av og på i hurtiginnstillingene, eller start Legion Go på nytt.",
                "Prøv igjen", () => _ = ReconnectWifiAsync(then), "Lukk", () => FocusHome(_focusGameId));
        }
    }

    private void WifiReconnectTile_Click(object sender, RoutedEventArgs e) => _ = ReconnectWifiAsync();

    // ---- Butikkklienter: autostart-sjekk (bare lesing) og valgfri lukking av Steam etter spillet ----

    private const string SteamCloseSetting = "stores.closeSteamAfterGame";

    private static bool CloseSteamAfterGame => ApplicationData.Current.LocalSettings.Values[SteamCloseSetting] is true;

    private DispatcherQueueTimer? _steamExitTimer;

    private void ShowSteamCloseSetting() => SteamCloseTile.Content = $"Lukk Steam etter spillet: {(CloseSteamAfterGame ? "på" : "av")}";

    private void SteamCloseTile_Click(object sender, RoutedEventArgs e)
    {
        var value = !CloseSteamAfterGame;
        ApplicationData.Current.LocalSettings.Values[SteamCloseSetting] = value;
        ShowSteamCloseSetting();
        _log.Write("butikk", $"Lukk Steam etter spillet: {(value ? "på" : "av")} (valgt av brukeren)");
    }

    /// <summary>
    /// Ber Steam avslutte seg selv ett minutt etter spillet (så skylagringen blir ferdig), med Steams egen kommando.
    /// Aldri med makt, og ikke hvis et nytt spill er startet i mellomtiden.
    /// </summary>
    private void ScheduleSteamExit()
    {
        _steamExitTimer?.Stop();
        _steamExitTimer = DispatcherQueue.CreateTimer();
        _steamExitTimer.Interval = TimeSpan.FromMinutes(1);
        _steamExitTimer.IsRepeating = false;
        _steamExitTimer.Tick += (timer, _) =>
        {
            timer.Stop();
            if (SessionActive || _launching)
            {
                _log.Write("butikk", "Steam lukkes ikke: et nytt spill er startet");
                return;
            }

            _log.Write("butikk", SteamShutdown.RequestExit() ? "Ba Steam avslutte etter spillet" : "Steam kjørte ikke lenger");
        };
        _steamExitTimer.Start();
        _log.Write("butikk", "Steam lukkes om ett minutt (skylagring får gjøre seg ferdig)");
    }

    private async void StoreAutostartTile_Click(object sender, RoutedEventArgs e)
    {
        DebugResultText.Visibility = Visibility.Visible;
        DebugResultText.Text = "Sjekker butikkklienter …";
        var clients = await Task.Run(StoreAutostart.Check);
        var lines = clients
            .Where(c => c.Autostarts || c.Running)
            .Select(c => string.Create(CultureInfo.InvariantCulture,
                $"{c.DisplayName}: {(c.Autostarts ? (c.Enabled ? "starter med Windows" : "autostart avslått") : "starter ikke med Windows")}, {(c.Running ? $"kjører nå ({c.WorkingSetMegabytes} MB)" : "kjører ikke")}"))
            .ToList();
        DebugResultText.Text = lines.Count == 0
            ? "Ingen butikkklienter starter med Windows eller kjører nå."
            : string.Join("\n", lines) + "\nAutostart slås av under Innstillinger → Apper → Oppstart. GameHome endrer det ikke.";
        _log.Write("butikk", "Butikkklienter: " + (lines.Count == 0 ? "ingen" : string.Join("; ", lines)));
    }

    // ---- Legion Space: tar forgrunnen fra Moonlight og spill mens den kjører (også i bakgrunnen) ----
    // GameHome lukker den aldri selv: tvangslukking 2026-11-04 etterlot kontrollerne uten data til Legion Space startet igjen.

    private const string LegionSpaceNotice = "legionspace";

    private const string LegionSpaceAdvice =
        "Avslutt den fra oppgavelinjen: trykk ^ nederst til høyre, hold på Legion Space-ikonet og velg Avslutt. Å lukke vinduet er ikke nok.";

    /// <summary>Viser eller fjerner varselet om Legion Space. Gir true hvis den kjører.</summary>
    private bool CheckLegionSpace(string when)
    {
        var running = LegionSpace.IsRunning();
        if (running)
        {
            if (!_notices.Exists(n => n.Key == LegionSpaceNotice))
            {
                _log.Write("legion", $"Legion Space kjører i bakgrunnen ({when})");
            }

            AddNotice(LegionSpaceNotice, null, "Legion Space kjører i bakgrunnen og kan ta forgrunnen fra spill og strøm. " + LegionSpaceAdvice);
        }
        else if (_notices.RemoveAll(n => n.Key == LegionSpaceNotice) > 0)
        {
            _log.Write("legion", $"Legion Space er avsluttet ({when})");
            UpdateNoticeButton();
        }

        return running;
    }

    // ---- M2-reserve: tastaturkrok under spill som slår av hurtigtaster (Assassin's Creed Rogue, 2026-11-02) ----
    // Fokusproblemene 2.–4. november skyldtes Legion Space, ikke kroken; de andre fokusbryterne er fjernet.

    private const string M2FallbackSetting = "m2.fallback";

    private static bool M2Fallback => ApplicationData.Current.LocalSettings.Values[M2FallbackSetting] is not false;

    private void ShowM2Fallback() => FlagHook.Content = $"M2-reserve (tastaturkrok): {(M2Fallback ? "på" : "av")}";

    private void FocusFlag_Click(object sender, RoutedEventArgs e)
    {
        var value = !M2Fallback;
        ApplicationData.Current.LocalSettings.Values[M2FallbackSetting] = value;
        ShowM2Fallback();
        ShowSteamCloseSetting();
        _log.Write("feilsøk", $"M2-reserve satt til {(value ? "på" : "av")}; gjelder fra neste spillstart");
    }

    private void DebugTile_Click(object sender, RoutedEventArgs e)
    {
        DebugPanel.Visibility = Visibility.Visible;
        FadeIn(DebugPanel);
        SetHomeEnabled(false);
        TestHostTile.Focus(FocusState.Keyboard);
    }

    private void DebugCloseButton_Click(object sender, RoutedEventArgs e) => CloseDebug();

    private void CloseDebug()
    {
        DebugPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        DebugTile.Focus(FocusState.Keyboard);
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
    private bool _launching;

    private bool _startDespiteLegionSpace;

    private async Task LaunchAsync(Game game, PlayAction? chosen = null)
    {
        // Ett trykk, én start: ekstra trykk mens noe starter eller avsluttes, ignoreres (0.1.87.0 startet Mega Man tre ganger).
        if (_launching)
        {
            _log.Write("økt", $"Start av «{GameLibrary.DisplayTitle(game)}» ignorert: en start pågår allerede");
            return;
        }

        // Legion Space i bakgrunnen tar forgrunnen fra spillet og strømmen; be brukeren avslutte den (GameHome gjør det aldri selv).
        if (!_startDespiteLegionSpace && CheckLegionSpace("spillstart"))
        {
            ShowMessage("Legion Space kjører", "Legion Space kan ta forgrunnen og kontrolleren fra spillet. " + LegionSpaceAdvice + " Velg så spillet igjen.",
                "Start likevel", () =>
                {
                    _startDespiteLegionSpace = true;
                    _ = LaunchAsync(game, chosen);
                },
                "Avbryt", () => FocusHome(game.Id));
            return;
        }

        _startDespiteLegionSpace = false;
        _launching = true;
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
        finally
        {
            _launching = false;
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

        // Ferskt svar fra PC-en før en stream startes: kjører noe der allerede (for eksempel etter «Koble fra»)?
        var remoteAction = _library.AvailableActionsFor(game.Id).FirstOrDefault(a => a.IsRemote);
        if (remoteAction is not null && (chosen is null || chosen.IsRemote))
        {
            await RefreshHostStatusAsync(force: true);
        }

        // Spillet kjører fortsatt på PC-en: fortsett der du slapp, eller avslutt det.
        if (chosen is null && remoteAction is not null && IsRunningOnHost(game))
        {
            ShowMessage($"{title} kjører på {HostLabel}", "Du koblet fra uten å avslutte. Fortsett der du slapp, eller avslutt spillet på PC-en.",
                "Fortsett", () => _ = LaunchAsync(game, remoteAction),
                $"Avslutt på {HostLabel}", () => _ = CloseHostAppAsync(game),
                onCancel: () => FocusHome(game.Id));
            return;
        }

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

        // Programfil: mangler den DirectX-tillegg eller Visual C++? Sjekkes én gang per fil (huskes til filen endres).
        if (action.RunnerId == GameLibrary.ExecutableRunnerId && !_dependencySkip.Contains(action.Target)
            && CheckDependencies(action.Target) is { Ok: false } dependencies)
        {
            ShowMissingDependencies(game, action, dependencies, afterCrash: false);
            return;
        }

        // Et annet spill kjører fortsatt på PC-en (for eksempel etter «Koble fra»): spør før det lukkes.
        if (action.IsRemote && _hostRunningApp is { } other
            && GameLibrary.TitleKey(other) != GameLibrary.TitleKey(action.Target))
        {
            ShowMessage($"{other} kjører på {HostLabel}", $"{other} må avsluttes før {title} kan starte. Ulagret fremgang i {other} kan gå tapt.",
                $"Avslutt og start {title}", () => _ = CloseHostAppThenLaunchAsync(game, action),
                "Avbryt", () => FocusHome(game.Id));
            return;
        }

        ShowLoading(game, action);

        // Stream: sover gaming-PC-en, vekkes den først (Wake-on-LAN), så Moonlight ikke gir opp etter 30 s.
        if (action.IsRemote && !await WakeHostAsync(action.HostId!))
        {
            HideLoading(_lastWakeNoNetwork ? "Legion Go har ikke nettverk" : "PC-en våknet ikke");
            if (_lastWakeNoNetwork)
            {
                ShowMessage("Legion Go har ikke nettverk",
                    "Legion Go når ikke ruteren, selv om Wi-Fi kan se tilkoblet ut. GameHome kan koble Wi-Fi fra og til igjen og prøve på nytt.",
                    "Koble til Wi-Fi på nytt", () => _ = ReconnectWifiAsync(() => _ = LaunchAsync(game, action)),
                    "Lukk", () => FocusHome(game.Id));
            }
            else
            {
                ShowMessage($"{HostLabel} svarer ikke",
                    $"GameHome prøvde å vekke {HostLabel}. Sjekk at den er på og har nettverkskabel, og prøv igjen.",
                    "Prøv igjen", () => _ = LaunchAsync(game, action), "Lukk", () => FocusHome(game.Id));
            }

            return;
        }

        try
        {
            _session = (IForegroundSession)await runner.LaunchAsync(action, Profile.Default, CancellationToken.None);
            _sessionRunnerId = action.RunnerId;
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
        var hook = M2Fallback && _hotkeys.StartHook();
        _log.Write("økt", $"Startet «{title}» ({action.RunnerId} {action.Target}, {session.Status}). Hurtigtaster: {string.Join(", ", registered)}"
            + (hook ? " (+ tastaturkrok som reserve)" : ""));
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
        RegisterHomeHotkey();
        HideLoading("økten avsluttet");
        var game = _sessionGame;
        var title = game is null ? "spillet" : GameLibrary.DisplayTitle(game);
        var minutes = (DateTime.UtcNow - _sessionStarted).TotalMinutes;
        _log.Write("økt", string.Create(CultureInfo.InvariantCulture,
            $"«{title}» avsluttet: {end.Reason}, kode {end.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "–"}, {minutes:F1} min{(end.Detail is null ? "" : $" ({end.Detail})")}"));

        if (game is not null && end.Reason != SessionEndReason.LaunchFailed)
        {
            RecordPlayTime(game, minutes);
        }

        if (CloseSteamAfterGame && game is not null
            && _library.PlayActionsFor(game.Id).Any(a => a.Id == session.PlayActionId && a.RunnerId == GameLibrary.SteamRunnerId))
        {
            ScheduleSteamExit();
        }

        session.Dispose();
        _session = null;
        _sessionGame = null;
        SessionPanel.Visibility = Visibility.Collapsed;
        SetHomeEnabled(true);
        RefreshTileTexts();

        // Etter «Koble fra» kjører spillet videre på PC-en; vis det på flisen med en gang.
        if (session is StreamSession)
        {
            _ = RefreshHostStatusAsync(force: true);
        }

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

        // Windows fant ikke en DLL spillet trenger (0xC0000135) eller fant feil versjon (0xC000007B): sjekk på nytt og forklar.
        if (game is not null && end.ExitCode is Dependencies.DllNotFound or Dependencies.BadImage
            && _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.RunnerId == GameLibrary.ExecutableRunnerId) is { } exeAction)
        {
            _dependencies.Forget(exeAction.Target);
            SaveDependencies();
            if (CheckDependencies(exeAction.Target) is { Ok: false } result)
            {
                ShowMissingDependencies(game, exeAction, result, afterCrash: true);
            }
            else
            {
                // Ingen gjetning: GameHome foreslår bare en pakke den faktisk har sett at mangler.
                ShowMessage($"{title} mangler en fil", "Windows fant ikke en fil spillet trenger, men GameHome ser ikke hvilken det er. " +
                    "Navnet står i Windows-meldingen (for eksempel «MSVCR110.dll»). Den lastes kanskje først når spillet kjører, og da kan ikke GameHome se den på forhånd.",
                    "Prøv igjen", () => _ = LaunchAsync(game, exeAction),
                    "Lukk", () => FocusHome(game.Id));
            }

            return;
        }

        if (end.Reason == SessionEndReason.LaunchFailed && game is not null)
        {
            ShowMessage($"{title} startet ikke", end.Detail ?? "Spillet startet ikke.",
                "Prøv igjen", () => _ = LaunchAsync(game), "Lukk", () => FocusHome(game.Id));
        }
    }

    // ---- Avhengigheter: DirectX-tillegg og Visual C++ (sjekkes én gang per programfil, huskes i dependencies.json) ----

    private DependencyDocument? _dependenciesDocument;
    private readonly HashSet<string> _dependencySkip = new(StringComparer.OrdinalIgnoreCase);

    private DependencyDocument _dependencies => _dependenciesDocument ??= DependencyStore.Load(DependenciesPath);

    private static string DependenciesPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "dependencies.json");

    private void SaveDependencies()
    {
        try
        {
            DependencyStore.Save(DependenciesPath, _dependencies);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("avhengigheter", $"Kunne ikke lagre dependencies.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Sjekker programfilen, men bare hvis den er ny, endret, eller manglet noe sist. Ellers null (kjent i orden).
    /// Leser bare importtabellen; tar millisekunder.
    /// </summary>
    private DependencyResult? CheckDependencies(string exePath)
    {
        try
        {
            var info = new FileInfo(exePath);
            if (!info.Exists || _dependencies.IsKnownGood(exePath, info.Length, info.LastWriteTimeUtc))
            {
                return null;
            }

            var gameDir = info.DirectoryName ?? "";
            var system64 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            bool Exists(string dll, bool is64) =>
                File.Exists(Path.Combine(gameDir, dll)) || File.Exists(Path.Combine(is64 ? system64 : system32, dll));

            var watch = Stopwatch.StartNew();
            DependencyResult result;
            using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096))
            {
                // Følger også DLL-ene spillet har med (andre ledd), som Tobii.EyeX.Client.dll → MSVCR110.dll i AC Rogue.
                Stream? OpenLocal(string dll)
                {
                    var local = Path.Combine(gameDir, dll);
                    return File.Exists(local) ? new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096) : null;
                }

                result = Dependencies.CheckTree(stream, OpenLocal, Exists);
            }

            _dependencies.Remember(exePath, info.Length, info.LastWriteTimeUtc, [.. result.Missing.Select(p => p.Id)]);
            SaveDependencies();
            var outcome = result.Ok
                ? "alt finnes"
                : $"mangler {string.Join(", ", result.MissingDlls)} ({string.Join(", ", result.Missing.Select(p => p.Name))})";
            _log.Write("avhengigheter", string.Create(CultureInfo.InvariantCulture,
                $"{info.Name} ({(result.Is64Bit ? "64" : "32")}-bit) sjekket på {watch.ElapsedMilliseconds} ms: {outcome}"));
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("avhengigheter", $"Kunne ikke sjekke {exePath}: {ex.Message}");
            return null;
        }
    }

    private void ShowMissingDependencies(Game game, PlayAction action, DependencyResult result, bool afterCrash)
    {
        var title = GameLibrary.DisplayTitle(game);
        var package = result.Missing[0];
        var names = string.Join(" og ", result.Missing.Select(p => p.Name));
        var text = (afterCrash ? $"{title} stoppet fordi Windows ikke fant {string.Join(", ", result.MissingDlls)}. " : $"{title} trenger {string.Join(", ", result.MissingDlls)}, som ikke finnes på maskinen. ")
            + $"Filene kommer fra {names} fra Microsoft. GameHome kan installere {package.Name} nå; Windows ber deg bekrefte.";
        ShowMessage($"{title} mangler {package.Name}", text,
            $"Installer {package.Name}", () => _ = InstallRedistributableAsync(game, action, package),
            afterCrash ? "Lukk" : "Start likevel", () =>
            {
                if (afterCrash)
                {
                    FocusHome(game.Id);
                    return;
                }

                _dependencySkip.Add(action.Target);
                _ = LaunchAsync(game, action);
            },
            onCancel: () => FocusHome(game.Id));
    }

    /// <summary>
    /// Installerer en Microsoft-pakke: installasjonsfilen spillet har med selv hvis den finnes, ellers nedlasting fra
    /// Microsoft. Filen kjøres bare hvis Windows bekrefter at den er gyldig signert av Microsoft. Installasjonen vises
    /// og bekreftes i Windows (UAC); GameHome installerer aldri noe i det stille.
    /// </summary>
    private async Task InstallRedistributableAsync(Game game, PlayAction action, Redistributable package)
    {
        var title = GameLibrary.DisplayTitle(game);
        ArtProgressPanel.Visibility = Visibility.Visible;
        ArtProgress.IsIndeterminate = true;
        ArtProgressText.Text = $"Gjør klar {package.Name} …";
        string? installer = null;
        try
        {
            installer = FindBundledInstaller(action.Target, package);
            if (installer is not null)
            {
                _log.Write("avhengigheter", $"Bruker installasjonsfilen som fulgte med spillet: {installer}");
            }
            else
            {
                ArtProgressText.Text = $"Laster ned {package.Name} fra Microsoft …";
                var folder = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "redist");
                Directory.CreateDirectory(folder);
                installer = Path.Combine(folder, package.Id + ".exe");
                using var response = await ArtHttp.GetAsync(new Uri(package.DownloadUrl));
                response.EnsureSuccessStatusCode();
                await using (var file = File.Create(installer))
                {
                    await response.Content.CopyToAsync(file);
                }

                _log.Write("avhengigheter", $"Lastet ned {package.Name} fra {package.DownloadUrl}");
            }

            if (!Authenticode.IsSignedByMicrosoft(installer))
            {
                _log.Write("avhengigheter", $"Installasjonsfilen for {package.Name} er ikke gyldig signert av Microsoft; kjøres ikke ({installer})");
                ShowMessage("Installasjonen ble stoppet", $"Filen for {package.Name} er ikke gyldig signert av Microsoft, så GameHome kjører den ikke.",
                    "OK", () => FocusHome(game.Id), "Lukk", () => FocusHome(game.Id));
                return;
            }

            ArtProgressText.Text = $"Installerer {package.Name} (bekreft i Windows) …";
            using var process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installer) });
            if (process is not null)
            {
                await process.WaitForExitAsync();
                _log.Write("avhengigheter", string.Create(CultureInfo.InvariantCulture, $"{package.Name}: installasjonen avsluttet med kode {process.ExitCode}"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            // Win32Exception: brukeren sa nei i Windows' bekreftelse, eller filen kunne ikke startes.
            _log.Write("avhengigheter", $"{package.Name} ble ikke installert: {ex.Message}");
            ShowMessage($"{package.Name} ble ikke installert", ex.Message, "OK", () => FocusHome(game.Id), "Lukk", () => FocusHome(game.Id));
            return;
        }
        finally
        {
            ArtProgress.IsIndeterminate = false;
            ArtProgressPanel.Visibility = Visibility.Collapsed;
        }

        // Sjekk på nytt og husk resultatet: neste start hopper over sjekken hvis alt nå finnes.
        _dependencies.Forget(action.Target);
        var after = CheckDependencies(action.Target);
        if (after is null or { Ok: true })
        {
            _dependencies.InstalledPackages[package.Id] = DateTime.UtcNow;
            SaveDependencies();
            ShowMessage($"{package.Name} er på plass", $"{title} har det den trenger.",
                $"Start {title}", () => _ = LaunchAsync(game, action), "Lukk", () => FocusHome(game.Id));
        }
        else
        {
            ShowMissingDependencies(game, action, after, afterCrash: false);
        }
    }

    /// <summary>
    /// Installasjonsfil som fulgte med spillet (for eksempel _CommonRedist\DirectX\Jun2010\DXSETUP.exe). For Visual C++
    /// må stien nevne årstallet, så en 2008-fil ikke brukes for 2010. Søker høyst tre mapper ned fra spillmappen.
    /// </summary>
    private static string? FindBundledInstaller(string exePath, Redistributable package)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(exePath)) ?? Path.GetDirectoryName(exePath);
        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        var name = new System.Text.RegularExpressions.Regex(package.LocalInstaller, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var years = package.Id switch
        {
            var id when id.StartsWith("vc2015", StringComparison.Ordinal) => new[] { "2015", "2017", "2019", "2022" },
            var id when id.StartsWith("vc", StringComparison.Ordinal) => new[] { id[2..6] },
            _ => Array.Empty<string>(),
        };
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, "*.exe", options)
            .Take(5000)
            .FirstOrDefault(f => name.IsMatch(Path.GetFileName(f)) && (years.Length == 0 || years.Any(y => f.Contains(y, StringComparison.Ordinal))));
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

            // Xbox-/Microsoft Store-spill fra Windows' pakkeliste (ingen Xbox-app trengs), i samme runde.
            var watch = Stopwatch.StartNew();
            var xbox = XboxGames.Scan(message => _log.Write("xbox", message));
            var xboxMs = watch.ElapsedMilliseconds;
            var battleNet = BattleNetGames.Scan();
            var gog = GogGames.Scan(message => _log.Write("gog", message));
            var epic = EpicGames.Scan(message => _log.Write("epic", message));
            var playStation = Chiaki.RegisteredConsoles();
            var retro = RetroArch.Scan();

            DispatcherQueue.TryEnqueue(() =>
            {
                ApplySteamScan(root, entries);
                ApplyXboxScan(xbox, xboxMs);
                ApplyBattleNetScan(battleNet);
                ApplyStoreScan(GogRunner.Id, "GOG", GogGames.AsStoreEntries(gog));
                ApplyStoreScan(EpicRunner.Id, "Epic", EpicGames.AsStoreEntries(epic));
                ApplyStoreScan(ChiakiRunner.Id, "PlayStation", Chiaki.AsStoreEntries(playStation));
                ApplyStoreScan(RetroArchRunner.Id, "retro", RetroArch.AsStoreEntries(retro));
            });
        });
    }

    /// <summary>Felles for butikker som leses fra disk uten klient (GOG, Epic): synk, lagre og hent bilder ved endring.</summary>
    private void ApplyStoreScan(string runnerId, string label, IReadOnlyList<StoreEntry> entries)
    {
        var result = _library.SyncStore(runnerId, entries);
        _log.Write(runnerId, $"Fant {entries.Count} {label}-spill ({string.Join(", ", entries.Select(e => e.Title))}); {result.Added.Count} nye, {result.MarkedMissing} borte");
        if (!result.Changed)
        {
            return;
        }

        SaveLibrary();
        BuildGameTiles();
        _ = FetchMissingArtAsync();
    }

    private void ApplyBattleNetScan(IReadOnlyList<BattleNetGame> games)
    {
        var result = _library.SyncStore(BattleNetRunner.Id, BattleNetGames.AsStoreEntries(games));
        _log.Write("battlenet", $"Fant {games.Count} Battle.net-spill ({string.Join(", ", games.Select(g => g.Title))}); {result.Added.Count} nye, {result.MarkedMissing} borte");
        if (!result.Changed)
        {
            return;
        }

        SaveLibrary();
        BuildGameTiles();
        _ = FetchMissingArtAsync();
    }

    private void ApplyXboxScan(IReadOnlyList<XboxGame> games, long elapsedMs)
    {
        var result = _library.SyncStore(XboxRunner.Id, XboxGames.AsStoreEntries(games));
        _log.Write("xbox", string.Create(CultureInfo.InvariantCulture,
            $"Fant {games.Count} Xbox-spill på {elapsedMs} ms ({string.Join(", ", games.Select(g => g.Title))}); {result.Added.Count} nye, {result.MarkedMissing} borte"));
        if (!result.Changed)
        {
            return;
        }

        SaveLibrary();
        BuildGameTiles();
        _ = FetchMissingArtAsync();
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
        var logo = await LoadImageAsync(art.Logo, Px(560));

        // Uten eget bakgrunnsbilde brukes omslaget (som Nuvio: bakgrunn, ellers plakat).
        var hero = await LoadImageAsync(art.Hero ?? art.Cover, ScreenPx(1));

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
            // Små uskarpe bakgrunner (48 px) dekodes rett fra originalen; ellers fra en nedskalert kopi på disk.
            if (decodeWidth >= 160)
            {
                path = await ThumbnailCache.GetAsync(path, decodeWidth);
            }

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

        // Spillet har tatt over: gi minnet tilbake til spillet mens Home ligger i bakgrunnen.
        if (SessionActive && !_windowActive)
        {
            var before = Environment.WorkingSet;
            MemoryTrim.Trim();
            _log.Write("minne", string.Create(CultureInfo.InvariantCulture,
                $"Minne gitt tilbake mens spillet kjører: {before / 1048576.0:F0} → {Environment.WorkingSet / 1048576.0:F0} MB"));
        }
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
        var name = id == HomeHotkey.Id ? HomeHotkey.Name : SessionHotkeys.FirstOrDefault(h => h.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture);
        if (id == HomeHotkey.Id && !SessionActive)
        {
            Activate();
            var home = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
            _log.Write("hurtigtast", $"Mottatt: {name}; {(home ? "Home hentet fram" : "Windows avslo forgrunnen")}");
            FocusHome(_focusGameId);
            return;
        }

        _log.Write("hurtigtast", $"Mottatt: {name}");
        HideLoading("M2");
        Activate();
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ok = Foreground.Bring(window);

        // Kom hurtigtasten via reserven (spillet slår av hurtigtaster), gir ikke Windows forgrunnen. Da minimeres spillet,
        // som Alt+Tab, og Windows aktiverer neste vindu, GameHome. «Tilbake til spillet» gjenoppretter det.
        if (!ok && _hotkeys.HookActive && _session is LocalProcessSession { MainWindowHandle: var game and not 0 })
        {
            Foreground.Minimize(game);
            Activate();
            ok = Foreground.Bring(window) || Foreground.IsForeground(window);
            _log.Write("fokus", ok ? "Windows avslo forgrunnen; spillet ble minimert og GameHome tok forgrunnen" : "Windows avslo forgrunnen, også etter at spillet ble minimert");
        }
        else
        {
            _log.Write("fokus", ok ? "Tok forgrunnen etter hurtigtast" : "Windows avslo forgrunnen etter hurtigtast");
        }

        ShowSessionPanel($"hurtigtast {name}");
    }

    private void ShowSessionPanel(string via)
    {
        if (!SessionActive)
        {
            return;
        }

        SessionTitle.Text = _sessionGame is null ? "Spill" : GameLibrary.DisplayTitle(_sessionGame);
        var minutes = (DateTime.UtcNow - _sessionStarted).TotalMinutes;
        SessionDetail.Text = minutes < 1 ? "Startet nå" : string.Create(CultureInfo.InvariantCulture,
            $"Startet for {minutes:F0} min siden");
        var streaming = _session is StreamSession;
        ResumeButton.Content = streaming ? "Tilbake til streamen" : "Tilbake til spillet";
        EndSessionButton.Content = streaming ? $"Koble fra (spillet fortsetter på {HostLabel})"
            : _sessionRunnerId == ChiakiRunner.Id ? "Koble fra (spillet fortsetter på konsollen)" : "Avslutt spillet";
        EndRemoteButton.Content = $"Avslutt spillet på {HostLabel}";
        EndRemoteButton.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
        StreamDebugPanel.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
        DebugStatusText.Visibility = Visibility.Collapsed;
        DebugStatusButton.Visibility = Vibepollo is null ? Visibility.Collapsed : Visibility.Visible;
        if (streaming)
        {
            SessionDetail.Text = $"Strømmer fra {HostLabel}";
        }

        ShowSessionArt();
        BatteryText.Text = BatteryLine();
        FillPerformance();
        PerformanceStatus.Visibility = Visibility.Collapsed;
        SessionPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        ResumeButton.Focus(FocusState.Keyboard);
        _log.Write("økt", $"«Kjører nå» vist via {via}");
    }

    /// <summary>Spillets bakgrunnsbilde og logo i menyen; uten logo vises tittelen.</summary>
    private void ShowSessionArt()
    {
        SessionHero.Source = null;
        SessionLogo.Source = null;
        SessionLogo.Visibility = Visibility.Collapsed;
        SessionTitle.Visibility = Visibility.Visible;
        if (_sessionGame is not { } game)
        {
            return;
        }

        var art = ArtFor(game);
        if (art.Hero is { } hero && File.Exists(hero))
        {
            _ = LoadTileImageAsync(SessionHero, hero, 1280);
        }

        if (art.Logo is { } logo && File.Exists(logo))
        {
            SessionLogo.Visibility = Visibility.Visible;
            SessionTitle.Visibility = Visibility.Collapsed;
            _ = LoadTileImageAsync(SessionLogo, logo, 840);
        }
    }

    private static string BatteryLine()
    {
        var percent = global::Windows.System.Power.PowerManager.RemainingChargePercent;
        var charging = global::Windows.System.Power.PowerManager.PowerSupplyStatus != global::Windows.System.Power.PowerSupplyStatus.NotPresent;
        return string.Create(CultureInfo.InvariantCulture, $"Batteri {percent} %{(charging ? " · lader" : "")}");
    }

    private void HideSessionPanel()
    {
        SessionPanel.Visibility = Visibility.Collapsed;
        SessionHero.Source = null; // Bildene trengs ikke når menyen er lukket.
        SessionLogo.Source = null;
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

    // ---- Ytelse (ADR 0007) ----

    /// <summary>Hurtigtasten Handheld Companion er satt opp til å åpne QuickTools med (scripts/Set-HandheldCompanion.ps1).</summary>
    private static readonly ushort[] QuickToolsChord =
        [Keyboard.LeftControl, Keyboard.LeftAlt, Keyboard.LeftShift, Keyboard.F9];

    private void FillPerformance()
    {
        RefreshRateButtons.Children.Clear();
        ResolutionButtons.Children.Clear();
        if (Display.Current() is not { } current)
        {
            return;
        }

        var modes = Display.Modes();
        foreach (var hz in modes.Where(m => m.Width == current.Width && m.Height == current.Height)
                     .Select(m => m.Hz).Distinct().Order().TakeLast(4))
        {
            RefreshRateButtons.Children.Add(PerformanceButton($"{hz} Hz", hz == current.Hz,
                new DisplayMode(current.Width, current.Height, hz)));
        }

        foreach (var size in PickResolutions(modes, current))
        {
            // Beholder frekvensen hvis den finnes i den nye oppløsningen, ellers den høyeste.
            var hz = modes.Any(m => m.Width == size.Width && m.Height == size.Height && m.Hz == current.Hz)
                ? current.Hz
                : modes.Where(m => m.Width == size.Width && m.Height == size.Height).Max(m => m.Hz);
            ResolutionButtons.Children.Add(PerformanceButton(
                string.Create(CultureInfo.InvariantCulture, $"{size.Width}×{size.Height}"),
                size.Width == current.Width && size.Height == current.Height,
                new DisplayMode(size.Width, size.Height, hz)));
        }
    }

    /// <summary>Opptil tre oppløsninger med samme form som skjermen nå: den største, omtrent tre firedeler og halvparten.</summary>
    private static List<DisplayMode> PickResolutions(IReadOnlyList<DisplayMode> modes, DisplayMode current)
    {
        var aspect = (double)current.Width / current.Height;
        var sizes = modes.Where(m => Math.Abs(((double)m.Width / m.Height) - aspect) < 0.02)
            .Select(m => new DisplayMode(m.Width, m.Height, 0)).Distinct().ToList();
        if (sizes.Count == 0)
        {
            return [];
        }

        var largest = sizes.Max(s => s.Width);
        var picked = new List<DisplayMode>();
        foreach (var share in new[] { 1.0, 0.75, 0.5 })
        {
            var best = sizes.MinBy(s => Math.Abs(s.Width - (largest * share)));
            if (!picked.Contains(best))
            {
                picked.Add(best);
            }
        }

        return picked;
    }


    /// <summary>
    /// Uthever det valgte (Hz, oppløsning, fane) i Windows' aksentfarge. WinUIs AccentButtonStyle lot seg ikke slå opp fra
    /// koden (0.1.138–139), så fargene settes direkte, også for pekertilstandene.
    /// </summary>
    private static void MarkSelected(Button button)
    {
        var accent = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.AccentLight2);
        var fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(accent);
        var text = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            button.Resources["ButtonBackground" + state] = fill;
            button.Resources["ButtonForeground" + state] = text;
            button.Resources["ButtonBorderBrush" + state] = fill;
        }

        button.Background = fill;
        button.Foreground = text;
    }

    private Button PerformanceButton(string label, bool selected, DisplayMode target)
    {
        // Tre knapper i bredden skal få plass i sidepanelet (344 px innenfor margen).
        var button = new Button
        {
            Content = label,
            Height = 52,
            Width = 104,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 16,
            Tag = target,
        };
        if (selected)
        {
            MarkSelected(button);
        }

        button.Click += PerformanceMode_Click;
        return button;
    }

    private async void PerformanceMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DisplayMode target })
        {
            return;
        }

        var error = Display.Apply(target);
        var text = string.Create(CultureInfo.InvariantCulture, $"{target.Width}×{target.Height} {target.Hz} Hz");
        _log.Write("ytelse", error is null ? $"Skjerm satt til {text}" : $"Skjerm {text}: {error}");
        PerformanceStatus.Text = error ?? $"Skjermen er satt til {text}";
        PerformanceStatus.Visibility = Visibility.Visible;
        FillPerformance();
        FocusPerformance(target);

        // Noen spill tar forgrunnen når oppløsningen endres (Broforce, 0.1.114.0). Menyen hentes tilbake, slik
        // hurtigtasten gjør.
        if (error is null)
        {
            await Task.Delay(1500);
            var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (SessionPanel.Visibility == Visibility.Visible && !Foreground.IsForeground(window))
            {
                Activate();
                var back = Foreground.Bring(window);
                _log.Write("fokus", back ? "Menyen hentet tilbake etter skjermendring" : "Windows avslo forgrunnen etter skjermendring");
                FocusPerformance(target);
            }
        }
    }

    // Etter at knappene er laget på nytt, settes fokus på den valgte, så kontrolleren ikke mister plassen sin.
    private void FocusPerformance(DisplayMode target)
    {
        foreach (var row in new[] { RefreshRateButtons, ResolutionButtons })
        {
            foreach (var child in row.Children)
            {
                if (child is Button { Tag: DisplayMode mode } button && mode == target)
                {
                    button.Focus(FocusState.Keyboard);
                    return;
                }
            }
        }
    }

    private async void QuickToolsButton_Click(object sender, RoutedEventArgs e)
    {
        // Handheld Companion lytter på hurtigtasten i sin egen tastaturkrok; hvilket vindu som har forgrunnen, spiller
        // ingen rolle.
        var sent = await Keyboard.SendChordHeldAsync(TimeSpan.FromMilliseconds(60), QuickToolsChord);
        _log.Write("ytelse", sent ? "QuickTools: hurtigtast sendt til Handheld Companion" : "QuickTools: Windows avslo tastetrykket");
        PerformanceStatus.Text = sent
            ? "Åpner Handheld Companion. Skjer ingenting, trykk høyre Legion-knapp."
            : "Fikk ikke sendt hurtigtasten. Trykk høyre Legion-knapp.";
        PerformanceStatus.Visibility = Visibility.Visible;
    }

    // ---- Feilsøking under strøm ----

    /// <summary>
    /// Gir streamvinduet forgrunnen og sender en tastekombinasjon til det. Moonlight tar selv imot sine snarveier;
    /// andre taster går videre til gaming-PC-en.
    /// </summary>
    private async Task SendToStreamAsync(string what, params ushort[] keys)
    {
        var handle = _session is StreamSession session ? session.MainWindowHandle : 0;
        if (handle == 0)
        {
            _log.Write("feilsøk", $"{what}: fant ikke streamvinduet");
            return;
        }

        HideSessionPanel();
        var (ok, _) = Foreground.BringGame(handle);
        await Task.Delay(400); // Moonlight må ha tastaturet før tastene kommer.
        var sent = ok && Foreground.IsForeground(handle) && Keyboard.SendChord(keys);
        _log.Write("feilsøk", sent ? $"{what}: sendt til Moonlight" : $"{what}: streamvinduet fikk ikke forgrunnen");
    }

    // Moonlights egen snarvei for statistikkoverlegget.
    private void DebugStatsButton_Click(object sender, RoutedEventArgs e) =>
        _ = SendToStreamAsync("Ytelse", Keyboard.Control, Keyboard.Alt, Keyboard.Shift, Keyboard.S);

    // Går gjennom streamen til gaming-PC-en, der Set-StreamDesktop.ps1 lytter på Ctrl+Shift+F10 under strømmen.
    private void DebugTaskbarButton_Click(object sender, RoutedEventArgs e) =>
        _ = SendToStreamAsync("Oppgavelinje", Keyboard.Control, Keyboard.Shift, Keyboard.F10);

    private async void DebugStatusButton_Click(object sender, RoutedEventArgs e)
    {
        DebugStatusText.Visibility = Visibility.Visible;
        DebugStatusText.Text = $"Spør {HostLabel} …";
        if (Vibepollo is not { } api)
        {
            DebugStatusText.Text = "Ingen Vibepollo-nøkkel på denne maskinen.";
            return;
        }

        try
        {
            var status = await api.GetStatusAsync();
            DebugStatusText.Text = status is null
                ? "Vibepollo svarte uten innhold."
                : (status.AppRunning ? $"Kjører: {status.AppName}" : "Ingen app kjører ifølge Vibepollo")
                  + $" · tilkoblinger: {status.ActiveSessions}"
                  + (status.Paused ? " · pause" : "")
                  + (status.LastEncoderProbeFailed ? " · videokoding feilet ved siste oppstart" : " · videokoding OK");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            DebugStatusText.Text = $"{HostLabel} svarte ikke: {ex.Message}";
        }

        _log.Write("feilsøk", $"Status: {DebugStatusText.Text}");
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

        // Å koble fra en strøm er ufarlig; å lukke et spill her kan miste ulagret fremgang og bekreftes først, som på
        // Steam Deck og Switch. «Avbryt» har fokus i bekreftelsen.
        if (session is not StreamSession && _sessionRunnerId != ChiakiRunner.Id)
        {
            var title = _sessionGame is null ? "spillet" : GameLibrary.DisplayTitle(_sessionGame);
            SessionPanel.Visibility = Visibility.Collapsed;
            ShowMessage($"Avslutte {title}?", "Spillet lukkes. Ulagret fremgang kan gå tapt.",
                "Avslutt", () =>
                {
                    ShowSessionPanel("bekreftet avslutning");
                    EndSession(session);
                },
                "Avbryt", () => ShowSessionPanel("avslutning avbrutt"));
            return;
        }

        EndSession(session);
    }

    private void EndSession(IForegroundSession session)
    {
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

    private async Task OpenHostPanelAsync()
    {
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
        HostTitle.Text = $"Legg til fra {host.DisplayName}";
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

        // Finnes spillet allerede lokalt, får det ingen ny flis: si fra at det nå har to spillemåter.
        var title = GameLibrary.DisplayTitle(game);
        if (!newTile && _library.AvailableActionsFor(game.Id).Count > 1)
        {
            CloseHostPanel();
            ShowMessage($"{title} kan spilles på to måter",
                $"{title} finnes både på Legion Go og på {HostLabel}. Når du starter spillet, velger du «Spill her» eller «Stream fra {HostLabel}».",
                $"Start {title} nå", () => _ = LaunchAsync(game),
                "OK", () => FocusHome(game.Id));
        }
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

    // ---- Fase 4: SteamGridDB og IGDB (av til nøklene er lagt inn, se Secrets) ----

    private SteamGridDbClient? _steamGridDb;
    private bool _steamGridDbChecked;
    private IgdbClient? _igdb;
    private bool _igdbChecked;

    private void KeyLog(string message) => _log.Write("nøkler", message);

    /// <summary>SteamGridDB-klienten hvis nøkkelen finnes, ellers null. Nøkkelen leses én gang.</summary>
    private SteamGridDbClient? SteamGridDb
    {
        get
        {
            if (!_steamGridDbChecked)
            {
                _steamGridDbChecked = true;
                if (Secrets.Load(Secrets.SteamGridDb, KeyLog) is { } key)
                {
                    _steamGridDb = new SteamGridDbClient(ArtHttp, key);
                    _log.Write("bilder", "SteamGridDB-nøkkel funnet; brukes for spill som ikke finnes på Steam");
                }
            }

            return _steamGridDb;
        }
    }

    /// <summary>IGDB-klienten hvis begge nøklene finnes, ellers null.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Lever like lenge som vinduet.")]
    private IgdbClient? Igdb
    {
        get
        {
            if (!_igdbChecked)
            {
                _igdbChecked = true;
                if (Secrets.Load(Secrets.IgdbClientId, KeyLog) is { } id && Secrets.Load(Secrets.IgdbClientSecret, KeyLog) is { } secret)
                {
                    _igdb = new IgdbClient(ArtHttp, id, secret);
                    _log.Write("info", "IGDB-nøkler funnet; brukes for info og spilletid");
                }
            }

            return _igdb;
        }
    }

    /// <summary>Omslag, bakgrunn og logo fra SteamGridDB (første treff på tittelen), eller null.</summary>
    private async Task<(string? Cover, string? Hero, string? Logo)?> SteamGridDbArtAsync(Game game)
    {
        if (SteamGridDb is not { } sgdb)
        {
            return null;
        }

        var appId = SteamAppIdFor(game);
        var match = appId is { } id ? await sgdb.GetGameBySteamAppIdAsync(id) : null;
        match ??= First(await sgdb.SearchAsync(GameLibrary.DisplayTitle(game)));
        if (match is null)
        {
            return null;
        }

        var cover = First(await sgdb.GetGridsAsync(match.Id, SteamGridDbGridShape.Portrait))?.Url;
        var hero = First(await sgdb.GetHeroesAsync(match.Id))?.Url;
        var logo = First(await sgdb.GetLogosAsync(match.Id))?.Url;
        return cover is null && hero is null && logo is null ? null : (cover, hero, logo);
    }

    /// <summary>Info og spilletid fra IGDB (via Steam-appid hvis kjent, ellers tittelen), eller null.</summary>
    private async Task<(GameDetails Details, IgdbTimeToBeat? Time)?> IgdbDetailsAsync(Game game, int? steamAppId)
    {
        if (Igdb is not { } igdb)
        {
            return null;
        }

        var match = steamAppId is { } id ? await igdb.FindBySteamAppIdAsync(id) : null;
        match ??= First(await igdb.SearchAsync(GameLibrary.DisplayTitle(game), 1));
        if (match is null)
        {
            return null;
        }

        var time = await igdb.GetTimeToBeatAsync(match.Id);
        return (IgdbClient.ToDetails(match, DateTime.UtcNow), time);
    }

    private static T? First<T>(IReadOnlyList<T> items) where T : class => items.Count > 0 ? items[0] : null;

    private static int? Hours(int? seconds) => seconds is { } s and > 0 ? (int)Math.Round(s / 3600.0) : null;

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
            await FillArtGapsAsync();
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
                    var appId = SteamAppIdFor(game) ?? await store.FindAppIdAsync(title);
                    if (appId is not { } steamId)
                    {
                        // Ikke på Steam: prøv Playnite-omslaget fra gaming-PC-en via Vibepollo (Ubisoft, EA, Epic …).
                        _vibepolloApps ??= Vibepollo is { } api ? await api.GetAppsAsync() : [];
                        if (await DownloadVibepolloCoverAsync(game, _vibepolloApps) is { } cover)
                        {
                            _library.SetArt(game.Id, game.Art with { Cover = cover });
                            SaveLibrary();
                            if (TileFor(game.Id) is { } coverTile && _library.Find(game.Id) is { } withCover)
                            {
                                SetTileContent(coverTile, withCover);
                            }

                            found++;
                            _log.Write("bilder", $"Omslag for «{title}» fra Vibepollo (Playnite)");
                        }
                        else if (await SteamGridDbArtAsync(game) is { } grid && await DownloadArtAsync(game, grid.Cover, grid.Hero, grid.Logo) is { } gridArt)
                        {
                            _library.SetArt(game.Id, gridArt);
                            SaveLibrary();
                            if (TileFor(game.Id) is { } gridTile && _library.Find(game.Id) is { } withGrid)
                            {
                                SetTileContent(gridTile, withGrid);
                            }

                            found++;
                            _log.Write("bilder", $"Bilder for «{title}» fra SteamGridDB");
                        }
                        else
                        {
                            _log.Write("bilder", $"Fant ikke bilder for «{title}» på Steam eller i Vibepollo{(SteamGridDb is null ? " (SteamGridDB har ingen nøkkel ennå)" : " eller på SteamGridDB")}");
                            AddNotice("art:" + game.Id, game.Id, $"Fant ikke bilder for «{title}». Riktig navn gjør at Steam finner spillet.");
                        }
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
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
                {
                    // Nettverksfeil (for eksempel Wi-Fi som har falt ut): prøv igjen neste gang Home blir aktiv.
                    _artTried.Remove(game.Id);
                    _log.Write("bilder", $"Henting av bilder for «{title}» feilet, prøver igjen senere: {ex.Message}");
                    AddNotice("network", null, "Bilder kunne ikke hentes akkurat nå (nettverk). GameHome prøver igjen når Home blir aktiv.");
                }

                ArtProgress.Value = i + 1;
            }
        }
        finally
        {
            // Fremdriften vises bare mens noe hentes. Det som ikke ble funnet, ligger i varslene med en knapp for å rette det.
            _hostArtRunning = false;
            ArtProgressPanel.Visibility = Visibility.Collapsed;
            _log.Write("bilder", string.Create(CultureInfo.InvariantCulture, $"Bilder hentet for {found} av {missing.Count} spill"));
        }

        await FillArtGapsAsync();
    }

    private readonly HashSet<string> _artGapTried = new(StringComparer.Ordinal);

    /// <summary>
    /// Spill som har bilder, men mangler omslag, bakgrunn eller logo, får det som mangler fra SteamGridDB. Det som finnes
    /// (også bilder brukeren har valgt selv), røres ikke. Hvert spill prøves én gang per kjøring.
    /// </summary>
    private async Task FillArtGapsAsync()
    {
        if (SteamGridDb is null || _hostArtRunning || SessionActive)
        {
            return;
        }

        static bool Has(Game g, Func<ArtSet, string?> kind) =>
            kind(g.Art) is not null || (g.Overrides.Art is { } mine && kind(mine) is not null);

        var gaps = _library.VisibleGames
            // Steam-spill har egne bilder (lokalt og fra Steam); de hentes ikke på nytt fra nett.
            .Where(g => !_artGapTried.Contains(g.Id) && SteamAppIdFor(g) is null && (!Has(g, a => a.Cover) || !Has(g, a => a.Hero) || !Has(g, a => a.Logo)))
            .ToList();
        if (gaps.Count == 0)
        {
            return;
        }

        _hostArtRunning = true;
        try
        {
            foreach (var game in gaps)
            {
                if (SessionActive)
                {
                    break;
                }

                _artGapTried.Add(game.Id);
                var title = GameLibrary.DisplayTitle(game);
                try
                {
                    if (await SteamGridDbArtAsync(game) is not { } grid)
                    {
                        continue;
                    }

                    var cover = Has(game, a => a.Cover) ? null : grid.Cover;
                    var hero = Has(game, a => a.Hero) ? null : grid.Hero;
                    var logo = Has(game, a => a.Logo) ? null : grid.Logo;
                    if ((cover ?? hero ?? logo) is null || await DownloadArtAsync(game, cover, hero, logo) is not { } art)
                    {
                        continue;
                    }

                    _library.SetArt(game.Id, art);
                    SaveLibrary();
                    if (TileFor(game.Id) is { } tile && _library.Find(game.Id) is { } updated)
                    {
                        SetTileContent(tile, updated);
                    }

                    _log.Write("bilder", $"Fylte inn for «{title}» fra SteamGridDB: " +
                        $"omslag {(cover is null ? "–" : "ja")}, bakgrunn {(hero is null ? "–" : "ja")}, logo {(logo is null ? "–" : "ja")}");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
                {
                    _artGapTried.Remove(game.Id);
                    _log.Write("bilder", $"Utfylling av bilder for «{title}» feilet, prøver igjen senere: {ex.Message}");
                }
            }
        }
        finally
        {
            _hostArtRunning = false;
        }
    }

    // ---- Varsler: det som ikke gikk, med en knapp for å rette det ----

    private sealed record Notice(string Key, string? GameId, string Text, DateTime When);

    private readonly List<Notice> _notices = [];

    /// <summary>Legger til (eller oppdaterer) et varsel. Samme nøkkel gir ett varsel, ikke flere.</summary>
    private void AddNotice(string key, string? gameId, string text)
    {
        _notices.RemoveAll(n => n.Key == key);
        _notices.Insert(0, new Notice(key, gameId, text, DateTime.Now));
        if (_notices.Count > 20)
        {
            _notices.RemoveRange(20, _notices.Count - 20);
        }

        UpdateNoticeButton();
    }

    private void RemoveNoticesFor(string gameId)
    {
        if (_notices.RemoveAll(n => n.GameId == gameId) > 0)
        {
            UpdateNoticeButton();
            if (NoticePanel.Visibility == Visibility.Visible)
            {
                FillNotices();
            }
        }
    }

    private void UpdateNoticeButton()
    {
        NoticeButton.Visibility = _notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeCount.Text = _notices.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void NoticeButton_Click(object sender, RoutedEventArgs e)
    {
        FillNotices();
        NoticePanel.Visibility = Visibility.Visible;
        FadeIn(NoticePanel);
        SetHomeEnabled(false);
        _log.Write("varsler", $"Varsler åpnet ({_notices.Count})");
    }

    private void FillNotices()
    {
        NoticeList.Children.Clear();
        foreach (var notice in _notices)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = notice.Text, FontSize = 18, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock
            {
                Text = notice.When.ToString("HH:mm", CultureInfo.InvariantCulture),
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhTextTertiaryBrush"],
            });
            grid.Children.Add(text);
            if (notice.GameId is { } gameId && _library.Find(gameId) is { } game)
            {
                var fix = new Button { Content = "Finn riktig spill", Height = 52, Padding = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
                fix.Click += (_, _) =>
                {
                    CloseNotices(focusHome: false);
                    ShowFind(game);
                };
                Grid.SetColumn(fix, 1);
                grid.Children.Add(fix);
            }

            NoticeList.Children.Add(new Border
            {
                Child = grid,
                Padding = new Thickness(16, 12, 12, 12),
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GhSurfaceBrush"],
            });
        }

        if (_notices.Count == 0)
        {
            NoticeList.Children.Add(new TextBlock { Text = "Ingen varsler.", FontSize = 18 });
        }

        var firstFix = NoticeList.Children.OfType<Border>().Select(b => (b.Child as Grid)?.Children.OfType<Button>().FirstOrDefault()).FirstOrDefault(b => b is not null);
        (firstFix ?? NoticeCloseButton).Focus(FocusState.Keyboard);
    }

    private void NoticeClearButton_Click(object sender, RoutedEventArgs e)
    {
        _notices.Clear();
        UpdateNoticeButton();
        CloseNotices();
    }

    private void NoticeCloseButton_Click(object sender, RoutedEventArgs e) => CloseNotices();

    private void CloseNotices(bool focusHome = true)
    {
        NoticePanel.Visibility = Visibility.Collapsed;
        NoticeList.Children.Clear();
        SetHomeEnabled(true);
        if (focusHome)
        {
            FocusHome(_focusGameId);
        }
    }

    /// <summary>Laster ned bildene som finnes (Steam har ikke alltid logo eller bakgrunn), til GameHomes bildemappe.</summary>
    private static Task<ArtSet?> DownloadArtAsync(Game game, SteamImageUrls urls) => DownloadArtAsync(game, urls.Cover, urls.Hero, urls.Logo);

    /// <summary>Laster ned de bildene som finnes (null hoppes over) til appens art-mappe.</summary>
    private static async Task<ArtSet?> DownloadArtAsync(Game game, string? coverUrl, string? heroUrl, string? logoUrl)
    {
        var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
        Directory.CreateDirectory(folder);

        async Task<string?> Get(string? url, string kind)
        {
            if (url is null)
            {
                return null;
            }

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

        var cover = await Get(coverUrl, "cover");
        var hero = await Get(heroUrl, "hero");
        var logo = await Get(logoUrl, "logo");
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
        DebugResultText.Visibility = Visibility.Visible;
        if (host?.Address is null)
        {
            DebugResultText.Text = "Moonlight kjenner ingen PC med adresse ennå.";
            return;
        }

        var lines = new List<string>
        {
            $"Adresse: {host.Address} · vekkeadresse (MAC): {(host.Mac is null ? "mangler" : "kjent")}",
        };
        void Show(string line)
        {
            lines.Add(line);
            DebugResultText.Text = string.Join('\n', lines);
        }

        Show("Sjekker om den svarer …");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await HostWaker.EnsureAwakeAsync(host, TimeSpan.FromSeconds(90),
            onWaking: () => DispatcherQueue.TryEnqueue(() => Show("Svarer ikke. Sender vekkesignal (Wake-on-LAN) …")),
            progress: p => DispatcherQueue.TryEnqueue(() =>
            {
                lines[^1] = WakeStatusText(host, p);
                DebugResultText.Text = string.Join('\n', lines);
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

                _ = EndRemoteGameAsync(stream);
            },
            "Avbryt", () => ShowSessionPanel("avslutt fjernspill avbrutt"));
    }

    /// <summary>
    /// Avslutter spillet på gaming-PC-en: via Vibepollos API når nøkkelen finnes (uten Moonlights «Quitting»-vindu),
    /// ellers med <c>Moonlight.exe quit</c>. Lukker deretter Moonlight lokalt.
    /// </summary>
    private async Task EndRemoteGameAsync(StreamSession stream)
    {
        if (Vibepollo is { } api)
        {
            try
            {
                if (await api.CloseAppAsync())
                {
                    _log.Write("økt", "Spillet avsluttet på PC-en via Vibepollo-API");
                    await stream.CloseClientAfterRemoteEndAsync();
                    return;
                }

                _log.Write("økt", "Vibepollo-API avslo avslutning; bruker Moonlight");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _log.Write("økt", $"Vibepollo-API svarte ikke ({ex.Message}); bruker Moonlight");
            }
        }

        await stream.EndRemoteAppAsync();
    }

    // ---- Vibepollo-API (valgfritt; virker bare når brukeren har laget en nøkkel) ----

    private const string VibepolloVaultResource = "GameHome.Vibepollo";
    private VibepolloClient? _vibepollo;
    private bool _vibepolloChecked;
    private string? _hostRunningApp;
    private IReadOnlyList<VibepolloApp>? _vibepolloApps;
    private DateTime _lastHostStatus = DateTime.MinValue;

    /// <summary>Klienten, eller null hvis det ikke finnes nøkkel eller host.</summary>
    private VibepolloClient? Vibepollo
    {
        get
        {
            if (_vibepolloChecked)
            {
                return _vibepollo;
            }

            _vibepolloChecked = true;
            var hosts = MoonlightClient.FindHosts();
            if (hosts.Count == 0 || hosts[0].Address is not { } address || LoadVibepolloToken() is not { } token)
            {
                return null;
            }

            var settings = ApplicationData.Current.LocalSettings.Values;
            _vibepollo = new VibepolloClient(address, token, settings["vibepollo.cert"] as string, thumbprint =>
            {
                settings["vibepollo.cert"] = thumbprint;
                _log.Write("vibepollo", "Sertifikatet til gaming-PC-en er festet (første tilkobling)");
            });
            _log.Write("vibepollo", "API-nøkkel funnet; bruker Vibepollo for omslag, status og avslutning");
            return _vibepollo;
        }
    }

    /// <summary>
    /// Nøkkelen ligger i Windows' sikre lager (Credential Locker). Legges en fil <c>vibepollo-token.txt</c> i appens
    /// LocalState (over SSH ved oppsett), flyttes den inn i lageret og filen slettes.
    /// </summary>
    private string? LoadVibepolloToken()
    {
        var vault = new global::Windows.Security.Credentials.PasswordVault();
        var file = Path.Combine(ApplicationData.Current.LocalFolder.Path, "vibepollo-token.txt");
        try
        {
            if (File.Exists(file))
            {
                var fromFile = File.ReadAllText(file).Trim();
                File.Delete(file);
                if (fromFile.Length > 0)
                {
                    vault.Add(new global::Windows.Security.Credentials.PasswordCredential(VibepolloVaultResource, "token", fromFile));
                    _log.Write("vibepollo", "Ny API-nøkkel flyttet til Windows' sikre lager; filen er slettet");
                }
            }

            var credential = vault.Retrieve(VibepolloVaultResource, "token");
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Henter hva som kjører på PC-en (høyst hvert 10. sekund, aldri under en økt) og oppdaterer flisene.</summary>
    private async Task RefreshHostStatusAsync(bool force = false)
    {
        if (SessionActive || Vibepollo is not { } api || (!force && DateTime.UtcNow - _lastHostStatus < TimeSpan.FromSeconds(10)))
        {
            return;
        }

        _lastHostStatus = DateTime.UtcNow;
        try
        {
            var status = await api.GetStatusAsync();
            var running = status is { AppRunning: true } ? status.AppName : null;
            if (!string.Equals(running, _hostRunningApp, StringComparison.Ordinal))
            {
                _hostRunningApp = running;
                _log.Write("vibepollo", running is null ? "Ingenting kjører på PC-en" : $"«{running}» kjører på PC-en");
                RefreshTileTexts();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            // PC-en er av eller utilgjengelig; ikke vis noe som kjører.
            _hostRunningApp = null;
        }
    }

    /// <summary>Lukker appen som kjører på PC-en via API-et og starter så det valgte spillet.</summary>
    private async Task CloseHostAppThenLaunchAsync(Game game, PlayAction action)
    {
        if (_launching)
        {
            return;
        }

        // Vis med en gang at noe skjer: Vibepollo kan bruke over 10 s på å svare mens spillet lukkes (målt 2026-11-02).
        var other = _hostRunningApp ?? "spillet";
        _launching = true;
        ShowLoading(game, action);
        LoadingStatus.Text = $"Avslutter {other} på {HostLabel} …";
        try
        {
            var closed = false;
            if (Vibepollo is { } api)
            {
                try
                {
                    closed = await api.CloseAppAsync();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    _log.Write("vibepollo", $"Svar på avslutning uteble ({ex.Message}); sjekker status");
                }
            }

            _log.Write("vibepollo", closed ? $"«{other}» avsluttet på PC-en før ny start" : "Avslutning ikke bekreftet; venter på status før ny start");

            // Også uten bekreftelse: vent til PC-en melder at ingenting kjører, så Moonlight ikke spør om å avslutte.
            await WaitUntilHostIdleAsync();
        }
        finally
        {
            _launching = false;
        }

        if (_hostRunningApp is { } still)
        {
            HideLoading("PC-en lukket ikke spillet");
            ShowMessage($"{still} kjører fortsatt på {HostLabel}", $"Fikk ikke avsluttet {still}. Start {still} og avslutt det fra M2-menyen, eller prøv igjen.",
                "Prøv igjen", () => _ = CloseHostAppThenLaunchAsync(game, action), "Lukk", () => FocusHome(game.Id));
            return;
        }

        await LaunchAsync(game, action);
    }

    /// <summary>Lukker appen på PC-en (etter at brukeren har bekreftet) og oppdaterer flisene.</summary>
    private async Task CloseHostAppAsync(Game game)
    {
        var closed = false;
        if (Vibepollo is { } api)
        {
            try
            {
                closed = await api.CloseAppAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _log.Write("vibepollo", $"Kunne ikke avslutte appen på PC-en: {ex.Message}");
            }
        }

        _log.Write("vibepollo", closed ? $"«{GameLibrary.DisplayTitle(game)}» avsluttet på PC-en fra Home" : "Avslutning fra Home feilet");
        if (closed)
        {
            await WaitUntilHostIdleAsync();
        }
        else
        {
            ShowMessage($"Fikk ikke avsluttet på {HostLabel}", $"{HostLabel} svarte ikke. Prøv igjen, eller start spillet og avslutt fra M2-menyen.",
                "OK", () => FocusHome(game.Id), "Lukk", () => FocusHome(game.Id));
        }

        FocusHome(game.Id);
    }

    /// <summary>Venter (høyst 15 s) til Vibepollo melder at ingen app kjører, så neste start ikke møter Moonlights «quit»-spørsmål.</summary>
    private async Task WaitUntilHostIdleAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            await RefreshHostStatusAsync(force: true);
            if (_hostRunningApp is null)
            {
                return;
            }

            await Task.Delay(500);
        }

        _log.Write("vibepollo", $"«{_hostRunningApp}» kjører fortsatt på PC-en etter 15 s");
    }

    /// <summary>Om spillet kjører på gaming-PC-en uten at GameHome strømmer det (for eksempel etter «Koble fra»).</summary>
    private bool IsRunningOnHost(Game game) =>
        !SessionActive && _hostRunningApp is { } running
        && _library.PlayActionsFor(game.Id).Any(a => a.IsRemote && GameLibrary.TitleKey(a.Target) == GameLibrary.TitleKey(running));

    /// <summary>Omslag fra Vibepollo (Playnite-omslaget) for et spill fra PC-en, eller null.</summary>
    private async Task<string?> DownloadVibepolloCoverAsync(Game game, IReadOnlyList<VibepolloApp> apps)
    {
        if (Vibepollo is not { } api
            || _library.PlayActionsFor(game.Id).FirstOrDefault(a => a.IsRemote) is not { } stream
            || apps.FirstOrDefault(a => a.Name is { } name && GameLibrary.TitleKey(name) == GameLibrary.TitleKey(stream.Target)) is not { Uuid: { } uuid, ImagePath.Length: > 0 })
        {
            return null;
        }

        if (await api.GetCoverAsync(uuid) is not { Length: > 0 } bytes)
        {
            return null;
        }

        var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{game.Id}-cover.png");
        await File.WriteAllBytesAsync(target, bytes);
        return target;
    }

    private void MessagePrimary_Click(object sender, RoutedEventArgs e) => CloseMessage(_messagePrimary);

    private void MessageSecondary_Click(object sender, RoutedEventArgs e) => CloseMessage(_messageSecondary);

    private void SetHomeEnabled(bool enabled)
    {
        // Er biblioteket åpent, ligger Home bak det og skal ikke kunne få fokus; biblioteket følger <paramref name="enabled"/>.
        var library = LibraryPanel.Visibility == Visibility.Visible;
        foreach (var child in GameTiles.Children.OfType<Button>()
            .Concat(ToolTiles.Children.OfType<Button>())
            .Append(PowerMenuButton))
        {
            child.IsEnabled = enabled && !library;
        }

        foreach (var child in LibraryGrid.Children.OfType<Button>()
            .Concat(LibraryTabs.Children.OfType<Button>())
            .Append(LibrarySortButton)
            .Append(LibraryCloseButton))
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

    /// <summary>Frigjør Vibepollo-klienten når vinduet lukkes.</summary>
    private void DisposeVibepollo()
    {
        _vibepollo?.Dispose();
        _vibepollo = null;
    }

    private void StopEverything()
    {
        StopHidCapture();
        _hotkeys.UnregisterAll();
        DisposeVibepollo();

        // Spillet får leve videre selv om GameHome lukkes.
        _session?.Dispose();
        _session = null;
    }

    // ---- Fokus, input og målinger ----

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _metricsTimer.Stop();

            // Uten spill eller filvelger fortsetter kontrollerlesingen sakte: noe annet (et varsel, en systemmeny) kan
            // ta fokus mens Home står fremme, og da skal første trykk hente Home tilbake, ikke kreve berøring.
            var keepListening = !SessionActive && !_pickerOpen;
            if (keepListening)
            {
                _gamepadTimer.Interval = TimeSpan.FromMilliseconds(250); // Sjelden og billig: bare en avlesning, ingen tråd.
            }
            else
            {
                _gamepadTimer.Stop();
            }

            _log.Write("fokus", $"Mistet fokus til {Foreground.CurrentProcessName() ?? "ukjent"}"
                + (keepListening ? " (lytter på kontrolleren for å hente Home tilbake)" : " (kontrollerlesing og målinger stoppet)"));

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
        _ = RefreshHostStatusAsync();
        RememberWifiNetwork();
        CheckLegionSpace("Home aktiv");
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
        _gamepadTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gamepadTimer.Start();
        _log.Write("fokus", $"Fikk fokus ({args.WindowActivationState})");
    }

    private static GamepadReading Combined(IReadOnlyList<Gamepad> gamepads)
    {
        var combined = gamepads[0].GetCurrentReading();
        for (var i = 1; i < gamepads.Count; i++)
        {
            var other = gamepads[i].GetCurrentReading();
            combined.Buttons |= other.Buttons;
            if ((other.LeftThumbstickX * other.LeftThumbstickX) + (other.LeftThumbstickY * other.LeftThumbstickY)
                > (combined.LeftThumbstickX * combined.LeftThumbstickX) + (combined.LeftThumbstickY * combined.LeftThumbstickY))
            {
                combined.LeftThumbstickX = other.LeftThumbstickX;
                combined.LeftThumbstickY = other.LeftThumbstickY;
            }

            combined.LeftTrigger = Math.Max(combined.LeftTrigger, other.LeftTrigger);
            combined.RightTrigger = Math.Max(combined.RightTrigger, other.RightTrigger);
        }

        return combined;
    }

    private void PollGamepad()
    {
        var gamepads = Gamepad.Gamepads;
        if (gamepads.Count == 0)
        {
            return;
        }

        // Alle kontrollere slås sammen: med Handheld Companion finnes både den fysiske (skjult av HidHide, sender ingenting
        // til GameHome) og den virtuelle, og rekkefølgen er ikke fast (målt 2026-11-05: Home reagerte ikke på kontrolleren).
        var reading = Combined(gamepads);
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

        // Home står fremme, men noe annet har fokus: første trykk henter Home tilbake (og brukes ikke til noe annet).
        if (!_windowActive)
        {
            if (SessionActive || _pickerOpen)
            {
                _gamepadTimer.Stop();
                return;
            }

            BringToFront($"kontroller ({Foreground.CurrentProcessName() ?? "ukjent"} hadde fokus)");
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
        if (direction is { } dir && FocusManager.TryMoveFocus(dir, new FindNextElementOptions { SearchRoot = Root.XamlRoot.Content }))
        {
            MenuSounds.Move();
        }

        var focusedButton = FocusManager.GetFocusedElement(Root.XamlRoot) as Button;

        // Hvis XAML selv leverer kontrollertaster, gjør knappene jobben; ellers aktiverer vi med A.
        if (pressed.HasFlag(GamepadButtons.A) && focusedButton is not null)
        {
            MenuSounds.Select();
            if (!_xamlGamepadKeysSeen)
            {
                new ButtonAutomationPeer(focusedButton).Invoke();
                return;
            }
        }

        // Y på en spillflis: om spillet. ☰ (Start/Menu): valg for spillet.
        var libraryOnTop = LibraryPanel.Visibility == Visibility.Visible && !PanelOverLibraryOpen;
        var onTile = focusedButton is not null
            && (libraryOnTop ? LibraryGrid.Children.Contains(focusedButton) : !AnyOverlayOpen && GameTiles.Children.Contains(focusedButton));
        var tileGame = onTile && focusedButton is { Tag: string id } ? _library.Find(id) : null;

        // Biblioteket: LB/RB bytter fane, X bytter sortering.
        if (libraryOnTop)
        {
            if (pressed.HasFlag(GamepadButtons.LeftShoulder) || pressed.HasFlag(GamepadButtons.RightShoulder))
            {
                MenuSounds.Move();
                MoveLibraryTab(pressed.HasFlag(GamepadButtons.LeftShoulder) ? -1 : 1);
                return;
            }

            if (pressed.HasFlag(GamepadButtons.X))
            {
                MenuSounds.Move();
                NextLibrarySort();
                return;
            }
        }

        if (tileGame is not null)
        {
            if (pressed.HasFlag(GamepadButtons.Y))
            {
                MenuSounds.Select();
                ShowDetails(tileGame);
                return;
            }

            if (pressed.HasFlag(GamepadButtons.Menu))
            {
                MenuSounds.Select();
                ShowOptions(tileGame);
                return;
            }
        }

        // ☰ i «Om spillet» går rett til valgene.
        if (pressed.HasFlag(GamepadButtons.Menu) && DetailsPanel.Visibility == Visibility.Visible && _detailsGame is { } detailsGame
            && MessagePanel.Visibility != Visibility.Visible)
        {
            CloseDetails(focusHome: false);
            ShowOptions(detailsGame);
            return;
        }

        // B lukker det øverste panelet.
        if (pressed.HasFlag(GamepadButtons.B))
        {
            MenuSounds.Back();
            if (MessagePanel.Visibility == Visibility.Visible)
            {
                CloseMessage(_messageCancel);
            }
            else if (ArtPanel.Visibility == Visibility.Visible)
            {
                CloseArtPicker();
            }
            else if (FindPanel.Visibility == Visibility.Visible)
            {
                CloseFind();
            }
            else if (NoticePanel.Visibility == Visibility.Visible)
            {
                CloseNotices();
            }
            else if (OptionsPanel.Visibility == Visibility.Visible)
            {
                CloseOptions();
            }
            else if (DetailsPanel.Visibility == Visibility.Visible)
            {
                CloseDetails();
            }
            else if (LibraryPanel.Visibility == Visibility.Visible)
            {
                CloseLibrary();
            }
            else if (DebugPanel.Visibility == Visibility.Visible)
            {
                CloseDebug();
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
                // B går tilbake til spillet, som på Steam Deck og Switch. «Til Home» ligger som egen knapp.
                ResumeButton_Click(ResumeButton, new RoutedEventArgs());
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
