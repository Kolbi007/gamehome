// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GameHome.App;

/// <summary>
/// M2 virker alltid. Under spill viser menyen spillet; uten spill viser den GameHome-valg (kontoer og kilder, bilder,
/// legg til spill, feilsøking, strøm). Ytelse ligger i sidepanelet i begge tilfeller.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>M2 (og reservene): menyen for spillet som kjører, ellers GameHome-menyen.</summary>
    private void ShowMenu(string via)
    {
        if (SessionActive)
        {
            ShowSessionPanel(via);
            return;
        }

        CloseLibrary(focusHome: false);
        SessionGameCard.Visibility = Visibility.Collapsed;
        HomeMenuCard.Visibility = Visibility.Visible;
        StreamDebugPanel.Visibility = Visibility.Collapsed;
        SessionHero.Source = null;
        SessionLogo.Source = null;
        HomeMenuStatus.Visibility = Visibility.Collapsed;
        HomeMenuDetail.Text = $"{_library.VisibleGames.Count} spill · ingen spill kjører";
        var missing = AccountStatuses().Count(a => !a.Ok);
        MenuAccountsHint.Text = missing == 0 ? "Alt er koblet til" : $"{missing} mangler eller er av";
        BatteryText.Text = BatteryLine();
        FillPerformance();
        PerformanceStatus.Visibility = Visibility.Collapsed;
        SessionPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        (MenuUpdateButton.Visibility == Visibility.Visible ? MenuUpdateButton : MenuAccountsButton).Focus(FocusState.Keyboard);
        _ = CheckForUpdateAsync(manual: false, fromMenu: true);
        _log.Write("meny", $"GameHome-menyen vist via {via}");
    }

    private sealed record AccountStatus(string Name, bool Ok, string Detail);

    /// <summary>Status for alt GameHome henter fra eller styrer. Leses lokalt; ingen nettverkskall.</summary>
    private List<AccountStatus> AccountStatuses()
    {
        var hosts = MoonlightClient.FindHosts();
        var consoles = Chiaki.RegisteredConsoles();
        var retro = RetroArch.FindExe() is null ? 0 : Sources.Retro.RetroLibrary.Systems.Count(s => RetroArch.CorePath(s) is not null);
        var handheldCompanion = Process.GetProcessesByName("HandheldCompanion");
        var hcRunning = handheldCompanion.Length > 0;
        foreach (var p in handheldCompanion)
        {
            p.Dispose();
        }

        return
        [
            new("Handheld Companion", hcRunning, hcRunning ? "Kjører: knapper, TDP og QuickTools" : "Kjører ikke: M2, Legion-knappene og TDP virker ikke"),
            new("Gaming-PC (Moonlight)", MoonlightClient.FindExe() is not null && hosts.Count > 0,
                MoonlightClient.FindExe() is null ? "Moonlight er ikke installert" : hosts.Count == 0 ? "Ikke paret med en PC" : $"Paret med {HostLabel}"),
            new("Vibepollo", Vibepollo is not null, Vibepollo is null ? "Mangler nøkkel: ingen omslag, status eller avslutning på PC-en" : "Nøkkel finnes: omslag, status og avslutning"),
            new("PlayStation (chiaki-ng)", consoles.Count > 0,
                Chiaki.FindExe() is null ? "chiaki-ng er ikke installert" : consoles.Count == 0 ? "Installert, men ingen konsoll koblet til ennå" : string.Join(", ", consoles.Select(c => c.Title))),
            new("Steam-biblioteket", SteamWebApi is not null, SteamWebApi is null ? "Mangler Steam Web API-nøkkel eller Steam-ID" : "«Installer fra Steam» er på"),
            new("SteamGridDB (bilder)", SteamGridDb is not null, SteamGridDb is null ? "Mangler nøkkel" : "Bilder for spill som ikke er på Steam"),
            new("IGDB (info og spilletid)", Igdb is not null, Igdb is null ? "Mangler Twitch-nøkler" : "Info og spilletid"),
            new("Retro (RetroArch)", retro > 0, RetroArch.FindExe() is null ? "RetroArch er ikke installert" : $"{retro} systemer klare"),
        ];
    }

    private void MenuAccountsButton_Click(object sender, RoutedEventArgs e)
    {
        AccountRows.Children.Clear();
        foreach (var account in AccountStatuses())
        {
            var row = new Grid { ColumnSpacing = 14, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = account.Ok ? "" : "", // hake / advarsel
                FontSize = 18,
                Foreground = new SolidColorBrush(account.Ok ? Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x6C, 0xCB, 0x5F) : Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFC, 0xC2, 0x3D)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = account.Name, FontSize = 17, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
            text.Children.Add(new TextBlock { Text = account.Detail, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xA0, 0xA7, 0xB0)) });
            Grid.SetColumn(text, 1);
            row.Children.Add(mark);
            row.Children.Add(text);
            AccountRows.Children.Add(row);
        }

        AccountsPanel.Visibility = Visibility.Visible;
        AccountsCloseButton.Focus(FocusState.Keyboard);
        _log.Write("meny", "Kontoer og kilder vist");
    }

    private void AccountsCloseButton_Click(object sender, RoutedEventArgs e) => CloseAccounts();

    // Menyen lukkes først, så panelene åpnes over Home og fokus går tilbake dit når de lukkes.
    private void MenuAddGameButton_Click(object sender, RoutedEventArgs e)
    {
        HideSessionPanel();
        AddGameTile_Click(sender, e);
    }

    private void MenuDebugButton_Click(object sender, RoutedEventArgs e)
    {
        HideSessionPanel();
        DebugTile_Click(sender, e);
    }

    private void CloseAccounts()
    {
        AccountsPanel.Visibility = Visibility.Collapsed;
        AccountRows.Children.Clear();
        MenuAccountsButton.Focus(FocusState.Keyboard);
    }

    /// <summary>Prøver alle spill uten bilder (og hull i bildene) på nytt, også de som er prøvd før i denne kjøringen.</summary>
    private async void MenuArtButton_Click(object sender, RoutedEventArgs e)
    {
        _artTried.Clear();
        _artGapTried.Clear();
        HomeMenuStatus.Text = "Henter bilder …";
        HomeMenuStatus.Visibility = Visibility.Visible;
        await FetchMissingArtAsync();
        HomeMenuStatus.Text = "Ferdig. Det som ikke ble funnet, ligger i varslene øverst i Home.";
        _log.Write("meny", "Henting av manglende bilder startet fra menyen");
    }
}
