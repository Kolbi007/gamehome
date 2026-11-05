// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

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

        if ((SteamAppIdFor(game) ?? await SteamAppIdByTitleAsync(game)) is not { } appId)
        {
            _log.Write("trailer", $"Ingen Steam-side for «{game.Title}»; ingen trailer");
            return;
        }

        if (_trailerGameId != game.Id)
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

        if (url is null)
        {
            _log.Write("trailer", $"Steam har ingen trailer for «{game.Title}» (appid {appId})");
            return;
        }

        if (_trailerGameId != game.Id || !TrailersAllowed)
        {
            return;
        }

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
        _log.Write("trailer", $"Trailer for «{game.Title}» (appid {appId})");
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
        if (HeroVideo.MediaPlayer is { } player)
        {
            HeroVideo.SetMediaPlayer(null);
            player.Dispose();
            _log.Write("trailer", "Trailer stoppet");
        }

        HeroVideo.Opacity = 0;
        MakeRoomForTrailer(false);
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
