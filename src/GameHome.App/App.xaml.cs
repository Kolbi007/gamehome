// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace GameHome.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Log = new FileLog(Path.Combine(ApplicationData.Current.LocalFolder.Path, "gamehome.log"));
    }

    internal FileLog Log { get; }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var current = AppInstance.GetCurrent();
        current.Activated += OnRedirectedActivation;

        Log.Write("start", $"Aktivering: {Describe(current.GetActivatedEventArgs())}");
        _window = new MainWindow(Log);
        _window.Activate();
    }

    // Fase 0A: Home- og Library-aktivering fra FSE logges her når adapteren er på plass.
    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        Log.Write("aktivering", $"Videresendt: {Describe(e)}");
        _window?.DispatcherQueue.TryEnqueue(() => _window.BringToFront());
    }

    private static string Describe(AppActivationArguments args) => args.Kind switch
    {
        ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs protocol => $"protokoll {protocol.Uri}",
        _ => args.Kind.ToString(),
    };
}
