// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Runners;

namespace GameHome.Windows;

/// <summary>En lokal økt med et vindu GameHome kan gi forgrunnen tilbake til.</summary>
public interface IForegroundSession : ISession, IDisposable
{
    /// <summary>Hovedvinduet til spillet, eller 0 hvis det ikke finnes ennå.</summary>
    nint MainWindowHandle { get; }

    /// <summary>Kort status for «Kjører nå» og loggen, for eksempel «Starter via Steam».</summary>
    string Status { get; }

    /// <summary>Hendelser som skal i loggen.</summary>
    event EventHandler<string>? Note;

    /// <summary>Statusen endret seg.</summary>
    event EventHandler? StatusChanged;
}
