// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Globalization;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Metadata;
using GameHome.Sources.Steam;
using GameHome.Windows;

namespace GameHome.App;

/// <summary>
/// Spill du kan installere fra Steam (eide og familiedelte), lest lokalt fra Steam-klienten. Brukes i visningsmodus
/// («Ikke installert» med A for å installere) og i «Installer fra Steam».
/// </summary>
public sealed partial class MainWindow
{
    private IReadOnlyList<SteamLibraryApp>? _steamLocal;
    private bool _steamLocalLoading;
    private SteamLibraryApp? _attractSuggestion;

    /// <summary>Leser Steams lokale bibliotek én gang per kjøring, i bakgrunnen.</summary>
    private async void LoadSteamLocalLibrary()
    {
        if (_steamLocal is not null || _steamLocalLoading || SteamClient.FindRoot() is not { } root)
        {
            return;
        }

        _steamLocalLoading = true;
        var apps = await Task.Run(() => SteamLocalLibrary.Read(root));
        _steamLocal = apps;
        _steamLocalLoading = false;
        _log.Write("steam", $"Steam-biblioteket på maskinen: {apps.Count} spill (også familiedelte)");
    }

    private HashSet<string> InstalledSteamIds() =>
    [
        .. _library.Games
            .SelectMany(g => _library.PlayActionsFor(g.Id))
            .Where(a => a.RunnerId == GameLibrary.SteamRunnerId && a.Availability == Availability.Installed)
            .Select(a => a.Target),
    ];

    /// <summary>Steam-spill som ikke er installert. Eies de ikke av kontoen (ifølge Steam Web API), er de familiedelte.</summary>
    private List<SteamLibraryApp> NotInstalledSteamGames()
    {
        if (_steamLocal is null)
        {
            return [];
        }

        var installed = InstalledSteamIds();
        return [.. _steamLocal.Where(a => !installed.Contains(a.AppId.ToString(CultureInfo.InvariantCulture)))];
    }

    private bool IsFamilyShared(SteamLibraryApp app) =>
        _ownedGames is { } owned && owned.All(o => o.AppId != app.AppId);

    /// <summary>Visningsmodus: viser et Steam-spill som ikke er installert. A installerer det.</summary>
    private bool ShowAttractSuggestion()
    {
        var candidates = NotInstalledSteamGames().Where(a => a.Hero is not null).ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var app = candidates[Random.Shared.Next(candidates.Count)];
        _attractSuggestion = app;
        _heroGameId = null;
        _heroLogoPath = app.Logo;
        HeroLogo.Source = null;
        if (app.Logo is { } logo)
        {
            _ = LoadTileImageAsync(HeroLogo, logo, Px(520));
        }

        HeroTitle.Text = app.Logo is null ? app.Name : "";
        HeroFacts.Text = IsFamilyShared(app) ? "Ikke installert · familiedeling" : "Ikke installert · i Steam-biblioteket ditt";
        SetHints(HeroHints, ("A", "Installer"), ("B", "Tilbake"));
        if (app.Hero is { } hero && !string.Equals(_ambientPath, hero, StringComparison.OrdinalIgnoreCase))
        {
            _ambientPath = hero;
            _ = LoadAmbientAsync(hero, sharp: true);
        }

        return true;
    }

    /// <summary>A på et forslag i visningsmodus: Steam viser installasjonen, fremdriften kommer i Home.</summary>
    private void InstallAttractSuggestion()
    {
        if (_attractSuggestion is not { } app)
        {
            return;
        }

        _attractSuggestion = null;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo($"steam://install/{app.AppId.ToString(CultureInfo.InvariantCulture)}") { UseShellExecute = true });
            _log.Write("steam", $"Installasjon av «{app.Name}» (appid {app.AppId}) startet fra visningsmodus");
            LastInput.Text = $"Bekreft installasjonen av {app.Name} i Steam. Fremdriften vises i Home.";
            StartDownloadTimer();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Write("steam", $"Steam kunne ikke åpnes: {ex.Message}");
        }
    }

    /// <summary>For «Installer fra Steam»: familiedelte spill som ikke finnes i listen over eide spill.</summary>
    private IEnumerable<SteamOwnedGame> FamilyGamesForInstallList() =>
        NotInstalledSteamGames().Where(IsFamilyShared).Select(a => new SteamOwnedGame((int)a.AppId, $"{a.Name} · familiedeling", 0, null));
}
