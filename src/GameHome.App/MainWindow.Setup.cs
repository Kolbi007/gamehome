// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// Oppsett uten PC: «Kontoer og kilder» har en knapp per rad som retter det som mangler (start HC, åpne Moonlight eller
/// chiaki-ng, legg inn nøkler med skjermtastaturet). Første gang GameHome starter uten spill, vises den som en veiviser.
/// Nøkler går rett til Windows' sikre lager og vises aldri igjen (CLAUDE.md, personvern).
/// </summary>
public sealed partial class MainWindow
{
    private const string SetupDoneSetting = "setup.done";
    private string? _keyEntryKind;

    private static string? AccountActionLabel(AccountStatus account) => account.Key switch
    {
        "hc" when !account.Ok => "Start",
        "moonlight" when !account.Ok && MoonlightClient.FindExe() is not null => "Åpne Moonlight",
        "chiaki" when !account.Ok && Chiaki.FindExe() is not null => "Åpne chiaki-ng",
        "vibepollo" or "steam" or "steamgriddb" or "igdb" => account.Ok ? "Bytt nøkkel" : "Legg inn",
        _ => null,
    };

    private void AccountAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        switch (key)
        {
            case "hc":
                StartHandheldCompanion();
                break;
            case "moonlight" when MoonlightClient.FindExe() is { } moonlight:
                StartProgram(moonlight);
                break;
            case "chiaki" when Chiaki.FindExe() is { } chiaki:
                StartProgram(chiaki);
                break;
            default:
                ShowKeyEntry(key);
                break;
        }
    }

    private void StartProgram(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _log.Write("meny", $"Startet {Path.GetFileNameWithoutExtension(path)} fra «Kontoer og kilder»");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Write("meny", $"Kunne ikke starte {path}: {ex.Message}");
        }
    }

    private void ShowKeyEntry(string kind)
    {
        _keyEntryKind = kind;
        KeyEntryBox1.Password = "";
        KeyEntryBox2.Password = "";
        KeyEntryStatus.Text = "";
        var two = kind is "steam" or "igdb";
        KeyEntryLabel2.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        KeyEntryBox2.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        (KeyEntryTitle.Text, KeyEntryHelp.Text, KeyEntryLabel1.Text, KeyEntryLabel2.Text) = kind switch
        {
            "steamgriddb" => ("SteamGridDB-nøkkel", "Gratis: logg inn på steamgriddb.com og åpne Profil → Preferences → API.", "API-nøkkel", ""),
            "steam" => ("Steam-biblioteket", "Nøkkel fra steamcommunity.com/dev/apikey (skriv «localhost» som domene). Steam-ID er tallet i profillenken din.",
                "Steam Web API-nøkkel", "Steam-ID (64-bit tall eller profilnavn)"),
            "igdb" => ("IGDB (info og spilletid)", "Gratis: lag en app på dev.twitch.tv/console/apps (OAuth Redirect: http://localhost).",
                "Client ID", "Client Secret"),
            _ => ("Vibepollo-nøkkel", "Lag en API-nøkkel i Vibepollo på gaming-PC-en (Innstillinger → API-nøkler).", "API-nøkkel", ""),
        };
        KeyEntryPanel.Visibility = Visibility.Visible;
        KeyEntryBox1.Focus(FocusState.Programmatic);
        ShowTouchKeyboard();

        _log.Write("meny", $"Legg inn nøkkel: {kind}");
    }

    /// <summary>
    /// Viser skjermtastaturet også når kontrolleren brukes (Windows viser det ellers bare ved berøring). InputPane virket
    /// ikke fra GameHome (typefeil i 0.1.166.0), så TouchKeyboard brukes. Kaster aldri: et unntak her ville krasjet appen.
    /// </summary>
    private void ShowTouchKeyboard()
    {
        if (!TouchKeyboard.Show())
        {
            _log.Write("meny", "Skjermtastaturet kunne ikke vises");
        }
    }

    private void KeyEntryCancel_Click(object sender, RoutedEventArgs e) => CloseKeyEntry();

    private void CloseKeyEntry()
    {
        KeyEntryPanel.Visibility = Visibility.Collapsed;
        _ = TouchKeyboard.HideAsync();
        KeyEntryBox1.Password = "";
        KeyEntryBox2.Password = "";
        if (AccountsPanel.Visibility == Visibility.Visible)
        {
            MenuAccountsButton_Click(this, new RoutedEventArgs()); // Oppdater statusen.
        }
    }

    private void KeyEntrySave_Click(object sender, RoutedEventArgs e)
    {
        var first = KeyEntryBox1.Password.Trim();
        var second = KeyEntryBox2.Password.Trim();
        if (first.Length == 0 || (KeyEntryBox2.Visibility == Visibility.Visible && second.Length == 0))
        {
            KeyEntryStatus.Text = "Fyll ut feltene først.";
            return;
        }

        var ok = _keyEntryKind switch
        {
            "steamgriddb" => Secrets.Store(Secrets.SteamGridDb, first, KeyLog),
            "steam" => Secrets.Store(Secrets.SteamWebApiKey, first, KeyLog) && Secrets.Store(Secrets.SteamId, second, KeyLog),
            "igdb" => Secrets.Store(Secrets.IgdbClientId, first, KeyLog) && Secrets.Store(Secrets.IgdbClientSecret, second, KeyLog),
            "vibepollo" => Secrets.Store("vibepollo", first, KeyLog, VibepolloVaultResource, "token"),
            _ => false,
        };
        if (!ok)
        {
            KeyEntryStatus.Text = "Nøkkelen kunne ikke lagres.";
            return;
        }

        // Klientene leses på nytt med de nye nøklene.
        _steamGridDbChecked = false;
        _steamGridDb = null;
        _igdbChecked = false;
        _igdb = null;
        _steamWebApiChecked = false;
        _steamWebApi = null;
        _ownedGames = null;
        if (_keyEntryKind == "vibepollo")
        {
            DisposeVibepollo();
            _vibepolloChecked = false;
        }

        ShowSteamLibraryTile();
        CloseKeyEntry();
        _ = FetchMissingArtAsync();
    }

    /// <summary>Første start uten spill: «Kontoer og kilder» vises som en velkomst med det som kan settes opp.</summary>
    private void ShowSetupIfFirstRun()
    {
        try
        {
            if (_library.Games.Count > 0 || ApplicationData.Current.LocalSettings.Values[SetupDoneSetting] is true)
            {
                return;
            }

            ApplicationData.Current.LocalSettings.Values[SetupDoneSetting] = true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return;
        }

        AccountsTitle.Text = "Velkommen til GameHome";
        AccountsIntro.Text = "Dette er det GameHome kan koble seg til. Trykk på knappene for å sette opp det du vil bruke; resten kan vente. Du finner dette igjen under M2 → Kontoer og kilder.";
        AccountsIntro.Visibility = Visibility.Visible;
        MenuAccountsButton_Click(this, new RoutedEventArgs());
        _log.Write("meny", "Første start: oppsett vist");
    }
}
