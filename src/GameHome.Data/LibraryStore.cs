// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using GameHome.Core.Library;
using GameHome.Core.Model;

namespace GameHome.Data;

/// <summary>Filformatet for biblioteket. Versjonen økes ved endringer som krever migrering.</summary>
public sealed record LibraryDocument(int Version, IReadOnlyList<Game> Games, IReadOnlyList<PlayAction> PlayActions)
{
    public const int CurrentVersion = 1;
}

public enum LibraryLoadStatus { Missing, Loaded, RecoveredFromBackup, Corrupt, UnsupportedVersion, Unavailable }

public sealed record LibraryLoadResult(GameLibrary Library, LibraryLoadStatus Status)
{
    public bool CanSave => Status is LibraryLoadStatus.Missing or LibraryLoadStatus.Loaded or LibraryLoadStatus.RecoveredFromBackup;
}

/// <summary>
/// Lagrer biblioteket som JSON. Home leses fra denne filen ved oppstart, uten nettverk.
/// Skriving går via en midlertidig fil og beholder en validert sikkerhetskopi.
/// </summary>
public static class LibraryStore
{
    public static GameLibrary Load(string path)
    {
        var result = LoadWithStatus(path);
        return result.CanSave ? result.Library : throw new IOException($"Kunne ikke lese biblioteket ({result.Status}).");
    }

    public static LibraryLoadResult LoadWithStatus(string path)
    {
        try
        {
            var (status, document) = ReadFile(path);
            if (document is not null)
            {
                return new(new GameLibrary(document.Games, document.PlayActions), status);
            }

            if (status is LibraryLoadStatus.Missing or LibraryLoadStatus.Corrupt)
            {
                var (backupStatus, backup) = ReadFile(path + ".bak");
                if (backup is not null)
                {
                    return new(new GameLibrary(backup.Games, backup.PlayActions), LibraryLoadStatus.RecoveredFromBackup);
                }

                if (backupStatus != LibraryLoadStatus.Missing)
                {
                    status = backupStatus;
                }
            }

            return new(new GameLibrary(), status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(new GameLibrary(), LibraryLoadStatus.Unavailable);
        }
    }

    private static (LibraryLoadStatus Status, LibraryDocument? Document) ReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty(nameof(LibraryDocument.Version), out var version)
                || version.ValueKind != JsonValueKind.Number)
            {
                return (LibraryLoadStatus.Corrupt, null);
            }

            if (!version.TryGetInt32(out var number) || number != LibraryDocument.CurrentVersion)
            {
                return (LibraryLoadStatus.UnsupportedVersion, null);
            }

            var document = json.RootElement.Deserialize(LibraryJsonContext.Default.LibraryDocument);
            return document is not null && IsValid(document)
                ? (LibraryLoadStatus.Loaded, document)
                : (LibraryLoadStatus.Corrupt, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (LibraryLoadStatus.Missing, null);
        }
        catch (JsonException)
        {
            return (LibraryLoadStatus.Corrupt, null);
        }
    }

    private static bool IsValid(LibraryDocument document) =>
        document.Games is not null && document.PlayActions is not null
        && document.Games.All(g => g is { Id: not null, Title: not null, Identities: not null, Art: not null, Overrides: not null }
            && g.Identities.All(i => i is { Namespace: not null, Value: not null }))
        && document.PlayActions.All(a => a is { Id: not null, GameId: not null, RunnerId: not null, Target: not null });

    public static void Save(GameLibrary library, string path)
    {
        ArgumentNullException.ThrowIfNull(library);
        var document = new LibraryDocument(LibraryDocument.CurrentVersion, library.Games, library.PlayActions);
        if (!IsValid(document))
        {
            throw new ArgumentException("Biblioteket inneholder ugyldige oppføringer.", nameof(library));
        }

        var (primaryStatus, _) = ReadFile(path);
        if (primaryStatus == LibraryLoadStatus.UnsupportedVersion)
        {
            throw new IOException("Biblioteket har en filversjon denne utgaven av GameHome ikke støtter.");
        }

        var backupPath = path + ".bak";
        var (backupStatus, _) = ReadFile(backupPath);
        if (primaryStatus != LibraryLoadStatus.Loaded && backupStatus != LibraryLoadStatus.Loaded
            && (primaryStatus != LibraryLoadStatus.Missing || backupStatus != LibraryLoadStatus.Missing))
        {
            throw new IOException("Bytte av bibliotekfilen krever en gyldig sikkerhetskopi.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteDocument(temp, document);
            switch (primaryStatus)
            {
                case LibraryLoadStatus.Missing:
                    if (backupStatus == LibraryLoadStatus.Missing)
                    {
                        var backupTemp = temp + ".bak";
                        try
                        {
                            WriteDocument(backupTemp, document);
                            File.Move(backupTemp, backupPath);
                        }
                        finally
                        {
                            File.Delete(backupTemp);
                        }
                    }

                    File.Move(temp, path);
                    break;
                case LibraryLoadStatus.Corrupt:
                    // La den validerte sikkerhetskopien ligge, og bevar originalen for gjenoppretting.
                    File.Replace(temp, path, CorruptPath(path));
                    break;
                default:
                    if (backupStatus == LibraryLoadStatus.Corrupt)
                    {
                        File.Move(backupPath, CorruptPath(backupPath));
                    }

                    // En sikkerhetskopi fra en annen filversjon kan fremdeles brukes av en annen appversjon.
                    File.Replace(temp, path, backupStatus == LibraryLoadStatus.UnsupportedVersion ? null : backupPath);
                    break;
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static string CorruptPath(string path) => $"{path}.{Guid.NewGuid():N}.corrupt";

    private static void WriteDocument(string path, LibraryDocument document)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, document, LibraryJsonContext.Default.LibraryDocument);
        stream.Flush(flushToDisk: true);
    }
}

/// <summary>Kildegenerert JSON, så lagringen virker med Native AOT og trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryDocument))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;
