// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text;
using GameHome.Sources.Steam;

namespace GameHome.Core.Tests;

public class SteamAppInfoTests
{
    // Bygger en minimal appinfo.vdf versjon 29: hode, én app med appinfo/common/{name,type}, slutt-markør, strengtabell.
    private static byte[] Build(uint appId, string name, string type)
    {
        string[] table = ["appinfo", "common", "name", "type", "appid"];
        using var kv = new MemoryStream();
        void Key(int index) => kv.Write(BitConverter.GetBytes(index));
        void Str(string s) { kv.Write(Encoding.UTF8.GetBytes(s)); kv.WriteByte(0); }
        kv.WriteByte(0x00); Key(0);              // appinfo {
        kv.WriteByte(0x02); Key(4); kv.Write(BitConverter.GetBytes((int)appId)); // appid (int32)
        kv.WriteByte(0x00); Key(1);              //   common {
        kv.WriteByte(0x01); Key(2); Str(name);   //     name
        kv.WriteByte(0x01); Key(3); Str(type);   //     type
        kv.WriteByte(0x08);                      //   }
        kv.WriteByte(0x08);                      // }
        kv.WriteByte(0x08);                      // slutt på oppføringen

        using var file = new MemoryStream();
        file.Write(BitConverter.GetBytes(0x07564429u));
        file.Write(BitConverter.GetBytes(1u));
        var tableOffsetPosition = file.Position;
        file.Write(BitConverter.GetBytes(0L));
        file.Write(BitConverter.GetBytes(appId));
        var payload = new byte[60];
        file.Write(BitConverter.GetBytes(payload.Length + (int)kv.Length));
        file.Write(payload);
        kv.Position = 0;
        kv.CopyTo(file);
        file.Write(BitConverter.GetBytes(0u)); // slutt på appene
        var tableOffset = file.Position;
        file.Write(BitConverter.GetBytes(table.Length));
        foreach (var s in table)
        {
            file.Write(Encoding.UTF8.GetBytes(s));
            file.WriteByte(0);
        }

        var bytes = file.ToArray();
        BitConverter.GetBytes(tableOffset).CopyTo(bytes, tableOffsetPosition);
        return bytes;
    }

    [Fact]
    public void NameAndTypeAreRead()
    {
        var apps = SteamAppInfo.Read(Build(1580180, "Gunbrella", "Game"));
        Assert.Equal("Gunbrella", apps[1580180].Name);
        Assert.Equal("Game", apps[1580180].Type);
    }

    [Fact]
    public void UnknownVersionIsRejected() =>
        Assert.Throws<FormatException>(() => SteamAppInfo.Read([1, 2, 3, 4, 5, 6, 7, 8]));

    [Fact]
    public void RealFileFromLegionGoParses()
    {
        // Lokal sjekk mot en kopi av Steams fil; hoppes over i CI og på maskiner uten kopien.
        var path = Environment.GetEnvironmentVariable("GAMEHOME_APPINFO");
        if (path is null || !File.Exists(path))
        {
            return;
        }

        var apps = SteamAppInfo.Read(File.ReadAllBytes(path));
        Assert.True(apps.Count > 100);
        Console.WriteLine($"appinfo: {apps.Count} apper, {apps.Values.Count(a => a.Type.Equals("game", StringComparison.OrdinalIgnoreCase))} spill");
    }
}
