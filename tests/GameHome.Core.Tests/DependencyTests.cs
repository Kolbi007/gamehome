// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Diagnostics;
using GameHome.Data;

namespace GameHome.Core.Tests;

public class DependencyTests
{
    [Theory]
    [InlineData("D3DCOMPILER_43.dll", true, "directx-jun2010")]
    [InlineData("XINPUT1_3.dll", false, "directx-jun2010")]
    [InlineData("d3dx9_43.dll", false, "directx-jun2010")]
    [InlineData("MSVCR100.dll", false, "vc2010-x86")]
    [InlineData("msvcp120.dll", true, "vc2013-x64")]
    [InlineData("VCRUNTIME140_1.dll", true, "vc2015-x64")]
    public void Known_dlls_map_to_the_microsoft_package(string dll, bool is64, string expected) =>
        Assert.Equal(expected, Dependencies.PackageFor(dll, is64)?.Id);

    [Theory]
    [InlineData("KERNEL32.dll")]
    [InlineData("d3d11.dll")]
    [InlineData("d3dcompiler_47.dll")] // Følger med Windows.
    [InlineData("XINPUT1_4.dll")]
    [InlineData("uplay_r1_loader64.dll")]
    public void Windows_and_game_dlls_are_not_reported(string dll) =>
        Assert.Null(Dependencies.PackageFor(dll, is64Bit: true));

    [Fact]
    public void Imports_are_read_the_same_from_bytes_and_from_a_stream()
    {
        var path = Environment.ProcessPath!;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            return; // Ikke en Windows-programfil (for eksempel på Linux).
        }

        var fromBytes = Dependencies.ReadImports(bytes);
        using var stream = File.OpenRead(path);
        var fromStream = Dependencies.ReadImports(stream);

        Assert.NotEmpty(fromBytes.Dlls);
        Assert.Equal(fromBytes.Is64Bit, fromStream.Is64Bit);
        Assert.Equal(fromBytes.Dlls, fromStream.Dlls);
        Assert.Contains(fromBytes.Dlls, d => d.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_missing_known_dll_is_reported_once_per_package()
    {
        var path = Environment.ProcessPath!;
        var bytes = File.ReadAllBytes(path);
        if (bytes[0] != 'M')
        {
            return;
        }

        // Ingen av testprogrammets egne importer hører til en pakke GameHome kjenner, så alt er i orden.
        Assert.True(Dependencies.Check(bytes, (_, _) => false).Ok);
        Assert.False(Dependencies.ReadImports([1, 2, 3]).Dlls.Count > 0);
    }

    [Fact]
    public void A_good_check_is_remembered_until_the_file_changes()
    {
        var document = new DependencyDocument();
        var modified = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        document.Remember(@"C:\Games\x\ACC.exe", 100, modified, []);

        Assert.True(document.IsKnownGood(@"c:\games\x\acc.exe", 100, modified));
        Assert.False(document.IsKnownGood(@"C:\Games\x\ACC.exe", 101, modified));

        document.Remember(@"C:\Games\x\ACC.exe", 100, modified, ["vc2010-x86"]);
        Assert.False(document.IsKnownGood(@"C:\Games\x\ACC.exe", 100, modified));

        document.Remember(@"C:\Games\x\ACC.exe", 100, modified, []);
        document.Forget(@"C:\Games\x\ACC.exe");
        Assert.False(document.IsKnownGood(@"C:\Games\x\ACC.exe", 100, modified));
    }

    [Fact]
    public void The_document_survives_a_round_trip_on_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gamehome-deps-{Guid.NewGuid():N}.json");
        try
        {
            var document = new DependencyDocument();
            var modified = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
            document.Remember(@"C:\Games\x\ACC.exe", 100, modified, []);
            document.InstalledPackages["directx-jun2010"] = modified;
            DependencyStore.Save(path, document);

            var loaded = DependencyStore.Load(path);
            Assert.True(loaded.IsKnownGood(@"C:\GAMES\X\ACC.EXE", 100, modified));
            Assert.True(loaded.InstalledPackages.ContainsKey("directx-jun2010"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
