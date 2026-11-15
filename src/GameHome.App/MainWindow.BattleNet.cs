// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.ComponentModel;
using System.Diagnostics;
using GameHome.Core.Model;
using GameHome.Windows;

namespace GameHome.App;

public sealed partial class MainWindow
{
    private void ShowBattleNetRecovery(Game game, PlayAction action, string? error = null)
    {
        ShowMessage("Sjekk Battle.net",
            error ?? "Fullfør eventuell innlogging eller oppdatering i Battle.net. Velg «Prøv igjen» når spillet er klart.",
            "Prøv igjen", () => _ = LaunchAsync(game, action),
            "Åpne Battle.net", () => OpenBattleNetForSignIn(game, action),
            onCancel: () => FocusHome(game.Id));
        MessageSecondary.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
    }

    private void OpenBattleNetForSignIn(Game game, PlayAction action)
    {
        if (BattleNetGames.LauncherPath() is not { } path)
        {
            ShowMessage("Finner ikke Battle.net", "Installer den offisielle Battle.net-appen og prøv igjen.",
                "Lukk", () => FocusHome(game.Id), "Tilbake", () => FocusHome(game.Id));
            return;
        }

        try
        {
            using var launcher = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ShowBattleNetRecovery(game, action);
        }
        catch (Win32Exception ex)
        {
            _log.Write("battlenet", $"Kunne ikke åpne Battle.net (Windows-feil {ex.NativeErrorCode})");
            ShowBattleNetRecovery(game, action, "Windows kunne ikke åpne Battle.net. Åpne appen fra Start-menyen og sjekk innloggingen.");
        }
    }
}
