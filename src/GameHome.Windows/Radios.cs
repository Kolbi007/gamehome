// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Windows.Devices.Radios;

namespace GameHome.Windows;

/// <summary>
/// Wi-Fi og Bluetooth av/på med <c>Windows.Devices.Radios</c> (dokumentert; krever enhetsfunksjonen «radios» i
/// manifestet). Samme som bryterne i Windows' hurtiginnstillinger. Kaster aldri.
/// </summary>
public static class Radios
{
    /// <summary>På/av for Wi-Fi og Bluetooth; null når radioen ikke finnes eller ikke kan leses.</summary>
    public static async Task<(bool? WiFi, bool? Bluetooth)> GetAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            return (StateOf(radios, RadioKind.WiFi), StateOf(radios, RadioKind.Bluetooth));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    public static async Task<bool> SetAsync(RadioKind kind, bool on)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                return false;
            }

            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind == kind)
                {
                    return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return false;
    }

    private static bool? StateOf(IReadOnlyList<Radio> radios, RadioKind kind)
    {
        foreach (var radio in radios)
        {
            if (radio.Kind == kind)
            {
                return radio.State == RadioState.On;
            }
        }

        return null;
    }
}
