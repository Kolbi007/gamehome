// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;

namespace GameHome.Core.Sources;

/// <summary>Oppdagede spill og hvor fullstendig kilden kunne leses.</summary>
public sealed record StoreScanResult(IReadOnlyList<StoreEntry> Entries, SourceHealth Health)
{
    public static StoreScanResult Complete(IReadOnlyList<StoreEntry> entries) =>
        new(entries, new(SourceHealthState.Ok, DateTimeOffset.UtcNow, entries.Count));

    public static StoreScanResult Degraded(IReadOnlyList<StoreEntry> entries, string message) =>
        new(entries, new(SourceHealthState.Degraded, DateTimeOffset.UtcNow, entries.Count, message));

    public static StoreScanResult Failed(string message) =>
        new([], new(SourceHealthState.Failed, DateTimeOffset.UtcNow, LastError: message));

    /// <summary>Bare fullstendige skanninger kan markere fraværende spill som borte.</summary>
    public StoreSyncResult? ApplyTo(GameLibrary library, string runnerId)
    {
        ArgumentNullException.ThrowIfNull(library);
        return Health.State switch
        {
            SourceHealthState.Ok => library.SyncStore(runnerId, Entries),
            SourceHealthState.Degraded => library.SyncStore(runnerId, Entries, markMissing: false),
            _ => null,
        };
    }
}

public static class StoreScanner
{
    /// <summary>Leser lokale kilder i tur på bakgrunnstråden. En kildefeil stanser bare den ene kilden.</summary>
    public static IReadOnlyList<(string RunnerId, StoreScanResult Result)> Scan(
        IEnumerable<(string RunnerId, Func<CancellationToken, StoreScanResult> Scan)> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var results = new List<(string RunnerId, StoreScanResult Result)>();
        foreach (var (runnerId, scan) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add((runnerId, scan(cancellationToken)));
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add((runnerId, StoreScanResult.Failed($"Kunne ikke lese kilden ({ex.GetType().Name})")));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }
}
