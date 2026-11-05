// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;

namespace GameHome.Sources.Epic;

/// <summary>Et installert Epic-spill fra en manifestfil (<c>*.item</c>).</summary>
public sealed record EpicManifest(
    string AppName,
    string DisplayName,
    string InstallLocation,
    string LaunchExecutable,
    string CatalogNamespace,
    string CatalogItemId)
{
    /// <summary>
    /// Epic-klientens start-URI, samme format som Playnites Epic-tillegg (<c>EpicLauncher.GameLaunchUrlMask</c>):
    /// <c>com.epicgames.launcher://apps/{namespace}%3A{itemId}%3A{appName}?action=launch&amp;silent=true</c>.
    /// </summary>
    public string LaunchUri =>
        $"com.epicgames.launcher://apps/{Uri.EscapeDataString(CatalogNamespace)}%3A{Uri.EscapeDataString(CatalogItemId)}"
        + $"%3A{Uri.EscapeDataString(AppName)}?action=launch&silent=true";

    /// <summary>Full sti til spillets programfil (Windows' skilletegn).</summary>
    public string ExecutablePath
    {
        get
        {
            var directory = InstallLocation.Replace('/', '\\').TrimEnd('\\');
            return $"{directory}\\{LaunchExecutable.Replace('/', '\\').TrimStart('\\')}";
        }
    }
}

/// <summary>
/// Leser Epic-klientens manifestfiler, <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item</c> (JSON). Mappen
/// og feltnavnene følger Playnites Epic-tillegg (<c>EpicLauncher.GetInstalledManifests</c>) og Legendary
/// (<c>legendary/models/egl.py</c>: <c>AppCategories</c>, <c>bIsIncompleteInstall</c>, <c>LaunchExecutable</c>,
/// <c>MainGameAppName</c> osv.). Epic-klienten trenger ikke å kjøre.
/// </summary>
public static class EpicManifestReader
{
    /// <summary>
    /// Leser én manifestfil. Null for ufullstendige installasjoner, alt som ikke har kategorien «games», tillegg (DLC,
    /// der <c>MainGameAppName</c> er et annet spill) og filer som mangler felt som trengs for å starte spillet.
    /// </summary>
    public static EpicManifest? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
                || !HasCategory(root, "games"))
            {
                return null;
            }

            var appName = String(root, "AppName");
            var mainGame = String(root, "MainGameAppName");
            if (!string.IsNullOrEmpty(mainGame) && !string.Equals(mainGame, appName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var displayName = String(root, "DisplayName");
            var location = String(root, "InstallLocation");
            var executable = String(root, "LaunchExecutable");
            var catalogNamespace = String(root, "CatalogNamespace");
            var catalogItemId = String(root, "CatalogItemId");
            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(location)
                || string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(catalogNamespace)
                || string.IsNullOrWhiteSpace(catalogItemId))
            {
                return null;
            }

            return new EpicManifest(
                appName,
                string.IsNullOrWhiteSpace(displayName) ? appName : displayName,
                location,
                executable,
                catalogNamespace,
                catalogItemId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Leser alle <c>*.item</c> i mappen. En fil som ikke kan leses, hoppes over; de andre leses likevel. Finnes ikke
    /// mappen, er listen tom. Samme spill to ganger (samme <c>AppName</c>) tas med én gang.
    /// </summary>
    public static IReadOnlyList<EpicManifest> ReadAll(string manifestsDirectory, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(manifestsDirectory);
        var games = new List<EpicManifest>();
        IEnumerable<string> files;
        try
        {
            if (!Directory.Exists(manifestsDirectory))
            {
                return games;
            }

            files = Directory.EnumerateFiles(manifestsDirectory, "*.item").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"Kunne ikke lese Epic-manifestene: {ex.Message}");
            return games;
        }

        foreach (var file in files)
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { } game
                    && !games.Exists(g => string.Equals(g.AppName, game.AppName, StringComparison.OrdinalIgnoreCase)))
                {
                    games.Add(game);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"Hoppet over {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return games;
    }

    private static bool HasCategory(JsonElement root, string category) =>
        root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
        && categories.EnumerateArray().Any(c =>
            c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), category, StringComparison.OrdinalIgnoreCase));

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
