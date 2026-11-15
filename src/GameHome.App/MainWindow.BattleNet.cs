// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.ComponentModel;
using System.Diagnostics;
using GameHome.Core.Model;
using GameHome.Windows;

namespace GameHome.App;

public sealed partial class MainWindow
{
    private void ConfirmBattleNetSignIn(Game game, PlayAction action, string? error = null)
    {
        ShowMessage("Logg inn i Battle.net",
            error ?? "Sjekk at riktig konto er logget inn og at spillet er klart i Battle.net. Gå tilbake til GameHome når du er klar.",
            "Jeg er logget inn", () => _ = LaunchAsync(game, action, battleNetConfirmed: true),
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
            ConfirmBattleNetSignIn(game, action);
        }
        catch (Win32Exception ex)
        {
            _log.Write("battlenet", $"Kunne ikke åpne Battle.net (Windows-feil {ex.NativeErrorCode})");
            ConfirmBattleNetSignIn(game, action, "Windows kunne ikke åpne Battle.net. Åpne appen fra Start-menyen og sjekk innloggingen.");
        }
    }
}
