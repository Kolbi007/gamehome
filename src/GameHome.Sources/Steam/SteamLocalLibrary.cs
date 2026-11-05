// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Sources.Steam;

/// <summary>Et spill i Steam-biblioteket på denne maskinen (eid eller familiedelt), med Steams egne bilder hvis de finnes.</summary>
public sealed record SteamLibraryApp(uint AppId, string Name, string? Hero, string? Logo, string? Cover);

/// <summary>
/// Spillene Steam-klienten viser i biblioteket, også familiedelte, uten nett og uten nøkkel: mappene i
/// <c>appcache/librarycache</c> (én per app i biblioteket, med bilder) slått sammen med navn og type fra
/// <c>appinfo.vdf</c>. Bare type «game» tas med (ikke tillegg, verktøy eller demoer). Målt 2026-11-05: 863 apper i
/// bufferen, 295 spill.
/// </summary>
public static class SteamLocalLibrary
{
    public static IReadOnlyList<SteamLibraryApp> Read(string steamRoot)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var info = Path.Combine(steamRoot, "appcache", "appinfo.vdf");
        if (!Directory.Exists(cache) || !File.Exists(info))
        {
            return [];
        }

        IReadOnlyDictionary<uint, SteamAppSummary> apps;
        try
        {
            apps = SteamAppInfo.Read(File.ReadAllBytes(info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return [];
        }

        var result = new List<SteamLibraryApp>();
        foreach (var folder in Directory.EnumerateDirectories(cache))
        {
            if (!uint.TryParse(Path.GetFileName(folder), out var appId) || !apps.TryGetValue(appId, out var app)
                || !app.Type.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new SteamLibraryApp(appId, app.Name, Find(folder, "library_hero.jpg"), Find(folder, "logo.png"), Find(folder, "library_600x900.jpg")));
        }

        return result;
    }

    // Bildene ligger enten rett i mappen eller i en undermappe (Steam lagrer noen under en hash).
    private static string? Find(string folder, string name)
    {
        var direct = Path.Combine(folder, name);
        if (File.Exists(direct))
        {
            return direct;
        }

        try
        {
            return Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
