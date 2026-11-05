// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Sources.Steam;
using GameHome.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GameHome.App;

/// <summary>
/// Steam-nedlastinger i Home: navn og prosent fra Steams appmanifester. Sjekkes når Home blir aktiv og hvert 3. sekund
/// bare mens en nedlasting pågår og ingen spill kjører. Når nedlastingen er ferdig, dukker spillet opp i Home.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan DownloadPollInterval = TimeSpan.FromSeconds(3);
    private DispatcherQueueTimer? _downloadTimer;
    private bool _downloadCheckRunning;
    private HashSet<string> _activeDownloads = new(StringComparer.Ordinal);

    /// <summary>Ser etter nedlastinger; finnes det noen, følges de til de er ferdige.</summary>
    private async void CheckDownloads()
    {
        if (SessionActive || _downloadCheckRunning || SteamClient.FindRoot() is not { } root)
        {
            StopDownloadTimer();
            return;
        }

        _downloadCheckRunning = true;
        try
        {
            var downloads = await Task.Run(() => SteamDownloads.Scan(root));
            var finished = _activeDownloads.Where(id => downloads.All(d => d.AppId != id)).ToList();
            _activeDownloads = downloads.Select(d => d.AppId).ToHashSet(StringComparer.Ordinal);

            if (downloads.Count == 0)
            {
                DownloadPill.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
            }
            else
            {
                var first = downloads[0];
                DownloadText.Text = downloads.Count == 1
                    ? $"Laster ned {first.Name} {first.Percent} %"
                    : $"Laster ned {first.Name} {first.Percent} % (+{downloads.Count - 1})";
                DownloadProgress.Value = first.Percent;
                DownloadPill.Visibility = Visibility.Visible;
                StartDownloadTimer();
            }

            if (finished.Count > 0)
            {
                // Ferdig: hent spillet inn i Home med en gang i stedet for å vente på neste synk.
                _log.Write("steam", $"Nedlasting ferdig ({string.Join(", ", finished)}); oppdaterer Home");
                _lastSteamSync = DateTime.MinValue;
                SyncStoresInBackground();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("steam", $"Kunne ikke lese nedlastinger: {ex.Message}");
        }
        finally
        {
            _downloadCheckRunning = false;
        }
    }

    private void StartDownloadTimer()
    {
        if (_downloadTimer is null)
        {
            _downloadTimer = DispatcherQueue.CreateTimer();
            _downloadTimer.Interval = DownloadPollInterval;
            _downloadTimer.Tick += (_, _) => CheckDownloads();
        }

        if (!_downloadTimer.IsRunning)
        {
            _downloadTimer.Start();
        }
    }

    private void StopDownloadTimer() => _downloadTimer?.Stop();
}
