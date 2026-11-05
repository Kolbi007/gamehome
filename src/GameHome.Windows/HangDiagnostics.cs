// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Hjelpere for vakthunden som oppdager at GameHome henger (MainWindow kaller dem fra en egen tråd):
/// minnebilde av egen prosess (<c>MiniDumpWriteDump</c>, dokumentert i dbghelp) og varsel når et annet program tar
/// forgrunnen (<c>SetWinEventHook</c> med EVENT_SYSTEM_FOREGROUND, dokumentert). Begge er billige: kroken kalles bare
/// når forgrunnen bytter, og minnebildet lages bare når noe henger.
/// </summary>
public static unsafe partial class HangDiagnostics
{
    private static Action<string>? s_onForeground;
    private static uint s_lastForegroundPid;

    /// <summary>
    /// Lite minnebilde (tråder og stakker, ingen heap) av GameHome til <paramref name="path"/>. Noen få MB. Kaster aldri.
    /// </summary>
    public static bool WriteMiniDump(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            const uint type = 0x00000000 /* Normal */ | 0x00000020 /* WithUnloadedModules */ | 0x00001000 /* WithThreadInfo */
                | 0x00000100 /* WithProcessThreadData */;
            return MiniDumpWriteDump(GetCurrentProcess(), (uint)Environment.ProcessId, file.SafeFileHandle.DangerousGetHandle(), type, 0, 0, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Kjører på den kallende tråden: tar imot forgrunnsvarsler (sendes til
    /// <paramref name="onForeground"/> med prosessnavnet) og kaller <paramref name="tick"/> omtrent hvert
    /// <paramref name="interval"/>, til <paramref name="stopRequested"/> gir true. Tråden må ikke gjøre noe annet.
    /// </summary>
    public static void RunLoop(Action<string> onForeground, Action tick, TimeSpan interval, Func<bool> stopRequested)
    {
        s_onForeground = onForeground;
        // EVENT_SYSTEM_FOREGROUND (3), utenfor prosessen (WINEVENT_OUTOFCONTEXT = 0): varselet kommer i denne trådens kø.
        var hook = SetWinEventHook(3, 3, 0, &OnWinEvent, 0, 0, 0);
        var milliseconds = (uint)interval.TotalMilliseconds;
        var next = Environment.TickCount64 + milliseconds;
        try
        {
            while (!stopRequested())
            {
                var wait = Math.Max(0, next - Environment.TickCount64);
                _ = MsgWaitForMultipleObjects(0, 0, 0, (uint)wait, 0x04FF /* QS_ALLINPUT */);
                Msg msg;
                while (PeekMessageW(&msg, 0, 0, 0, 1 /* PM_REMOVE */))
                {
                    _ = DispatchMessageW(&msg);
                }

                if (Environment.TickCount64 >= next)
                {
                    next = Environment.TickCount64 + milliseconds;
                    tick();
                }
            }
        }
        finally
        {
            if (hook != 0)
            {
                _ = UnhookWinEvent(hook);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            uint pid;
            _ = GetWindowThreadProcessId(window, &pid);
            if (pid == 0 || pid == s_lastForegroundPid)
            {
                return;
            }

            s_lastForegroundPid = pid;
            s_onForeground?.Invoke(ProcessName(pid));
        }
        catch (Exception)
        {
            // Et unntak hit (fra Windows inn i .NET) ville avsluttet prosessen; et tapt varsel er greit.
        }
    }

    /// <summary>Filnavnet til prosessen uten sti (stier kan inneholde brukernavn), eller «pid N».</summary>
    private static string ProcessName(uint pid)
    {
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, 0, pid);
        if (process == 0)
        {
            return $"pid {pid}";
        }

        try
        {
            var buffer = stackalloc char[520];
            var size = 520u;
            return QueryFullProcessImageNameW(process, 0, buffer, &size) != 0
                ? Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size))
                : $"pid {pid}";
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(nint process, uint processId, nint file, uint type, nint exceptionParam, nint userStreamParam, nint callbackParam);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    private static partial int QueryFullProcessImageNameW(nint process, uint flags, char* name, uint* size);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, uint* processId);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjects(uint count, nint handles, int waitAll, uint milliseconds, uint wakeMask);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(Msg* msg, nint window, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(Msg* msg);
}
