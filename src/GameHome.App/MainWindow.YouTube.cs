// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
            _youTubePreview = preview;
            preview.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
            AutomationProperties.SetAccessibilityView(preview, AccessibilityView.Raw);
            LayoutYouTubePlayer(expanded: false);
            YouTubePreviewSlot.Child = preview;
            await preview.EnsureCoreWebView2Async().AsTask(ct);
            if (!IsCurrentPreview(preview, ct))
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
            // The iframe's strict-origin-when-cross-origin policy already sends the app origin as Referer.
            core.NavigationStarting += (_, args) => args.Cancel = !IsCurrentPreview(preview, ct) || args.Uri != _youTubePreviewPage;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.WebMessageReceived += async (_, args) => await YouTubePreviewMessageAsync(preview, core, args, ct);
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    EndYouTubePreview(preview, ct, $"Navigation: {args.WebErrorStatus}");
                }
            };
            core.ProcessFailed += (_, args) =>
                EndYouTubePreview(preview, ct, $"WebView2: {args.ProcessFailedKind}");
            _youTubePreviewPage = $"https://{YouTubePreviewHost}/index.html?video={videoId}&preview={Guid.NewGuid():N}";
            core.Navigate(_youTubePreviewPage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (_youTubePreview is { } preview)
            {
                EndYouTubePreview(preview, ct, ex.GetType().Name);
            }
            else
            {
                _log.Write("trailer", $"Kunne ikke opprette YouTube-spilleren: {ex.GetType().Name}");
                StopTrailer();
            }
        }
    }

    private bool IsCurrentPreview(WebView2 preview, CancellationToken ct) =>
        !ct.IsCancellationRequested && ReferenceEquals(preview, _youTubePreview);

    private async Task YouTubePreviewMessageAsync(WebView2 preview, CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs args, CancellationToken ct)
    {
        if (!IsCurrentPreview(preview, ct) || _youTubePreviewPage is not { } page || args.Source != page)
        {
            return;
        }

        try
        {
            var message = args.TryGetWebMessageAsString();
            switch (message)
            {
                case "cued" when CanPreview(ct) && YouTubePreviewFits:
                    if (preview.Opacity == 0)
                    {
                        // YouTube requires a visible viewport before autoplay. Display its cued poster in the small
                        // slot and keep the game's artwork and layout until playback actually starts.
                        preview.Opacity = 1;
                        await core.ExecuteScriptAsync("window.startPreview()").AsTask(ct);
                    }
                    break;
                case "playing" when CanPreview(ct) && YouTubePreviewFits:
                    if (!_trailerPlaying)
                    {
                        TrailerStarted();
                        LayoutYouTubePlayer(expanded: true);
                        FitHeroBesideYouTube();
                        MakeRoomForTrailer(true, youtube: true);
                        _log.Write("trailer", "YouTube-trailer spiller");
                    }
                    break;
                case "ended":
                case "cued":
                case "playing":
                    EndYouTubePreview(preview, ct);
                    break;
                case "api-unavailable":
                case "autoplay-blocked":
                case "invalid-video":
                case "player-script-error":
                    EndYouTubePreview(preview, ct, message);
                    break;
                case var error when error.StartsWith("error:", StringComparison.Ordinal)
                    && int.TryParse(error.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var code):
                    EndYouTubePreview(preview, ct, $"YouTube-feilkode {code}");
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            EndYouTubePreview(preview, ct, ex.GetType().Name);
        }
    }

    private bool DismissYouTubeTrailer()
    {
        if (_youTubePreview is null || AnyOverlayOpen)
        {
            return false;
        }

        StopTrailer();
        return true;
    }

    private void EndYouTubePreview(WebView2 preview, CancellationToken ct, string? reason = null)
    {
        // Close outside the WebView2 callback. Another preview may take ownership before this runs.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentPreview(preview, ct))
            {
                return;
            }

            if (reason is not null)
            {
                _log.Write("trailer", $"YouTube-forhåndsvisning feilet: {reason}");
            }

            StopTrailer();
        });
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
        YouTubePreviewSlot.Width = YouTubePreviewSlot.Height = double.NaN;
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
