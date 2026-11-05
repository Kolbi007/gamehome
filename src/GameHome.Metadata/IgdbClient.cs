// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameHome.Core.Metadata;

namespace GameHome.Metadata;

/// <summary>
/// Et spill fra IGDB. Bildene er <c>image_id</c>-er; bygg adresser med <see cref="IgdbClient.ImageUrl"/>.
/// Vurderingene er 0–100: <see cref="TotalRating"/> blander brukere og kritikere, <see cref="AggregatedRating"/> er bare
/// kritikere.
/// </summary>
public sealed record IgdbGame(
    long Id,
    string Name,
    string? Summary,
    IReadOnlyList<string> Genres,
    DateTime? FirstReleaseUtc,
    double? TotalRating,
    int? TotalRatingCount,
    double? AggregatedRating,
    int? AggregatedRatingCount,
    string? CoverImageId,
    IReadOnlyList<string> ArtworkImageIds,
    IReadOnlyList<string> ScreenshotImageIds,
    string? Url);

/// <summary>Gjennomsnittlig tid å fullføre fra IGDB, i sekunder, og antall innsendinger. Null betyr at tallet mangler.</summary>
public sealed record IgdbTimeToBeat(long GameId, int? HastilySeconds, int? NormallySeconds, int? CompletelySeconds, int? Count);

/// <summary>
/// IGDB (offisielt, Twitch-nøkkel fra brukeren, ADR 0003): søk, oppslag fra Steam-appid, detaljer, bilder, vurderinger
/// og tid å fullføre.
/// <para>
/// Kilde: https://api-docs.igdb.com (Account Creation, Authentication, Requests, Rate Limits, Game, External Game,
/// Game Time To Beat, Images, Expander, Filters, Search). Tokenet hentes med client credentials fra
/// <c>https://id.twitch.tv/oauth2/token</c>; parameterne sendes i en x-www-form-urlencoded kropp slik Twitch
/// dokumenterer (https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/), så hemmeligheten aldri står i en
/// adresse. Svaret har <c>access_token</c> og <c>expires_in</c> (sekunder). Maks 4 forespørsler i sekundet
/// (<see cref="KnownProviders.Igdb"/>); for mange gir 429.
/// </para>
/// <para>
/// Ikke verifisert i dokumentasjonen: at Steam har ID 1 i <c>external_game_sources</c>. Dokumentasjonen viser
/// <c>steam = 1</c> i den utfasede <c>category</c>-enumen og sier at det nye endepunktet erstatter enum-verdiene, men
/// lister ikke ID-ene. Utvikler og utgiver er utelatt fordi navnet ligger to nivåer ned
/// (<c>involved_companies.company.name</c>), og dokumentasjonen viser bare utvidelse ett nivå ned.
/// </para>
/// <para>Klient-ID, hemmelighet og token kommer aldri med i unntak, logg eller returverdier.</para>
/// </summary>
public sealed class IgdbClient : IDisposable
{
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string Api = "https://api.igdb.com/v4/";
    private const string ImageBase = "https://images.igdb.com/igdb/image/upload/t_";

    /// <summary>Steam i <c>external_game_sources</c> (se merknaden over).</summary>
    private const int SteamSource = 1;

    private const string GameFields =
        "fields name,summary,genres.name,first_release_date,total_rating,total_rating_count,aggregated_rating,"
        + "aggregated_rating_count,cover.image_id,artworks.image_id,screenshots.image_id,url;";

    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static readonly Queue<DateTime> RecentCalls = new();

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc;

    public IgdbClient(HttpClient http, string clientId, string clientSecret, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _time = time ?? TimeProvider.System;
    }

    public void Dispose() => _tokenGate.Dispose();

    /// <summary>Søk på navn, beste treff først. Tom liste ved feil.</summary>
    public async Task<IReadOnlyList<IgdbGame>> SearchAsync(string name, int max = 10, CancellationToken ct = default)
    {
        var query = SearchQuery(name, max);
        var json = await PostAsync("games", query, ct).ConfigureAwait(false);
        return json is null ? [] : ParseGames(json);
    }

    /// <summary>Spillet med denne IGDB-ID-en, eller null.</summary>
    public async Task<IgdbGame?> GetGameAsync(long gameId, CancellationToken ct = default)
    {
        var json = await PostAsync("games", GameFields + " where id = " + gameId.ToString(CultureInfo.InvariantCulture) + ";", ct)
            .ConfigureAwait(false);
        var games = json is null ? [] : ParseGames(json);
        return games.Count > 0 ? games[0] : null;
    }

    /// <summary>IGDB-spillet som hører til en Steam-appid (via <c>external_games</c>), eller null.</summary>
    public async Task<IgdbGame?> FindBySteamAppIdAsync(int steamAppId, CancellationToken ct = default)
    {
        var json = await PostAsync("external_games", SteamLookupQuery(steamAppId), ct).ConfigureAwait(false);
        return json is not null && ParseExternalGameId(json) is { } gameId
            ? await GetGameAsync(gameId, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>Tid å fullføre fra <c>game_time_to_beats</c>, eller null.</summary>
    public async Task<IgdbTimeToBeat?> GetTimeToBeatAsync(long gameId, CancellationToken ct = default)
    {
        var query = "fields game_id,hastily,normally,completely,count; where game_id = "
            + gameId.ToString(CultureInfo.InvariantCulture) + ";";
        var json = await PostAsync("game_time_to_beats", query, ct).ConfigureAwait(false);
        return json is null ? null : ParseTimeToBeat(json);
    }

    /// <summary>
    /// Bildeadresse: <c>https://images.igdb.com/igdb/image/upload/t_{size}/{image_id}.jpg</c>. Dokumenterte størrelser er
    /// blant annet <c>cover_big</c> (264x374), <c>screenshot_huge</c> (1280x720), <c>720p</c> og <c>1080p</c>; <c>_2x</c> gir
    /// dobbel oppløsning.
    /// </summary>
    public static string ImageUrl(string imageId, string size = "cover_big")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(size);
        return ImageBase + size + "/" + imageId + ".jpg";
    }

    /// <summary>Apicalypse-spørringen for et navnesøk. Anførselstegn og omvendt skråstrek fjernes fra navnet.</summary>
    public static string SearchQuery(string name, int max = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var clean = new string([.. name.Where(c => c is not '"' and not '\\' && !char.IsControl(c))]).Trim();
        return $"search \"{clean}\"; {GameFields} limit {Math.Clamp(max, 1, 500).ToString(CultureInfo.InvariantCulture)};";
    }

    /// <summary>Apicalypse-spørringen som finner IGDB-spillet for en Steam-appid.</summary>
    public static string SteamLookupQuery(int steamAppId) =>
        $"fields game,uid,external_game_source; where uid = \"{steamAppId.ToString(CultureInfo.InvariantCulture)}\" "
        + $"& external_game_source = {SteamSource.ToString(CultureInfo.InvariantCulture)}; limit 1;";

    /// <summary>Leser en liste fra <c>/games</c> med feltene i <see cref="SearchQuery"/>, utvidet med <c>.name</c>/<c>.image_id</c>.</summary>
    public static IReadOnlyList<IgdbGame> ParseGames(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. document.RootElement.EnumerateArray().Select(ReadGame).OfType<IgdbGame>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>IGDB-spill-ID-en fra et <c>/external_games</c>-svar: <c>[{"id", "game", "uid"}]</c>.</summary>
    public static long? ParseExternalGameId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(e => JsonRead.Long(e, "game")).FirstOrDefault(id => id is not null)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Leser <c>[{"game_id", "hastily", "normally", "completely", "count"}]</c> (sekunder).</summary>
    public static IgdbTimeToBeat? ParseTimeToBeat(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (JsonRead.Long(entry, "game_id") is { } gameId)
                {
                    return new IgdbTimeToBeat(gameId, Positive(JsonRead.Int(entry, "hastily")),
                        Positive(JsonRead.Int(entry, "normally")), Positive(JsonRead.Int(entry, "completely")),
                        JsonRead.Int(entry, "count"));
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Leser tokensvaret <c>{"access_token", "expires_in", "token_type"}</c>. Gir tokenet og når det går ut, eller null.
    /// Tokenet er hemmelig; ikke logg det.
    /// </summary>
    public static string? ParseToken(string json, DateTime nowUtc, out DateTime expiresUtc)
    {
        expiresUtc = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var token = JsonRead.Text(document.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token) || JsonRead.Long(document.RootElement, "expires_in") is not { } seconds || seconds <= 0)
            {
                return null;
            }

            expiresUtc = nowUtc.AddSeconds(Math.Min(seconds, TimeSpan.FromDays(3650).TotalSeconds));
            return token;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// IGDB-spillet som <see cref="GameDetails"/>. Utgivelsesdatoen skrives som <c>yyyy-MM-dd</c>. Utvikler og utgiver er
    /// tomme (se klassebeskrivelsen); <see cref="GameDetails.Metacritic"/> fylles ikke, siden IGDB ikke er Metacritic.
    /// </summary>
    public static GameDetails ToDetails(IgdbGame game, DateTime fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new GameDetails(
            string.IsNullOrWhiteSpace(game.Summary) ? null : game.Summary.Trim(),
            null,
            null,
            game.FirstReleaseUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. game.Genres.Take(3)],
            null,
            "IGDB",
            fetchedUtc);
    }

    /// <summary>Vurderingene som felles <see cref="Rating"/>: kritikere fra <c>aggregated_rating</c>.</summary>
    public static IReadOnlyList<Rating> ToRatings(IgdbGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.AggregatedRating is { } critic
            ? [new Rating("igdb", RatingKind.Critic, (int)Math.Round(Math.Clamp(critic, 0, 100)), game.AggregatedRatingCount, game.Url)]
            : [];
    }

    /// <summary>
    /// Tid å fullføre som felles <see cref="PlayTime"/>: <c>hastily</c> er hovedhistorien, <c>normally</c> med ekstra,
    /// <c>completely</c> alt (slik IGDB beskriver feltene).
    /// </summary>
    public static PlayTime ToPlayTime(IgdbTimeToBeat time, string? url = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        static TimeSpan? Span(int? seconds) => seconds is { } s ? TimeSpan.FromSeconds(s) : null;
        return new PlayTime("igdb", Span(time.HastilySeconds), Span(time.NormallySeconds), Span(time.CompletelySeconds), url);
    }

    private static int? Positive(int? value) => value is > 0 ? value : null;

    private static IgdbGame? ReadGame(JsonElement game)
    {
        var name = JsonRead.Text(game, "name");
        if (JsonRead.Long(game, "id") is not { } id || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        static IReadOnlyList<string> Names(JsonElement element, string list, string field) =>
            [.. JsonRead.Array(element, list).Select(e => JsonRead.Text(e, field)).OfType<string>().Where(s => s.Length > 0)];

        var cover = game.TryGetProperty("cover", out var c) ? JsonRead.Text(c, "image_id") : null;
        return new IgdbGame(
            id,
            name,
            JsonRead.Text(game, "summary"),
            Names(game, "genres", "name"),
            JsonRead.UnixSeconds(game, "first_release_date"),
            JsonRead.Double(game, "total_rating"),
            JsonRead.Int(game, "total_rating_count"),
            JsonRead.Double(game, "aggregated_rating"),
            JsonRead.Int(game, "aggregated_rating_count"),
            string.IsNullOrWhiteSpace(cover) ? null : cover,
            Names(game, "artworks", "image_id"),
            Names(game, "screenshots", "image_id"),
            JsonRead.Text(game, "url"));
    }

    /// <summary>POST med Apicalypse-kropp. Null ved feil; et avvist token (401) hentes på nytt neste gang.</summary>
    private async Task<string?> PostAsync(string endpoint, string body, CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct).ConfigureAwait(false);
            if (token is null)
            {
                return null;
            }

            await WaitForRateLimitAsync(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Api + endpoint));
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _token = null;
            }

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

    /// <summary>Gyldig token fra cachen, eller et nytt fra Twitch når det gamle går ut innen ett minutt.</summary>
    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (_token is not null && now < _tokenExpiresUtc - TimeSpan.FromMinutes(1))
            {
                return _token;
            }

            _token = null;
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenUrl))
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("client_id", _clientId),
                    new KeyValuePair<string, string>("client_secret", _clientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                ]),
            };
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _token = ParseToken(json, now, out _tokenExpiresUtc);
            return _token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Høyst <see cref="KnownProviders.Igdb"/>.MaxRequestsPerSecond forespørsler i et glidende vindu på ett sekund.</summary>
    private async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        var max = (int)(KnownProviders.Igdb.MaxRequestsPerSecond ?? 4);
        await RateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (RecentCalls.Count >= max)
            {
                var wait = RecentCalls.Dequeue() + TimeSpan.FromSeconds(1) - _time.GetUtcNow().UtcDateTime;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, ct).ConfigureAwait(false);
                }
            }

            RecentCalls.Enqueue(_time.GetUtcNow().UtcDateTime);
        }
        finally
        {
            RateGate.Release();
        }
    }
}
