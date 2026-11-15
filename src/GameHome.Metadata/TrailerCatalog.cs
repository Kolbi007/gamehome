// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Metadata;

public static class TrailerCatalog
{
    public const string SonicCombinedTitle = "Sonic the Hedgehog 3 & Knuckles";

    public static string SearchTitle(string title) => Normalize(title) switch
    {
        "sonic3andknuckles" or "sonicandknucklessonicthehedgehog3" => SonicCombinedTitle,
        "sonic3" => "Sonic the Hedgehog 3",
        _ => title,
    };

    public static bool IsTitleMatch(string catalogName, string searchTitle) =>
        Normalize(catalogName) == Normalize(searchTitle);

    public static string? KnownVideoId(string searchTitle) =>
        IsTitleMatch(searchTitle, SonicCombinedTitle)
            // https://www.igdb.com/games/sonic-the-hedgehog-3-and-knuckles/presskit
            ? "8pLqpfwedGc"
            : null;

    private static string Normalize(string title)
    {
        var normalized = title.Replace("&", " and ", StringComparison.Ordinal);
        return new string([.. normalized.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
    }
}
