// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHome.Data;

/// <summary>Spilletid målt av GameHome selv: når spillet sist ble spilt, og hvor lenge totalt.</summary>
public sealed record PlayStat(DateTime LastPlayedUtc, double Minutes);

/// <summary>Lagrer spilletid lokalt (spill-ID → <see cref="PlayStat"/>). Ingenting sendes noe sted.</summary>
public static class PlayStatsStore
{
    public static Dictionary<string, PlayStat> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize(stream, PlayStatsJson.Default.DictionaryStringPlayStat);
                if (loaded is not null)
                {
                    return new Dictionary<string, PlayStat>(loaded, StringComparer.Ordinal);
                }
            }
        }
        catch (JsonException)
        {
            // Et ødelagt lager starter på nytt; spilletid er hyggelig, ikke kritisk.
        }

        return new Dictionary<string, PlayStat>(StringComparer.Ordinal);
    }

    public static void Save(string path, Dictionary<string, PlayStat> stats)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, stats, PlayStatsJson.Default.DictionaryStringPlayStat);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Legger til en økt. Korte økter (under ett minutt, for eksempel en start som feilet) teller ikke.</summary>
    public static PlayStat Add(PlayStat? existing, DateTime endedUtc, double minutes) =>
        minutes < 1
            ? existing ?? new PlayStat(endedUtc, 0)
            : new PlayStat(endedUtc, (existing?.Minutes ?? 0) + minutes);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, PlayStat>))]
internal sealed partial class PlayStatsJson : JsonSerializerContext;
