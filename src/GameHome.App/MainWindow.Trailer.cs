// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Core.Model;
using GameHome.Metadata;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GameHome.App;

/// <summary>
/// Stille trailer bak hovedfeltet: etter 3 sekunder på et spill spilles Steam-butikkens trailer uten lyd (spill fra andre
/// kilder slås opp på navn), dempet bak
/// gradienten, som på PS5. Bare i lader (video bruker batteri), aldri under spill, og ikke når animasjoner er av.
/// Nettverkskall: Steam-butikkens appdetails (filters=movies) og selve videoen fra Steams CDN.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan TrailerDelay = TimeSpan.FromSeconds(3);
    private readonly Dictionary<int, string?> _trailers = [];
    private readonly Dictionary<string, string?> _youtubeTrailers = [];
    private global::Windows.Foundation.TypedEventHandler<Microsoft.Web.WebView2.Core.CoreWebView2, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs>? _youtubeMessageHandler;
    private DispatcherQueueTimer? _trailerTimer;
    private string? _trailerGameId;

    private bool TrailersAllowed =>
        !OnBattery && !SessionActive && _windowActive && new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Kalles når en flis i Home får fokus: traileren starter hvis fokus blir værende.</summary>
    private void ScheduleTrailer(Game game)
    {
        StopTrailer();
        if (!TrailersAllowed)
        {
            return;
        }

        _trailerGameId = game.Id;
        if (_trailerTimer is null)
        {
            _trailerTimer = DispatcherQueue.CreateTimer();
            _trailerTimer.IsRepeating = false;
            _trailerTimer.Interval = TrailerDelay;
            _trailerTimer.Tick += (_, _) =>
            {
                if (_trailerGameId is { } id && _library.Find(id) is { } focused)
                {
                    _ = PlayTrailerAsync(focused);
                }
            };
        }

        _trailerTimer.Start();
    }

    private async Task PlayTrailerAsync(Game game)
    {
        if (!TrailersAllowed || _trailerGameId != game.Id || AnyOverlayOpen || _attracting)
        {
            return;
        }

        if (_trailerGameId != game.Id)
        {
            return;
        }

        var appId = SteamAppIdFor(game) ?? await SteamAppIdByTitleAsync(game);
        string? url = null;
        if (appId is { } steamId && !_trailers.TryGetValue(steamId, out url))
        {
            try
            {
                url = await new SteamStoreClient(ArtHttp).GetTrailerAsync(steamId);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                url = null;
            }

            _trailers[steamId] = url;
        }

        if (url is not null)
        {
            if (_trailerGameId == game.Id && TrailersAllowed)
            {
                PlaySteamTrailer(game, appId, url);
            }

            return;
        }

        var youtubeId = await YouTubeTrailerIdAsync(game, appId);
        if (_trailerGameId != game.Id || !TrailersAllowed)
        {
            return;
        }

        if (youtubeId is { } id)
        {
            await PlayYouTubeTrailerAsync(game, id);
        }
        else
        {
            _log.Write("trailer", $"Ingen treffende trailer for «{game.Title}»");
        }
    }

    private void PlaySteamTrailer(Game game, int? appId, string url)
    {
        YouTubeTrailer.Opacity = 0;
        YouTubeTrailer.CoreWebView2?.Stop();

        // Spilles én gang: en trailer som går i loop, bruker strøm og blir støy.
        var player = new MediaPlayer { IsMuted = true, IsLoopingEnabled = false, AutoPlay = true };
        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                StopTrailer();
            }
        });
        player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                FadeTo(HeroVideo, 0.9, 900);
                MakeRoomForTrailer(true);
                _log.Write("trailer", "Trailer spiller");
            }
        });
        player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
        {
            _log.Write("trailer", $"Traileren feilet: {args.Error} 0x{args.ExtendedErrorCode?.HResult ?? 0:X8}");
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                StopTrailer();
            }
        });
        player.Source = MediaSource.CreateFromUri(new Uri(url));
        HeroVideo.SetMediaPlayer(player);
        _log.Write("trailer", $"Steam-trailer for «{game.Title}» (appid {appId})");
    }

    private async Task<string?> YouTubeTrailerIdAsync(Game game, int? steamAppId)
    {
        if (Igdb is not { } igdb)
        {
            return null;
        }

        if (_youtubeTrailers.TryGetValue(game.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var match = steamAppId is { } id ? await igdb.FindBySteamAppIdAsync(id) : null;
            if (match is null)
            {
                // IGDB-søk kan returnere lignende spill; godta kun normalisert eksakt tittel.
                var title = NormalizeTrailerTitle(GameLibrary.DisplayTitle(game));
                match = (await igdb.SearchAsync(GameLibrary.DisplayTitle(game), 10))
                .FirstOrDefault(candidate => TrailerTitlesMatch(title, NormalizeTrailerTitle(candidate.Name)));
            }

            if (match is null)
            {
                _youtubeTrailers[game.Id] = null;
                return null;
            }

            var videos = await igdb.GetVideosAsync(match.Id);
            IgdbGameVideo? video = null;
            foreach (var candidate in videos)
            {
                video ??= candidate;
                if (candidate.Name.Contains("trailer", StringComparison.OrdinalIgnoreCase))
                {
                    video = candidate;
                    break;
                }
            }
            _youtubeTrailers[game.Id] = video?.VideoId;
            if (video is not null)
            {
                _log.Write("trailer", $"IGDB knytter «{video.Name}» til «{match.Name}»; YouTube-ID {video.VideoId}");
            }

            return video?.VideoId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static string NormalizeTrailerTitle(string title)
    {
        var normalized = title.Replace("&", " and ", StringComparison.Ordinal);
        return new string([.. normalized.Where(char.IsAsciiLetterOrDigit)]).ToLowerInvariant();
    }

    private static bool TrailerTitlesMatch(string localTitle, string catalogTitle) =>
        localTitle == catalogTitle
        || (localTitle == "sonic3andknuckles" && catalogTitle == "sonicthehedgehog3andknuckles");

    private async Task PlayYouTubeTrailerAsync(Game game, string videoId)
    {
        try
        {
            YouTubeTrailer.DefaultBackgroundColor = global::Windows.UI.Color.FromArgb(255, 18, 17, 16);
            await YouTubeTrailer.EnsureCoreWebView2Async();
            if (_trailerGameId != game.Id || !TrailersAllowed)
            {
                return;
            }

            YouTubeTrailer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            YouTubeTrailer.CoreWebView2.Settings.IsStatusBarEnabled = false;
            var playback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_youtubeMessageHandler is not null)
            {
                YouTubeTrailer.CoreWebView2.WebMessageReceived -= _youtubeMessageHandler;
            }

            _youtubeMessageHandler = (_, args) =>
            {
                var message = args.TryGetWebMessageAsString();
                if (message == "playing")
                {
                    playback.TrySetResult(true);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_trailerGameId == game.Id && TrailersAllowed)
                        {
                            FadeTo(YouTubeTrailer, 0.9, 900);
                            MakeRoomForTrailer(true);
                            _log.Write("trailer", $"YouTube spiller for «{game.Title}» (IGDB ID {videoId})");
                        }
                    });
                }
                else if (message is "ended" or "error")
                {
                    playback.TrySetResult(false);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_trailerGameId == game.Id)
                        {
                            _log.Write("trailer", message == "ended" ? "YouTube-trailer ferdig" : "YouTube kunne ikke spille traileren");
                            StopTrailer();
                        }
                    });
                }
            };
            YouTubeTrailer.CoreWebView2.WebMessageReceived += _youtubeMessageHandler;
            YouTubeTrailer.CoreWebView2.NavigateToString(YouTubeEmbedHtml(videoId));
            var completed = await Task.WhenAny(playback.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completed != playback.Task || !await playback.Task)
            {
                YouTubeTrailer.CoreWebView2.WebMessageReceived -= _youtubeMessageHandler;
                _youtubeMessageHandler = null;
                if (_trailerGameId == game.Id)
                {
                    _log.Write("trailer", "YouTube begynte ikke å spille innen 15 sekunder");
                    StopTrailer();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Write("trailer", $"YouTube-avspilling utilgjengelig: {ex.GetType().Name}");
        }
    }

    private static string YouTubeEmbedHtml(string videoId)
    {
        var safeId = System.Net.WebUtility.HtmlEncode(videoId);
        return """
            <!doctype html>
            <html><head><meta name="viewport" content="width=device-width,initial-scale=1">
            <style>
            html,body,#player{margin:0;width:100%;height:100%;overflow:hidden;background:#121110}
            #player{position:absolute;left:50%;top:50%;width:max(100vw,177.78vh);height:max(56.25vw,100vh);transform:translate(-50%,-50%)}
            iframe{border:0}
            </style></head><body><div id="player"></div>
            <script>
            function onYouTubeIframeAPIReady(){
              new YT.Player('player',{videoId:'__VIDEO_ID__',width:'100%',height:'100%',
                playerVars:{autoplay:1,mute:1,controls:0,playsinline:1,rel:0},
                events:{onStateChange:e=>{if(e.data===1)chrome.webview.postMessage('playing');if(e.data===0)chrome.webview.postMessage('ended')},
                  onError:()=>chrome.webview.postMessage('error')}
              });
            }
            </script><script src="https://www.youtube.com/iframe_api"></script></body></html>
            """.Replace("__VIDEO_ID__", safeId, StringComparison.Ordinal);
    }


    private readonly Dictionary<string, int?> _steamIdsByTitle = [];

    /// <summary>
    /// Spill som ikke er fra Steam (for eksempel Broforce fra AnkerGames) får traileren fra Steam-siden med samme navn.
    /// Slås opp én gang per spill og kjøring.
    /// </summary>
    private async Task<int?> SteamAppIdByTitleAsync(Game game)
    {
        if (_steamIdsByTitle.TryGetValue(game.Id, out var cached))
        {
            return cached;
        }

        int? appId;
        try
        {
            appId = await new SteamStoreClient(ArtHttp).FindAppIdAsync(Core.Library.GameLibrary.DisplayTitle(game));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        _steamIdsByTitle[game.Id] = appId;
        return appId;
    }

    private void StopTrailer()
    {
        _trailerTimer?.Stop();
        _trailerGameId = null;
        CancelFade(HeroVideo);
        CancelFade(YouTubeTrailer);
        if (HeroVideo.MediaPlayer is { } player)
        {
            HeroVideo.SetMediaPlayer(null);
            player.Dispose();
            _log.Write("trailer", "Trailer stoppet");
        }

        YouTubeTrailer.Opacity = 0;
        if (_youtubeMessageHandler is not null && YouTubeTrailer.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= _youtubeMessageHandler;
            _youtubeMessageHandler = null;
        }
        YouTubeTrailer.CoreWebView2?.Stop();
        YouTubeTrailer.CoreWebView2?.Navigate("about:blank");

        HeroVideo.Opacity = 0;
        MakeRoomForTrailer(false);
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
