// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Radios;

namespace GameHome.App;

/// <summary>
/// Hurtiginnstillinger i Ytelse-panelet: lysstyrke, volum, Wi-Fi og Bluetooth, så man slipper å gå ut til Windows'
/// hurtiginnstillinger midt i et spill. Bare dokumenterte Windows-API-er uten administrator (Brightness, Volume, Radios).
/// </summary>
public sealed partial class MainWindow
{
    private int? _brightness;

    /// <summary>Leser verdiene på nytt; kalles hver gang panelet vises.</summary>
    private async void FillQuickSettings()
    {
        ShowTdp();
        ShowVolume(Volume.Get());

        _brightness = await Brightness.GetAsync();
        ShowBrightness();

        var (wifi, bluetooth) = await Radios.GetAsync();
        ShowRadio(WifiToggle, "Wi-Fi", wifi);
        ShowRadio(BluetoothToggle, "Bluetooth", bluetooth);
    }

    // ---- TDP: fjernstyrt i Handheld Companion (ADR 0011) ----

    private void ShowTdp()
    {
        var hcRunning = HandheldCompanionRunning();
        var watts = HandheldCompanionTdp.Read();
        TdpValue.Text = watts is { } w && hcRunning ? $"{w:0} W" : "–";
        TdpDown.IsEnabled = TdpUp.IsEnabled = hcRunning && watts is not null;
    }

    private async void TdpStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string direction } button)
        {
            return;
        }

        var before = HandheldCompanionTdp.Read();
        var chord = direction == "+" ? HandheldCompanionTdp.IncreaseChord : HandheldCompanionTdp.DecreaseChord;
        var sent = await Keyboard.SendChordHeldAsync(TimeSpan.FromMilliseconds(60), chord);

        // HC lagrer profilen når verdien er endret; vent litt på at filen viser den nye verdien.
        var after = before;
        for (var i = 0; i < 10 && after == before; i++)
        {
            await Task.Delay(100);
            after = HandheldCompanionTdp.Read();
        }

        ShowTdp();
        button.Focus(FocusState.Keyboard);
        _log.Write("ytelse", !sent ? "TDP: Windows avslo tastetrykket"
            : after != before ? $"TDP {(direction == "+" ? "opp" : "ned")} i Handheld Companion: {before:0} → {after:0} W"
            : $"TDP {(direction == "+" ? "opp" : "ned")}: Handheld Companion endret ikke verdien ({before:0} W)");
        if (sent && after == before)
        {
            PerformanceStatus.Text = after is null
                ? "TDP styres i Handheld Companion. Kjør oppsettet (Set-HandheldCompanion.ps1) eller bruk knappen under."
                : $"TDP er {after:0} W, som er grensen i Handheld Companion.";
            PerformanceStatus.Visibility = Visibility.Visible;
        }
    }

    private void ShowBrightness()
    {
        BrightnessValue.Text = _brightness is { } percent ? $"{percent} %" : "–";
        BrightnessDown.IsEnabled = BrightnessUp.IsEnabled = _brightness is not null;
    }

    private void ShowVolume((int Percent, bool Muted)? volume)
    {
        VolumeMute.Content = volume switch
        {
            null => "–",
            { Muted: true } => "Lyd av",
            { } v => $"{v.Percent} %",
        };
        AutomationName(VolumeMute, volume is { Muted: true } ? "Lyd av" : $"Volum {VolumeMute.Content}");
        VolumeDown.IsEnabled = VolumeUp.IsEnabled = VolumeMute.IsEnabled = volume is not null;
    }

    private static void ShowRadio(Button button, string name, bool? on)
    {
        button.IsEnabled = on is not null;
        button.Tag = on;
        button.Content = on switch
        {
            null => $"{name}: –",
            true => $"{name}: på",
            false => $"{name}: av",
        };
        AutomationName(button, (string)button.Content);
        if (on == true)
        {
            MarkSelected(button);
        }
        else
        {
            Unmark(button);
        }
    }

    private static void AutomationName(UIElement element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    private static void Unmark(Button button)
    {
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            button.Resources.Remove("ButtonBackground" + state);
            button.Resources.Remove("ButtonForeground" + state);
            button.Resources.Remove("ButtonBorderBrush" + state);
        }

        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.ForegroundProperty);
    }

    private async void BrightnessStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string step } || _brightness is not { } current)
        {
            return;
        }

        // Trinn på 10, men alltid til et rundt tall (26 → 30 eller 20), og aldri helt svart.
        var delta = int.Parse(step, System.Globalization.CultureInfo.InvariantCulture);
        var target = delta > 0
            ? ((current / 10) + 1) * 10
            : current % 10 == 0 ? current - 10 : current / 10 * 10;
        target = Math.Clamp(target, 5, 100);
        _brightness = target;
        ShowBrightness();
        var ok = await Brightness.SetAsync(target);
        _log.Write("ytelse", ok ? $"Lysstyrke satt til {target} %" : $"Lysstyrke {target} %: Windows avslo");
        if (!ok)
        {
            _brightness = await Brightness.GetAsync();
            ShowBrightness();
        }
    }

    private void VolumeStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string step } || Volume.Get() is not { } current)
        {
            return;
        }

        var delta = int.Parse(step, System.Globalization.CultureInfo.InvariantCulture);
        var target = Math.Clamp((int)Math.Round((current.Percent + delta) / 10.0) * 10, 0, 100);
        var ok = Volume.SetPercent(target);
        _log.Write("ytelse", ok ? $"Volum satt til {target} %" : $"Volum {target} %: Windows avslo");
        ShowVolume(Volume.Get());
    }

    private void VolumeMute_Click(object sender, RoutedEventArgs e)
    {
        if (Volume.Get() is not { } current)
        {
            return;
        }

        var ok = Volume.SetMuted(!current.Muted);
        _log.Write("ytelse", ok ? (current.Muted ? "Lyd på" : "Lyd av") : "Lyd av/på: Windows avslo");
        ShowVolume(Volume.Get());
    }

    private async void WifiToggle_Click(object sender, RoutedEventArgs e)
    {
        if (WifiToggle.Tag is not bool on)
        {
            return;
        }

        // Strømmen fra gaming-PC-en går over Wi-Fi: å slå det av ville kuttet spillet.
        if (on && StreamDebugPanel.Visibility == Visibility.Visible)
        {
            PerformanceStatus.Text = "Wi-Fi kan ikke slås av mens du strømmer fra PC-en.";
            PerformanceStatus.Visibility = Visibility.Visible;
            return;
        }

        await ToggleRadioAsync(WifiToggle, "Wi-Fi", RadioKind.WiFi, !on);
    }

    private async void BluetoothToggle_Click(object sender, RoutedEventArgs e)
    {
        if (BluetoothToggle.Tag is bool on)
        {
            await ToggleRadioAsync(BluetoothToggle, "Bluetooth", RadioKind.Bluetooth, !on);
        }
    }

    private async Task ToggleRadioAsync(Button button, string name, RadioKind kind, bool on)
    {
        button.IsEnabled = false;
        var ok = await Radios.SetAsync(kind, on);
        _log.Write("ytelse", ok ? $"{name} slått {(on ? "på" : "av")}" : $"{name} {(on ? "på" : "av")}: Windows avslo");
        if (!ok)
        {
            PerformanceStatus.Text = $"Fikk ikke slått {name} {(on ? "på" : "av")}.";
            PerformanceStatus.Visibility = Visibility.Visible;
        }

        var (wifi, bluetooth) = await Radios.GetAsync();
        ShowRadio(button, name, kind == RadioKind.WiFi ? wifi : bluetooth);
        button.Focus(FocusState.Keyboard);
    }
}
