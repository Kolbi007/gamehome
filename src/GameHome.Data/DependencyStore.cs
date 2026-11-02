// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHome.Data;

/// <summary>Én sjekket programfil: hvordan den så ut da (størrelse og endringstid) og hva som manglet.</summary>
public sealed record CheckedExecutable(long Size, DateTime ModifiedUtc, DateTime CheckedUtc, IReadOnlyList<string> MissingPackages);

/// <summary>
/// GameHomes lille oversikt over avhengigheter: hvilke programfiler som er sjekket (så uendrede spill der alt var i
/// orden ikke sjekkes på nytt), og hvilke Microsoft-pakker som er sett installert.
/// </summary>
public sealed class DependencyDocument
{
    public Dictionary<string, CheckedExecutable> Executables { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DateTime> InstalledPackages { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Om programfilen er sjekket uten mangler og ikke endret siden.</summary>
    public bool IsKnownGood(string exePath, long size, DateTime modifiedUtc) =>
        Executables.TryGetValue(exePath, out var known)
        && known.Size == size && known.ModifiedUtc == modifiedUtc && known.MissingPackages.Count == 0;

    public void Remember(string exePath, long size, DateTime modifiedUtc, IReadOnlyList<string> missingPackages) =>
        Executables[exePath] = new CheckedExecutable(size, modifiedUtc, DateTime.UtcNow, missingPackages);

    /// <summary>Glemmer sjekken (for eksempel når spillet krasjet med «DLL ikke funnet»), så neste start sjekker på nytt.</summary>
    public void Forget(string exePath) => Executables.Remove(exePath);
}

public static class DependencyStore
{
    public static DependencyDocument Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize(stream, DependencyJson.Default.DependencyDocument);
                if (loaded is not null)
                {
                    return new DependencyDocument
                    {
                        Executables = new(loaded.Executables, StringComparer.OrdinalIgnoreCase),
                        InstalledPackages = new(loaded.InstalledPackages, StringComparer.Ordinal),
                    };
                }
            }
        }
        catch (JsonException)
        {
            // Ødelagt oversikt: start på nytt; sjekkene er billige.
        }

        return new DependencyDocument();
    }

    public static void Save(string path, DependencyDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, document, DependencyJson.Default.DependencyDocument);
        }

        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DependencyDocument))]
internal sealed partial class DependencyJson : JsonSerializerContext;
