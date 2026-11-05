// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using GameHome.Windows;

namespace GameHome.App;

/// <summary>
/// Dvale på Legion Go (Modern Standby) merkes som at skjermen slås av og på. Etter oppvåkning: under strømming vises
/// menyen (Moonlight har ofte mistet forbindelsen, og brukeren velger å fortsette eller koble fra); i Home tegnes
/// hovedfeltet på nytt og Handheld Companion sjekkes. Lokale spill får være i fred.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan SleepThreshold = TimeSpan.FromSeconds(10);
    private DateTime? _displayOffAt;

    private void OnDisplayPowerChanged(object? sender, bool on)
    {
        if (!on)
        {
            _displayOffAt = DateTime.UtcNow;
            _log.Write("dvale", "Skjermen av");
            return;
        }

        var off = _displayOffAt is { } at ? DateTime.UtcNow - at : TimeSpan.Zero;
        _displayOffAt = null;
        _log.Write("dvale", string.Create(CultureInfo.InvariantCulture, $"Skjermen på igjen etter {off.TotalSeconds:F0} s"));
        if (off < SleepThreshold)
        {
            return;
        }

        if (SessionActive && _session is StreamSession)
        {
            Activate();
            Foreground.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
            ShowSessionPanel("oppvåkning fra dvale");
            SessionDetail.Text = "Våknet fra dvale. Fortsett strømmen, eller koble fra hvis bildet står stille.";
            return;
        }

        if (!SessionActive)
        {
            RefreshHeroAfterDisplayChange();
            CheckHandheldCompanion();
        }
    }
}
