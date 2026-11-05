// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;

namespace GameHome.Sources.Updates;

/// <summary>En utgivelse av GameHome på GitHub med pakken for denne arkitekturen.</summary>
public sealed record AvailableRelease(Version Version, string Tag, string AssetName, Uri AssetUrl, long Size);

/// <summary>
/// Leser svaret fra GitHubs offentlige API for utgivelser (<c>GET /repos/{eier}/{repo}/releases</c>) og finner den
/// nyeste utgivelsen med en MSIX for gitt arkitektur. Taggen <c>vX.Y.Z</c> gir pakkeversjonen X.Y.Z.0 (se ci.yml).
/// </summary>
public static class ReleaseFeed
{
    public static AvailableRelease? Newest(string json, string architecture)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(architecture);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        AvailableRelease? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!release.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag
                || ParseTag(tag) is not { } version)
            {
                continue;
            }

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name is null || url is null
                    || !name.EndsWith($"_{architecture}.msix", StringComparison.OrdinalIgnoreCase)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                if (best is null || version > best.Version)
                {
                    best = new AvailableRelease(version, tag, name, uri, size);
                }
            }
        }

        return best;
    }

    /// <summary>«v0.2.15» → 0.2.15.0. Andre tagger gir null.</summary>
    public static Version? ParseTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var text = tag.StartsWith('v') ? tag[1..] : tag;
        var parts = text.Split('.');
        return parts.Length == 3 && parts.All(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            ? new Version(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), 0)
            : null;
    }
}
