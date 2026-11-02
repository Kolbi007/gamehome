// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GameHome.Core.Network;

namespace GameHome.Windows;

/// <summary>Resultatet av et forsøk på å få hosten til å svare.</summary>
public sealed record WakeResult(bool Awake, bool WasAsleep, int PacketsSent, TimeSpan Elapsed);

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

    /// <summary>
    /// Svarer hosten ikke, sendes magiske pakker (på nytt hvert 10. sekund) til den svarer eller tiden er ute.
    /// <paramref name="onWaking"/> kalles én gang når vekking starter, så Home kan vise «Vekker …».
    /// </summary>
    public static async Task<WakeResult> EnsureAwakeAsync(MoonlightHost host, TimeSpan limit, Action? onWaking = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var watch = Stopwatch.StartNew();
        if (host.Address is null || await IsAwakeAsync(host.Address, ct).ConfigureAwait(false))
        {
            return new WakeResult(Awake: host.Address is not null, WasAsleep: false, 0, watch.Elapsed);
        }

        if (host.Mac is null)
        {
            return new WakeResult(false, true, 0, watch.Elapsed);
        }

        onWaking?.Invoke();
        var sent = 0;

        // Ingen pakke sendt ennå. (TimeSpan.MinValue her ga overflyt i 0.1.68.0–0.1.69.0, så vekkingen stoppet.)
        TimeSpan? lastSend = null;
        while (watch.Elapsed < limit && !ct.IsCancellationRequested)
        {
            if (lastSend is null || watch.Elapsed - lastSend.Value >= ResendEvery)
            {
                sent += Send(host.Mac, host.Address);
                lastSend = watch.Elapsed;
            }

            if (await IsAwakeAsync(host.Address, ct).ConfigureAwait(false))
            {
                return new WakeResult(true, true, sent, watch.Elapsed);
            }

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
