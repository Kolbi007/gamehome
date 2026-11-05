// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Sources.Steam;

/// <summary>Et Steam-spill som lastes ned eller oppdateres.</summary>
public sealed record SteamDownload(string AppId, string Name, long BytesDownloaded, long BytesToDownload)
{
    public int Percent => BytesToDownload <= 0 ? 0 : (int)Math.Clamp(BytesDownloaded * 100 / BytesToDownload, 0, 100);
}

/// <summary>
/// Nedlastinger fra appmanifestene (<c>BytesDownloaded</c>/<c>BytesToDownload</c>), som Steam skriver underveis. Et spill
/// som ikke er ferdig installert (StateFlags uten 4), eller som har bytes igjen å laste ned, regnes som pågående.
/// </summary>
public static class SteamDownloads
{
    public static SteamDownload? Parse(string acfText)
    {
        KeyValues? state;
        try
        {
            state = KeyValues.Parse(acfText)["AppState"];
        }
        catch (FormatException)
        {
            return null;
        }

        var appId = state?.GetString("appid");
        var name = state?.GetString("name");
        if (state is null || string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        _ = int.TryParse(state.GetString("StateFlags"), out var flags);
        _ = long.TryParse(state.GetString("BytesToDownload"), out var total);
        _ = long.TryParse(state.GetString("BytesDownloaded"), out var done);
        var installed = (flags & 4) != 0;
        return !installed || (total > 0 && done < total) ? new SteamDownload(appId, name, done, total) : null;
    }

    /// <summary>Pågående nedlastinger i alle Steam-bibliotekene.</summary>
    public static IReadOnlyList<SteamDownload> Scan(string steamRoot)
    {
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        IReadOnlyList<string> folders;
        try
        {
            folders = SteamLibraryReader.LibraryFolders(steamRoot, File.Exists(libraryFile) ? File.ReadAllText(libraryFile) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            folders = [steamRoot];
        }

        var downloads = new List<SteamDownload>();
        foreach (var folder in folders)
        {
            try
            {
                var steamapps = Path.Combine(folder, "steamapps");
                if (!Directory.Exists(steamapps))
                {
                    continue;
                }

                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    if (Parse(File.ReadAllText(manifest)) is { } download)
                    {
                        downloads.Add(download);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Et bibliotek som ikke kan leses, hoppes over.
            }
        }

        return downloads;
    }
}
