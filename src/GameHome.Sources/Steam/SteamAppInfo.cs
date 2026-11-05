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

    public static IReadOnlyDictionary<uint, SteamAppSummary> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new Dictionary<uint, SteamAppSummary>();
        if (data.Length < 8)
        {
            return result;
        }

        var magic = BitConverter.ToUInt32(data, 0);
        if (magic is not (Version28 or Version29))
        {
            throw new FormatException($"Ukjent appinfo-versjon 0x{magic:X8}");
        }

        var pos = 8;
        string[]? strings = null;
        if (magic == Version29)
        {
            var tableOffset = BitConverter.ToInt64(data, pos);
            pos += 8;
            strings = ReadStringTable(data, checked((int)tableOffset));
        }

        while (pos + 8 <= data.Length)
        {
            var appId = BitConverter.ToUInt32(data, pos);
            pos += 4;
            if (appId == 0)
            {
                break;
            }

            var size = BitConverter.ToInt32(data, pos);
            pos += 4;
            var end = pos + size;
            if (size <= 0 || end > data.Length)
            {
                break;
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
            catch (IndexOutOfRangeException)
            {
                // Én ødelagt oppføring stopper ikke resten.
            }

            if (name is not null)
            {
                result[appId] = new SteamAppSummary(appId, name, type ?? "");
            }

            pos = end;
        }

        return result;
    }

    private static string[] ReadStringTable(byte[] data, int offset)
    {
        var count = BitConverter.ToInt32(data, offset);
        var pos = offset + 4;
        var strings = new string[count];
        for (var i = 0; i < count; i++)
        {
            strings[i] = ReadCString(data, ref pos);
        }

        return strings;
    }

    private static string ReadCString(byte[] data, ref int pos)
    {
        var start = pos;
        while (data[pos] != 0)
        {
            pos++;
        }

        var text = Encoding.UTF8.GetString(data, start, pos - start);
        pos++;
        return text;
    }

    private static string ReadKey(byte[] data, ref int pos, string[]? strings)
    {
        if (strings is null)
        {
            return ReadCString(data, ref pos);
        }

        var index = BitConverter.ToInt32(data, pos);
        pos += 4;
        return strings[index];
    }

    /// <summary>Leser binære nøkkel/verdi-par til slutt-markøren; strengverdier meldes med full sti («a/b/c»).</summary>
    private static void ReadObject(byte[] data, ref int pos, int end, string[]? strings, string path, Action<string, string> onString)
    {
        while (pos < end)
        {
            var type = data[pos++];
            if (type is 0x08 or 0x0B)
            {
                return;
            }

            var key = ReadKey(data, ref pos, strings);
            var full = path.Length == 0 ? key : $"{path}/{key}";
            switch (type)
            {
                case 0x00:
                    ReadObject(data, ref pos, end, strings, full, onString);
                    break;
                case 0x01:
                    onString(full.ToLowerInvariant(), ReadCString(data, ref pos));
                    break;
                case 0x02 or 0x03 or 0x04 or 0x06:
                    pos += 4;
                    break;
                case 0x07 or 0x0A:
                    pos += 8;
                    break;
                case 0x05:
                    while (data[pos] != 0 || data[pos + 1] != 0)
                    {
                        pos += 2;
                    }

                    pos += 2;
                    break;
                default:
                    throw new FormatException($"Ukjent verditype 0x{type:X2}");
            }
        }
    }
}
