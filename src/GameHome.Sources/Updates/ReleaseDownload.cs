// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Sources.Updates;

public static class ReleaseDownload
{
    private const long MaxPackageSize = 512L * 1024 * 1024;

    /// <summary>Copies a release package, enforcing its published size and a deadline through the response body.</summary>
    public static async Task CopyAsync(HttpClient http, AvailableRelease release, Stream destination,
        TimeSpan timeout, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(destination);

        if (release.Size <= 0 || release.Size > MaxPackageSize)
        {
            throw new InvalidDataException("Ugyldig pakkestørrelse i utgivelsen.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            using var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
            {
                throw new InvalidDataException("Pakkestørrelsen samsvarer ikke med utgivelsen.");
            }

            // Disposing the response also disposes its content stream.
            var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[81920];
            long written = 0;
            var lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                if (read > release.Size - written)
                {
                    throw new InvalidDataException("Nedlastingen er større enn utgivelsen oppgir.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                written += read;
                var percent = (int)(written * 100 / release.Size);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(percent);
                }
            }

            if (written != release.Size)
            {
                throw new InvalidDataException("Nedlastingen er ufullstendig.");
            }

            await destination.FlushAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Nedlastingen tok for lang tid.", ex);
        }
    }
}
