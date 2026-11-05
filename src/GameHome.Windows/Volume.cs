// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Windows;

/// <summary>
/// Volum og lyd av/på for standard lydutgang, med Windows' Core Audio (<c>IMMDeviceEnumerator</c> og
/// <c>IAudioEndpointVolume</c>, dokumentert). Krever ikke administrator. Kaster aldri; null betyr at det ikke gikk.
/// </summary>
public static unsafe class Volume
{
    private static readonly Guid DeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid DeviceEnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid EndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>Volum 0–100 og om lyden er av, eller null.</summary>
    public static (int Percent, bool Muted)? Get()
    {
        return WithEndpoint(volume =>
        {
            float level;
            int muted;
            if (((delegate* unmanaged<nint, float*, int>)ComCall.Slot(volume, 9))(volume, &level) != ComCall.SOk
                || ((delegate* unmanaged<nint, int*, int>)ComCall.Slot(volume, 15))(volume, &muted) != ComCall.SOk)
            {
                return ((int, bool)?)null;
            }

            return ((int)Math.Round(level * 100), muted != 0);
        });
    }

    public static bool SetPercent(int percent)
    {
        var level = Math.Clamp(percent, 0, 100) / 100f;
        return WithEndpoint(volume =>
        {
            // Volum opp slår også lyden på igjen, som på kontrollerens volumknapper.
            if (level > 0)
            {
                ((delegate* unmanaged<nint, int, Guid*, int>)ComCall.Slot(volume, 14))(volume, 0, null);
            }

            return ((delegate* unmanaged<nint, float, Guid*, int>)ComCall.Slot(volume, 7))(volume, level, null) == ComCall.SOk;
        });
    }

    public static bool SetMuted(bool muted) =>
        WithEndpoint(volume => ((delegate* unmanaged<nint, int, Guid*, int>)ComCall.Slot(volume, 14))(volume, muted ? 1 : 0, null) == ComCall.SOk);

    private static T? WithEndpoint<T>(Func<nint, T> action)
    {
        nint enumerator = 0, device = 0, volume = 0;
        try
        {
            enumerator = ComCall.Create(DeviceEnumeratorClsid, DeviceEnumeratorIid);
            if (enumerator == 0)
            {
                return default;
            }

            // GetDefaultAudioEndpoint(eRender = 0, eMultimedia = 1).
            if (((delegate* unmanaged<nint, int, int, nint*, int>)ComCall.Slot(enumerator, 4))(enumerator, 0, 1, &device) != ComCall.SOk)
            {
                return default;
            }

            var iid = EndpointVolumeIid;
            // IMMDevice.Activate(iid, CLSCTX_ALL, null, out volume).
            if (((delegate* unmanaged<nint, Guid*, uint, nint, nint*, int>)ComCall.Slot(device, 3))(device, &iid, 0x17, 0, &volume) != ComCall.SOk)
            {
                return default;
            }

            return action(volume);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return default;
        }
        finally
        {
            ComCall.Release(volume);
            ComCall.Release(device);
            ComCall.Release(enumerator);
        }
    }
}
