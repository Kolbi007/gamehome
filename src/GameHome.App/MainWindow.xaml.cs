// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameHome.Core.Diagnostics;
using GameHome.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Gaming.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace GameHome.App;

/// <summary>
/// Testflate. Kontrolleren leses med Windows.Gaming.Input bare mens vinduet har fokus. Hurtigtasten for
/// øktmenyen er registrert bare mens en testøkt kjører.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MaxVisibleEvents = 14;
    private const double StickThreshold = 0.6;
    private const double TriggerThreshold = 0.5;
    private const string LegionGoController = "vid_17ef&pid_61eb";
    private static readonly TimeSpan HidCaptureDuration = TimeSpan.FromSeconds(15);

    // Kandidater for M2. Legion Space kan legge M2 på én av dem; loggen viser hvilken som kom.
    private static readonly (int Id, HotkeyModifiers Modifiers, uint Key, string Name)[] SessionHotkeys =
    [
        (1, HotkeyModifiers.None, 0x7C, "F13"),
        (2, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7B, "Ctrl+Alt+Shift+F12"),
        (3, HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B, "Ctrl+Shift+F12"),
    ];

    private readonly FileLog _log;
    private readonly List<string> _events = [];
    private readonly DispatcherQueueTimer _gamepadTimer;
    private readonly DispatcherQueueTimer _metricsTimer;
    private readonly HotkeyListener _hotkeys;
    private readonly List<HidReportListener> _hidListeners = [];
    private Process? _session;
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

        // Ytelsespanelet oppdateres bare mens vinduet har fokus.
        _metricsTimer = DispatcherQueue.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(2);
        _metricsTimer.Tick += (_, _) => UpdateMemory();

        _hotkeys = new HotkeyListener(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _hotkeys.Pressed += OnSessionHotkey;

        Gamepad.GamepadAdded += (_, _) => _log.Write("kontroller", "Kontroller tilkoblet");
        Gamepad.GamepadRemoved += (_, _) => _log.Write("kontroller", "Kontroller frakoblet");

        var variant = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT";
        InfoText.Text = $"Versjon {typeof(MainWindow).Assembly.GetName().Version} · {variant} · logg: gamehome.log";
        _log.Write("start", "Vindu klart");
    }

    private bool SessionActive => _session is { HasExited: false };

    /// <summary>Henter vinduet frem, for eksempel når <c>gamehome://session</c> sendes videre hit.</summary>
    internal void BringToFront()
    {
        Activate();
        var ok = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _log.Write("fokus", ok ? "Tok forgrunnen" : "Windows avslo forgrunnen");
        if (SessionActive)
        {
            ShowSessionPanel("protokoll");
        }
    }

    // ---- Testøkt og øktmeny (POC B) ----

    private void StartTestSession()
    {
        if (SessionActive)
        {
            ShowSessionPanel("flis");
            return;
        }

        try
        {
            // Tegnkart er et klassisk Win32-program og fungerer som stand-in for et spill.
            _session = Process.Start(new ProcessStartInfo("charmap.exe") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Write("økt", $"Kunne ikke starte stand-in: {ex.Message}");
            return;
        }

        if (_session is null)
        {
            _log.Write("økt", "Stand-in startet ikke");
            return;
        }

        _session.EnableRaisingEvents = true;
        _session.Exited += (_, _) => DispatcherQueue.TryEnqueue(EndTestSession);

        var registered = SessionHotkeys.Where(h => _hotkeys.Register(h.Id, h.Modifiers, h.Key)).Select(h => h.Name).ToList();
        _log.Write("økt", $"Testøkt startet (Tegnkart, PID {_session.Id}). Hurtigtaster: {string.Join(", ", registered)}");
        LastInput.Text = "Testøkt kjører. Trykk M2 for øktmenyen.";
    }

    private void EndTestSession()
    {
        _hotkeys.UnregisterAll();
        if (_session is not null)
        {
            _log.Write("økt", "Testøkt avsluttet (hurtigtaster fjernet)");
            _session.Dispose();
            _session = null;
        }

        HideSessionPanel();
    }

    private void OnSessionHotkey(object? sender, int id)
    {
        var name = SessionHotkeys.FirstOrDefault(h => h.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture);
        _log.Write("hurtigtast", $"Mottatt: {name}");
        Activate();
        var ok = Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _log.Write("fokus", ok ? "Tok forgrunnen etter hurtigtast" : "Windows avslo forgrunnen etter hurtigtast");
        ShowSessionPanel($"hurtigtast {name}");
    }

    private void ShowSessionPanel(string via)
    {
        SessionTitle.Text = "Tegnkart (testøkt)";
        SessionPanel.Visibility = Visibility.Visible;
        SetTilesEnabled(false);
        ResumeButton.Focus(FocusState.Keyboard);
        _log.Write("økt", $"«Kjører nå» vist via {via}");
    }

    private void HideSessionPanel()
    {
        SessionPanel.Visibility = Visibility.Collapsed;
        SetTilesEnabled(true);
        SessionTile.Focus(FocusState.Keyboard);
    }

    private void SetTilesEnabled(bool enabled)
    {
        foreach (var child in Tiles.Children.OfType<Button>())
        {
            child.IsEnabled = enabled;
        }
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { HasExited: false } session)
        {
            HideSessionPanel();
            return;
        }

        session.Refresh();
        var ok = Foreground.Bring(session.MainWindowHandle);
        _log.Write("fokus", ok ? "Tilbake til spillet: stand-in fikk forgrunnen" : "Tilbake til spillet: Windows avslo");
        HideSessionPanel();
    }

    private void EndSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is { HasExited: false } session)
        {
            session.CloseMainWindow();
            _log.Write("økt", "Ba stand-in avslutte normalt (WM_CLOSE)");
        }
        else
        {
            EndTestSession();
        }
    }

    private void ClosePanelButton_Click(object sender, RoutedEventArgs e) => HideSessionPanel();

    // ---- Legion-knapp via Lenovos HID-kanaler (diagnostikk) ----

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
            return;
        }

        if (!_startupLogged)
        {
            _startupLogged = true;
            var startup = DateTime.Now - Process.GetCurrentProcess().StartTime;
            var memoryMb = Environment.WorkingSet / (1024.0 * 1024.0);
            StartupText.Text = string.Create(CultureInfo.InvariantCulture, $"Oppstart: {startup.TotalMilliseconds:F0} ms");
            _log.Write("ytelse", string.Create(CultureInfo.InvariantCulture,
                $"Oppstart til første aktivering: {startup.TotalMilliseconds:F0} ms, minne: {memoryMb:F0} MB"));
            SessionTile.Focus(FocusState.Keyboard);
        }

        UpdateMemory();
        _metricsTimer.Start();
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
            names.Add(string.Create(CultureInfo.InvariantCulture, $"L2 ({reading.LeftTrigger:F2})"));
        }

        if (rightTriggerPressed)
        {
            names.Add(string.Create(CultureInfo.InvariantCulture, $"R2 ({reading.RightTrigger:F2})"));
        }

        if (stickMoved)
        {
            names.Add($"Venstre stikke {stick}");
        }

        var text = string.Join(", ", names);
        LastInput.Text = $"Kontroller: {text}";
        _log.Write("input", $"Kontroller (Windows.Gaming.Input): {text}");

        var direction = DPadDirection(pressed) ?? (stickMoved ? stick : null);
        if (direction is { } dir)
        {
            var moved = FocusManager.TryMoveFocus(dir, new FindNextElementOptions { SearchRoot = Root.XamlRoot.Content });
            if (moved && FocusManager.GetFocusedElement(Root.XamlRoot) is Button focused)
            {
                _log.Write("fokus", $"Fokus: {focused.Content}");
            }
        }

        // Hvis XAML selv leverer kontrollertaster, gjør knappene jobben; ellers aktiverer vi med A.
        if (pressed.HasFlag(GamepadButtons.A) && !_xamlGamepadKeysSeen
            && FocusManager.GetFocusedElement(Root.XamlRoot) is Button button)
        {
            new ButtonAutomationPeer(button).Invoke();
        }

        // B lukker øktpanelet.
        if (pressed.HasFlag(GamepadButtons.B) && SessionPanel.Visibility == Visibility.Visible)
        {
            HideSessionPanel();
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
        _log.Write("input", $"Tast via XAML: {e.Key}");
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _lastSource = e.Pointer.PointerDeviceType switch
        {
            PointerDeviceType.Touch => "berøring",
            PointerDeviceType.Pen => "penn",
            _ => "mus",
        };
        _log.Write("input", $"Peker: {_lastSource}");
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button tile)
        {
            return;
        }

        LastInput.Text = $"Valgt: {tile.Content} ({_lastSource})";
        _log.Write("valg", $"{tile.Content} valgt via {_lastSource}");

        if (tile == SessionTile)
        {
            StartTestSession();
        }
        else if (tile == HidTile)
        {
            StartHidCapture();
        }
    }

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
