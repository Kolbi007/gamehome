// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Lysstyrken på den innebygde skjermen via WMI-klassene <c>WmiMonitorBrightness</c> og
/// <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c> (dokumentert, root\WMI). Krever ikke administrator.
/// Kalles fra en bakgrunnstråd (WMI kan bruke et par hundre millisekunder). Kaster aldri; null/false betyr at det ikke gikk.
/// </summary>
public static class Brightness
{
    private static readonly Guid LocatorClsid = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorIid = new("DC12A687-737F-11CF-884D-00AA004B2E24");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Lysstyrken i prosent, eller null (ingen innebygd skjerm, eller WMI svarte ikke).</summary>
    public static Task<int?> GetAsync() => RunAsync(services =>
    {
        var instance = QueryFirst(services, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
        try
        {
            return instance != 0 && GetProperty(instance, "CurrentBrightness") is { Type: ComCall.Variant.VtUi1 } value
                ? value.Byte
                : (int?)null;
        }
        finally
        {
            ComCall.Release(instance);
        }
    });

    public static async Task<bool> SetAsync(int percent)
    {
        var value = (byte)Math.Clamp(percent, 0, 100);
        return await RunAsync(services => (int?)(SetBrightness(services, value) ? 1 : 0)) == 1;
    }

    private static unsafe bool SetBrightness(nint services, byte value)
    {
        nint instance = 0, methodClass = 0, inSignature = 0, outSignature = 0, inParams = 0, outParams = 0;
        try
        {
            instance = QueryFirst(services, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
            if (instance == 0 || GetProperty(instance, "__PATH") is not { Type: ComCall.Variant.VtBstr } pathValue)
            {
                return false;
            }

            var path = Marshal.PtrToStringBSTR(pathValue.Pointer);
            var copy = pathValue;
            ComCall.VariantClear(ref copy);

            var className = Marshal.StringToBSTR("WmiMonitorBrightnessMethods");
            try
            {
                // IWbemServices.GetObject(path, 0, null, out class, null).
                if (((delegate* unmanaged<nint, nint, int, nint, nint*, nint, int>)ComCall.Slot(services, 6))(services, className, 0, 0, &methodClass, 0) != ComCall.SOk)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeBSTR(className);
            }

            fixed (char* name = "WmiSetBrightness")
            {
                // IWbemClassObject.GetMethod(name, 0, out in, out out).
                if (((delegate* unmanaged<nint, char*, int, nint*, nint*, int>)ComCall.Slot(methodClass, 19))(methodClass, name, 0, &inSignature, &outSignature) != ComCall.SOk
                    || inSignature == 0)
                {
                    return false;
                }
            }

            // IWbemClassObject.SpawnInstance(0, out inParams).
            if (((delegate* unmanaged<nint, int, nint*, int>)ComCall.Slot(inSignature, 15))(inSignature, 0, &inParams) != ComCall.SOk)
            {
                return false;
            }

            if (!Put(inParams, "Timeout", new ComCall.Variant { Type = ComCall.Variant.VtI4, Int = 0 })
                || !Put(inParams, "Brightness", new ComCall.Variant { Type = ComCall.Variant.VtUi1, Byte = value }))
            {
                return false;
            }

            var objectPath = Marshal.StringToBSTR(path);
            var method = Marshal.StringToBSTR("WmiSetBrightness");
            try
            {
                // IWbemServices.ExecMethod(path, method, 0, null, inParams, out outParams, null).
                return ((delegate* unmanaged<nint, nint, nint, int, nint, nint, nint*, nint, int>)ComCall.Slot(services, 24))(
                    services, objectPath, method, 0, 0, inParams, &outParams, 0) == ComCall.SOk;
            }
            finally
            {
                Marshal.FreeBSTR(objectPath);
                Marshal.FreeBSTR(method);
            }
        }
        finally
        {
            ComCall.Release(outParams);
            ComCall.Release(inParams);
            ComCall.Release(outSignature);
            ComCall.Release(inSignature);
            ComCall.Release(methodClass);
            ComCall.Release(instance);
        }
    }

    private static async Task<int?> RunAsync(Func<nint, int?> action)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Connect(action)).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static unsafe int? Connect(Func<nint, int?> action)
    {
        {
            var initialized = ComCall.CoInitializeEx(0, 0x0 /* COINIT_MULTITHREADED */) >= 0;
            nint locator = 0, services = 0;
            try
            {
                locator = ComCall.Create(LocatorClsid, LocatorIid);
                if (locator == 0)
                {
                    return null;
                }

                var ns = Marshal.StringToBSTR(@"ROOT\WMI");
                try
                {
                    // IWbemLocator.ConnectServer(ns, null, null, null, 0, null, null, out services).
                    if (((delegate* unmanaged<nint, nint, nint, nint, nint, int, nint, nint, nint*, int>)ComCall.Slot(locator, 3))(
                            locator, ns, 0, 0, 0, 0, 0, 0, &services) != ComCall.SOk)
                    {
                        return null;
                    }
                }
                finally
                {
                    Marshal.FreeBSTR(ns);
                }

                // RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, EOAC_NONE.
                _ = ComCall.CoSetProxyBlanket(services, 10, 0, 0, 3, 3, 0, 0);
                return action(services);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                return null;
            }
            finally
            {
                ComCall.Release(services);
                ComCall.Release(locator);
                if (initialized)
                {
                    ComCall.CoUninitialize();
                }
            }
        }
    }

    private static unsafe nint QueryFirst(nint services, string query)
    {
        nint enumerator = 0;
        var language = Marshal.StringToBSTR("WQL");
        var text = Marshal.StringToBSTR(query);
        try
        {
            // IWbemServices.ExecQuery(WQL, query, FORWARD_ONLY | RETURN_IMMEDIATELY, null, out enumerator).
            if (((delegate* unmanaged<nint, nint, nint, int, nint, nint*, int>)ComCall.Slot(services, 20))(services, language, text, 0x30, 0, &enumerator) != ComCall.SOk)
            {
                return 0;
            }

            nint instance = 0;
            uint returned = 0;
            // IEnumWbemClassObject.Next(5 s, 1, out instance, out returned).
            var hr = ((delegate* unmanaged<nint, int, uint, nint*, uint*, int>)ComCall.Slot(enumerator, 4))(enumerator, 5000, 1, &instance, &returned);
            return hr == ComCall.SOk && returned == 1 ? instance : 0;
        }
        finally
        {
            Marshal.FreeBSTR(language);
            Marshal.FreeBSTR(text);
            ComCall.Release(enumerator);
        }
    }

    private static unsafe ComCall.Variant? GetProperty(nint instance, string name)
    {
        var value = default(ComCall.Variant);
        fixed (char* property = name)
        {
            // IWbemClassObject.Get(name, 0, out value, null, null).
            return ((delegate* unmanaged<nint, char*, int, ComCall.Variant*, int*, int*, int>)ComCall.Slot(instance, 4))(instance, property, 0, &value, null, null) == ComCall.SOk
                ? value
                : null;
        }
    }

    private static unsafe bool Put(nint instance, string name, ComCall.Variant value)
    {
        fixed (char* property = name)
        {
            // IWbemClassObject.Put(name, 0, value, 0).
            return ((delegate* unmanaged<nint, char*, int, ComCall.Variant*, int, int>)ComCall.Slot(instance, 5))(instance, property, 0, &value, 0) == ComCall.SOk;
        }
    }
}