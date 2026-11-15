// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Data;

namespace GameHome.Core.Tests;

public sealed class LibraryRecoveryTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("gamehome-library-");

    private string LibraryPath => Path.Combine(_directory.FullName, "library.json");
    private string BackupPath => LibraryPath + ".bak";

    [Fact]
    public void Save_roundtrips_overrides_and_backs_up_the_previous_library()
    {
        var library = new GameLibrary();
        var (game, _) = library.AddExecutable("/games/Broforce.exe");
        var action = Assert.Single(library.PlayActions);
        var overrides = new UserOverrides(Title: "Mitt spill", DefaultPlayActionId: action.Id);
        library.SetOverrides(game.Id, overrides);

        LibraryStore.Save(library, LibraryPath);
        var original = File.ReadAllBytes(LibraryPath);
        Assert.Equal(original, File.ReadAllBytes(BackupPath));

        library.AddExecutable("/games/Fez.exe");
        LibraryStore.Save(library, LibraryPath);

        var result = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.Loaded, result.Status);
        Assert.True(result.CanSave);
        Assert.Equal(2, result.Library.Games.Count);
        Assert.Equal(overrides, result.Library.Find(game.Id)?.Overrides);
        Assert.Equal(action, result.Library.DefaultPlayAction(game.Id));
        Assert.Equal(original, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public void Corrupt_primary_recovers_from_backup_and_saves_without_losing_the_original_bytes()
    {
        var library = new GameLibrary();
        var (game, _) = library.AddExecutable("/games/Broforce.exe");
        var overrides = new UserOverrides(Title: "Mitt spill", Hidden: true);
        library.SetOverrides(game.Id, overrides);
        LibraryStore.Save(library, LibraryPath);
        var backup = File.ReadAllBytes(BackupPath);
        byte[] corrupt = [0xff, 0x00, 0x03];
        File.WriteAllBytes(LibraryPath, corrupt);

        var result = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.RecoveredFromBackup, result.Status);
        Assert.True(result.CanSave);
        Assert.Equal(overrides, Assert.Single(result.Library.Games).Overrides);
        Assert.Equal(corrupt, File.ReadAllBytes(LibraryPath));

        result.Library.AddExecutable("/games/Fez.exe");
        LibraryStore.Save(result.Library, LibraryPath);

        var archive = Assert.Single(Directory.GetFiles(_directory.FullName, "library.json.*.corrupt"));
        Assert.Equal(corrupt, File.ReadAllBytes(archive));
        Assert.Equal(backup, File.ReadAllBytes(BackupPath));
        var repaired = File.ReadAllBytes(LibraryPath);
        LibraryStore.Save(result.Library, LibraryPath);

        var saved = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.Loaded, saved.Status);
        Assert.Equal(2, saved.Library.Games.Count);
        Assert.Equal(overrides, saved.Library.Find(game.Id)?.Overrides);
        Assert.Equal(repaired, File.ReadAllBytes(BackupPath));
        Assert.Equal(corrupt, File.ReadAllBytes(archive));
    }

    [Theory]
    [InlineData("{\"Version\":1,")]
    [InlineData("null")]
    [InlineData("{\"Version\":1,\"Games\":[],\"PlayActions\":null}")]
    [InlineData("{\"Version\":1,\"Games\":[null],\"PlayActions\":[]}")]
    public void Corrupt_primary_without_a_valid_backup_is_preserved_on_save(string damage)
    {
        File.WriteAllText(LibraryPath, damage);
        var original = File.ReadAllBytes(LibraryPath);

        var result = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.Corrupt, result.Status);
        Assert.False(result.CanSave);
        Assert.Empty(result.Library.Games);

        Assert.Throws<IOException>(() => LibraryStore.Save(result.Library, LibraryPath));
        Assert.Equal(original, File.ReadAllBytes(LibraryPath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void Unsupported_primary_is_preserved_even_with_a_readable_backup()
    {
        var library = new GameLibrary();
        library.AddExecutable("/games/Broforce.exe");
        LibraryStore.Save(library, LibraryPath);
        var backup = File.ReadAllBytes(BackupPath);
        File.WriteAllText(LibraryPath, """{"Version":999,"Games":{"FutureShape":true}}""");
        var original = File.ReadAllBytes(LibraryPath);

        var result = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.UnsupportedVersion, result.Status);
        Assert.False(result.CanSave);
        Assert.Empty(result.Library.Games);

        Assert.Throws<IOException>(() => LibraryStore.Save(library, LibraryPath));
        Assert.Equal(original, File.ReadAllBytes(LibraryPath));
        Assert.Equal(backup, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public void Missing_primary_recovers_from_backup()
    {
        var library = new GameLibrary();
        var (game, _) = library.AddExecutable("/games/Broforce.exe");
        LibraryStore.Save(library, LibraryPath);
        File.Delete(LibraryPath);

        var result = LibraryStore.LoadWithStatus(LibraryPath);
        Assert.Equal(LibraryLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(game.Id, Assert.Single(result.Library.Games).Id);

        LibraryStore.Save(result.Library, LibraryPath);
        Assert.Equal(LibraryLoadStatus.Loaded, LibraryStore.LoadWithStatus(LibraryPath).Status);
    }

    [Fact]
    public void Corrupt_backup_is_preserved_when_rotating_from_a_valid_primary()
    {
        var library = new GameLibrary();
        library.AddExecutable("/games/Broforce.exe");
        LibraryStore.Save(library, LibraryPath);
        var original = File.ReadAllBytes(LibraryPath);
        byte[] corrupt = [0xff, 0x00, 0x03];
        File.WriteAllBytes(BackupPath, corrupt);

        library.AddExecutable("/games/Fez.exe");
        LibraryStore.Save(library, LibraryPath);

        var archive = Assert.Single(Directory.GetFiles(_directory.FullName, "library.json.bak.*.corrupt"));
        Assert.Equal(corrupt, File.ReadAllBytes(archive));
        Assert.Equal(original, File.ReadAllBytes(BackupPath));
        Assert.Equal(2, LibraryStore.Load(LibraryPath).Games.Count);
    }

    [Fact]
    public void Unsupported_backup_is_preserved_when_saving_a_valid_primary()
    {
        var library = new GameLibrary();
        library.AddExecutable("/games/Broforce.exe");
        LibraryStore.Save(library, LibraryPath);
        File.WriteAllText(BackupPath, """{"Version":999,"Games":[]}""");
        var backup = File.ReadAllBytes(BackupPath);

        library.AddExecutable("/games/Fez.exe");
        LibraryStore.Save(library, LibraryPath);

        Assert.Equal(backup, File.ReadAllBytes(BackupPath));
        Assert.Equal(2, LibraryStore.Load(LibraryPath).Games.Count);
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
