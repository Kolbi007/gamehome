// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;

namespace GameHome.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        StatusText.Text = $"Skjelett for fase 0A · versjon {typeof(MainWindow).Assembly.GetName().Version}";
        FocusProbe.Focus(FocusState.Programmatic);
    }
}
