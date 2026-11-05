// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GameHome.App;

/// <summary>
/// Oppstartsanimasjon: ordmerket toner inn og en tynn aksentlinje brettes ut fra midten; så pakker Home seg ut fra midten
/// og utover (hovedfeltet, spillraden, knappene, til slutt toppen). Rolig og kort (ca. 1,2 s), uten sprett. Hoppes over
/// når animasjoner er slått av i Windows. Tar aldri imot trykk, og fokus er allerede satt.
/// </summary>
public sealed partial class MainWindow
{
    private bool _introPlayed;

    private static readonly EasingFunctionBase Gentle = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };

    private void PlayIntro()
    {
        if (_introPlayed)
        {
            return;
        }

        _introPlayed = true;
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            SplashPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var story = new Storyboard();

        // 1. Ordmerket og linjen (0–550 ms).
        Animate(story, SplashWordmark, "Opacity", 0, 1, 0, 450);
        Animate(story, SplashScale, "ScaleX", 0.94, 1, 0, 600);
        Animate(story, SplashScale, "ScaleY", 0.94, 1, 0, 600);
        Animate(story, SplashLineScale, "ScaleX", 0, 1, 150, 500);

        // 2. Splash forsvinner mens Home pakker seg ut fra midten (650–1250 ms).
        Animate(story, SplashPanel, "Opacity", 1, 0, 650, 450);
        Unfold(story, HeroInfo, 650, fromY: 18);
        Unfold(story, GameTilesScroll, 740, fromY: -14);
        Unfold(story, ToolTiles, 830, fromY: -24);
        Unfold(story, TopBar, 920, fromY: 24);

        story.Completed += (_, _) =>
        {
            SplashPanel.Visibility = Visibility.Collapsed;
            foreach (var element in new UIElement[] { HeroInfo, GameTilesScroll, ToolTiles, TopBar })
            {
                element.Opacity = 1;
                element.RenderTransform = null;
            }
        };
        story.Begin();

        // Sikkerhetsnett: splash skal aldri bli liggende over Home.
        var failsafe = DispatcherQueue.CreateTimer();
        failsafe.Interval = TimeSpan.FromSeconds(3);
        failsafe.IsRepeating = false;
        failsafe.Tick += (_, _) =>
        {
            SplashPanel.Visibility = Visibility.Collapsed;
            foreach (var element in new UIElement[] { HeroInfo, GameTilesScroll, ToolTiles, TopBar })
            {
                element.Opacity = 1;
            }
        };
        failsafe.Start();
    }

    /// <summary>Elementet toner inn, vokser litt og glir mot sin plass fra midten av skjermen.</summary>
    private static void Unfold(Storyboard story, FrameworkElement element, int beginMs, double fromY)
    {
        var transform = new CompositeTransform { ScaleX = 0.96, ScaleY = 0.96, TranslateY = fromY };
        element.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5);
        element.RenderTransform = transform;
        element.Opacity = 0;
        Animate(story, element, "Opacity", 0, 1, beginMs, 500);
        Animate(story, transform, "ScaleX", 0.96, 1, beginMs, 600);
        Animate(story, transform, "ScaleY", 0.96, 1, beginMs, 600);
        Animate(story, transform, "TranslateY", fromY, 0, beginMs, 600);
    }

    private static void Animate(Storyboard story, DependencyObject target, string property, double from, double to, int beginMs, int durationMs)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = Gentle,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
