// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GameHome.App;

public sealed partial class MainWindow
{
    // The HTTPS host identifies GameHome to YouTube and matches the MSIX identity.
    private const string YouTubePreviewHost = "gamehome";
    private const double YouTubeMinimumHeight = 200;
    private const double YouTubeAspectRatio = 16.0 / 9.0;
    private const double YouTubeStartingHeight = 203;
    private const double YouTubeHeroGap = 24;
    private string? _youTubePreviewPage;
    private WebView2? _youTubePreview;

    private double YouTubeAvailableWidth => Surface.ActualWidth - Surface.Padding.Left - Surface.Padding.Right;

    private double YouTubePlayerHeight
    {
        get
        {
            const double minTitleWidth = 200;
            const double preferredTitleWidth = 280;
            var minPlayerWidth = YouTubeMinimumHeight * YouTubeAspectRatio;
            var titleWidth = Math.Clamp(YouTubeAvailableWidth - YouTubeHeroGap - minPlayerWidth, minTitleWidth, preferredTitleWidth);
            var playerWidth = YouTubeAvailableWidth - YouTubeHeroGap - titleWidth;
            return Math.Min(Surface.RowDefinitions[1].ActualHeight, playerWidth / YouTubeAspectRatio);
        }
    }

    private bool YouTubePreviewFits => YouTubePlayerHeight >= YouTubeMinimumHeight;

    private void TrailerSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_youTubePreview is { } preview)
        {
            if (YouTubePreviewFits)
            {
                LayoutYouTubePlayer(expanded: _trailerPlaying);
                if (preview.Opacity > 0)
                {
                    FitHeroBesideYouTube();
                }
            }
            else
            {
                StopTrailer();
            }
        }
    }

    private void LayoutYouTubePlayer(bool expanded)
    {
        var availableHeight = YouTubePlayerHeight;
        var height = expanded ? availableHeight : Math.Min(availableHeight, YouTubeStartingHeight);
        YouTubePreviewSlot.Height = height;
        YouTubePreviewSlot.Width = height * YouTubeAspectRatio;
    }

    private void FitHeroBesideYouTube() =>
        HeroInfo.MaxWidth = Math.Min(820, YouTubeAvailableWidth - YouTubePreviewSlot.Width - YouTubeHeroGap);

    private async Task PlayYouTubePreviewAsync(string videoId, CancellationToken ct)
    {
        if (!YouTubePreviewFits)
        {
            return;
        }

        StartTrailerDeadline(TimeSpan.FromSeconds(10));
        try
        {
            var preview = new WebView2 { Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
            AutomationProperties.SetName(preview, "YouTube-trailer");
            _youTubePreview = preview;
            LayoutYouTubePlayer(expanded: false);
            YouTubePreviewSlot.Child = preview;
            await preview.EnsureCoreWebView2Async();
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (!CanPreview(ct) || !YouTubePreviewFits)
            {
                StopTrailer();
                return;
            }

            var core = preview.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(YouTubePreviewHost,
                Path.Combine(AppContext.BaseDirectory, "TrailerPreview"), CoreWebView2HostResourceAccessKind.Deny);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.AddWebResourceRequestedFilter("https://www.youtube.com/embed/*",
                CoreWebView2WebResourceContext.Document, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
                args.Request.Headers.SetHeader("Referer", $"https://{YouTubePreviewHost}/");
            core.NavigationStarting += (_, args) => args.Cancel = args.Uri != _youTubePreviewPage;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.WebMessageReceived += YouTubePreviewMessage;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && !ct.IsCancellationRequested)
                {
                    YouTubePreviewFailed($"Navigation: {args.WebErrorStatus}");
                }
            };
            core.ProcessFailed += (_, args) =>
            {
                if (!ct.IsCancellationRequested)
                {
                    YouTubePreviewFailed($"WebView2: {args.ProcessFailedKind}");
                }
            };
            _youTubePreviewPage = $"https://{YouTubePreviewHost}/index.html?video={videoId}&preview={Guid.NewGuid():N}";
            core.Navigate(_youTubePreviewPage);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                YouTubePreviewFailed(ex.GetType().Name);
            }
        }
    }

    private async void YouTubePreviewMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var page = _youTubePreviewPage;
        if (page is null || args.Source != page || _trailerLifetime is not { } lifetime || _youTubePreview is not { } preview)
        {
            return;
        }

        var ct = lifetime.Token;
        try
        {
            var message = args.TryGetWebMessageAsString();
            switch (message)
            {
                case "ready" when CanPreview(ct) && YouTubePreviewFits:
                    FitHeroBesideYouTube();
                    preview.Opacity = 1;
                    preview.IsHitTestVisible = true;
                    await sender.ExecuteScriptAsync("window.startPreview()");
                    break;
                case "playing":
                    if (TrailersAllowed && !AnyOverlayOpen && !_trailerPlaying)
                    {
                        TrailerStarted();
                        LayoutYouTubePlayer(expanded: true);
                        FitHeroBesideYouTube();
                        MakeRoomForTrailer(true, youtube: true);
                        _log.Write("trailer", "YouTube-trailer spiller");
                    }
                    break;
                case "ended":
                case "ready":
                    StopTrailer();
                    break;
                case "api-unavailable":
                case "autoplay-blocked":
                case "invalid-video":
                    YouTubePreviewFailed(message);
                    break;
                case var error when error.StartsWith("error:", StringComparison.Ordinal)
                    && int.TryParse(error.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var code):
                    YouTubePreviewFailed($"YouTube-feilkode {code}");
                    break;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                YouTubePreviewFailed(ex.GetType().Name);
            }
        }
    }

    private bool DismissYouTubeTrailer()
    {
        if (_youTubePreview is null)
        {
            return false;
        }

        var tile = _trailerTile;
        StopTrailer();
        if (tile is not null && tile.FocusState == FocusState.Unfocused)
        {
            tile.Focus(FocusState.Programmatic);
            StopTrailer(); // The restored tile's focus event can schedule another preview.
        }

        return true;
    }

    private void YouTubePreviewFailed(string reason)
    {
        if (_youTubePreviewPage is not null || _trailerLifetime is not null)
        {
            _log.Write("trailer", $"YouTube-forhåndsvisning feilet: {reason}");
            StopTrailer();
        }
    }

    private void StopYouTubePreview()
    {
        if (_youTubePreview is not { } preview)
        {
            return;
        }

        _youTubePreviewPage = null;
        _youTubePreview = null;
        YouTubePreviewSlot.Child = null;
        HeroInfo.MaxWidth = 820;
        try
        {
            preview.Close();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _log.Write("trailer", $"YouTube-spilleren kunne ikke lukkes: {ex.GetType().Name}");
        }
    }
}
