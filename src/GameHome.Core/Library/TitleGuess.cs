// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameHome.Core.Library;

/// <summary>Hvor et forslag til tittel kommer fra, så Home kan si det til brukeren.</summary>
public enum TitleOrigin
{
    /// <summary>Produktnavnet i programfilen.</summary>
    ProductName,

    /// <summary>Filnavnet.</summary>
    FileName,

    /// <summary>En mappe over programfilen.</summary>
    Folder,
}

public sealed record TitleCandidate(string Title, TitleOrigin Origin);

/// <summary>
/// Gjetter tittelen på et spill som er lagt til som programfil, og sammenligner titler tilgivende. «ACC.exe» i mappen
/// «Assassin's Creed Rogue-273» gir «Assassin's Creed Rogue», som også treffer Steams «Assassin's Creed® Rogue».
/// </summary>
public static partial class TitleGuess
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "launcher", "start", "play", "run", "main", "app", "client", "bin", "binaries", "win64", "win32",
        "x64", "x86", "win64-shipping", "win32-shipping", "shipping", "release", "retail", "program files",
        "program files (x86)", "spill", "downloads", "steamapps", "common", "gog games", "epic games",
    };

    /// <summary>
    /// Forslag i rekkefølge: produktnavnet i filen, filnavnet hvis det ser ut som et ord og ikke en forkortelse, så
    /// mappene oppover (høyst tre). Filnavnet kommer til slutt også når det er en forkortelse, så det alltid finnes et forslag.
    /// </summary>
    public static IReadOnlyList<TitleCandidate> Candidates(string exePath, string? productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var list = new List<TitleCandidate>();
        void Add(string? raw, TitleOrigin origin)
        {
            if (raw is null)
            {
                return;
            }

            var title = Clean(raw);
            if (title.Length >= 2 && !Generic.Contains(title) && !list.Exists(c => MatchKey(c.Title) == MatchKey(title)))
            {
                list.Add(new TitleCandidate(title, origin));
            }
        }

        if (productName is { Length: > 2 } product && !LooksLikeAbbreviation(Clean(product)))
        {
            Add(product, TitleOrigin.ProductName);
        }

        var parts = exePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var file = parts.Length > 0 ? parts[^1] : exePath;
        var dot = file.LastIndexOf('.');
        var fileName = dot > 0 ? file[..dot] : file;
        var fileIsAbbreviation = LooksLikeAbbreviation(Clean(fileName)) || Generic.Contains(Clean(fileName));
        if (!fileIsAbbreviation)
        {
            Add(fileName, TitleOrigin.FileName);
        }

        var folders = 0;
        for (var i = parts.Length - 2; i >= 0 && folders < 3; i--)
        {
            if (parts[i].EndsWith(':') || Generic.Contains(Clean(parts[i])))
            {
                continue;
            }

            Add(parts[i], TitleOrigin.Folder);
            folders++;
        }

        if (fileIsAbbreviation)
        {
            Add(fileName, TitleOrigin.FileName);
        }

        return list.Count > 0 ? list : [new TitleCandidate(fileName, TitleOrigin.FileName)];
    }

    /// <summary>
    /// Rydder et navn: understrek blir mellomrom; tall etter bindestrek på slutten («-273», men ikke «Mega Man 11»), versjoner («v3148», «1.0.2»), innhold i
    /// klammer og varemerketegn fjernes.
    /// </summary>
    public static string Clean(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = name.Replace('_', ' ').Replace('™', ' ').Replace('®', ' ').Replace('©', ' ');
        text = Brackets().Replace(text, " ");
        text = Version().Replace(text, " ");
        text = TrailingNumber().Replace(text, "");
        return Spaces().Replace(text, " ").Trim(' ', '-', '.');
    }

    /// <summary>Kort, uten mellomrom og uten vokaler eller med store bokstaver: «ACC», «GTA5», «ffxv».</summary>
    public static bool LooksLikeAbbreviation(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var letters = name.Where(char.IsLetter).ToArray();
        return name.Length <= 3
            || (name.Any(char.IsDigit) && name.Length <= 5)
            || (letters.Length > 0 && letters.All(char.IsUpper) && name.Length <= 6)
            || !letters.Any(c => "aeiouyæøåAEIOUYÆØÅ".Contains(c, StringComparison.Ordinal));
    }

    /// <summary>
    /// Nøkkel for tilgivende sammenligning: små bokstaver, uten apostrofer, varemerketegn og tegnsetting, «&amp;» som
    /// «and», og romertall som tall («II» = «2»). Like nøkler betyr samme tittel; alle ordene må stemme.
    /// </summary>
    public static string MatchKey(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var text = title.ToLower(CultureInfo.InvariantCulture)
            .Replace("&", " and ", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("’", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Replace("´", "", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Roman);
        return string.Join(' ', words);
    }

    private static string Roman(string word) => word switch
    {
        "ii" => "2",
        "iii" => "3",
        "iv" => "4",
        "v" => "5",
        "vi" => "6",
        "vii" => "7",
        "viii" => "8",
        "ix" => "9",
        "x" => "10",
        _ => word,
    };

    [GeneratedRegex(@"[\[\(\{][^\]\)\}]*[\]\)\}]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"(?i)\bv?\d+(\.\d+){1,3}\b|\bv\d+\b")]
    private static partial Regex Version();

    [GeneratedRegex(@"\s*[\-_]\d+$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
