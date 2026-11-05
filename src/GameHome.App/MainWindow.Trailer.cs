// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Model;
using GameHome.Metadata;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GameHome.App;

/// <summary>
/// Stille trailer bak hovedfeltet: etter 3 sekunder på et Steam-spill spilles butikkens trailer uten lyd, dempet bak
/// gradienten, som på PS5. Bare i lader (video bruker batteri), aldri under spill, og ikke når animasjoner er av.
/// Nettverkskall: Steam-butikkens appdetails (filters=movies) og selve videoen fra Steams CDN.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan TrailerDelay = TimeSpan.FromSeconds(3);
    private readonly Dictionary<int, string?> _trailers = [];
    private DispatcherQueueTimer? _trailerTimer;
    private string? _trailerGameId;

    private bool TrailersAllowed =>
        !OnBattery && !SessionActive && _windowActive && new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Kalles når en flis i Home får fokus: traileren starter hvis fokus blir værende.</summary>
    private void ScheduleTrailer(Game game)
    {
        StopTrailer();
        if (!TrailersAllowed || SteamAppIdFor(game) is null)
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
        if (!TrailersAllowed || _trailerGameId != game.Id || AnyOverlayOpen || _attracting || SteamAppIdFor(game) is not { } appId)
        {
            return;
        }

        if (!_trailers.TryGetValue(appId, out var url))
        {
            try
            {
                url = await new SteamStoreClient(ArtHttp).GetTrailerAsync(appId);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                url = null;
            }

            _trailers[appId] = url;
        }

        if (url is null || _trailerGameId != game.Id || !TrailersAllowed)
        {
            return;
        }

        var player = new MediaPlayer { IsMuted = true, IsLoopingEnabled = true, AutoPlay = true };
        player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(HeroVideo.MediaPlayer, player))
            {
                FadeTo(HeroVideo, 0.9, 900);
            }
        });
        player.Source = MediaSource.CreateFromUri(new Uri(url));
        HeroVideo.SetMediaPlayer(player);
        _log.Write("trailer", $"Trailer for «{game.Title}» (appid {appId})");
    }

    private void StopTrailer()
    {
        _trailerTimer?.Stop();
        _trailerGameId = null;
        if (HeroVideo.MediaPlayer is { } player)
        {
            HeroVideo.SetMediaPlayer(null);
            player.Dispose();
        }

        HeroVideo.Opacity = 0;
    }

    private static void FadeTo(UIElement element, double to, int milliseconds)
    {
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
        story.Begin();
    }
}
