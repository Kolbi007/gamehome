// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;

namespace GameHome.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Fase 0A: single-instance (AppInstance) og videresending av Home- og Library-aktivering.
        _window = new MainWindow();
        _window.Activate();
    }
}
