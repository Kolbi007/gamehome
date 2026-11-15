// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace GameHome.App;

public sealed partial class MainWindow
{
    private enum SettingsCategory { Quick, Appearance, Accounts, System, Power }

    private const string SlideshowSetting = "home.slideshow";
    private const string TrailersSetting = "home.trailers";
    private bool _slideshowEnabled;
    private bool _trailersEnabled;
    private Button? _settingsOpener;
    private bool _settingsFromGame;
    private bool _debugFromPowerPanel;
    private bool _settingsCompact;
    private bool _settingsShowingCategory;
    private SettingsCategory _settingsCategory;

    private void LoadSettings()
    {
        var settings = ApplicationData.Current.LocalSettings.Values;
        _slideshowEnabled = settings[SlideshowSetting] is not false;
        _trailersEnabled = settings[TrailersSetting] is not false;
        RefreshMediaSettings();
        SetHints(SettingsHints, ("A", "Velg"), ("B", "Tilbake"));
    }

    private void RefreshMediaSettings()
    {
        SettingsSlideshowButton.Content = _slideshowEnabled ? "Lysbildefremvisning: på" : "Lysbildefremvisning: av";
        SettingsTrailersButton.Content = _trailersEnabled ? "Trailere: på" : "Trailere: av";
    }

    private void SettingsSlideshow_Click(object sender, RoutedEventArgs e)
    {
        _slideshowEnabled = !_slideshowEnabled;
        ApplicationData.Current.LocalSettings.Values[SlideshowSetting] = _slideshowEnabled;
        RefreshMediaSettings();
    }

    private void SettingsTrailers_Click(object sender, RoutedEventArgs e)
    {
        _trailersEnabled = !_trailersEnabled;
        ApplicationData.Current.LocalSettings.Values[TrailersSetting] = _trailersEnabled;
        StopTrailer();
        RefreshMediaSettings();
    }

    private void PowerMenuButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateClock();
        StopTrailer();
        _settingsOpener = sender as Button;
        _settingsFromGame = SessionActive;
        SessionPanel.Visibility = Visibility.Collapsed;
        _settingsShowingCategory = false;
        SettingsSurface.IsEnabled = true;
        SettingsAppearanceButton.IsEnabled = SettingsAccountsButton.IsEnabled = SettingsSystemButton.IsEnabled = !SessionActive;
        SettingsSessionHint.Visibility = SessionActive ? Visibility.Visible : Visibility.Collapsed;
        if (!SessionActive)
        {
            StreamDebugPanel.Visibility = Visibility.Collapsed;
        }

        var game = _settingsFromGame && _sessionGame is not null ? GameLibrary.DisplayTitle(_sessionGame) : null;
        PowerText.Text = game is null
            ? "Dvale setter enheten på pause. Omstart og avslåing lukker åpne programmer."
            : $"{game} kjører. Omstart og avslåing avslutter spillet, og ulagret fremgang kan gå tapt.";
        SettingsAccountStatus.Visibility = SettingsSystemStatus.Visibility = Visibility.Collapsed;
        RefreshUpdateChannelButton();
        UpdateButton.IsEnabled = !_updateDownloading;
        PowerPanel.Visibility = Visibility.Visible;
        SetHomeEnabled(false);
        SelectSettingsCategory(SettingsCategory.Quick);
        PowerCancelButton.Focus(FocusState.Keyboard);
        _log.Write("innstillinger", $"Innstillinger og strøm åpnet via {_lastSource}");
    }

    private void SettingsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 720;
        if (compact == _settingsCompact)
        {
            return;
        }

        _settingsCompact = compact;
        SettingsNavColumn.Width = new GridLength(compact ? 1 : 2, GridUnitType.Star);
        SettingsDetailColumn.Width = compact ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        SettingsBody.ColumnSpacing = compact ? 0 : 24;
        Grid.SetColumn(SettingsDetail, compact ? 0 : 1);
        UpdateSettingsLayout();
        if (PowerPanel.Visibility == Visibility.Visible)
        {
            PowerCancelButton.Focus(FocusState.Keyboard);
        }
    }

    private void UpdateSettingsLayout()
    {
        SettingsNav.Visibility = !_settingsCompact || !_settingsShowingCategory ? Visibility.Visible : Visibility.Collapsed;
        SettingsDetail.Visibility = !_settingsCompact || _settingsShowingCategory ? Visibility.Visible : Visibility.Collapsed;
        SettingsBackButton.Visibility = _settingsCompact && _settingsShowingCategory ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button SelectedSettingsButton => _settingsCategory switch
    {
        SettingsCategory.Appearance => SettingsAppearanceButton,
        SettingsCategory.Accounts => SettingsAccountsButton,
        SettingsCategory.System => SettingsSystemButton,
        SettingsCategory.Power => SettingsPowerButton,
        _ => SettingsQuickButton,
    };

    private void SettingsCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<SettingsCategory>(tag, out var category))
        {
            _settingsShowingCategory = true;
            SelectSettingsCategory(category);
            if (FocusManager.FindFirstFocusableElement(SettingsDetail) is Control first)
            {
                first.Focus(FocusState.Keyboard);
            }
        }
    }

    private void SelectSettingsCategory(SettingsCategory category)
    {
        _settingsCategory = category;
        var choice = SelectedSettingsButton;
        SettingsHeading.Text = (string)choice.Content;
        foreach (var button in SettingsNav.Children.OfType<Button>())
        {
            var selected = button == choice;
            if (selected)
            {
                button.BorderBrush = (Brush)Application.Current.Resources["GhTextSecondaryBrush"];
            }
            else
            {
                button.ClearValue(Control.BorderBrushProperty);
            }
            AutomationProperties.SetItemStatus(button, selected ? "Valgt" : "");
        }

        SettingsQuickHost.Visibility = category == SettingsCategory.Quick ? Visibility.Visible : Visibility.Collapsed;
        SettingsAppearance.Visibility = category == SettingsCategory.Appearance ? Visibility.Visible : Visibility.Collapsed;
        SettingsAccounts.Visibility = category == SettingsCategory.Accounts ? Visibility.Visible : Visibility.Collapsed;
        SettingsSystem.Visibility = category == SettingsCategory.System ? Visibility.Visible : Visibility.Collapsed;
        SettingsPower.Visibility = category == SettingsCategory.Power ? Visibility.Visible : Visibility.Collapsed;
        if (category == SettingsCategory.Quick && SettingsQuickHost.Child is null)
        {
            SessionPerformanceHost.Children.Remove(PerformancePanel);
            SettingsQuickHost.Child = PerformancePanel;
            BatteryText.Text = BatteryLine();
            PerformanceStatus.Visibility = Visibility.Collapsed;
            FillPerformance();
            FillQuickSettings();
        }
        else if (category == SettingsCategory.Accounts)
        {
            PopulateAccountRows(SettingsAccountRows);
        }
        else if (category == SettingsCategory.System)
        {
            SettingsVersionText.Text = InfoText.Text;
            RefreshSettingsTools();
        }

        UpdateSettingsLayout();
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void RefreshSettingsTools()
    {
        SettingsStartHcButton.Visibility = File.Exists(HandheldCompanionExe) && !HandheldCompanionRunning()
            ? Visibility.Visible : Visibility.Collapsed;
        SettingsRestoreScreenButton.Visibility = Vibepollo is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) => BackFromSettingsCategory();

    private void BackFromSettingsCategory()
    {
        _settingsShowingCategory = false;
        UpdateSettingsLayout();
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
        SelectedSettingsButton.Focus(FocusState.Keyboard);
    }

    private void BackFromSettings()
    {
        for (var element = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element == SettingsDetail)
            {
                BackFromSettingsCategory();
                return;
            }
        }

        if (_settingsCompact && _settingsShowingCategory)
        {
            BackFromSettingsCategory();
        }
        else
        {
            ClosePowerMenu(restoreOpener: true);
        }
    }

    private void PowerCancel_Click(object sender, RoutedEventArgs e) => ClosePowerMenu(restoreOpener: true);

    private void PowerOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag, Content: string label } button)
        {
            return;
        }

        var warning = tag == "exit" ? "Dette lukker bare GameHome." : PowerText.Text;
        SettingsSurface.IsEnabled = false;
        void Cancel()
        {
            SettingsSurface.IsEnabled = true;
            button.Focus(FocusState.Keyboard);
        }

        ShowMessage($"{label}?", warning, label, () => ExecutePowerOption(tag), "Avbryt", Cancel);
    }

    private void ExecutePowerOption(string tag)
    {
        if (tag == "exit")
        {
            _log.Write("strøm", "Avslutt GameHome valgt");
            StopEverything();
            Application.Current.Exit();
            return;
        }

        PowerAction? choice = tag switch
        {
            "sleep" => PowerAction.Sleep,
            "restart" => PowerAction.Restart,
            "shutdown" => PowerAction.Shutdown,
            _ => null,
        };
        if (choice is not { } action)
        {
            ClosePowerMenu(restoreOpener: true);
            return;
        }

        if (Power.Run(action) is { } error)
        {
            _log.Write("strøm", $"{action} feilet: {error}");
            void ReturnToPower()
            {
                SettingsSurface.IsEnabled = true;
                BackFromSettingsCategory();
            }
            ShowMessage("Det gikk ikke", error, "Lukk", ReturnToPower, "Tilbake", ReturnToPower);
            return;
        }

        _log.Write("strøm", $"{action} valgt");
        ClosePowerMenu(restoreOpener: true);
    }

    private void SettingsAddGame_Click(object sender, RoutedEventArgs e)
    {
        ClosePowerMenu(restoreOpener: false);
        AddGameTile_Click(sender, e);
    }

    private void PowerDebugButton_Click(object sender, RoutedEventArgs e)
    {
        _debugFromPowerPanel = true;
        PowerPanel.Visibility = Visibility.Collapsed;
        DebugTile_Click(sender, e);
    }

    private void ClosePowerMenu(bool restoreOpener)
    {
        SettingsSurface.IsEnabled = true;
        PowerPanel.Visibility = Visibility.Collapsed;
        if (SettingsQuickHost.Child is not null)
        {
            SettingsQuickHost.Child = null;
            SessionPerformanceHost.Children.Add(PerformancePanel);
        }

        SettingsAccountRows.Children.Clear();
        var opener = _settingsOpener;
        _settingsOpener = null;
        if (restoreOpener && opener == SessionPowerButton && SessionActive)
        {
            ShowSessionPanel("innstillinger lukket");
        }
        else if (restoreOpener && opener == MenuPowerButton)
        {
            SessionPanel.Visibility = Visibility.Visible;
            SetHomeEnabled(false);
        }
        else
        {
            SetHomeEnabled(true);
        }

        if (!restoreOpener || opener?.Focus(FocusState.Keyboard) != true)
        {
            FocusHome(_focusGameId);
        }
    }

    private void CloseSettingsIfGameEnded()
    {
        if (!_settingsFromGame || PowerPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (MessagePanel.Visibility == Visibility.Visible && !SettingsSurface.IsEnabled)
        {
            CloseMessage(null);
        }

        ClosePowerMenu(restoreOpener: false);
    }

    private static void ShowSettingsStatus(TextBlock target, string text)
    {
        target.Text = text;
        target.Visibility = Visibility.Visible;
    }
}
