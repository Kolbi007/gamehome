// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHome.Windows;

/// <summary>En app i Vibepollos liste (bare feltene GameHome bruker).</summary>
public sealed record VibepolloApp(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("uuid")] string? Uuid,
    [property: JsonPropertyName("image-path")] string? ImagePath,
    [property: JsonPropertyName("playnite-id")] string? PlayniteId);

public sealed record VibepolloAppList(
    [property: JsonPropertyName("apps")] IReadOnlyList<VibepolloApp>? Apps);

/// <summary>Hva som kjører på gaming-PC-en.</summary>
public sealed record VibepolloStatus(
    [property: JsonPropertyName("activeSessions")] int ActiveSessions,
    [property: JsonPropertyName("appRunning")] bool AppRunning,
    [property: JsonPropertyName("appName")] string? AppName,
    [property: JsonPropertyName("paused")] bool Paused);

/// <summary>
/// Vibepollos web-API på gaming-PC-en (HTTPS 47990) med en nøkkel med smal tilgang: liste over apper, omslag, status og
/// «avslutt appen». Sertifikatet er egensignert; det festes første gang (som SSH) og må være det samme senere.
/// Endepunkter og tilgangsmodell: docs/research/2026-11-02-neste-faser.md.
/// </summary>
public sealed class VibepolloClient : IDisposable
{
    private readonly HttpClient _http;

    /// <param name="pinnedCertificate">Lagret SHA-256 av sertifikatet, eller null første gang.</param>
    /// <param name="pinCertificate">Kalles med sertifikatets SHA-256 første gang, så det kan lagres.</param>
    public VibepolloClient(string address, string token, string? pinnedCertificate, Action<string> pinCertificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    var thumbprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
                    if (pinnedCertificate is null)
                    {
                        pinnedCertificate = thumbprint;
                        pinCertificate(thumbprint);
                        return true;
                    }

                    return string.Equals(pinnedCertificate, thumbprint, StringComparison.OrdinalIgnoreCase);
                },
            },
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{address}:47990/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<IReadOnlyList<VibepolloApp>> GetAppsAsync(CancellationToken ct = default)
    {
        await using var stream = await _http.GetStreamAsync(new Uri("api/apps", UriKind.Relative), ct).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync(stream, VibepolloJson.Default.VibepolloAppList, ct).ConfigureAwait(false);
        return list?.Apps ?? [];
    }

    /// <summary>Omslaget som PNG, eller null hvis appen ikke har eget omslag.</summary>
    public async Task<byte[]?> GetCoverAsync(string uuid, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(new Uri($"api/apps/{Uri.EscapeDataString(uuid)}/cover", UriKind.Relative), ct)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false) : null;
    }

    public async Task<VibepolloStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        await using var stream = await _http.GetStreamAsync(new Uri("api/session/status", UriKind.Relative), ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, VibepolloJson.Default.VibepolloStatus, ct).ConfigureAwait(false);
    }

    /// <summary>Avslutter appen som kjører på gaming-PC-en. Kun etter bekreftelse fra brukeren.</summary>
    public async Task<bool> CloseAppAsync(CancellationToken ct = default)
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(new Uri("api/apps/close", UriKind.Relative), content, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public void Dispose() => _http.Dispose();
}

[JsonSerializable(typeof(VibepolloAppList))]
[JsonSerializable(typeof(VibepolloStatus))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class VibepolloJson : JsonSerializerContext;
