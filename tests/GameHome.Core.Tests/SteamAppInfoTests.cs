// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text;
using GameHome.Sources.Steam;

namespace GameHome.Core.Tests;

public class SteamAppInfoTests
{
    private static IReadOnlyDictionary<uint, SteamAppSummary> Read(byte[] data) =>
        SteamAppInfo.Read(data, TestContext.Current.CancellationToken);

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
        var apps = Read(Build(1580180, "Gunbrella", "Game"));
        Assert.Equal("Gunbrella", apps[1580180].Name);
        Assert.Equal("Game", apps[1580180].Type);
    }

    [Fact]
    public void UnknownVersionIsRejected() =>
        Assert.Throws<FormatException>(() => Read([1, 2, 3, 4, 5, 6, 7, 8]));

    [Fact]
    public void CancelledReadStopsBeforeParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SteamAppInfo.Read(Build(1, "Name", "Game"), cancellation.Token));
    }

    [Fact]
    public void Version28ReadsInlineKeys()
    {
        using var file = new MemoryStream();
        using var kv = new MemoryStream();
        void Text(string value) { kv.Write(Encoding.UTF8.GetBytes(value)); kv.WriteByte(0); }
        kv.WriteByte(0); Text("AppInfo");
        kv.WriteByte(0); Text("Common");
        kv.WriteByte(1); Text("Name"); Text("Game 28");
        kv.WriteByte(1); Text("Type"); Text("Game");
        kv.WriteByte(8); kv.WriteByte(8); kv.WriteByte(8);
        file.Write(BitConverter.GetBytes(0x07564428u));
        file.Write(BitConverter.GetBytes(1u));
        file.Write(BitConverter.GetBytes(42u));
        file.Write(BitConverter.GetBytes(60 + (int)kv.Length));
        file.Write(new byte[60]);
        kv.Position = 0; kv.CopyTo(file);
        file.Write(BitConverter.GetBytes(0u));
        Assert.Equal("Game 28", Read(file.ToArray())[42].Name);
    }

    [Fact]
    public void InvalidVersion29MetadataIsRejected()
    {
        var valid = Build(1, "Name", "Game");
        Assert.Throws<FormatException>(() => Read(valid[..12]));
        foreach (var offset in new long[] { -1, 15, long.MaxValue, valid.Length - 3 })
        {
            var bytes = (byte[])valid.Clone();
            BitConverter.GetBytes(offset).CopyTo(bytes, 8);
            Assert.Throws<FormatException>(() => Read(bytes));
        }

        var table = (int)BitConverter.ToInt64(valid, 8);
        foreach (var count in new[] { -1, int.MaxValue })
        {
            var bytes = (byte[])valid.Clone();
            BitConverter.GetBytes(count).CopyTo(bytes, table);
            Assert.Throws<FormatException>(() => Read(bytes));
        }
        var unterminated = valid[..^1];
        Assert.Throws<FormatException>(() => Read(unterminated));
    }

    [Fact]
    public void DamagedRecordDoesNotConsumeFollowingRecord()
    {
        var first = Build(1, "First", "Game");
        var second = Build(2, "Second", "Game");
        var firstTable = (int)BitConverter.ToInt64(first, 8);
        var secondTable = (int)BitConverter.ToInt64(second, 8);
        using var combined = new MemoryStream();
        combined.Write(first, 0, firstTable - 4);
        combined.Write(second, 16, secondTable - 16);
        var tableOffset = combined.Position;
        combined.Write(second, secondTable, second.Length - secondTable);
        var bytes = combined.ToArray();
        BitConverter.GetBytes(tableOffset).CopyTo(bytes, 8);
        // An unterminated value at the end of the first record cannot read the next header.
        var firstEnd = firstTable - 4;
        Array.Fill(bytes, (byte)'x', 16 + 8 + 60, firstEnd - (16 + 8 + 60));
        bytes[16 + 8 + 60] = 1;
        BitConverter.GetBytes(2).CopyTo(bytes, 16 + 8 + 61);
        var apps = Read(bytes);
        Assert.False(apps.ContainsKey(1));
        Assert.Equal("Second", apps[2].Name);
    }

    [Fact]
    public void InvalidRecordLengthsAndKeysAreContained()
    {
        var bytes = Build(1, "Name", "Game");
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 20);
        Assert.Empty(Read(bytes));
        bytes = Build(1, "Name", "Game");
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 85);
        Assert.Empty(Read(bytes));
    }

    [Fact]
    public void RealFileFromLegionGoParses()
    {
        // Lokal sjekk mot en kopi av Steams fil; hoppes over i CI og på maskiner uten kopien.
        var path = Environment.GetEnvironmentVariable("GAMEHOME_APPINFO");
        if (path is null || !File.Exists(path))
        {
            return;
        }

        var apps = Read(File.ReadAllBytes(path));
        Assert.True(apps.Count > 100);
        Console.WriteLine($"appinfo: {apps.Count} apper, {apps.Values.Count(a => a.Type.Equals("game", StringComparison.OrdinalIgnoreCase))} spill");
    }
}
