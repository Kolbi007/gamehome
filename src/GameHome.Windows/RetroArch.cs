// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Diagnostics;
using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Core.Runners;
using GameHome.Sources.Retro;

namespace GameHome.Windows;

/// <summary>
/// RetroArch (GPL-3.0, https://www.retroarch.com) kjører retrospillene som et eget program (ADR 0009). GameHome velger
/// kjerne ut fra mappen spillet ligger i og starter <c>retroarch.exe -L cores\&lt;kjerne&gt;.dll &lt;spill&gt;</c>.
/// Fullskjerm, pause når GameHome er framme og «fortsett der du slapp» står i RetroArchs egen <c>retroarch.cfg</c>.
/// </summary>
public static class RetroArch
{
    /// <summary>Mappen brukeren legger spill i: <c>%USERPROFILE%\Games\ROMs\&lt;system&gt;</c>.</summary>
    public static string RomRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "ROMs");

    public static string? FindExe()
    {
        foreach (var folder in new[]
        {
            @"C:\RetroArch-Win64", // standard for RetroArchs installasjonsprogram
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RetroArch"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroArch"),
        })
        {
            var exe = Path.Combine(folder, "retroarch.exe");
            if (File.Exists(exe))
            {
                return exe;
            }
        }

        return null;
    }

    public static string? CorePath(RetroSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return FindExe() is { } exe && Path.Combine(Path.GetDirectoryName(exe)!, "cores", system.Core + ".dll") is var core
            && File.Exists(core) ? core : null;
    }

    /// <summary>Spillene i spillmappen for systemene som har en kjerne installert. Tom hvis RetroArch mangler.</summary>
    public static IReadOnlyList<RetroGame> Scan() =>
        FindExe() is null ? [] : RetroLibrary.Scan(RomRoot, s => CorePath(s) is not null);

    public static IReadOnlyList<StoreEntry> AsStoreEntries(IReadOnlyList<RetroGame> games) =>
        [.. games.Select(g => new StoreEntry(g.Path, g.Title, Path.GetDirectoryName(g.Path)))];
}

/// <summary>Starter et retrospill i RetroArch. <see cref="PlayAction.Target"/> er stien til spillfilen.</summary>
public sealed class RetroArchRunner : IRunner
{
    public const string Id = "retroarch";

    public string RunnerId => Id;

    public RunnerCapabilities Capabilities =>
        RunnerCapabilities.ObserveExit | RunnerCapabilities.RequestClose | RunnerCapabilities.ForceTerminate;

    public Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        var exe = RetroArch.FindExe() ?? throw new LaunchException(LaunchFailure.FileMissing, "RetroArch er ikke installert");
        if (!File.Exists(action.Target))
        {
            throw new LaunchException(LaunchFailure.FileMissing, "Spillfilen finnes ikke lenger");
        }

        var system = RetroLibrary.SystemFor(action.Target)
            ?? throw new LaunchException(LaunchFailure.FileMissing, "Vet ikke hvilket system spillet hører til");
        var core = RetroArch.CorePath(system)
            ?? throw new LaunchException(LaunchFailure.FileMissing, $"Mangler emulator for {system.Name} i RetroArch");

        try
        {
            var directory = Path.GetDirectoryName(exe) ?? string.Empty;
            var process = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = directory,
                ArgumentList = { "-L", core, action.Target },
            }) ?? throw new LaunchException(LaunchFailure.StartFailed, "RetroArch startet ikke");
            return Task.FromResult<ISession>(new LocalProcessSession(action, process, directory));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException(LaunchFailure.StartFailed, ex.Message, ex);
        }
    }
}
