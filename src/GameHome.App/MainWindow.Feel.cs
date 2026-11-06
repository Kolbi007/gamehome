// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Sources.Steam;
using GameHome.Windows;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Gaming.Input;

namespace GameHome.App;

/// <summary>
/// Følelsen av en konsoll: knappesymboler i hintlinjene (som på kontrolleren), litt vibrasjon ved valg, og en rolig
/// visningsmodus i Home som viser fram andre spill når ingen rører noe.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Knappesymboler ----

    /// <summary>Fyller en hintlinje: runde, fargede A/B/X/Y som på kontrolleren, grå for skulder- og menyknapper.</summary>
    private static void SetHints(StackPanel target, params (string Button, string Label)[] hints)
    {
        target.Children.Clear();
        foreach (var (button, label) in hints)
        {
            var color = button switch
            {
                "A" => ColorHelper.FromArgb(0xFF, 0x5C, 0xB8, 0x3E),
                "B" => ColorHelper.FromArgb(0xFF, 0xE0, 0x4F, 0x45),
                "X" => ColorHelper.FromArgb(0xFF, 0x3D, 0x8B, 0xF2),
                "Y" => ColorHelper.FromArgb(0xFF, 0xE8, 0xB9, 0x1A),
                _ => ColorHelper.FromArgb(0xFF, 0x5A, 0x60, 0x68),
            };
            var round = button.Length == 1 && char.IsLetter(button[0]);
            var glyph = new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(round ? 11 : 6),
                MinWidth = 22,
                Height = 22,
                Padding = new Thickness(round ? 0 : 6, 0, round ? 0 : 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = button,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            item.Children.Add(glyph);
            item.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 15,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA0, 0xA7, 0xB0)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            target.Children.Add(item);
        }
    }

    private void SetStaticHints()
    {
        SetHints(LibraryHints, ("A", "Spill"), ("Y", "Om spillet"), ("☰", "Valg og fest"), ("X", "Sortering"), ("⧉", "Søk"),
            ("LB RB", "Fane"), ("LT RT", "Bla"), ("B", "Tilbake"));
        SetHints(SessionHints, ("A", "Velg"), ("B", "Tilbake til spillet"), ("◀", "Ytelse"));
        SetHints(HomeMenuHints, ("A", "Velg"), ("B", "Tilbake"), ("◀", "Ytelse"));
    }

    // ---- Vibrasjon ----

    private DispatcherQueueTimer? _rumbleTimer;

    /// <summary>
    /// Et kort, svakt dunk i kontrolleren (via Handheld Companions virtuelle kontroller) når noe velges eller et spill
    /// starter. Følger av/på for menylyder.
    /// </summary>
    private void Pulse(double strength = 0.18, int milliseconds = 45)
    {
        if (!MenuSounds.Enabled || SessionActive)
        {
            return;
        }

        try
        {
            foreach (var pad in Gamepad.Gamepads)
            {
                pad.Vibration = new GamepadVibration { LeftMotor = strength, RightMotor = strength };
            }

            _rumbleTimer ??= CreateRumbleTimer();
            _rumbleTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            _rumbleTimer.Stop();
            _rumbleTimer.Start();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Ingen vibrasjon å få; det er greit.
        }
    }

    private DispatcherQueueTimer CreateRumbleTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            foreach (var pad in Gamepad.Gamepads)
            {
                pad.Vibration = default;
            }
        };
        return timer;
    }


    // ---- Lesing av kontrolleren ----

    /// <summary>
    /// 60 avlesninger i sekundet når noen bruker Home, 30 etter 10 s uten input og 20 etter ett minutt. Første trykk
    /// gir 60 igjen (forsinkelsen er da høyst 50 ms, ikke merkbart). Sparer CPU og batteri når Home bare står på.
    /// </summary>
    private void AdaptPollRate()
    {
        if (!_windowActive)
        {
            return; // Inaktivt vindu har sin egen, lave takt (250 ms).
        }

        var idle = DateTime.UtcNow - _lastUserInput;
        var wanted = idle > TimeSpan.FromMinutes(1) ? 50 : idle > TimeSpan.FromSeconds(10) ? 33 : 16;
        if ((int)_gamepadTimer.Interval.TotalMilliseconds != wanted)
        {
            _gamepadTimer.Interval = TimeSpan.FromMilliseconds(wanted);
        }
    }

    // ---- Visningsmodus ----

    private static readonly TimeSpan AttractAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttractEvery = TimeSpan.FromSeconds(10);

    /// <summary>Lysbildene i blandet rekkefølge: installerte spill (Game) og Steam-spill som ikke er installert (SteamLibraryApp).</summary>
    private readonly Queue<object> _attractDeck = new();
    private DateTime _lastUserInput = DateTime.UtcNow;
    private DateTime _lastAttractSwitch = DateTime.MinValue;
    private DispatcherQueueTimer? _attractTimer;
    private bool _attracting;

    /// <summary>Kalles ved all input (kontroller, tastatur, berøring). Stopper visningsmodus.</summary>
    /// <returns>true hvis input bare vekket Home fra visningsmodus og ikke skal brukes til noe annet.</returns>
    private bool NoteUserInput(bool primary = false)
    {
        _lastUserInput = DateTime.UtcNow;
        if (!_attracting)
        {
            return false;
        }

        _attracting = false;
        FadeTo(HeroInfo, 1, 200); // Kan ha vært midt i en uttoning i lysbildeserien.
        if (primary && _attractSuggestion is not null)
        {
            InstallAttractSuggestion(); // A på et forslag som ikke er installert: installer det.
        }

        _attractSuggestion = null;
        _heroGameId = null;
        RefreshHero(force: false);
        if (Root.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is Button { Tag: string id } tile && GameTiles.Children.Contains(tile))
        {
            OnTileFocused(tile, id);
        }

        return true;
    }

    private void StartAttractTimer()
    {
        if (_attractTimer is null)
        {
            _attractTimer = DispatcherQueue.CreateTimer();
            _attractTimer.Interval = TimeSpan.FromSeconds(2); // Billig sjekk; gir bytte hvert 10.–12. sekund.
            _attractTimer.Tick += (_, _) => AttractTick();
        }

        _lastUserInput = DateTime.UtcNow;
        _attractTimer.Start();
    }

    private void StopAttractTimer() => _attractTimer?.Stop();

    /// <summary>
    /// Lysbildeserie: etter 30 s uten input i Home viser hovedfeltet et nytt spill hvert 10. sekund. Alle spillene i
    /// biblioteket og Steam-spillene du har, men ikke har installert, vises i blandet rekkefølge, hvert spill én gang
    /// før rekken begynner på nytt. Bare bilder som allerede ligger på disken brukes (ingen nedlasting).
    /// </summary>
    private async void AttractTick()
    {
        if (SessionActive || AnyOverlayOpen || _attractSwitching || DateTime.UtcNow - _lastUserInput < AttractAfter
            || DateTime.UtcNow - _lastAttractSwitch < AttractEvery)
        {
            return;
        }

        if (_attractDeck.Count == 0)
        {
            FillAttractDeck();
        }

        object? slide = null;
        while (slide is null && _attractDeck.TryDequeue(out var next))
        {
            slide = next switch
            {
                Game game when game.Id != _heroGameId && ArtFor(game).Hero is { } hero && File.Exists(hero) => game,
                SteamLibraryApp app when app.Hero is { } appHero && File.Exists(appHero) => app,
                _ => null,
            };
        }

        if (slide is null)
        {
            return;
        }

        // Myk overgang: logo og tekst tones ut, bakgrunnen krysstones (1,6 s, MainWindow.LoadAmbientAsync), og logo og
        // tekst tones rolig inn igjen. Rører brukeren noe underveis, avbrytes byttet (NoteUserInput).
        StopTrailer();
        var inputBefore = _lastUserInput;
        _attractSwitching = true;
        _lastAttractSwitch = DateTime.UtcNow;
        try
        {
            if (_attracting)
            {
                FadeTo(HeroInfo, 0, 450);
                await Task.Delay(470);
                if (_lastUserInput != inputBefore || SessionActive || AnyOverlayOpen)
                {
                    return;
                }
            }

            _attracting = true;
            if (slide is Game game)
            {
                _attractSuggestion = null;
                UpdateHero(game);
                HeroFacts.Text = $"{AttractReason(game)} · {HeroFacts.Text}";
                ShowAmbientFor(game);
            }
            else if (slide is SteamLibraryApp app)
            {
                ShowAttractSuggestion(app);
            }

            await Task.Delay(250); // Logoen rekker å bli lest inn før den tones inn.
            if (_attracting)
            {
                FadeTo(HeroInfo, 1, 900);
            }
        }
        finally
        {
            _attractSwitching = false;
        }
    }

    private bool _attractSwitching;

    private void FillAttractDeck()
    {
        var slides = new List<object>();
        slides.AddRange(_library.VisibleGames);
        slides.AddRange(NotInstalledSteamGames().Where(a => a.Hero is not null));
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(slides));
        foreach (var slide in slides)
        {
            _attractDeck.Enqueue(slide);
        }

        _log.Write("visning", string.Create(CultureInfo.InvariantCulture, $"Lysbildeserie: {slides.Count} spill i ny rekkefølge"));
    }

    private string AttractReason(Game game)
    {
        var last = LastPlayed(game);
        if (last == DateTime.MinValue)
        {
            return "Ikke prøvd ennå";
        }

        var days = (DateTime.UtcNow - last).TotalDays;
        return days >= 14 ? $"Ikke spilt på {(int)(days / 7)} uker"
            : days >= 2 ? $"Ikke spilt på {(int)days} dager"
            : $"Du spilte {GameLibrary.DisplayTitle(game)} nylig";
    }
}
