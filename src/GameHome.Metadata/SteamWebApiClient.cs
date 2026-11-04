// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Text.Json;

namespace GameHome.Metadata;

/// <summary>
/// Et spill i brukerens Steam-bibliotek. <see cref="PlaytimeForeverMinutes"/> er total spilletid i minutter.
/// <see cref="IconHash"/> er filnavnet til ikonet (<c>img_icon_url</c>), ikke en adresse; se <see cref="IconUrl"/>.
/// </summary>
public sealed record SteamOwnedGame(int AppId, string? Name, int PlaytimeForeverMinutes, string? IconHash)
{
    /// <summary>Adressen til ikonet, eller null hvis Steam ikke ga et.</summary>
    public string? IconUrl => string.IsNullOrWhiteSpace(IconHash)
        ? null
        : $"https://media.steampowered.com/steamcommunity/public/images/apps/{AppId.ToString(CultureInfo.InvariantCulture)}/{IconHash}.jpg";
}

/// <summary>
/// Steam Web API (offisielt, nøkkel fra brukeren): spillene en bruker eier, og Steam-ID fra et profilnavn.
/// <para>
/// Kilder: https://partner.steamgames.com/doc/webapi/IPlayerService (GetOwnedGames: <c>key</c>, <c>steamid</c>,
/// <c>include_appinfo</c>, <c>include_played_free_games</c>), https://partner.steamgames.com/doc/webapi/ISteamUser
/// (ResolveVanityURL: <c>key</c>, <c>vanityurl</c>) og https://partner.steamgames.com/doc/webapi_overview (offentlig
/// vert <c>api.steampowered.com</c>, <c>key</c> og <c>format</c> som egne parametere). Partnersidene beskriver ikke
/// svaret. Svarfeltene (<c>response.games[].appid/name/playtime_forever/img_icon_url</c>, <c>response.success/steamid</c>,
/// success 1 = treff, 42 = ingen treff) og ikonadressen er hentet fra Steam Web API-referansen på
/// wiki.teamfortress.com (WebAPI/GetOwnedGames og WebAPI/ResolveVanityURL), som gjengir Valves dokumentasjon.
/// </para>
/// <para>
/// Steam tar nøkkelen bare i adressen. Derfor kommer adressen aldri med i unntak eller returverdier: alle nettverksfeil
/// gir null. Den injiserte <see cref="HttpClient"/> må ikke logge forespørselsadresser.
/// </para>
/// </summary>
public sealed class SteamWebApiClient
{
    private const string Base = "https://api.steampowered.com/";
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamWebApiClient(HttpClient http, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Spillene brukeren eier, med navn og ikon, og med gratisspill brukeren har spilt. Null ved feil; tom liste hvis
    /// profilen ikke viser spillene sine.
    /// </summary>
    public async Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(ulong steamId, CancellationToken ct = default)
    {
        var query = "IPlayerService/GetOwnedGames/v1/?format=json&include_appinfo=1&include_played_free_games=1&steamid="
            + steamId.ToString(CultureInfo.InvariantCulture);
        var json = await GetAsync(query, ct).ConfigureAwait(false);
        return json is null ? null : ParseOwnedGames(json);
    }

    /// <summary>64-bits Steam-ID for et profilnavn eller en profiladresse (<c>steamcommunity.com/id/navn</c>), eller null.</summary>
    public async Task<ulong?> ResolveVanityUrlAsync(string profileName, CancellationToken ct = default)
    {
        var name = VanityName(profileName);
        if (name is null)
        {
            return null;
        }

        var json = await GetAsync("ISteamUser/ResolveVanityURL/v1/?format=json&vanityurl=" + Uri.EscapeDataString(name), ct)
            .ConfigureAwait(false);
        return json is null ? null : ParseVanity(json);
    }

    /// <summary>Leser <c>{"response": {"game_count": n, "games": [{"appid", "name", "playtime_forever", "img_icon_url"}]}}</c>.</summary>
    public static IReadOnlyList<SteamOwnedGame>? ParseOwnedGames(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return [.. JsonRead.Array(response, "games")
                .Where(g => JsonRead.Int(g, "appid") is not null)
                .Select(g => new SteamOwnedGame(
                    JsonRead.Int(g, "appid")!.Value,
                    JsonRead.Text(g, "name"),
                    JsonRead.Int(g, "playtime_forever") ?? 0,
                    JsonRead.Text(g, "img_icon_url") is { Length: > 0 } icon ? icon : null))];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Leser <c>{"response": {"success": 1, "steamid": "7656..."}}</c>. Alt annet enn success 1 gir null.</summary>
    public static ulong? ParseVanity(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("response", out var response)
                && JsonRead.Int(response, "success") == 1
                && ulong.TryParse(JsonRead.Text(response, "steamid"), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Profilnavnet fra et navn eller en adresse som <c>https://steamcommunity.com/id/navn/</c>.</summary>
    public static string? VanityName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim().TrimEnd('/');
        var marker = text.LastIndexOf("/id/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            text = text[(marker + 4)..];
        }

        return text.Length == 0 || text.Contains('/', StringComparison.Ordinal) ? null : text;
    }

    /// <summary>GET med nøkkelen i adressen. Null ved nettverksfeil eller feilkode; ingen feil bærer adressen videre.</summary>
    private async Task<string?> GetAsync(string relative, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Base + relative + "&key=" + Uri.EscapeDataString(_apiKey)));
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
