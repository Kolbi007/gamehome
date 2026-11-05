// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace GameHome.App;

/// <summary>
/// Overgangen tilbake til Home etter et spill. Skjermbilder hvert ~50 ms etter at Broforce ble lukket (2026-11-05) viste:
/// Home i spillets oppløsning med feil spill i hovedfeltet, svart skjermbytte, vinduet i den gamle størrelsen (en fjerdedel
/// av skjermen, skrivebordet rundt), tomme fliser og til slutt hovedfeltet som tonet inn på nytt. Nå ligger et rolig dekke
/// med spillets logo over Home så lenge spillet kjører; når spillet slutter, settes skjermen tilbake, vinduet får riktig
/// størrelse med en gang, hovedfeltet tegnes under dekket, og dekket tones bort.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan RevealMaxWait = TimeSpan.FromMilliseconds(1500);
    private int _revealGeneration;
    private string? _returnCoverGameId;

    /// <summary>Kalles når spillet har tatt over: dekket legges over Home (billig: én farge og en liten logo).</summary>
    private void ShowReturnCover(Game game)
    {
        if (ReturnCover.Visibility == Visibility.Visible && _returnCoverGameId == game.Id)
        {
            return;
        }

        _returnCoverGameId = game.Id;
        _revealGeneration++;
        ReturnLogo.Source = null;
        var logo = ArtFor(game).Logo;
        ReturnTitle.Text = logo is null ? GameLibrary.DisplayTitle(game) : "";
        if (logo is not null)
        {
            _ = LoadTileImageAsync(ReturnLogo, logo, Px(420));
        }

        ReturnCover.Opacity = 1;
        ReturnCover.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Etter spillet: vinduet får skjermens størrelse med en gang (i stedet for å vente på Windows), hovedfeltet tegnes på
    /// nytt under dekket, og dekket tones bort når vinduet har riktig størrelse (høyst 1,5 s).
    /// </summary>
    private async void RevealHomeAfterGame(bool displayChanged)
    {
        if (ReturnCover.Visibility != Visibility.Visible)
        {
            return;
        }

        var generation = ++_revealGeneration;
        var started = DateTime.UtcNow;
        if (displayChanged)
        {
            FitWindowToDisplay();
            while (DateTime.UtcNow - started < RevealMaxWait && !WindowFitsDisplay())
            {
                await Task.Delay(50);
            }

            await Task.Delay(150); // Ett par bilder til, så flisene rekker å tegnes.
        }

        if (generation != _revealGeneration)
        {
            return; // Et nytt spill startet i mellomtiden.
        }

        RefreshHero(force: true);
        await Task.Delay(120);
        FadeTo(ReturnCover, 0, 250);
        await Task.Delay(260);
        if (generation == _revealGeneration)
        {
            ReturnCover.Visibility = Visibility.Collapsed;
            ReturnLogo.Source = null;
            _returnCoverGameId = null;
        }

        _log.Write("overgang", $"Home vist etter spillet på {(DateTime.UtcNow - started).TotalMilliseconds:0} ms"
            + (displayChanged ? (WindowFitsDisplay() ? " (vinduet hadde riktig størrelse)" : " (vinduet hadde ennå ikke riktig størrelse)") : ""));
    }

    private void FitWindowToDisplay()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            AppWindow.MoveAndResize(area.OuterBounds);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _log.Write("overgang", $"Vinduet kunne ikke tilpasses skjermen: {ex.Message}");
        }
    }

    private bool WindowFitsDisplay()
    {
        try
        {
            var bounds = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).OuterBounds;
            var size = AppWindow.Size;
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            return size.Width == bounds.Width && size.Height == bounds.Height
                && Math.Abs((Root.ActualWidth * scale) - bounds.Width) < 2;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return true;
        }
    }
}
