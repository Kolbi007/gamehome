// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// Skjerm per spill: Hz og oppløsning brukeren velger i M2-menyen under et spill, huskes for spillet, settes ved neste
/// start og tilbakestilles når spillet avsluttes.
/// </summary>
public sealed partial class MainWindow
{
    private DisplayMode? _displayBeforeGame;

    private static string DisplayKey(Game game) => "display." + game.Id;

    private static DisplayMode? SavedDisplay(Game game)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[DisplayKey(game)] is string text
                && text.Split(',') is [var w, var h, var hz]
                && int.TryParse(w, CultureInfo.InvariantCulture, out var width)
                && int.TryParse(h, CultureInfo.InvariantCulture, out var height)
                && int.TryParse(hz, CultureInfo.InvariantCulture, out var rate))
            {
                return new DisplayMode(width, height, rate);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Ingen lagret skjerm.
        }

        return null;
    }

    private void SaveDisplay(Game game, DisplayMode mode)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[DisplayKey(game)] =
                string.Create(CultureInfo.InvariantCulture, $"{mode.Width},{mode.Height},{mode.Hz}");
            _log.Write("ytelse", string.Create(CultureInfo.InvariantCulture,
                $"Husket {mode.Width}×{mode.Height} {mode.Hz} Hz for «{GameLibrary.DisplayTitle(game)}»"));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("ytelse", $"Kunne ikke huske skjermen: {ex.Message}");
        }
    }


    /// <summary>Valget i spillets ☰-meny: viser den huskede skjermen og lar brukeren glemme den.</summary>
    private void ShowDisplayOption(Game game)
    {
        if (SavedDisplay(game) is { } mode)
        {
            OptionDisplay.Content = string.Create(CultureInfo.InvariantCulture,
                $"Glem skjerminnstilling ({mode.Width}×{mode.Height} {mode.Hz} Hz)");
            OptionDisplay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            OptionDisplay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private void OptionDisplay_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_optionsGame is not { } game)
        {
            return;
        }

        try
        {
            ApplicationData.Current.LocalSettings.Values.Remove(DisplayKey(game));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("ytelse", $"Kunne ikke glemme skjermen: {ex.Message}");
            return;
        }

        OptionDisplay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        OptionsStatus.Text = "Skjerminnstillingen er glemt; spillet starter med skjermen slik den står.";
        _log.Write("ytelse", $"Glemte skjerminnstilling for «{GameLibrary.DisplayTitle(game)}»");
    }

    /// <summary>
    /// Før start: spillets lagrede skjerm settes, og den som var, huskes til spillet er ferdig.
    /// <para>
    /// Lasteskjermen er svart mens skjermen byttes og vinduet får ny størrelse, og tones inn etterpå. Før ble skjermen byttet
    /// før lasteskjermen rakk å tegnes, og et halvt sekund viste den en forstørret fjerdedel av Home (skjermbilder
    /// 2026-11-05).
    /// </para>
    /// </summary>
    private async Task ApplyGameDisplayAsync(Game game)
    {
        if (SavedDisplay(game) is not { } wanted || Display.Current() is not { } current || wanted == current)
        {
            return;
        }

        var loading = LoadingPanel.Visibility == Visibility.Visible;
        if (loading)
        {
            LoadingIntro.Stop();
            LoadingHero.Opacity = 0;
            LoadingCenter.Opacity = 0;
            await Task.Delay(80); // La den svarte lasteskjermen bli tegnet før skjermen byttes.
        }

        var started = DateTime.UtcNow;
        var error = Display.Apply(wanted);
        var text = string.Create(CultureInfo.InvariantCulture, $"{wanted.Width}×{wanted.Height} {wanted.Hz} Hz");
        if (error is null)
        {
            _displayBeforeGame ??= current;
            FitWindowToDisplay();
            while (DateTime.UtcNow - started < RevealMaxWait && !WindowFitsDisplay())
            {
                await Task.Delay(50);
            }

            RefreshHeroAfterDisplayChange();
            _log.Write("ytelse", $"Skjerm for «{GameLibrary.DisplayTitle(game)}»: {text}"
                + string.Create(CultureInfo.InvariantCulture, $" (vinduet tilpasset på {(DateTime.UtcNow - started).TotalMilliseconds:0} ms)"));
        }
        else
        {
            _log.Write("ytelse", $"Skjerm for «{GameLibrary.DisplayTitle(game)}» ({text}) kunne ikke settes: {error}");
        }

        if (loading && LoadingPanel.Visibility == Visibility.Visible)
        {
            LoadingIntro.Begin();
        }
    }

    /// <summary>Etter spillet (eller en mislykket start): skjermen slik den var før spillet. True hvis skjermen ble endret.</summary>
    private bool RestoreDisplayAfterGame()
    {
        if (_displayBeforeGame is not { } before)
        {
            return false;
        }

        _displayBeforeGame = null;
        var error = Display.Apply(before);
        if (ReturnCover.Visibility != Visibility.Visible)
        {
            RefreshHeroAfterDisplayChange(); // Med dekket tegnes hovedfeltet på nytt når dekket fjernes (MainWindow.Return.cs).
        }

        _log.Write("ytelse", error is null
            ? string.Create(CultureInfo.InvariantCulture, $"Skjerm tilbake til {before.Width}×{before.Height} {before.Hz} Hz")
            : $"Skjermen kunne ikke settes tilbake: {error}");
        return error is null;
    }
}
