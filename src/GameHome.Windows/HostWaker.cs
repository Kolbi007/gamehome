// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GameHome.Core.Network;

namespace GameHome.Windows;

/// <summary>Resultatet av et forsøk på å få hosten til å svare.</summary>
/// <param name="NoLocalNetwork">Legion Go når ikke engang ruteren; problemet er Wi-Fi her, ikke at hosten sover.</param>
public sealed record WakeResult(bool Awake, bool WasAsleep, int PacketsSent, TimeSpan Elapsed, bool NoLocalNetwork = false);

/// <summary>Status underveis i vekkingen, for live visning: pakker sendt, forsøk på å nå hosten og tid brukt.</summary>
public sealed record WakeProgress(int PacketsSent, int Probes, TimeSpan Elapsed);

/// <summary>
/// Sjekker om hosten svarer (TCP 47989, Vibepollos HTTP-port for Moonlight), og vekker den med Wake-on-LAN hvis ikke.
/// Bare før start; ingen nettverkstrafikk mens et spill kjører.
/// </summary>
public static class HostWaker
{
    private const int StreamPort = 47989;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ResendEvery = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Om hosten svarer. Har en hard tidsgrense: et tilkoblingsforsøk mot en maskin som sover, kan ellers bli hengende
    /// mye lenger enn avbruddet ber om (0.1.68.0 ble stående på «Vekker …» etter at maskinen var våken).
    /// </summary>
    public static async Task<bool> IsAwakeAsync(string address, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return false;
        }

        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var connect = socket.ConnectAsync(new IPEndPoint(ip, StreamPort));
            var finished = await Task.WhenAny(connect, Task.Delay(ProbeTimeout, ct)).ConfigureAwait(false);
            if (finished != connect)
            {
                return false;
            }

            await connect.ConfigureAwait(false);
            return socket.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            // Lukker også et forsøk som fortsatt pågår.
            socket.Dispose();
        }
    }

    /// <summary>Om Legion Go har nettverk: et aktivt kort med standard ruter, og ruteren svarer på ping innen 1 s.</summary>
    public static async Task<bool> LocalNetworkOkAsync()
    {
        var gateways = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
            .Distinct()
            .ToList();
        foreach (var gateway in gateways)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(gateway, 1000).ConfigureAwait(false);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return true;
                }
            }
            catch (System.Net.NetworkInformation.PingException)
            {
            }

            // Noen rutere svarer ikke på ping; prøv også nettsiden og DNS på ruteren.
            if (await TcpReachableAsync(gateway, 80).ConfigureAwait(false) || await TcpReachableAsync(gateway, 53).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TcpReachableAsync(IPAddress ip, int port)
    {
        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var connect = socket.ConnectAsync(new IPEndPoint(ip, port));
            return await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false) == connect
                && socket.Connected;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            socket.Dispose();
        }
    }

    /// <summary>
    /// Svarer hosten ikke, sendes magiske pakker (på nytt hvert 10. sekund) til den svarer eller tiden er ute.
    /// <paramref name="onWaking"/> kalles én gang når vekking starter, så Home kan vise «Vekker …».
    /// </summary>
    public static async Task<WakeResult> EnsureAwakeAsync(MoonlightHost host, TimeSpan limit, Action? onWaking = null,
        Action<WakeProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var watch = Stopwatch.StartNew();
        if (host.Address is null || await IsAwakeAsync(host.Address, ct).ConfigureAwait(false))
        {
            return new WakeResult(Awake: host.Address is not null, WasAsleep: false, 0, watch.Elapsed);
        }

        // Svarer ikke engang ruteren, er det Legion Gos eget nettverk som er nede (målt 2026-11-02: Wi-Fi «No Internet»).
        // Da hjelper det ikke å vekke hosten i 90 s.
        if (!await LocalNetworkOkAsync().ConfigureAwait(false))
        {
            return new WakeResult(false, false, 0, watch.Elapsed, NoLocalNetwork: true);
        }

        if (host.Mac is null)
        {
            return new WakeResult(false, true, 0, watch.Elapsed);
        }

        onWaking?.Invoke();
        var sent = 0;

        // Ingen pakke sendt ennå. (TimeSpan.MinValue her ga overflyt i 0.1.68.0–0.1.69.0, så vekkingen stoppet.)
        TimeSpan? lastSend = null;
        var probes = 1;
        while (watch.Elapsed < limit && !ct.IsCancellationRequested)
        {
            if (lastSend is null || watch.Elapsed - lastSend.Value >= ResendEvery)
            {
                sent += Send(host.Mac, host.Address);
                lastSend = watch.Elapsed;
            }

            probes++;
            if (await IsAwakeAsync(host.Address, ct).ConfigureAwait(false))
            {
                return new WakeResult(true, true, sent, watch.Elapsed);
            }

            progress?.Invoke(new WakeProgress(sent, probes, watch.Elapsed));

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        return new WakeResult(false, true, sent, watch.Elapsed);
    }

    /// <summary>Sender den magiske pakken til nettets kringkastingsadresse og til 255.255.255.255, port 9 og 7.</summary>
    private static int Send(byte[] mac, string address)
    {
        var packet = WakeOnLan.BuildMagicPacket(mac);
        var targets = new List<IPAddress> { IPAddress.Broadcast };
        if (WakeOnLan.SubnetBroadcast(address) is { } subnet && IPAddress.TryParse(subnet, out var subnetAddress))
        {
            targets.Add(subnetAddress);
        }

        using var udp = new UdpClient { EnableBroadcast = true };
        var sent = 0;
        foreach (var target in targets)
        {
            foreach (var port in new[] { 9, 7 })
            {
                try
                {
                    udp.Send(packet, packet.Length, new IPEndPoint(target, port));
                    sent++;
                }
                catch (SocketException)
                {
                    // Et nett som ikke tillater kringkasting, skal ikke stoppe de andre forsøkene.
                }
            }
        }

        return sent;
    }
}
