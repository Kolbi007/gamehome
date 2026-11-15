// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Diagnostics;

namespace GameHome.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gamehome-log-" + Guid.NewGuid().ToString("N"));

    public FileLogTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Unwritable_log_does_not_block_in_memory_diagnostics()
    {
        var log = new FileLog(_directory); // A directory cannot be opened as an appendable file.
        string? observed = null;
        log.Written += (_, line) => observed = line;

        log.Write("test", "still usable");

        Assert.Contains("still usable", observed);
    }

    [Fact]
    public void Unavailable_parent_does_not_break_construction_or_logging()
    {
        var parentFile = Path.Combine(_directory, "file");
        File.WriteAllText(parentFile, "existing data");
        var log = new FileLog(Path.Combine(parentFile, "log.txt"));

        log.Write("test", "cannot create parent");

        Assert.Equal("existing data", File.ReadAllText(parentFile));
    }

    [Fact]
    public void Large_log_rotates_and_retains_previous_diagnostics()
    {
        var path = Path.Combine(_directory, "log.txt");
        var previous = new string('x', 2 * 1024 * 1024);
        File.WriteAllText(path, previous);
        File.WriteAllText(path + ".1", "older log");

        new FileLog(path).Write("test", "new entry");

        Assert.Equal(previous, File.ReadAllText(path + ".1"));
        Assert.Contains("new entry", File.ReadAllText(path));
        Assert.True(new FileInfo(path).Length < 1024);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void Oversized_message_is_bounded()
    {
        var path = Path.Combine(_directory, "log.txt");

        new FileLog(path).Write("test", new string('x', 2 * 1024 * 1024));

        Assert.True(new FileInfo(path).Length < 100_000);
        Assert.Contains("[avkortet]", File.ReadAllText(path));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
