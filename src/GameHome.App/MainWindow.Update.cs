// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
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
    private const string ReleasesUrl = "https://api.github.com/repos/Kolbi007/gamehome/releases?per_page=10";
    private const string LastUpdateCheckSetting = "update.lastCheck";
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private static readonly HttpClient UpdateHttp = CreateUpdateHttp();
    private AvailableRelease? _update;
    private bool _updateChecking;

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

    /// <summary>Bakgrunnssjekk høyst hver 6. time; når menyen åpnes, høyst hvert 10. minutt; <paramref name="manual"/> alltid.</summary>
    private async Task CheckForUpdateAsync(bool manual, bool fromMenu = false)
    {
        if (SessionActive || _updateChecking)
        {
            return;
        }

        var settings = ApplicationData.Current.LocalSettings.Values;
        if (!manual && settings[LastUpdateCheckSetting] is long last
            && DateTime.UtcNow - DateTime.FromBinary(last) < (fromMenu ? TimeSpan.FromMinutes(10) : UpdateCheckInterval))
        {
            return;
        }

        _updateChecking = true;
        try
        {
            settings[LastUpdateCheckSetting] = DateTime.UtcNow.ToBinary();
            var json = await UpdateHttp.GetStringAsync(new Uri(ReleasesUrl));
            var newest = ReleaseFeed.Newest(json, Architecture);
            var installed = InstalledVersion;
            if (newest is null || newest.Version <= installed)
            {
                _log.Write("oppdatering", $"Ingen nyere versjon (installert {installed}, nyeste {newest?.Version.ToString() ?? "ingen"})");
                if (manual)
                {
                    HomeMenuStatus.Text = $"GameHome {installed} er nyeste versjon.";
                    HomeMenuStatus.Visibility = Visibility.Visible;
                }

                return;
            }

            _update = newest;
            MenuUpdateText.Text = $"Oppdater til {Short(newest.Version)}";
            MenuUpdateHint.Text = string.Create(CultureInfo.InvariantCulture,
                $"Du har {Short(installed)} · {newest.Size / 1_000_000.0:F0} MB");
            MenuUpdateButton.Visibility = Visibility.Visible;
            AddNotice("update", null, $"GameHome {Short(newest.Version)} er klar. Trykk M2 og velg «Oppdater».");
            _log.Write("oppdatering", $"Ny versjon {newest.Version} ({newest.Tag}); installert {installed}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.Write("oppdatering", $"Kunne ikke se etter oppdatering: {ex.Message}");
        }
        finally
        {
            _updateChecking = false;
        }
    }

    private static string Short(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    private async void MenuUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } update || SessionActive)
        {
            return;
        }

        MenuUpdateButton.IsEnabled = false;
        HomeMenuStatus.Visibility = Visibility.Visible;
        try
        {
            var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("update", CreationCollisionOption.OpenIfExists);
            var file = await folder.CreateFileAsync(update.AssetName, CreationCollisionOption.ReplaceExisting);
            using (var response = await UpdateHttp.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? update.Size;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file.Path);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                var lastShown = -1;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    var percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != lastShown)
                    {
                        lastShown = percent;
                        HomeMenuStatus.Text = $"Laster ned {Short(update.Version)} … {percent} %";
                    }
                }
            }

            HomeMenuStatus.Text = "Åpner Windows' installasjonsprogram. Trykk «Oppdater» der; GameHome starter på nytt.";
            _log.Write("oppdatering", $"Lastet ned {update.AssetName}; åpner App Installer");
            var launched = await global::Windows.System.Launcher.LaunchFileAsync(file);
            if (!launched)
            {
                HomeMenuStatus.Text = "Windows åpnet ikke installasjonsprogrammet.";
                _log.Write("oppdatering", "App Installer ble ikke åpnet");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            HomeMenuStatus.Text = $"Nedlastingen feilet: {ex.Message}";
            _log.Write("oppdatering", $"Nedlasting feilet: {ex.Message}");
        }
        finally
        {
            MenuUpdateButton.IsEnabled = true;
        }
    }
}
