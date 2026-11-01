// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameHome.Windows;

/// <summary>
/// Diagnostikk: leser rå HID-rapporter fra et grensesnitt (for eksempel Legion Go-kontrollerens egne kanaler).
/// Bruker Windows' dokumenterte API-er (CM_Get_Device_Interface_List, CreateFile, ReadFile). Lesingen blokkerer en
/// bakgrunnstråd og bruker ingen CPU før en rapport kommer. Brukes bare i korte, eksplisitte målinger.
/// </summary>
public sealed class HidReportListener : IDisposable
{
    private static readonly Guid HidInterfaceClass = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private readonly SafeFileHandle _handle;
    private readonly Thread _thread;
    private volatile bool _stopping;

    private HidReportListener(string path, SafeFileHandle handle)
    {
        Path = path;
        _handle = handle;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "HID " + path };
    }

    public string Path { get; }

    /// <summary>Hver rapport som hex. Kommer fra bakgrunnstråden.</summary>
    public event EventHandler<string>? ReportReceived;

    /// <summary>Feil under lesing, for eksempel at enheten ble koblet fra.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Alle HID-grensesnitt som finnes nå, med enhets-id som inneholder <paramref name="filter"/>.</summary>
    public static unsafe IReadOnlyList<string> FindInterfaces(string filter)
    {
        var guid = HidInterfaceClass;
        const uint Present = 0;
        if (NativeMethods.CM_Get_Device_Interface_List_SizeW(out var length, ref guid, 0, Present) != 0 || length == 0)
        {
            return [];
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (NativeMethods.CM_Get_Device_Interface_ListW(ref guid, 0, p, length, Present) != 0)
            {
                return [];
            }
        }

        return new string(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Åpner grensesnittet for lesing. Returnerer null og en feiltekst hvis det ikke går.</summary>
    public static HidReportListener? TryOpen(string path, out string? error)
    {
        const uint GenericRead = 0x80000000, ShareReadWrite = 0x3, OpenExisting = 3;
        var handle = NativeMethods.CreateFileW(path, GenericRead, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"Kan ikke åpne (feil {Marshal.GetLastPInvokeError()})");
            handle.Dispose();
            return null;
        }

        error = null;
        return new HidReportListener(path, handle);
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stopping = true;
        _handle.Dispose();
    }

    private void ReadLoop()
    {
        var buffer = new byte[512];
        try
        {
            using var stream = new FileStream(_handle, FileAccess.Read, 0, isAsync: false);
            while (!_stopping)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                ReportReceived?.Invoke(this, Convert.ToHexString(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            if (!_stopping)
            {
                Failed?.Invoke(this, ex.Message);
            }
        }
    }
}
