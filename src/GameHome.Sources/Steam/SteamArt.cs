// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;

namespace GameHome.Sources.Steam;

/// <summary>
/// Bilder Steam allerede har lagret lokalt for et spill (<c>appcache/librarycache</c>). Virker uten nett og uten
/// API-nøkkel. Nyere Steam legger bildene i <c>librarycache/&lt;appid&gt;/&lt;hash&gt;/</c>, eldre som
/// <c>librarycache/&lt;appid&gt;_logo.png</c>; begge leses.
/// </summary>
public static class SteamArt
{
    public static ArtSet Find(string steamRoot, string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        return new ArtSet(
            Cover: FindFile(cache, appId, "library_600x900.jpg"),
            Hero: FindFile(cache, appId, "library_hero.jpg"),
            Logo: FindFile(cache, appId, "logo.png"));
    }

    private static string? FindFile(string cache, string appId, string name)
    {
        try
        {
            var folder = Path.Combine(cache, appId);
            if (Directory.Exists(folder))
            {
                var found = Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }

            var legacy = Path.Combine(cache, $"{appId}_{name}");
            return File.Exists(legacy) ? legacy : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
