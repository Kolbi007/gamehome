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
    private const string Details = "https://store.steampowered.com/api/appdetails?l=norwegian&cc=no&appids=";
    private const string Cdn ="https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    /// <summary>Steam-appid for tittelen, eller null hvis Steam ikke har et spill med akkurat den tittelen.</summary>
    public async Task<int?> FindAppIdAsync(string title, CancellationToken ct = default) =>
        (await FindMatchAsync(title, ct).ConfigureAwait(false))?.Id;

    /// <summary>
    /// Velger bare et treff med samme tittel, sammenlignet tilgivende (<see cref="TitleGuess.MatchKey"/>): apostrofer,
    /// varemerketegn og tegnsetting teller ikke, og romertall er lik tall. Alle ordene må stemme.
    /// </summary>
    public static int? PickAppId(string json, string title) => PickMatch(json, title)?.Id;

    public static SteamSearchHit? PickMatch(string json, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var key = TitleGuess.MatchKey(title);
        return ParseSearch(json).FirstOrDefault(h => TitleGuess.MatchKey(h.Name) == key);
    }

    /// <summary>Søker etter tittelen og gir treffet med samme tittel, eller null.</summary>
    public async Task<SteamSearchHit?> FindMatchAsync(string title, CancellationToken ct = default) =>
        PickMatch(await SearchJsonAsync(title, ct).ConfigureAwait(false), title);

    /// <summary>De beste treffene for et fritt søk (for «Finn riktig spill»).</summary>
    public async Task<IReadOnlyList<SteamSearchHit>> SearchAsync(string term, int max, CancellationToken ct = default) =>
        [.. ParseSearch(await SearchJsonAsync(term, ct).ConfigureAwait(false)).Take(max)];

    public static IReadOnlyList<SteamSearchHit> ParseSearch(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize(json, SteamStoreJson.Default.StoreSearchResult);
            return [.. (result?.Items ?? []).Where(i => i.Type == "app" && !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new SteamSearchHit(i.Id, CleanName(i.Name!), i.TinyImage))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Steams navn uten varemerketegn, for visning.</summary>
    public static string CleanName(string name) =>
        string.Join(' ', name.Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal)
            .Replace("©", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private async Task<string> SearchJsonAsync(string term, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            _lastCall = DateTime.UtcNow;
            return await http.GetStringAsync(new Uri(Search + Uri.EscapeDataString(term)), ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Beskrivelse, utvikler, utgivelse, sjangre og Metacritic-score fra butikkens appdetails, på norsk der Steam har det.
    /// Null hvis Steam ikke har siden.
    /// </summary>
    public async Task<GameDetails?> GetDetailsAsync(int appId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            _lastCall = DateTime.UtcNow;
            var id = appId.ToString(CultureInfo.InvariantCulture);
            var json = await http.GetStringAsync(new Uri(Details + id), ct).ConfigureAwait(false);
            return ParseDetails(json, appId, DateTime.UtcNow);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Leser svaret fra appdetails: <c>{"appid": {"success": true, "data": {...}}}</c>.</summary>
    public static GameDetails? ParseDetails(string json, int appId, DateTime fetchedUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry)
                || !entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            static string? Text(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            static string? First(JsonElement element, string name) =>
                element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
                    && list[0].ValueKind == JsonValueKind.String ? list[0].GetString() : null;

            var genres = new List<string>();
            if (data.TryGetProperty("genres", out var genreList) && genreList.ValueKind == JsonValueKind.Array)
            {
                genres.AddRange(genreList.EnumerateArray().Select(g => Text(g, "description")).OfType<string>().Take(3));
            }

            int? metacritic = data.TryGetProperty("metacritic", out var meta) && meta.TryGetProperty("score", out var score)
                && score.TryGetInt32(out var value) ? value : null;
            var released = data.TryGetProperty("release_date", out var release) ? Text(release, "date") : null;

            return new GameDetails(
                GameDetailsStore.PlainText(Text(data, "short_description")),
                First(data, "developers"),
                First(data, "publishers"),
                string.IsNullOrWhiteSpace(released) ? null : released,
                genres,
                metacritic,
                "Steam Store",
                fetchedUtc);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Skjermbilder fra butikksiden (lite forhåndsbilde og full størrelse), til valg av bakgrunn.</summary>
    public async Task<IReadOnlyList<(string Preview, string Full)>> GetScreenshotsAsync(int appId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            _lastCall = DateTime.UtcNow;
            var id = appId.ToString(CultureInfo.InvariantCulture);
            var json = await http.GetStringAsync(new Uri($"https://store.steampowered.com/api/appdetails?filters=screenshots&appids={id}"), ct).ConfigureAwait(false);
            return ParseScreenshots(json, appId);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static IReadOnlyList<(string Preview, string Full)> ParseScreenshots(string json, int appId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry)
                || !entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("screenshots", out var shots) || shots.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. shots.EnumerateArray()
                .Select(s => (Preview: s.TryGetProperty("path_thumbnail", out var t) ? t.GetString() : null,
                              Full: s.TryGetProperty("path_full", out var f) ? f.GetString() : null))
                .Where(s => s.Preview is not null && s.Full is not null)
                .Select(s => (s.Preview!, s.Full!))
                .Take(8)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Flere offisielle butikkbilder enn standardsettet: bred «header» og kapsel som alternative omslag.</summary>
    public static IReadOnlyList<string> ExtraCoverUrls(int appId)
    {
        var root = Cdn + appId.ToString(CultureInfo.InvariantCulture) + "/";
        return [root + "library_600x900_2x.jpg", root + "header.jpg", root + "capsule_616x353.jpg"];
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
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tiny_image")] string? TinyImage);

/// <summary>Et treff i Steams butikksøk: appid, navn (uten varemerketegn) og et lite bilde.</summary>
public sealed record SteamSearchHit(int Id, string Name, string? TinyImage);

[JsonSerializable(typeof(StoreSearchResult))]
internal sealed partial class SteamStoreJson : JsonSerializerContext;
