// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Diagnostics;
using GameHome.Windows;
using Microsoft.UI.Dispatching;

namespace GameHome.App;

/// <summary>
/// Vakthund for henging (2026-11-05: GameHome hang to ganger mens en trailer spilte, uten spor i loggen). En egen
/// lavprioritert tråd sender et lite «ping» til UI-tråden hvert andre sekund:
/// <list type="bullet">
/// <item>Svarer den ikke på 5 s, logges det én gang med de siste logglinjene og om GameHome bruker CPU (en løkke) eller
/// står stille (venter på noe).</item>
/// <item>Etter 15 s lagres et lite minnebilde (tråder og stakker) i LocalState, høyst ett per kjøring og to totalt.</item>
/// <item>Når UI-tråden svarer igjen, logges hvor lenge den hang.</item>
/// </list>
/// Den samme tråden logger hvilket program som tar forgrunnen, så det synes når noe annet (startmenyen, et vindu som
/// kjører som administrator) legger seg over GameHome. Kostnad: én liten jobb hvert andre sekund og en kort linje per
/// forgrunnsbytte.
/// </summary>
internal sealed class UiWatchdog
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReportAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DumpAfter = TimeSpan.FromSeconds(15);
    private const int KeptDumps = 2;

    private readonly DispatcherQueue _queue;
    private readonly FileLog _log;
    private readonly string _dumpFolder;
    private volatile bool _stop;
    private readonly string[] _recent = new string[6];
    private int _recentNext;
    private long _pingSent; // TickCount64 da et ubesvart ping ble sendt; 0 når alt er besvart.
    private bool _reported;
    private bool _dumped;
    private TimeSpan _cpuAtReport;

    public UiWatchdog(DispatcherQueue queue, FileLog log, string dumpFolder)
    {
        _queue = queue;
        _log = log;
        _dumpFolder = dumpFolder;
        _log.Written += (_, line) =>
        {
            lock (_recent)
            {
                _recent[_recentNext] = line;
                _recentNext = (_recentNext + 1) % _recent.Length;
            }
        };
    }

    public void Start()
    {
        var thread = new Thread(() => HangDiagnostics.RunLoop(OnForeground, Tick, Interval, () => _stop))
        {
            IsBackground = true,
            Name = "GameHome vakthund",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
    }

    public void Stop() => _stop = true;

    private void OnForeground(string process) => _log.Write("forgrunn", process);

    private void Tick()
    {
        var sent = Interlocked.Read(ref _pingSent);
        if (sent == 0)
        {
            Interlocked.Exchange(ref _pingSent, Environment.TickCount64);
            if (!_queue.TryEnqueue(DispatcherQueuePriority.Normal, Pong))
            {
                Interlocked.Exchange(ref _pingSent, 0); // Køen er stengt (appen avslutter).
            }

            return;
        }

        var waited = TimeSpan.FromMilliseconds(Environment.TickCount64 - sent);
        if (!_reported && waited >= ReportAfter)
        {
            _reported = true;
            _cpuAtReport = CpuTime();
            string[] recent;
            lock (_recent)
            {
                recent = [.. _recent.Skip(_recentNext).Concat(_recent.Take(_recentNext)).Where(l => l is not null)];
            }

            _log.Write("heng", $"UI-tråden har ikke svart på {waited.TotalSeconds:0} s. Siste i loggen før:\n    "
                + string.Join("\n    ", recent));
        }
        else if (_reported && !_dumped && waited >= DumpAfter)
        {
            _dumped = true;
            var cpu = (CpuTime() - _cpuAtReport).TotalSeconds / (waited - ReportAfter).TotalSeconds * 100 / Environment.ProcessorCount;
            var path = Path.Combine(_dumpFolder, $"heng-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");
            PruneDumps();
            var ok = HangDiagnostics.WriteMiniDump(path);
            _log.Write("heng", $"Fortsatt ingen svar etter {waited.TotalSeconds:0} s; GameHome brukte {cpu:0} % CPU i mellomtiden "
                + (cpu > 100.0 / Environment.ProcessorCount / 2 ? "(henger i en løkke)" : "(venter på noe)")
                + (ok ? $". Minnebilde: {Path.GetFileName(path)}" : ". Minnebildet kunne ikke lagres"));
        }
        else if (_dumped && !_restarting && waited >= RestartAfter && !InGame)
        {
            // Ingen spill kjører: en omstart av GameHome er bedre enn en Home som ikke svarer. Under spill bare logges det;
            // spillet går uansett, og M2 virker igjen når UI-tråden svarer.
            _restarting = true;
            _log.Write("heng", $"Ingen svar etter {waited.TotalSeconds:0} s og ingen spill kjører; GameHome startes på nytt");
            try
            {
                var reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart("--etter-heng");
                _log.Write("heng", $"Omstart avslått av Windows: {reason}");
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                _log.Write("heng", $"Omstart feilet: {ex.Message}");
            }
        }
    }

    private static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(30);
    private bool _restarting;

    /// <summary>Settes av MainWindow mens et spill kjører; da startes GameHome aldri på nytt av vakthunden.</summary>
    public static volatile bool InGame;

    private void Pong()
    {
        var sent = Interlocked.Exchange(ref _pingSent, 0);
        if (_reported && sent != 0)
        {
            _log.Write("heng", $"UI-tråden svarer igjen etter {(Environment.TickCount64 - sent) / 1000.0:0.0} s");
            _reported = false;
        }
    }

    private static TimeSpan CpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }

    private void PruneDumps()
    {
        try
        {
            foreach (var old in new DirectoryInfo(_dumpFolder).GetFiles("heng-*.dmp")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptDumps - 1))
            {
                old.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
