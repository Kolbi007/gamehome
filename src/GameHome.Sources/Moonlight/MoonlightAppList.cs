// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.RegularExpressions;

namespace GameHome.Sources.Moonlight;

/// <summary>En app i hostens liste, slik Moonlight rapporterer den.</summary>
public sealed record HostApp(string Name, int Id, bool Hidden)
{
    /// <summary>
    /// Om appen er et spill. Vibepollos innebygde verktøy (skrivebord, Steam Big Picture, fjernstyring) er ikke spill;
    /// Playnite-synkede spill er installerte spill (Vibepollo fjerner avinstallerte, se docs/architecture.md).
    /// </summary>
    public bool IsGame => !Hidden && !MoonlightAppList.IsHostTool(Name);
}

/// <summary>
/// Leser utskriften fra <c>Moonlight.exe list &lt;host&gt; --csv</c>. Formatet (moonlight-qt <c>app/cli/listapps.cpp</c>):
/// en header, så <c>"navn",id,hdr,collection,hidden,direct,"url"</c>. Anførselstegn inni navnet escapes ikke, så navnet
/// leses grådig fram til <c>",&lt;id&gt;,</c>.
/// </summary>
public static partial class MoonlightAppList
{
    // Vibepollos og Sunshines innebygde oppføringer. Hypotese til den er testet mot hostens liste.
    private static readonly HashSet<string> HostTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Desktop", "Steam Big Picture", "Remote Input", "Remote Monitor", "Terminate",
        "Playnite", "Playnite (Fullscreen)", "Virtual Desktop",
    };

    public static bool IsHostTool(string name) => HostTools.Contains(name.Trim());

    public static IReadOnlyList<HostApp> ParseCsv(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var apps = new List<HostApp>();
        foreach (var raw in output.Split('\n'))
        {
            var match = Row().Match(raw.TrimEnd('\r'));
            if (match.Success)
            {
                apps.Add(new HostApp(
                    match.Groups["name"].Value,
                    int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups["hidden"].Value == "true"));
            }
        }

        return apps;
    }

    [GeneratedRegex("^\"(?<name>.*)\",(?<id>-?\\d+),(?:true|false),(?:true|false),(?<hidden>true|false),(?:true|false),\".*\"$")]
    private static partial Regex Row();
}
