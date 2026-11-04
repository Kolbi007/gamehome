// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GameHome.Metadata;

/// <summary>Et spill hos SteamGridDB. <see cref="Id"/> er SteamGridDBs egen spill-ID, ikke Steam-appid.</summary>
public sealed record SteamGridDbGame(int Id, string Name, bool Verified, DateTime? ReleaseDateUtc);

/// <summary>
/// Et bilde fra SteamGridDB: full adresse, forhåndsbilde, størrelse i piksler, stil og merkene «nsfw» og «humor».
/// </summary>
public sealed record SteamGridDbImage(
    int Id,
    string Url,
    string? Thumb,
    int? Width,
    int? Height,
    string? Style,
    bool Nsfw,
    bool Humor);

/// <summary>Formen på et omslag (grid).</summary>
public enum SteamGridDbGridShape
{
    /// <summary>Stående omslag, 600x900.</summary>
    Portrait,

    /// <summary>Liggende omslag, 920x430 og 460x215.</summary>
    Landscape,
}

/// <summary>
/// SteamGridDB API v2 (offisielt, nøkkel fra brukeren, ADR 0003): søk, oppslag fra Steam-appid, omslag (grids),
/// bakgrunner (heroes) og logoer.
/// <para>
/// Kilde: OpenAPI-beskrivelsen bak https://www.steamgriddb.com/api/v2 (https://www.steamgriddb.com/static/openapi.yml):
/// basisadresse, <c>Authorization: Bearer</c>, stiene, parameterne <c>dimensions</c>, <c>nsfw</c> og <c>humor</c>, og
/// feltene <c>success</c>, <c>data</c>, <c>id</c>, <c>name</c>, <c>verified</c>, <c>url</c>, <c>thumb</c>, <c>style</c>
/// og <c>tags</c>. Feltene <c>width</c>, <c>height</c> og <c>release_date</c> (Unix-tid) står ikke i OpenAPI-filen, men
/// i SteamGridDBs offisielle Node-klient (github.com/SteamGridDB/node-steamgriddb, <c>SGDBImage</c>/<c>SGDBGame</c>).
/// Merkene «nsfw» og «humor» leses fra <c>tags</c> (verdiene står i parameteren <c>oneoftag</c>); de udokumenterte
/// boolske feltene <c>nsfw</c>/<c>humor</c> leses også hvis de finnes.
/// </para>
/// <para>Nøkkelen sendes bare i headeren og kommer aldri med i unntak, logg eller returverdier.</para>
/// </summary>
public sealed class SteamGridDbClient
{
    private const string Base = "https://www.steamgriddb.com/api/v2/";
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamGridDbClient(HttpClient http, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _apiKey = apiKey;
    }

    /// <summary>Søk etter spill på navn. Tom liste ved feil.</summary>
    public async Task<IReadOnlyList<SteamGridDbGame>> SearchAsync(string term, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        var json = await GetAsync("search/autocomplete/" + Uri.EscapeDataString(term), ct).ConfigureAwait(false);
        return json is null ? [] : ParseGames(json);
    }

    /// <summary>SteamGridDB-spillet for en Steam-appid, eller null.</summary>
    public async Task<SteamGridDbGame?> GetGameBySteamAppIdAsync(int steamAppId, CancellationToken ct = default)
    {
        var json = await GetAsync("games/steam/" + steamAppId.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        return json is null ? null : ParseGame(json);
    }

    /// <summary>Omslag i valgt form. NSFW og humor er utelatt med mindre <paramref name="includeNsfwAndHumor"/> er satt.</summary>
    public Task<IReadOnlyList<SteamGridDbImage>> GetGridsAsync(
        int gameId, SteamGridDbGridShape shape, bool includeNsfwAndHumor = false, CancellationToken ct = default) =>
        GetImagesAsync("grids", gameId, "&dimensions=" + GridDimensions(shape), includeNsfwAndHumor, ct);

    /// <summary>Bakgrunner (heroes).</summary>
    public Task<IReadOnlyList<SteamGridDbImage>> GetHeroesAsync(
        int gameId, bool includeNsfwAndHumor = false, CancellationToken ct = default) =>
        GetImagesAsync("heroes", gameId, "", includeNsfwAndHumor, ct);

    /// <summary>Logoer.</summary>
    public Task<IReadOnlyList<SteamGridDbImage>> GetLogosAsync(
        int gameId, bool includeNsfwAndHumor = false, CancellationToken ct = default) =>
        GetImagesAsync("logos", gameId, "", includeNsfwAndHumor, ct);

    /// <summary>Verdien til <c>dimensions</c> for formen (kommaseparert, som dokumentert).</summary>
    public static string GridDimensions(SteamGridDbGridShape shape) => shape switch
    {
        SteamGridDbGridShape.Portrait => "600x900",
        SteamGridDbGridShape.Landscape => "920x430,460x215",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Leser søkesvaret: <c>{"success": true, "data": [{"id", "name", "types", "verified", "release_date"}]}</c>.</summary>
    public static IReadOnlyList<SteamGridDbGame> ParseGames(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!Succeeded(document.RootElement))
            {
                return [];
            }

            return [.. JsonRead.Array(document.RootElement, "data").Select(ReadGame).OfType<SteamGridDbGame>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Leser ett spill: <c>{"success": true, "data": {"id", "name", ...}}</c>.</summary>
    public static SteamGridDbGame? ParseGame(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Succeeded(document.RootElement) && document.RootElement.TryGetProperty("data", out var data)
                ? ReadGame(data)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Leser en bildeliste: <c>{"success": true, "data": [{"id", "url", "thumb", "width", "height", "style", "tags"}]}</c>.
    /// Bilder merket nsfw eller humor hoppes over med mindre <paramref name="includeNsfwAndHumor"/> er satt.
    /// </summary>
    public static IReadOnlyList<SteamGridDbImage> ParseImages(string json, bool includeNsfwAndHumor = false)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!Succeeded(document.RootElement))
            {
                return [];
            }

            return [.. JsonRead.Array(document.RootElement, "data")
                .Select(ReadImage)
                .OfType<SteamGridDbImage>()
                .Where(i => includeNsfwAndHumor || (!i.Nsfw && !i.Humor))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool Succeeded(JsonElement root) => JsonRead.Bool(root, "success");

    private static SteamGridDbGame? ReadGame(JsonElement game)
    {
        var name = JsonRead.Text(game, "name");
        return JsonRead.Int(game, "id") is { } id && !string.IsNullOrWhiteSpace(name)
            ? new SteamGridDbGame(id, name, JsonRead.Bool(game, "verified"), JsonRead.UnixSeconds(game, "release_date"))
            : null;
    }

    private static SteamGridDbImage? ReadImage(JsonElement image)
    {
        var url = JsonRead.Text(image, "url");
        if (JsonRead.Int(image, "id") is not { } id || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var tags = JsonRead.Array(image, "tags").Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()).ToList();
        var nsfw = JsonRead.Bool(image, "nsfw") || tags.Contains("nsfw", StringComparer.OrdinalIgnoreCase);
        var humor = JsonRead.Bool(image, "humor") || tags.Contains("humor", StringComparer.OrdinalIgnoreCase);

        // OpenAPI-filen sier streng, Node-klienten sier liste; godta begge.
        var style = JsonRead.Text(image, "style")
            ?? JsonRead.Array(image, "style").Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()).FirstOrDefault();

        return new SteamGridDbImage(id, url, JsonRead.Text(image, "thumb"), JsonRead.Int(image, "width"),
            JsonRead.Int(image, "height"), style, nsfw, humor);
    }

    private async Task<IReadOnlyList<SteamGridDbImage>> GetImagesAsync(
        string kind, int gameId, string extraQuery, bool includeNsfwAndHumor, CancellationToken ct)
    {
        var filter = includeNsfwAndHumor ? "any" : "false";
        var path = $"{kind}/game/{gameId.ToString(CultureInfo.InvariantCulture)}?nsfw={filter}&humor={filter}{extraQuery}";
        var json = await GetAsync(path, ct).ConfigureAwait(false);
        return json is null ? [] : ParseImages(json, includeNsfwAndHumor);
    }

    /// <summary>GET med nøkkelen i headeren. Null ved nettverksfeil eller feilkode; avbrudd fra kalleren kastes videre.</summary>
    private async Task<string?> GetAsync(string relative, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Base + relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // tidsavbrudd i HttpClient
        }
    }
}
