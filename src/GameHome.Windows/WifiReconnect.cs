// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using Windows.Networking.Connectivity;

namespace GameHome.Windows;

/// <summary>
/// Kobler Wi-Fi fra og til igjen på det lagrede nettverket, når Legion Go ikke når ruteren (nettet henger selv om
/// Windows sier «tilkoblet»). Bruker Windows' egen <c>netsh wlan</c>, som ikke krever administrator. Bare når brukeren
/// trykker på knappen; aldri automatisk i en løkke.
/// </summary>
public static class WifiReconnect
{
    /// <summary>Navnet (SSID) på Wi-Fi-nettverket som er tilkoblet nå, eller null.</summary>
    public static string? CurrentNetwork()
    {
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (profile.IsWlanConnectionProfile
                    && profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None
                    && profile.WlanConnectionProfileDetails?.GetConnectedSsid() is { Length: > 0 } ssid)
                {
                    return ssid;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>Kobler fra, venter litt, og kobler til <paramref name="network"/> igjen. Sant hvis ruteren svarer etterpå.</summary>
    public static async Task<bool> ReconnectAsync(string network, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);
        await RunAsync("wlan disconnect", log).ConfigureAwait(false);
        await Task.Delay(2000).ConfigureAwait(false);
        await RunAsync($"wlan connect name=\"{network.Replace("\"", "", StringComparison.Ordinal)}\"", log).ConfigureAwait(false);

        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            if (await HostWaker.LocalNetworkOkAsync().ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task RunAsync(string arguments, Action<string>? log)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return;
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            log?.Invoke($"netsh {arguments.Split(' ')[1]}: {output.Trim()}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log?.Invoke($"netsh feilet: {ex.Message}");
        }
    }
}
