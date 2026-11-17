// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text;

namespace GameHome.Sources.Steam;

/// <summary>Navn og type for en Steam-app fra klientens lokale <c>appcache/appinfo.vdf</c>.</summary>
public sealed record SteamAppSummary(uint AppId, string Name, string Type);

/// <summary>
/// Leser Steams lokale app-database (<c>appinfo.vdf</c>, versjon 28 og 29). Formatet er udokumentert av Valve, men beskrevet
/// i åpne prosjekter (SteamKit, ValvePython/vdf): et hode, så én oppføring per app med binære nøkkel/verdi-par; i versjon 29
/// er nøklene indekser i en strengtabell bakerst i filen. Vi henter bare <c>appinfo/common/name</c> og <c>type</c>.
/// Leses lokalt, uten nett og uten innlogging.
/// </summary>
public static class SteamAppInfo
{
    private const uint Version28 = 0x07564428;
    private const uint Version29 = 0x07564429;
    private const int MaxObjectDepth = 64;

    public static IReadOnlyDictionary<uint, SteamAppSummary> Read(byte[] data) => Read(data, default);

    public static IReadOnlyDictionary<uint, SteamAppSummary> Read(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<uint, SteamAppSummary>();
        if (data.Length < 8)
        {
            throw new FormatException("Ufullstendig appinfo-hode");
        }

        var magic = BitConverter.ToUInt32(data, 0);
        if (magic is not (Version28 or Version29))
        {
            throw new FormatException($"Ukjent appinfo-versjon 0x{magic:X8}");
        }

        var pos = 8;
        var recordsEnd = data.Length;
        string[]? strings = null;
        if (magic == Version29)
        {
            if (data.Length < 16)
            {
                throw new FormatException("Ufullstendig appinfo-hode");
            }

            var tableOffset = BitConverter.ToInt64(data, pos);
            pos += 8;
            if (tableOffset < pos || tableOffset > data.Length - 4)
            {
                throw new FormatException("Ugyldig strengtabell-offset");
            }

            recordsEnd = (int)tableOffset;
            strings = ReadStringTable(data, recordsEnd, cancellationToken);
        }

        while (pos <= recordsEnd - 4)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appId = BitConverter.ToUInt32(data, pos);
            pos += 4;
            if (appId == 0)
            {
                break;
            }

            if (pos > recordsEnd - 4)
            {
                break;
            }

            var size = BitConverter.ToInt32(data, pos);
            pos += 4;
            if (size < 0 || size > recordsEnd - pos)
            {
                break;
            }

            var end = pos + size;
            if (size < 60)
            {
                pos = end;
                continue;
            }

            // infoState (4), lastUpdated (4), picsToken (8), sha1 (20), changeNumber (4), binary sha1 (20).
            var kv = pos + 60;
            string? name = null;
            string? type = null;
            try
            {
                ReadObject(data, ref kv, end, strings, "", (path, value) =>
                {
                    if (path == "appinfo/common/name")
                    {
                        name = value;
                    }
                    else if (path == "appinfo/common/type")
                    {
                        type = value;
                    }
                });
            }
            catch (FormatException)
            {
                // Én ødelagt oppføring stopper ikke resten.
                pos = end;
                continue;
            }

            if (name is not null)
            {
                result[appId] = new SteamAppSummary(appId, name, type ?? "");
            }

            pos = end;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string[] ReadStringTable(byte[] data, int offset, CancellationToken cancellationToken)
    {
        var count = BitConverter.ToInt32(data, offset);
        var pos = offset + 4;
        // Every string needs at least a terminating zero byte.
        if (count < 0 || count > data.Length - pos)
        {
            throw new FormatException("Ugyldig antall strenger");
        }

        var strings = new string[count];
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            strings[i] = ReadCString(data, ref pos, data.Length);
        }

        return strings;
    }

    private static string ReadCString(byte[] data, ref int pos, int end)
    {
        var length = data.AsSpan(pos, end - pos).IndexOf((byte)0);
        if (length < 0)
        {
            throw new FormatException("Ufullstendig streng");
        }

        var text = Encoding.UTF8.GetString(data, pos, length);
        pos += length + 1;
        return text;
    }

    private static void RequireBytes(int pos, int count, int end)
    {
        if (pos > end - count)
        {
            throw new FormatException("Ufullstendig verdi");
        }
    }

    private static string ReadKey(byte[] data, ref int pos, int end, string[]? strings)
    {
        if (strings is null)
        {
            return ReadCString(data, ref pos, end);
        }

        RequireBytes(pos, 4, end);
        var index = BitConverter.ToInt32(data, pos);
        pos += 4;
        if (index < 0 || index >= strings.Length)
        {
            throw new FormatException("Ugyldig strengindeks");
        }

        return strings[index];
    }

    /// <summary>Leser binære nøkkel/verdi-par til slutt-markøren; strengverdier meldes med full sti («a/b/c»).</summary>
    private static void ReadObject(byte[] data, ref int pos, int end, string[]? strings, string path, Action<string, string> onString, int depth = 0)
    {
        if (depth > MaxObjectDepth)
        {
            throw new FormatException("For dypt nestet objekt");
        }

        while (pos < end)
        {
            var type = data[pos++];
            if (type is 0x08 or 0x0B)
            {
                return;
            }

            var key = ReadKey(data, ref pos, end, strings);
            var full = path.Length == 0 ? key : $"{path}/{key}";
            switch (type)
            {
                case 0x00:
                    ReadObject(data, ref pos, end, strings, full, onString, depth + 1);
                    break;
                case 0x01:
                    onString(full.ToLowerInvariant(), ReadCString(data, ref pos, end));
                    break;
                case 0x02 or 0x03 or 0x04 or 0x06:
                    RequireBytes(pos, 4, end);
                    pos += 4;
                    break;
                case 0x07 or 0x0A:
                    RequireBytes(pos, 8, end);
                    pos += 8;
                    break;
                case 0x05:
                    while (true)
                    {
                        RequireBytes(pos, 2, end);
                        if (data[pos] == 0 && data[pos + 1] == 0)
                        {
                            pos += 2;
                            break;
                        }

                        pos += 2;
                    }
                    break;
                default:
                    throw new FormatException($"Ukjent verditype 0x{type:X2}");
            }
        }

        throw new FormatException("Ufullstendig objekt");
    }
}
