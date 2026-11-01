// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace GameHome.App;

/// <summary>
/// Single-instance: én GameHome kjører. En ny aktivering (for eksempel <c>gamehome://session</c>) sendes
/// videre til den som kjører, som da henter seg frem.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var main = AppInstance.FindOrRegisterForKey("main");
        if (!main.IsCurrent)
        {
            // Denne prosessen ble startet av brukeren og har lov til å ta forgrunnen. Gi retten videre.
            Foreground.AllowFor(main.ProcessId);
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => main.RedirectActivationToAsync(args).AsTask()).Wait();
            return 0;
        }

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
