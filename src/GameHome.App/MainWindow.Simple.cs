// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Windows;
using Microsoft.UI.Xaml;
using Windows.System.Power;

namespace GameHome.App;

/// <summary>
/// Kortere vei til spill: spillemåten huskes, batteriet vises i toppen, og lavt batteri senker Hz ved spillstart.
/// </summary>
public sealed partial class MainWindow
{
    private const int LowBatteryPercent = 20;

    /// <summary>Første valg av spillemåte huskes, så A starter direkte neste gang. Byttes under ☰ Valg.</summary>
    private void RememberPlayMode(Game game, PlayAction action)
    {
        if (_library.Find(game.Id) is not { } current || current.Overrides.DefaultPlayActionId == action.Id)
        {
            return;
        }

        _library.SetOverrides(game.Id, current.Overrides with { DefaultPlayActionId = action.Id });
        SaveLibrary();
        _log.Write("valg", $"«{GameLibrary.DisplayTitle(game)}»: spillemåte husket ({(action.IsRemote ? "strøm" : "her")})");
    }

    private static bool OnBattery => PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;

    /// <summary>«82 %» og, på batteri, omtrent hvor lenge det varer («82 % · 2 t 10 min»).</summary>
    private static string BatteryShort()
    {
        var percent = PowerManager.RemainingChargePercent;
        if (!OnBattery)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{percent} % · lader");
        }

        var left = PowerManager.RemainingDischargeTime;
        if (left <= TimeSpan.Zero || left > TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{percent} %");
        }

        return left.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{percent} % · {(int)left.TotalHours} t {left.Minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{percent} % · {left.Minutes} min");
    }

    /// <summary>
    /// Lavt batteri og ingen lagret skjerm for spillet: 60 Hz i stedet for 144 under spillet (tilbake etterpå).
    /// </summary>
    private void SaveBatteryForGame(Game game)
    {
        if (!OnBattery || PowerManager.RemainingChargePercent > LowBatteryPercent || SavedDisplay(game) is not null
            || Display.Current() is not { } current || current.Hz <= 60)
        {
            return;
        }

        var target = Display.Modes().FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && m.Hz == 60);
        if (target == default || Display.Apply(target) is not null)
        {
            return;
        }

        _displayBeforeGame ??= current;
        RefreshHeroAfterDisplayChange();
        _log.Write("ytelse", string.Create(CultureInfo.InvariantCulture,
            $"Lavt batteri ({PowerManager.RemainingChargePercent} %): 60 Hz under «{GameLibrary.DisplayTitle(game)}»"));
    }

    private void MenuNoticesButton_Click(object sender, RoutedEventArgs e)
    {
        HideSessionPanel();
        NoticeButton_Click(sender, e);
    }
}
