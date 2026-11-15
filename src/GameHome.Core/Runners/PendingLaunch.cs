// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Runners;

/// <summary>Tracks the current launch on the caller's UI thread. Each attempt disposes its token source when it returns.</summary>
public sealed class PendingLaunch
{
    private CancellationToken _token;
    private Action? _cancel;

    public bool IsActive => _cancel is not null;

    public CancellationTokenSource Begin()
    {
        if (IsActive)
        {
            throw new InvalidOperationException("A launch is already in progress.");
        }

        var attempt = new CancellationTokenSource();
        _token = attempt.Token;
        _cancel = attempt.Cancel;
        return attempt;
    }

    public bool Owns(CancellationToken token) => IsActive && _token == token;

    public bool Cancel()
    {
        if (_cancel is not { } cancel)
        {
            return false;
        }

        _cancel = null;
        _token = default;
        cancel();
        return true;
    }

    public void Complete(CancellationTokenSource attempt)
    {
        if (Owns(attempt.Token))
        {
            _cancel = null;
            _token = default;
        }
    }
}
