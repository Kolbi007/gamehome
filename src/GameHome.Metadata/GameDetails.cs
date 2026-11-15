// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameHome.Metadata;

/// <summary>Fakta om et spill til detaljvisningen. Lagres lokalt, så visningen virker uten nett.</summary>
public sealed record GameDetails(
    string? Description,
    string? Developer,
    string? Publisher,
    string? Released,
    IReadOnlyList<string> Genres,
    int? Metacritic,
    string Source,
    DateTime FetchedUtc,
    int? HoursMain = null,
    int? HoursComplete = null);

/// <summary>Lokalt lager for <see cref="GameDetails"/>, én fil med alle spill (spill-ID → detaljer).</summary>
public static partial class GameDetailsStore
{
    private static readonly Lock SaveGate = new();

    public static Dictionary<string, GameDetails> Load(string path) =>
        TryLoad(path) ?? TryLoad(path + ".bak") ?? new Dictionary<string, GameDetails>(StringComparer.Ordinal);

    private static Dictionary<string, GameDetails>? TryLoad(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, GameDetails>? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var loaded = JsonSerializer.Deserialize(stream, MetadataJson.Default.DictionaryStringGameDetails);
            return loaded is not null && loaded.Values.All(details => details is not null)
                ? new Dictionary<string, GameDetails>(loaded, StringComparer.Ordinal)
                : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string path, Dictionary<string, GameDetails> details)
    {
        lock (SaveGate)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew))
                {
                    JsonSerializer.Serialize(stream, details, MetadataJson.Default.DictionaryStringGameDetails);
                }

                if (Read(path) is not null)
                {
                    File.Replace(temp, path, backup);
                    return;
                }

                // Never replace a valid backup with a corrupt or missing primary.
                if (Read(backup) is null)
                {
                    File.Copy(temp, temp + ".bak");
                    File.Move(temp + ".bak", backup, overwrite: true);
                }

                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                File.Delete(temp);
                File.Delete(temp + ".bak");
            }
        }
    }

    /// <summary>Steam gir beskrivelser med HTML-entiteter og av og til tagger; vis ren tekst.</summary>
    public static string? PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        return Spaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, GameDetails>))]
internal sealed partial class MetadataJson : JsonSerializerContext;
