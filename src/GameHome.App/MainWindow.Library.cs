// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// Biblioteket: alle spillene i et rutenett, med faner per kilde (LB/RB) og sortering (X). Home viser bare «Fortsett».
/// </summary>
public sealed partial class MainWindow
{
    private sealed record LibraryTab(string Key, string Label, Func<IReadOnlyList<PlayAction>, bool> Includes);

    private enum LibrarySort { LastPlayed, MostPlayed, Title, Added }

    private const string LibrarySortSetting = "library.sort";
    private const string LibraryTabSetting = "library.tab";

    // Rekkefølgen fanene vises i. Fanene uten spill skjules.
    private static readonly LibraryTab[] LibraryTabDefinitions =
    [
        new("all", "Alle", _ => true),
        new("local", "På Legion Go", a => a.Any(x => !x.IsRemote && x.RunnerId is not (GameLibrary.MoonlightRunnerId or ChiakiRunner.Id))),
        new("stream", "Fra PC-en", a => a.Any(x => x.RunnerId == GameLibrary.MoonlightRunnerId)),
        new("playstation", "PlayStation", a => a.Any(x => x.RunnerId == ChiakiRunner.Id)),
        new("steam", "Steam", a => a.Any(x => x.RunnerId == GameLibrary.SteamRunnerId)),
        new("xbox", "Xbox", a => a.Any(x => x.RunnerId == XboxRunner.Id)),
        new("retro", "Retro", a => a.Any(x => x.RunnerId == RetroArchRunner.Id)),
        new("short", "Kort økt", _ => false), // Se IsShortSession.
        new("battlenet", "Battle.net", a => a.Any(x => x.RunnerId == BattleNetRunner.Id)),
        new("gog", "GOG", a => a.Any(x => x.RunnerId == GogRunner.Id)),
        new("epic", "Epic", a => a.Any(x => x.RunnerId == EpicRunner.Id)),
    ];


    // Sjangre der en økt typisk er kort (Steam-sjangre og IGDB-sjangre). Brukes til fanen «Kort økt».
    private static readonly string[] ShortSessionGenres =
        ["Casual", "Arcade", "Puzzle", "Card", "Card & Board Game", "Roguelike", "Roguelite", "Pinball", "Platform", "Sport", "Racing", "Fighting"];

    private bool InTab(LibraryTab tab, Game game, IReadOnlyList<PlayAction> actions) =>
        tab.Key == "short" ? IsShortSession(game, actions) : tab.Includes(actions);

    /// <summary>
    /// Passer for 15–30 minutter: retrospill, og spill der en sjanger tyder på korte økter. Blir mer presist med IGDBs
    /// spilletid når nøklene er lagt inn.
    /// </summary>
    private bool IsShortSession(Game game, IReadOnlyList<PlayAction> actions) =>
        actions.Any(a => a.RunnerId == RetroArchRunner.Id)
        || (_details.TryGetValue(game.Id, out var details)
            && details.Genres.Any(g => ShortSessionGenres.Any(s => g.Contains(s, StringComparison.OrdinalIgnoreCase))));
    private string _libraryTab = "all";
    private string _libraryQuery = "";
    private LibrarySort _librarySort = LibrarySort.LastPlayed;

    private static string SortLabel(LibrarySort sort) => sort switch
    {
        LibrarySort.MostPlayed => "Mest spilt",
        LibrarySort.Title => "A–Å",
        LibrarySort.Added => "Nylig lagt til",
        _ => "Sist spilt",
    };

    private void LibraryTile_Click(object sender, RoutedEventArgs e) => ShowLibrary();

    private void LibraryCloseButton_Click(object sender, RoutedEventArgs e) => CloseLibrary();

    private void LibrarySortButton_Click(object sender, RoutedEventArgs e) => NextLibrarySort();

    private void ShowLibrary()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            _libraryTab = settings[LibraryTabSetting] as string ?? "all";
            _librarySort = settings[LibrarySortSetting] is int sort && Enum.IsDefined(typeof(LibrarySort), sort) ? (LibrarySort)sort : LibrarySort.LastPlayed;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Standardvalgene brukes.
        }

        StopTrailer();
        _libraryQuery = "";
        LibrarySearchBox.Text = "";
        LibraryPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(true); // Home bak biblioteket blir utilgjengelig for fokus, biblioteket tilgjengelig.
        BuildLibrary();
        FocusLibrary(null);
        _log.Write("bibliotek", $"Biblioteket åpnet via {_lastSource}");
    }

    private void CloseLibrary(bool focusHome = true)
    {
        LibraryPanel.Visibility = Visibility.Collapsed;
        LibraryGrid.Children.Clear(); // Bildene trengs ikke når biblioteket er lukket.
        TouchKeyboard.Hide();
        LibrarySearchBox.IsTabStop = false;
        SetHomeEnabled(true);
        if (focusHome)
        {
            FocusHome(_focusGameId);
        }
    }

    private void BuildLibrary()
    {
        var games = _library.VisibleGames.Select((g, index) => (Game: g, Index: index, Actions: _library.PlayActionsFor(g.Id))).ToList();
        var tabs = LibraryTabDefinitions.Where(t => t.Key == "all" || games.Any(g => InTab(t, g.Game, g.Actions))).ToList();
        if (!tabs.Any(t => t.Key == _libraryTab))
        {
            _libraryTab = "all";
        }

        LibraryTabs.Children.Clear();
        foreach (var tab in tabs)
        {
            var count = games.Count(g => InTab(tab, g.Game, g.Actions));
            // Strømmede spill får navnet på PC-en, så det er tydelig at de kjører der og strømmes hit.
            var label = tab.Key == "stream" ? $"Strøm fra {HostLabel}" : tab.Label;
            var button = new Button
            {
                Content = $"{label} ({count})",
                Tag = tab.Key,
                Height = 44,
                Padding = new Thickness(16, 0, 16, 0),
                CornerRadius = new CornerRadius(22),
            };
            if (tab.Key == _libraryTab)
            {
                MarkSelected(button);
            }

            button.Click += (_, _) => SelectLibraryTab(tab.Key);
            LibraryTabs.Children.Add(button);
        }

        var current = tabs.First(t => t.Key == _libraryTab);
        var shown = games.Where(g => InTab(current, g.Game, g.Actions));
        shown = _librarySort switch
        {
            LibrarySort.MostPlayed => shown.OrderByDescending(g => MinutesPlayed(g.Game)).ThenBy(g => GameLibrary.DisplayTitle(g.Game), StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Title => shown.OrderBy(g => GameLibrary.DisplayTitle(g.Game), StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Added => shown.OrderByDescending(g => g.Index),
            _ => shown.OrderByDescending(g => LastPlayed(g.Game)).ThenBy(g => GameLibrary.DisplayTitle(g.Game), StringComparer.CurrentCultureIgnoreCase),
        };

        if (_libraryQuery.Length > 0)
        {
            shown = shown.Where(g => GameLibrary.DisplayTitle(g.Game).Contains(_libraryQuery, StringComparison.CurrentCultureIgnoreCase));
        }

        var shownList = shown.ToList();
        LibraryGrid.Children.Clear();
        foreach (var (game, _, _) in shownList)
        {
            LibraryGrid.Children.Add(CreateLibraryTile(game));
        }

        LibrarySortButton.Content = $"Sortering: {SortLabel(_librarySort)}";
        var tabName = current.Key == "stream" ? $"Strøm fra {HostLabel}" : current.Label;
        LibraryHeading.Text = _libraryQuery.Length > 0
            ? $"Bibliotek · «{_libraryQuery}» ({shownList.Count} treff)"
            : $"Bibliotek · {tabName}";
    }

    private Button CreateLibraryTile(Game game)
    {
        var tile = CreateTile(game);
        tile.Width = 236;
        tile.Height = 133;
        tile.Margin = new Thickness(0, 0, 16, 17);
        tile.Click -= GameTile_Click;
        tile.Click += (sender, e) =>
        {
            CloseLibrary(focusHome: false);
            GameTile_Click(sender, e);
        };
        return tile;
    }

    private Button? LibraryTileFor(string? gameId) =>
        LibraryGrid.Children.OfType<Button>().FirstOrDefault(b => b.Tag is string id && id == gameId);

    private void FocusLibrary(string? gameId)
    {
        Button? target = LibraryTileFor(gameId) ?? LibraryGrid.Children.OfType<Button>().FirstOrDefault() ?? LibraryCloseButton;
        if (target.IsLoaded)
        {
            target.Focus(FocusState.Keyboard);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= OnLoaded;
            if (LibraryPanel.Visibility == Visibility.Visible)
            {
                target.Focus(FocusState.Keyboard);
            }
        }

        target.Loaded += OnLoaded;
    }

    private void SelectLibraryTab(string key)
    {
        _libraryTab = key;
        SaveLibrarySetting(LibraryTabSetting, key);
        BuildLibrary();
        FocusLibrary(null);
        _log.Write("bibliotek", $"Fane: {key}");
    }

    /// <summary>LB/RB: forrige eller neste fane.</summary>
    private void MoveLibraryTab(int step)
    {
        var keys = LibraryTabs.Children.OfType<Button>().Select(b => (string)b.Tag).ToList();
        var at = keys.IndexOf(_libraryTab);
        if (keys.Count > 1)
        {
            SelectLibraryTab(keys[(at + step + keys.Count) % keys.Count]);
        }
    }

    private void NextLibrarySort()
    {
        _librarySort = (LibrarySort)(((int)_librarySort + 1) % Enum.GetValues<LibrarySort>().Length);
        SaveLibrarySetting(LibrarySortSetting, (int)_librarySort);
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) is Button { Tag: string id } ? id : null;
        BuildLibrary();
        FocusLibrary(focused);
        _log.Write("bibliotek", $"Sortering: {SortLabel(_librarySort)}");
    }

    private void SaveLibrarySetting(string key, object value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _log.Write("bibliotek", $"Kunne ikke lagre valget: {ex.Message}");
        }
    }


    // ---- Søk og bla ----

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _libraryQuery = LibrarySearchBox.Text.Trim();
        BuildLibrary();
    }

    private void LibrarySearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Enter (eller Ferdig på skjermtastaturet): til første treff.
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            TouchKeyboard.Hide();
            LibrarySearchBox.IsTabStop = false;
            FocusLibrary(null);
        }
    }

    /// <summary>View-knappen (⧉): søkefeltet får fokus og skjermtastaturet vises, også når kontrolleren brukes.</summary>
    private void OpenLibrarySearch()
    {
        LibrarySearchBox.IsTabStop = true; // Søkefeltet får fokus bare via View-knappen, ellers dukker tastaturet opp.
        LibrarySearchBox.Focus(FocusState.Programmatic);
        ShowTouchKeyboard();

        _log.Write("bibliotek", "Søk åpnet");
    }

    /// <summary>
    /// LT/RT: bla. Sortert A–Å hoppes det til neste (eller forrige) forbokstav; ellers to rader om gangen.
    /// </summary>
    private void PageLibrary(int step)
    {
        var tiles = LibraryGrid.Children.OfType<Button>().ToList();
        if (tiles.Count == 0)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as Button;
        var at = focused is null ? 0 : Math.Max(0, tiles.IndexOf(focused));
        int target;
        if (_librarySort == LibrarySort.Title)
        {
            static char Letter(Button b) =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) is { Length: > 0 } name ? char.ToUpperInvariant(name[0]) : ' ';
            var letter = Letter(tiles[at]);
            if (step > 0)
            {
                target = tiles.FindIndex(at, b => Letter(b) != letter);
                target = target < 0 ? tiles.Count - 1 : target;
            }
            else
            {
                // Til starten av denne bokstaven, eller av forrige hvis vi allerede står der.
                var start = at;
                while (start > 0 && Letter(tiles[start - 1]) == letter)
                {
                    start--;
                }

                target = start;
                if (start == at && start > 0)
                {
                    var previous = Letter(tiles[start - 1]);
                    target = start - 1;
                    while (target > 0 && Letter(tiles[target - 1]) == previous)
                    {
                        target--;
                    }
                }
            }
        }
        else
        {
            var columns = Math.Max(1, (int)(LibraryGrid.ActualWidth / LibraryGrid.ItemWidth));
            target = Math.Clamp(at + (step * columns * 2), 0, tiles.Count - 1);
        }

        tiles[target].Focus(FocusState.Keyboard);
    }

    // ---- Fest i Home ----

    private static string PinText(Game game) => LoadPinned().Contains(game.Id) ? "Løsne fra Home" : "Fest i Home";

    private void OptionPin_Click(object sender, RoutedEventArgs e)
    {
        if (_optionsGame is not { } game)
        {
            return;
        }

        var pinned = LoadPinned();
        var pin = !pinned.Remove(game.Id);
        if (pin)
        {
            pinned.Insert(0, game.Id);
        }

        SavePinned(pinned);
        OptionPin.Content = PinText(game);
        BuildGameTiles();
        MenuSounds.Move();
        _log.Write("home", $"«{GameLibrary.DisplayTitle(game)}» {(pin ? "festet i" : "løsnet fra")} Home");
    }
}
