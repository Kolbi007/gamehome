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
/// Testflate. Kontrolleren leses med Windows.Gaming.Input bare mens vinduet har fokus; når vinduet mister
/// fokus stopper lesingen helt (ingen bakgrunnsarbeid mens et spill kjører).
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MaxVisibleEvents = 14;
    private const double StickThreshold = 0.6;
    private const double TriggerThreshold = 0.5;

    private readonly FileLog _log;
    private readonly List<string> _events = [];
    private readonly DispatcherQueueTimer _gamepadTimer;
    private readonly DispatcherQueueTimer _metricsTimer;
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
        _log.Write("start", "Logg koblet til skjermen");

        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        Activated += OnWindowActivated;

        _gamepadTimer = DispatcherQueue.CreateTimer();
        _gamepadTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gamepadTimer.Tick += (_, _) => PollGamepad();

        // Ytelsespanelet oppdateres bare mens vinduet har fokus.
        _metricsTimer = DispatcherQueue.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(2);
        _metricsTimer.Tick += (_, _) => UpdateMemory();

        Gamepad.GamepadAdded += (_, _) => _log.Write("kontroller", "Kontroller tilkoblet");
        Gamepad.GamepadRemoved += (_, _) => _log.Write("kontroller", "Kontroller frakoblet");
        _log.Write("start", "Kontroller-hendelser koblet");

        var variant = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT";
        InfoText.Text = $"Versjon {typeof(MainWindow).Assembly.GetName().Version} · {variant} · logg: gamehome.log";
    }

    /// <summary>Henter vinduet frem, for eksempel når <c>gamehome://session</c> sendes videre hit.</summary>
    internal void BringToFront()
    {
        Activate();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ok = Foreground.Bring(handle);
        _log.Write("fokus", ok ? "Tok forgrunnen" : "Windows avslo forgrunnen");
    }

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
            Tile1.Focus(FocusState.Keyboard);
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
        if (sender is Button tile)
        {
            LastInput.Text = $"Valgt: {tile.Content} ({_lastSource})";
            _log.Write("valg", $"{tile.Content} valgt via {_lastSource}");
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
