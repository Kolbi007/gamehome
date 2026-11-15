// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;
using GameHome.Sources.Updates;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// Oppdateringer fra GitHub (ADR 0010). GameHome ser etter en nyere utgivelse høyst hver sjette time og aldri under spill,
/// laster ned pakken når brukeren velger «Oppdater» og åpner den i Windows' installasjonsprogram (App Installer), som
/// sjekker signaturen og ber om bekreftelse. Nettverkskall: GET api.github.com/repos/Kolbi007/gamehome/releases og
/// nedlasting av pakken fra github.com.
/// </summary>
public sealed partial class MainWindow
{
    // Beta may use prereleases; Stable uses GitHub's dedicated latest non-prerelease endpoint below.
    private const string ReleasesUrl = "https://api.github.com/repos/Kolbi007/gamehome/releases?per_page=100";
    private const string LatestStableReleaseUrl = "https://api.github.com/repos/Kolbi007/gamehome/releases/latest";
    private const string LastUpdateCheckSetting = "update.lastCheck";
    private const string UpdateChannelSetting = "update.channel";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan UpdateDownloadTimeout = TimeSpan.FromMinutes(10);

    private static readonly HttpClient UpdateHttp = CreateUpdateHttp();
    private AvailableRelease? _update;
    private bool _manualUpdateCheckQueued;
    private CancellationTokenSource? _updateCheckCancellation;
    private CancellationTokenSource? _updateDownloadCancellation;
    private bool _updateShuttingDown;
    private bool _updateDownloading => _updateDownloadCancellation is not null;
    private bool CanUpdate => !_updateShuttingDown && !_launching && !SessionActive;

    private static bool BetaChannel => ApplicationData.Current.LocalSettings.Values[UpdateChannelSetting] as string == "beta";

    private void RefreshUpdateChannelButton() =>
        UpdateChannelText.Text = $"Kanal: {(BetaChannel ? "Beta" : "Stable")}";

    private async void UpdateChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanUpdate)
        {
            return;
        }

        _updateCheckCancellation?.Cancel();
        _updateDownloadCancellation?.Cancel();
        var settings = ApplicationData.Current.LocalSettings.Values;
        settings[UpdateChannelSetting] = BetaChannel ? "stable" : "beta";
        settings.Remove(LastUpdateCheckSetting);
        ClearAvailableUpdate();
        RefreshUpdateChannelButton();
        UpdateStatus.Text = $"Sjekker {(BetaChannel ? "Beta" : "Stable")} …";
        UpdateStatus.Visibility = Visibility.Visible;
        _log.Write("oppdatering", $"Oppdateringskanal endret til {(BetaChannel ? "Beta" : "Stable")}");
        await CheckForUpdateAsync(manual: true, fromMenu: true);
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanUpdate || _updateDownloading)
        {
            return;
        }

        UpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = $"Sjekker {(BetaChannel ? "Beta" : "Stable")} …";
        UpdateStatus.Visibility = Visibility.Visible;
        await CheckForUpdateAsync(manual: true, fromMenu: true);
    }

    private static HttpClient CreateUpdateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameHome-updater"); // GitHub krever en User-Agent.
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static Version InstalledVersion
    {
        get
        {
            var v = global::Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(v.Major, v.Minor, v.Build, v.Revision);
        }
    }

    private static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    private void CancelUpdateOperations(bool shuttingDown = false)
    {
        _updateShuttingDown |= shuttingDown;
        _manualUpdateCheckQueued = false;
        _updateCheckCancellation?.Cancel();
        _updateDownloadCancellation?.Cancel();
        if (!_updateShuttingDown && (_updateDownloading || _updateCheckCancellation is not null)
            && UpdateStatus.Visibility == Visibility.Visible)
        {
            UpdateStatus.Text = "Oppdateringen ble avbrutt ved spillstart.";
            UpdateButton.Visibility = _update is not null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ClearAvailableUpdate()
    {
        _update = null;
        UpdateButton.Visibility = Visibility.Collapsed;
        if (_notices.RemoveAll(n => n.Key == "update") > 0)
        {
            UpdateNoticeButton();
        }
    }

    /// <summary>Bakgrunnssjekk høyst hver 6. time; når menyen åpnes, høyst hvert 10. minutt; <paramref name="manual"/> alltid.</summary>
    private async Task CheckForUpdateAsync(bool manual, bool fromMenu = false)
    {
        if (!CanUpdate || (!manual && _updateDownloading))
        {
            return;
        }

        if (_updateCheckCancellation is not null)
        {
            _manualUpdateCheckQueued |= manual;
            return;
        }

        var settings = ApplicationData.Current.LocalSettings.Values;
        if (!manual && settings[LastUpdateCheckSetting] is long last
            && DateTime.UtcNow - DateTime.FromBinary(last) < (fromMenu ? TimeSpan.FromMinutes(10) : UpdateCheckInterval))
        {
            return;
        }

        var beta = BetaChannel;
        var channel = beta ? "Beta" : "Stable";
        using var cancellation = new CancellationTokenSource();
        _updateCheckCancellation = cancellation;
        try
        {
            var json = await UpdateHttp.GetStringAsync(new Uri(beta ? ReleasesUrl : LatestStableReleaseUrl), cancellation.Token);
            if (cancellation.IsCancellationRequested || !CanUpdate)
            {
                return;
            }

            var newest = ReleaseFeed.Newest(json, Architecture, includePrerelease: beta);
            var installed = InstalledVersion;
            if (newest is null || newest.Version <= installed)
            {
                var hadUpdate = _update is not null;
                ClearAvailableUpdate();
                _log.Write("oppdatering", $"Ingen nyere versjon (installert {installed}, nyeste {newest?.Version.ToString() ?? "ingen"})");
                if (manual || hadUpdate)
                {
                    UpdateStatus.Text = newest is null ? $"Fant ingen pakke på {channel}-kanalen."
                        : newest.Version < installed ? $"Installert versjon ({Short(installed)}) er nyere enn nyeste {channel}-versjon ({Short(newest.Version)})."
                        : $"Du har nyeste {channel}-versjon ({Short(installed)}).";
                    UpdateStatus.Visibility = Visibility.Visible;
                }

                return;
            }

            _update = newest;
            UpdateText.Text = $"Oppdater til {Short(newest.Version)}";
            UpdateButton.Visibility = Visibility.Visible;
            UpdateStatus.Text = $"Ny versjon klar · {newest.Size / 1_000_000.0:F0} MB (du har {Short(installed)}).";
            UpdateStatus.Visibility = Visibility.Visible;
            AddNotice("update", null, $"GameHome {Short(newest.Version)} ({channel}) er klar på hovedsiden.");
            _log.Write("oppdatering", $"Ny {channel}-kanalversjon {newest.Version} ({newest.Tag}); installert {installed}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!cancellation.IsCancellationRequested && CanUpdate)
            {
                UpdateStatus.Text = $"Kunne ikke se etter oppdateringer: {ex.Message}";
                UpdateStatus.Visibility = Visibility.Visible;
                UpdateButton.Visibility = _update is not null ? Visibility.Visible : Visibility.Collapsed;
                _log.Write("oppdatering", $"Kunne ikke se etter oppdatering: {ex.Message}");
            }
        }
        finally
        {
            _updateCheckCancellation = null;
            if (!cancellation.IsCancellationRequested && CanUpdate)
            {
                settings[LastUpdateCheckSetting] = DateTime.UtcNow.ToBinary();
            }

            if (_manualUpdateCheckQueued && CanUpdate)
            {
                _manualUpdateCheckQueued = false;
                await CheckForUpdateAsync(manual: true, fromMenu: true);
            }
        }
    }

    private static string Short(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } update || !CanUpdate || _updateDownloading || _updateCheckCancellation is not null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _updateDownloadCancellation = cancellation;
        var token = cancellation.Token;
        UpdateButton.IsEnabled = false;
        UpdateCheckButton.IsEnabled = false;
        UpdateStatus.Text = $"Laster ned {Short(update.Version)} …";
        UpdateStatus.Visibility = Visibility.Visible;
        string? partialPath = null;
        string? packagePath = null;
        var receiving = true;
        var launched = false;
        try
        {
            var folder = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "update");
            Directory.CreateDirectory(folder);
            partialPath = Path.Combine(folder, "GameHome.msix.partial");
            var progress = new Progress<int>(percent =>
            {
                if (receiving && !token.IsCancellationRequested && CanUpdate)
                {
                    UpdateStatus.Text = $"Laster ned {Short(update.Version)} … {percent} %";
                }
            });
            await using (var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await ReleaseDownload.CopyAsync(UpdateHttp, update, target, UpdateDownloadTimeout, progress, token);
            }

            receiving = false;
            if (token.IsCancellationRequested || !CanUpdate)
            {
                return;
            }

            var destination = Path.Combine(folder, "GameHome.msix");
            File.Move(partialPath, destination, overwrite: true);
            partialPath = null;
            packagePath = destination;
            var file = await StorageFile.GetFileFromPathAsync(packagePath);
            if (token.IsCancellationRequested || !CanUpdate)
            {
                return;
            }

            UpdateStatus.Text = "Åpner Windows' installasjonsprogram. Trykk «Oppdater» der; GameHome starter på nytt.";
            _log.Write("oppdatering", $"Lastet ned {update.AssetName}; åpner App Installer");
            launched = await global::Windows.System.Launcher.LaunchFileAsync(file);
            if (!launched && !token.IsCancellationRequested && CanUpdate)
            {
                UpdateStatus.Text = "Windows åpnet ikke installasjonsprogrammet.";
                _log.Write("oppdatering", "App Installer ble ikke åpnet");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException
            or InvalidDataException or IOException or UnauthorizedAccessException or COMException)
        {
            if (!token.IsCancellationRequested && CanUpdate)
            {
                UpdateStatus.Text = $"Oppdateringen feilet: {ex.Message}";
                _log.Write("oppdatering", $"Oppdatering feilet: {ex.Message}");
            }
        }
        finally
        {
            receiving = false;
            DeleteUpdateFile(partialPath);
            if (!launched)
            {
                DeleteUpdateFile(packagePath);
            }

            _updateDownloadCancellation = null;
            if (!_updateShuttingDown)
            {
                UpdateButton.IsEnabled = CanUpdate;
                UpdateCheckButton.IsEnabled = CanUpdate;
            }
        }
    }

    private void DeleteUpdateFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("oppdatering", $"Kunne ikke rydde opp i oppdateringspakken: {ex.GetType().Name}");
        }
    }
}
