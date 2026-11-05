// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;
using Microsoft.UI.Xaml;

namespace GameHome.App;

/// <summary>
/// Hovedfeltet i Home skal aldri stå tomt. Har ingen flis fokus (ved oppstart, etter låst skjerm), vises et utvalgt spill:
/// det som ble spilt sist («Fortsett der du slapp»), eller et spill som ikke er spilt på en stund («Hva med …?»). Etter en
/// skjermendring tegnes bildene på nytt, siden Windows kan kaste dem (tomt hovedfelt etter testens skjermbytte 2026-11-05).
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan SuggestAfter = TimeSpan.FromDays(3);
    private string? _featuredLabel;

    /// <summary>Bakgrunnsbildet i Home for spillet: det store bildet hvis det finnes, ellers flisbildet.</summary>
    private void ShowAmbientFor(Game game)
    {
        var image = TileImageFor(game);
        var hero = ArtFor(game).Hero is { } heroFile && File.Exists(heroFile) ? heroFile : null;
        if ((hero ?? image) is not { } backdrop)
        {
            return;
        }

        if (!string.Equals(_ambientPath, backdrop, StringComparison.OrdinalIgnoreCase))
        {
            _ambientPath = backdrop;
            _ = LoadAmbientAsync(backdrop, sharp: hero is not null);
        }
    }

    /// <summary>
    /// Sist spilte, hvis det er under tre dager siden; ellers et tilfeldig spill som ikke er spilt på en stund, så Home
    /// foreslår noe nytt å ta opp igjen.
    /// </summary>
    private Game? FeaturedGame(out string label)
    {
        var games = HomeGames();
        label = "";
        if (games.Count == 0)
        {
            return null;
        }

        var last = games.MaxBy(LastPlayed);
        if (last is not null && LastPlayed(last) > DateTime.UtcNow - SuggestAfter)
        {
            label = "Fortsett der du slapp";
            return last;
        }

        var resting = _library.VisibleGames.Where(g => LastPlayed(g) < DateTime.UtcNow - SuggestAfter).ToList();
        if (resting.Count == 0)
        {
            return games[0];
        }

        label = "Hva med denne i dag?";
        return resting[Random.Shared.Next(resting.Count)];
    }

    /// <summary>Viser spillet hovedfeltet allerede viser, eller et utvalgt spill. <paramref name="force"/> laster bildene på nytt.</summary>
    private void RefreshHero(bool force)
    {
        if (LibraryPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        _featuredLabel = null;
        var game = _heroGameId is { } id ? _library.Find(id) : null;
        if (game is null)
        {
            game = FeaturedGame(out var label);
            _featuredLabel = label.Length > 0 ? label : null;
        }

        if (game is null)
        {
            return;
        }

        if (force)
        {
            _ambientPath = null;
            _heroLogoPath = null;
            _heroGameId = null;
        }

        UpdateHero(game);
        if (_featuredLabel is { } featured)
        {
            HeroFacts.Text = $"{featured} · {HeroFacts.Text}";
        }

        ShowAmbientFor(game);
    }

    /// <summary>Etter en skjermendring: vent til vinduet har fått ny størrelse, og tegn hovedfeltet på nytt.</summary>
    private void RefreshHeroAfterDisplayChange()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(800);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => RefreshHero(force: true);
        timer.Start();
    }
}
