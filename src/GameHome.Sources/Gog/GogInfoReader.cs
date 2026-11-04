// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;

namespace GameHome.Sources.Gog;

/// <summary>
/// Én spilleoppgave fra <c>goggame-&lt;id&gt;.info</c>: en programfil (<c>FileTask</c>) med argumenter og arbeidsmappe.
/// Stiene er relative til installasjonsmappen.
/// </summary>
public sealed record GogPlayTask(
    string? Name,
    string Path,
    string? Arguments,
    string? WorkingDirectory,
    bool IsPrimary,
    bool IsHidden,
    string? Category);

/// <summary>Innholdet i <c>goggame-&lt;id&gt;.info</c> som GameHome bruker: ID, navn og spilleoppgaver.</summary>
public sealed record GogGameInfo(string? GameId, string? Name, IReadOnlyList<GogPlayTask> PlayTasks)
{
    /// <summary>
    /// Oppgaven som starter selve spillet: den primære med kategori «game» (eller uten kategori), ellers den første
    /// synlige oppgaven med kategori «game». Null når filen bare har lenker eller verktøy.
    /// </summary>
    public GogPlayTask? PrimaryGameTask =>
        PlayTasks.FirstOrDefault(t => t.IsPrimary && IsGame(t))
        ?? PlayTasks.FirstOrDefault(t => t.IsPrimary && t.Category is null)
        ?? PlayTasks.FirstOrDefault(t => !t.IsHidden && IsGame(t));

    private static bool IsGame(GogPlayTask task) => string.Equals(task.Category, "game", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Leser GOGs egen fil i installasjonsmappen, <c>goggame-&lt;id&gt;.info</c> (JSON). Feltnavnene følger åpen kildekode
/// som leser samme fil: Heroic (<c>src/common/types/gog.ts</c>, <c>GOGGameDotInfoFile</c>: <c>playTasks</c> med
/// <c>type</c>, <c>isPrimary</c>, <c>isHidden</c>, <c>category</c>, <c>path</c>, <c>arguments</c>, <c>workingDir</c>)
/// og heroic-gogdl (<c>gogdl/launch.py</c>: <c>path</c> og <c>workingDir</c> er relative til installasjonen).
/// GOG Galaxy trenger ikke å kjøre.
/// </summary>
public static class GogInfoReader
{
    /// <summary>Filnavnet for et spill: <c>goggame-&lt;id&gt;.info</c>.</summary>
    public static string FileName(string gameId) => $"goggame-{gameId}.info";

    /// <summary>Leser filen. Null når den ikke er gyldig JSON eller mangler <c>playTasks</c>.</summary>
    public static GogGameInfo? Parse(string json)
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
                || !root.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var playTasks = new List<GogPlayTask>();
            foreach (var task in tasks.EnumerateArray())
            {
                if (ReadTask(task) is { } playTask)
                {
                    playTasks.Add(playTask);
                }
            }

            return new GogGameInfo(String(root, "gameId"), String(root, "name"), playTasks);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Setter sammen installasjonsmappen og en relativ sti fra filen, med Windows' skilletegn (GOG-filene er for Windows).
    /// En absolutt sti returneres som den er.
    /// </summary>
    public static string Combine(string installDirectory, string relative)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(relative);
        var path = relative.Replace('/', '\\');
        if (path.Length >= 2 && (path[1] == ':' || path.StartsWith(@"\\", StringComparison.Ordinal)))
        {
            return path;
        }

        path = path.TrimStart('\\');
        if (path.StartsWith(@".\", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        var directory = installDirectory.Replace('/', '\\').TrimEnd('\\');
        return path.Length == 0 || path == "." ? directory : $"{directory}\\{path}";
    }

    // Bare programfiler; lenker (URLTask) og oppgaver uten sti hoppes over.
    private static GogPlayTask? ReadTask(JsonElement task)
    {
        if (task.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = String(task, "type");
        var path = String(task, "path");
        if ((type is not null && !string.Equals(type, "FileTask", StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new GogPlayTask(
            String(task, "name"),
            path,
            String(task, "arguments"),
            String(task, "workingDir") is { Length: > 0 } dir ? dir : null,
            Bool(task, "isPrimary"),
            Bool(task, "isHidden"),
            String(task, "category"));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
