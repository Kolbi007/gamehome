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
    DateTime FetchedUtc);

/// <summary>Lokalt lager for <see cref="GameDetails"/>, én fil med alle spill (spill-ID → detaljer).</summary>
public static partial class GameDetailsStore
{
    public static Dictionary<string, GameDetails> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize(stream, MetadataJson.Default.DictionaryStringGameDetails);
                if (loaded is not null)
                {
                    return new Dictionary<string, GameDetails>(loaded, StringComparer.Ordinal);
                }
            }
        }
        catch (JsonException)
        {
            // Et ødelagt lager hentes bare på nytt.
        }

        return new Dictionary<string, GameDetails>(StringComparer.Ordinal);
    }

    public static void Save(string path, Dictionary<string, GameDetails> details)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, details, MetadataJson.Default.DictionaryStringGameDetails);
        }

        File.Move(temp, path, overwrite: true);
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
