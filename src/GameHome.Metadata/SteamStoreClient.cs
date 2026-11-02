// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHome.Core.Library;

namespace GameHome.Metadata;

/// <summary>Adresser til et spills butikkbilder på Steams offentlige CDN.</summary>
public sealed record SteamImageUrls(string Cover, string Hero, string Logo);

/// <summary>
/// Steam Store (uoffisiell, på som standard etter valg fra produkteier, ADR 0003): finner Steam-appid fra en tittel med
/// butikksøket, og gir adressene til de offentlige butikkbildene. Høyst én forespørsel i sekundet; aldri mens et spill
/// kjører (det styrer kalleren).
/// </summary>
public sealed class SteamStoreClient(HttpClient http)
{
    private const string Search = "https://store.steampowered.com/api/storesearch/?l=english&cc=us&term=";
    private const string Cdn = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    /// <summary>Steam-appid for tittelen, eller null hvis Steam ikke har et spill med akkurat den tittelen.</summary>
    public async Task<int?> FindAppIdAsync(string title, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            _lastCall = DateTime.UtcNow;
            var json = await http.GetStringAsync(new Uri(Search + Uri.EscapeDataString(title)), ct).ConfigureAwait(false);
            return PickAppId(json, title);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Velger bare et treff med samme tittel (uten store bokstaver, varemerketegn og ekstra mellomrom).</summary>
    public static int? PickAppId(string json, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        StoreSearchResult? result;
        try
        {
            result = JsonSerializer.Deserialize(json, SteamStoreJson.Default.StoreSearchResult);
        }
        catch (JsonException)
        {
            return null;
        }

        var key = GameLibrary.TitleKey(title);
        return result?.Items?.FirstOrDefault(i => i.Type == "app" && i.Name is { } name && GameLibrary.TitleKey(name) == key)?.Id;
    }

    public static SteamImageUrls ImageUrls(int appId)
    {
        var root = Cdn + appId.ToString(CultureInfo.InvariantCulture) + "/";
        return new SteamImageUrls(root + "library_600x900_2x.jpg", root + "library_hero.jpg", root + "logo.png");
    }
}

internal sealed record StoreSearchResult(
    [property: JsonPropertyName("items")] IReadOnlyList<StoreSearchItem>? Items);

internal sealed record StoreSearchItem(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("id")] int Id);

[JsonSerializable(typeof(StoreSearchResult))]
internal sealed partial class SteamStoreJson : JsonSerializerContext;
