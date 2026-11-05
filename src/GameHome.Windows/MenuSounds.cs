// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;

namespace GameHome.Windows;

/// <summary>
/// Korte, lave menylyder laget i koden (ingen lydfiler): et mykt tikk når fokus flytter seg, en varm tone når noe
/// velges, og en lavere tone tilbake. Spilles med <c>PlaySound</c> fra minnet, uten forsinkelse. Kan slås av.
/// </summary>
public static class MenuSounds
{
    private const int SampleRate = 44100;

    // Lydene ligger fast i minnet (festet), fordi PlaySound leser bufferen mens lyden spilles.
    private static readonly Sound MoveSound = Tone([(1760, 0.026)], 0.05);
    private static readonly Sound SelectSound = Tone([(660, 0.045), (990, 0.08)], 0.11);
    private static readonly Sound BackSound = Tone([(587, 0.04), (440, 0.06)], 0.08);

    public static bool Enabled { get; set; } = true;

    public static void Move() => Play(MoveSound);

    public static void Select() => Play(SelectSound);

    public static void Back() => Play(BackSound);

    private static void Play(Sound sound)
    {
        if (Enabled)
        {
            // SND_MEMORY | SND_ASYNC | SND_NODEFAULT. En ny lyd avbryter den forrige, som er riktig for menylyder.
            _ = SoundNative.PlaySoundW(sound.Address, 0, 0x0004 | 0x0001 | 0x0002);
        }
    }

    /// <summary>Toner etter hverandre (frekvens, sekunder) med myk start og rask, naturlig uttoning.</summary>
    private static Sound Tone((double Hz, double Seconds)[] notes, double volume)
    {
        var samples = new List<short>();
        foreach (var (hz, seconds) in notes)
        {
            var count = (int)(SampleRate * seconds);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                var attack = Math.Min(1, i / (SampleRate * 0.003));
                var decay = Math.Exp(-t / (seconds * 0.35));
                var wave = Math.Sin(2 * Math.PI * hz * t) + (0.25 * Math.Sin(4 * Math.PI * hz * t));
                samples.Add((short)(wave / 1.25 * attack * decay * volume * short.MaxValue));
            }
        }

        var data = samples.Count * 2;
        var wav = new byte[44 + data];
        void Text(int at, string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                wav[at + i] = (byte)s[i];
            }
        }

        void Int(int at, int v) => BitConverter.TryWriteBytes(wav.AsSpan(at, 4), v);
        void Short(int at, short v) => BitConverter.TryWriteBytes(wav.AsSpan(at, 2), v);
        Text(0, "RIFF");
        Int(4, 36 + data);
        Text(8, "WAVE");
        Text(12, "fmt ");
        Int(16, 16);
        Short(20, 1); // PCM
        Short(22, 1); // mono
        Int(24, SampleRate);
        Int(28, SampleRate * 2);
        Short(32, 2);
        Short(34, 16);
        Text(36, "data");
        Int(40, data);
        for (var i = 0; i < samples.Count; i++)
        {
            Short(44 + (i * 2), samples[i]);
        }

        return new Sound(wav);
    }

    private sealed class Sound
    {
        public Sound(byte[] bytes) => Address = GCHandle.Alloc(bytes, GCHandleType.Pinned).AddrOfPinnedObject();

        public nint Address { get; }
    }
}

internal static partial class SoundNative
{
    [LibraryImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PlaySoundW(nint sound, nint module, uint flags);
}
