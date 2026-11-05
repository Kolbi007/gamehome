// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.RegularExpressions;

namespace GameHome.Sources.Retro;

/// <summary>Et system GameHome kan starte spill for, med mappen spillene ligger i og libretro-kjernen som kjører dem.</summary>
public sealed record RetroSystem(string Folder, string Name, string Core, IReadOnlyList<string> Extensions);

/// <summary>Et spill funnet i en systemmappe.</summary>
public sealed record RetroGame(string Path, string Title, RetroSystem System);

/// <summary>
/// Retrospill fra mapper per system (<c>ROMs\snes</c>, <c>ROMs\megadrive</c> …). Brukeren legger inn spill brukeren eier
/// selv; GameHome laster aldri ned spill. Zip-filer støttes, RetroArch pakker dem ut selv.
/// </summary>
public static partial class RetroLibrary
{
    public static readonly IReadOnlyList<RetroSystem> Systems =
    [
        new("snes", "Super Nintendo", "snes9x_libretro", [".sfc", ".smc", ".zip"]),
        new("megadrive", "Mega Drive", "genesis_plus_gx_libretro", [".md", ".gen", ".smd", ".bin", ".zip"]),
        new("nes", "NES", "nestopia_libretro", [".nes", ".zip"]),
        new("gb", "Game Boy", "gambatte_libretro", [".gb", ".gbc", ".zip"]),
        new("gba", "Game Boy Advance", "mgba_libretro", [".gba", ".zip"]),
    ];

    /// <summary>Spillene under <paramref name="root"/> for systemene <paramref name="available"/> sier ja til.</summary>
    public static IReadOnlyList<RetroGame> Scan(string root, Func<RetroSystem, bool> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var games = new List<RetroGame>();
        if (!Directory.Exists(root))
        {
            return games;
        }

        foreach (var system in Systems.Where(available))
        {
            var folder = System.IO.Path.Combine(root, system.Folder);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (system.Extensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    games.Add(new RetroGame(file, CleanTitle(System.IO.Path.GetFileNameWithoutExtension(file)), system));
                }
            }
        }

        return games;
    }

    /// <summary>Systemet en spillfil hører til, ut fra mappen den ligger i.</summary>
    public static RetroSystem? SystemFor(string path)
    {
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        return Systems.FirstOrDefault(s => string.Equals(s.Folder, folder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>«Super Mario World (USA) [!]» → «Super Mario World»: region, versjon og merker i parentes fjernes.</summary>
    public static string CleanTitle(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var title = Tags().Replace(fileName, " ").Replace('_', ' ');
        title = Spaces().Replace(title, " ").Trim();
        return title.Length > 0 ? title : fileName;
    }

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
