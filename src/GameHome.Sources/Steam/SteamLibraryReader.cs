// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;

namespace GameHome.Sources.Steam;

/// <summary>
/// Finner installerte Steam-spill fra Steams egne filer: <c>steamapps/libraryfolders.vdf</c> og
/// <c>appmanifest_*.acf</c> i hvert bibliotek. Leser bare; Steam trenger ikke å kjøre, og ingenting sendes på nett.
/// </summary>
public static class SteamLibraryReader
{
    // Steam-verktøy som ikke er spill.
    private static readonly HashSet<string> NonGames = ["228980"]; // Steamworks Common Redistributables

    private const int StateFullyInstalled = 4;

    /// <summary>Bibliotekmappene. Steam-mappen selv er alltid med.</summary>
    public static IReadOnlyList<string> LibraryFolders(string steamRoot, string? libraryFoldersText)
    {
        var folders = new List<string> { steamRoot };
        if (libraryFoldersText is null)
        {
            return folders;
        }

        var root = KeyValues.Parse(libraryFoldersText)["libraryfolders"];
        foreach (var (_, node) in root?.Children ?? [])
        {
            // Nytt format: { "path" "..." }. Gammelt format: "1" "D:\\SteamLibrary".
            var path = node.GetString("path") ?? node.Value;
            if (!string.IsNullOrWhiteSpace(path)
                && !folders.Exists(f => string.Equals(Normalize(f), Normalize(path), StringComparison.OrdinalIgnoreCase)))
            {
                folders.Add(path);
            }
        }

        return folders;
    }

    /// <summary>Leser én appmanifest. Returnerer null for verktøy, ødelagte filer og spill som ikke er ferdig installert.</summary>
    public static StoreEntry? ParseAppManifest(string acfText, string libraryFolder)
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
        var installDir = state?.GetString("installdir");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name) || NonGames.Contains(appId))
        {
            return null;
        }

        _ = int.TryParse(state!.GetString("StateFlags"), out var flags);
        if ((flags & StateFullyInstalled) == 0)
        {
            return null;
        }

        var directory = string.IsNullOrWhiteSpace(installDir)
            ? null
            : Path.Combine(libraryFolder, "steamapps", "common", installDir);
        return new StoreEntry(appId, name, directory);
    }

    /// <summary>Skanner alle bibliotekene. Et bibliotek som ikke kan leses (for eksempel en frakoblet disk), hoppes over.</summary>
    public static IReadOnlyList<StoreEntry> Scan(string steamRoot, Action<string>? log = null)
    {
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        string? libraryText = null;
        try
        {
            libraryText = File.Exists(libraryFile) ? File.ReadAllText(libraryFile) : null;
        }
        catch (IOException ex)
        {
            log?.Invoke($"Kunne ikke lese libraryfolders.vdf: {ex.Message}");
        }

        IReadOnlyList<string> folders;
        try
        {
            folders = LibraryFolders(steamRoot, libraryText);
        }
        catch (FormatException ex)
        {
            log?.Invoke($"libraryfolders.vdf er ødelagt: {ex.Message}");
            folders = [steamRoot];
        }

        var found = new Dictionary<string, StoreEntry>();
        foreach (var folder in folders)
        {
            var steamapps = Path.Combine(folder, "steamapps");
            IEnumerable<string> manifests;
            try
            {
                manifests = Directory.Exists(steamapps) ? Directory.EnumerateFiles(steamapps, "appmanifest_*.acf") : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"Hopper over bibliotek: {ex.Message}");
                continue;
            }

            foreach (var manifest in manifests)
            {
                try
                {
                    if (ParseAppManifest(File.ReadAllText(manifest), folder) is { } entry)
                    {
                        found.TryAdd(entry.Id, entry);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke($"Hopper over {Path.GetFileName(manifest)}: {ex.Message}");
                }
            }
        }

        return [.. found.Values];
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
