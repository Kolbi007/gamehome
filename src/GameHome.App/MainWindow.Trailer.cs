// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Metadata;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GameHome.App;

/// <summary>
/// Stille trailere på Home fra Steam eller IGDB/YouTube. Bare i lader, aldri under spill.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan TrailerDelay = TimeSpan.FromSeconds(3);
    private readonly Dictionary<int, string?> _trailers = [];
    private readonly Dictionary<string, string> _youtubeTrailers = [];
    private DispatcherQueueTimer? _trailerTimer;
    private DispatcherQueueTimer? _trailerPlaybackTimer;
    private CancellationTokenSource? _trailerLifetime;
    private string? _trailerGameId;
    private Button? _trailerTile;
    private bool _trailerPlaying;

    private bool TrailersAllowed =>
        _trailersEnabled && !OnBattery && !SessionActive && _windowActive && new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Kalles når en flis i Home får fokus: traileren starter hvis fokus blir værende.</summary>
    private void ScheduleTrailer(Game game, Button tile)
    {
        StopTrailer();
        if (!TrailersAllowed)
        {
            return;
        }

        _trailerGameId = game.Id;
        _trailerTile = tile;
        _trailerLifetime = new CancellationTokenSource();
        if (_trailerTimer is null)
        {
            _trailerTimer = DispatcherQueue.CreateTimer();
            _trailerTimer.IsRepeating = false;
            _trailerTimer.Interval = TrailerDelay;
            _trailerTimer.Tick += (_, _) =>
            {
                if (_trailerLifetime is { } lifetime && _trailerGameId is { } id && _library.Find(id) is { } focused)
                {
                    _ = PlayTrailerAsync(focused, lifetime.Token);
                }
            };
        }

        _trailerTimer.Start();
    }

    private bool CanPreview(CancellationToken ct) =>
        !ct.IsCancellationRequested && TrailersAllowed && !AnyOverlayOpen && !_attracting
        && Root.XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), _trailerTile);

    private async Task PlayTrailerAsync(Game game, CancellationToken ct)
    {
        if (!CanPreview(ct))
        {
            return;
        }

        var store = new SteamStoreClient(ArtHttp);
        var appId = SteamAppIdFor(game);
        var lookupTitle = TrailerCatalog.SearchTitle(GameLibrary.DisplayTitle(game));
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookup.CancelAfter(TimeSpan.FromSeconds(6));
        var lookupToken = lookup.Token;
        try
        {
            if (appId is { } steamId && await SteamTrailerUrlAsync(store, steamId, lookupToken) is { } url)
            {
                if (CanPreview(ct))
                {
                    PlaySteamTrailer(game, steamId, url, ct);
                }

                return;
            }

            if (!CanPreview(ct))
            {
                return;
            }

            if (YouTubePreviewFits && await YouTubeTrailerIdAsync(game, appId, lookupToken) is { } video)
            {
                if (CanPreview(ct))
                {
                    await PlayYouTubePreviewAsync(video, ct);
                }

                return;
            }

            if (appId is null && CanPreview(ct)
                && await SteamAppIdByTitleAsync(game, lookupToken) is { } match
                && await SteamTrailerUrlAsync(store, match, lookupToken) is { } matchedUrl && CanPreview(ct))
            {
                PlaySteamTrailer(game, match, matchedUrl, ct);
            }
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested)
        {
            if (CanPreview(ct) && YouTubePreviewFits && TrailerCatalog.KnownVideoId(lookupTitle) is { } fallback)
            {
                await PlayYouTubePreviewAsync(fallback, ct);
            }
        }
    }

    private async Task<string?> SteamTrailerUrlAsync(SteamStoreClient store, int appId, CancellationToken ct)
    {
        if (_trailers.TryGetValue(appId, out var url))
        {
            return url;
        }

        try
        {
            url = await store.GetTrailerAsync(appId, ct);
            ct.ThrowIfCancellationRequested();
            _trailers[appId] = url;
            return url;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (HttpRequestException or TaskCanceledException))
        {
            return null;
        }
    }

    private void PlaySteamTrailer(Game game, int appId, string url, CancellationToken ct)
    {

        // Spilles én gang: en trailer som går i loop, bruker strøm og blir støy.
        var player = new MediaPlayer { IsMuted = true, IsLoopingEnabled = false, AutoPlay = true };
        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                StopTrailer();
            }
        });
        var playback = player.PlaybackSession;
        void OnPositionChanged(MediaPlaybackSession session, object args)
        {
            if (session.Position <= TimeSpan.Zero || session.NaturalVideoWidth == 0)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ct.IsCancellationRequested && ReferenceEquals(HeroVideo.MediaPlayer, player) && !_trailerPlaying)
                {
                    playback.PositionChanged -= OnPositionChanged;
                    TrailerStarted();
                    FadeTo(HeroVideo, 0.9, 900);
                    MakeRoomForTrailer(true);
                    _log.Write("trailer", "Steam-trailer spiller");
                }
            });
        }

        playback.PositionChanged += OnPositionChanged;
        player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                _log.Write("trailer", $"Traileren feilet: {args.Error} 0x{args.ExtendedErrorCode?.HResult ?? 0:X8}");
                StopTrailer();
            }
        });
        player.Source = MediaSource.CreateFromUri(new Uri(url));
        HeroVideo.SetMediaPlayer(player);
        StartTrailerDeadline(TimeSpan.FromSeconds(10));
        _log.Write("trailer", $"Steam-trailer for «{game.Title}» (appid {appId})");
    }

    private async Task<string?> YouTubeTrailerIdAsync(Game game, int? steamAppId, CancellationToken ct = default)
    {
        var title = TrailerCatalog.SearchTitle(GameLibrary.DisplayTitle(game));
        var fallback = TrailerCatalog.KnownVideoId(title);
        if (Igdb is not { } igdb)
        {
            return fallback;
        }

        if (_youtubeTrailers.TryGetValue(game.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var match = steamAppId is { } id ? await igdb.FindBySteamAppIdAsync(id, ct) : null;
            if (match is null)
            {
                // IGDB-søk kan returnere lignende spill; godta kun normalisert eksakt tittel.
                match = (await igdb.SearchAsync(title, 10, ct))
                    .FirstOrDefault(candidate => TrailerCatalog.IsTitleMatch(candidate.Name, title));
            }

            if (match is null)
            {
                return fallback;
            }

            var videos = await igdb.GetVideosAsync(match.Id, ct);
            ct.ThrowIfCancellationRequested();
            var video = videos.FirstOrDefault(candidate => candidate.Name.Contains("trailer", StringComparison.OrdinalIgnoreCase))
                ?? (videos.Count > 0 ? videos[0] : null);
            if (video is not null)
            {
                _youtubeTrailers[game.Id] = video.VideoId;
                _log.Write("trailer", $"IGDB knytter «{video.Name}» til «{match.Name}»; YouTube-ID {video.VideoId}");
            }

            return video?.VideoId ?? fallback;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (HttpRequestException or TaskCanceledException))
        {
            return fallback;
        }
    }

    private async void DetailsYouTubeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsGame is not { } game || SessionActive)
        {
            return;
        }

        DetailsYouTubeButton.IsEnabled = false;
        try
        {
            var videoId = await YouTubeTrailerIdAsync(game, SteamAppIdFor(game));
            if (_detailsGame?.Id != game.Id || SessionActive)
            {
                return;
            }

            var address = videoId is not null
                ? $"https://www.youtube.com/watch?v={videoId}"
                : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(GameLibrary.DisplayTitle(game) + " game trailer")}";
            if (!await global::Windows.System.Launcher.LaunchUriAsync(new Uri(address)))
            {
                ShowYouTubeOpenError(game);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ShowYouTubeOpenError(game);
        }
        finally
        {
            DetailsYouTubeButton.IsEnabled = true;
            if (_detailsGame?.Id == game.Id && _windowActive && MessagePanel.Visibility != Visibility.Visible)
            {
                DetailsYouTubeButton.Focus(FocusState.Keyboard);
            }
        }
    }

    private void ShowYouTubeOpenError(Game game)
    {
        if (_detailsGame?.Id == game.Id)
        {
            void RestoreFocus() => DetailsYouTubeButton.Focus(FocusState.Keyboard);
            ShowMessage("Kunne ikke åpne YouTube", "Sjekk at en standardnettleser er valgt i Windows.",
                "Lukk", RestoreFocus, "Tilbake", RestoreFocus);
        }
    }

    private readonly Dictionary<string, int?> _steamIdsByTitle = [];

    /// <summary>
    /// Spill som ikke er fra Steam (for eksempel Broforce fra AnkerGames) får traileren fra Steam-siden med samme navn.
    /// Slås opp én gang per spill og kjøring.
    /// </summary>
    private async Task<int?> SteamAppIdByTitleAsync(Game game, CancellationToken ct)
    {
        if (_steamIdsByTitle.TryGetValue(game.Id, out var cached))
        {
            return cached;
        }

        int? appId;
        try
        {
            appId = await new SteamStoreClient(ArtHttp).FindAppIdAsync(GameLibrary.DisplayTitle(game), ct);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (HttpRequestException or TaskCanceledException))
        {
            return null;
        }

        _steamIdsByTitle[game.Id] = appId;
        return appId;
    }

    private void StopTrailer()
    {
        _trailerTimer?.Stop();
        _trailerPlaybackTimer?.Stop();
        _trailerLifetime?.Cancel();
        _trailerLifetime?.Dispose();
        _trailerLifetime = null;
        _trailerGameId = null;
        _trailerTile = null;
        _trailerPlaying = false;
        StopYouTubePreview();
        CancelFade(HeroVideo);
        if (HeroVideo.MediaPlayer is { } player)
        {
            HeroVideo.SetMediaPlayer(null);
            player.Dispose();
            _log.Write("trailer", "Trailer stoppet");
        }

        HeroVideo.Opacity = 0;
        MakeRoomForTrailer(false);
    }

    private void TrailerStarted()
    {
        _trailerPlaying = true;
        StartTrailerDeadline(TimeSpan.FromMinutes(2));
    }

    private void StartTrailerDeadline(TimeSpan duration)
    {
        if (_trailerPlaybackTimer is null)
        {
            _trailerPlaybackTimer = DispatcherQueue.CreateTimer();
            _trailerPlaybackTimer.IsRepeating = false;
            _trailerPlaybackTimer.Tick += (_, _) =>
            {
                _log.Write("trailer", _trailerPlaying ? "Forhåndsvisningen nådde tidsgrensen" : "Forhåndsvisningen startet ikke i tide");
                StopTrailer();
            };
        }

        _trailerPlaybackTimer.Stop();
        _trailerPlaybackTimer.Interval = duration;
        _trailerPlaybackTimer.Start();
    }

    private void CancelFade(UIElement element)
    {
        if (_fadeStories.Remove(element, out var story))
        {
            story.Stop();
        }
    }

    private bool _roomForTrailer;

    /// <summary>
    /// Mens traileren spiller, glir tekst og fliser litt til siden og nedover, og sløret over bildet blir lettere, så
    /// videoen får plass. Alt glir tilbake når traileren stopper (ved input, nytt spill eller slutt).
    /// </summary>
    private void MakeRoomForTrailer(bool room)
    {
        if (_roomForTrailer == room)
        {
            return;
        }

        // Tydelig, ikke bare antydet (produkteier så ingen forskjell med 16/56 px, 2026-11-05): logo og tekst krymper og
        // glir ned mot venstre hjørne, flisraden glir nesten ut nederst, verktøyknappene forsvinner, og sløret letter.
        _roomForTrailer = room;
        var duration = room ? 900 : 350;
        MoveTo(HeroInfo, room ? -24 : 0, room ? 150 : 0, room ? 0.7 : 1, duration, originX: 0, originY: 1);
        MoveTo(GameTilesScroll, 0, room ? 170 : 0, 1, duration);
        MoveTo(ToolTiles, 0, room ? 120 : 0, 1, duration);
        FadeTo(GameTilesScroll, room ? 0.55 : 1, duration);
        FadeTo(ToolTiles, room ? 0 : 1, duration);
        FadeTo(ScrimLeft, room ? 0.3 : 1, duration);
        FadeTo(ScrimBottom, room ? 0.55 : 1, duration);
    }

    private static void MoveTo(UIElement element, double x, double y, double scale, int milliseconds, double originX = 0.5, double originY = 0.5)
    {
        if (element.RenderTransform is not CompositeTransform)
        {
            element.RenderTransform = new CompositeTransform();
            element.RenderTransformOrigin = new global::Windows.Foundation.Point(originX, originY);
        }

        var story = new Storyboard();
        foreach (var (property, value) in new[] { ("TranslateX", x), ("TranslateY", y), ("ScaleX", scale), ("ScaleY", scale) })
        {
            var animation = new DoubleAnimation
            {
                To = value,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, $"(UIElement.RenderTransform).(CompositeTransform.{property})");
            story.Children.Add(animation);
        }

        story.Begin();
    }

    private readonly Dictionary<UIElement, Storyboard> _fadeStories = new();

    private void FadeTo(UIElement element, double to, int milliseconds)
    {
        if (_fadeStories.Remove(element, out var previous))
        {
            previous.Stop();
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var story = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        story.Children.Add(animation);
        _fadeStories[element] = story;
        story.Completed += (_, _) =>
        {
            if (_fadeStories.TryGetValue(element, out var current) && ReferenceEquals(current, story))
            {
                _fadeStories.Remove(element);
            }
        };
        story.Begin();
    }
}
