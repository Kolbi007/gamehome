// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Home;

/// <summary>
/// Eksperimentell FSE-adapter (Windows Xbox mode). Kan mangle helt; GameHome skal da fungere som vanlig fullskjermsapp.
/// </summary>
public interface IHomeIntegration
{
    bool IsAvailable { get; }

    bool IsFullScreenExperienceActive();

    event EventHandler? FullScreenExperienceChanged;
}

/// <summary>Brukes når FSE-adapteren ikke er tilgjengelig.</summary>
public sealed class NoHomeIntegration : IHomeIntegration
{
    public bool IsAvailable => false;

    public bool IsFullScreenExperienceActive() => false;

    public event EventHandler? FullScreenExperienceChanged
    {
        add { }
        remove { }
    }
}
