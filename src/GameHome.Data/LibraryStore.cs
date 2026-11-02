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

/// <summary>
/// Lagrer biblioteket som JSON. Home leses fra denne filen ved oppstart, uten nettverk.
/// Skriving går via en midlertidig fil, så et krasj midt i lagringen ikke ødelegger biblioteket.
/// </summary>
public static class LibraryStore
{
    public static GameLibrary Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GameLibrary();
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize(stream, LibraryJsonContext.Default.LibraryDocument);
        return document is null ? new GameLibrary() : new GameLibrary(document.Games, document.PlayActions);
    }

    public static void Save(GameLibrary library, string path)
    {
        ArgumentNullException.ThrowIfNull(library);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new LibraryDocument(LibraryDocument.CurrentVersion, library.Games, library.PlayActions);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, document, LibraryJsonContext.Default.LibraryDocument);
        }

        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Kildegenerert JSON, så lagringen virker med Native AOT og trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryDocument))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;
