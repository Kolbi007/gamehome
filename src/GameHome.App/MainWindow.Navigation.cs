// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GameHome.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<UIElement, Action> _overlayBack = [];

    private void ConfigureNavigationOverlays()
    {
        _overlayBack.Add(LibraryPanel, () => CloseLibrary());
        _overlayBack.Add(LoadingPanel, () => LoadingHomeButton_Click(LoadingHomeButton, new RoutedEventArgs()));
        _overlayBack.Add(SessionPanel, BackFromSessionPanel);
        _overlayBack.Add(PowerPanel, BackFromSettings);
        _overlayBack.Add(AccountsPanel, CloseAccounts);
        _overlayBack.Add(KeyEntryPanel, CloseKeyEntry);
        _overlayBack.Add(HostPanel, CloseHostPanel);
        _overlayBack.Add(DetailsPanel, () => CloseDetails());
        _overlayBack.Add(OptionsPanel, () => CloseOptions());
        _overlayBack.Add(ArtPanel, CloseArtPicker);
        _overlayBack.Add(NoticePanel, () => CloseNotices());
        _overlayBack.Add(FindPanel, CloseFind);
        _overlayBack.Add(DebugPanel, CloseDebug);
        _overlayBack.Add(MessagePanel, () => CloseMessage(_messageCancel));
    }

    private UIElement? ActiveNavigationOverlay()
    {
        // The interactive panels are siblings in Root. Later children render on top. Visual-only covers are excluded.
        for (var index = Root.Children.Count - 1; index >= 0; index--)
        {
            var child = Root.Children[index];
            if (child.Visibility == Visibility.Visible && _overlayBack.ContainsKey(child))
            {
                return child;
            }
        }

        return null;
    }

    private bool RestoreOverlayFocus(UIElement? overlay, XamlRoot root)
    {
        if (overlay is null || (FocusManager.GetFocusedElement(root) is Control { IsEnabled: true } control && IsWithinFocusScope(control, overlay)))
        {
            return false;
        }

        if (overlay == PowerPanel)
        {
            RestoreSettingsFocus();
        }
        else if (overlay == MessagePanel)
        {
            MessageSecondary.Focus(FocusState.Keyboard);
        }
        else if (FocusManager.FindFirstFocusableElement(overlay) is Control first)
        {
            first.Focus(FocusState.Keyboard);
        }

        // Consumes the first confirmation when focus was outside the active panel.
        return true;
    }

    private void GoBack()
    {
        if (ActiveNavigationOverlay() is { } overlay)
        {
            _overlayBack[overlay]();
        }
    }

    private void BackFromSessionPanel()
    {
        if (SessionActive)
        {
            ResumeButton_Click(ResumeButton, new RoutedEventArgs());
        }
        else
        {
            HideSessionPanel();
        }
    }

    private static bool IsWithinFocusScope(DependencyObject? element, DependencyObject scope)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element == scope)
            {
                return true;
            }
        }

        return false;
    }
}
