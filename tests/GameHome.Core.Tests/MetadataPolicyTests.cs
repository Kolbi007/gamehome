// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Metadata;
using GameHome.Metadata;

namespace GameHome.Core.Tests;

public class MetadataPolicyTests
{
    [Fact]
    public void Unofficial_sources_are_never_enabled_by_default()
    {
        foreach (var provider in KnownProviders.All.Where(p => p.Terms != TermsStatus.Approved))
        {
            Assert.False(KnownProviders.IsEnabledByDefault(provider), provider.ProviderId);
        }
    }

    [Fact]
    public void Only_the_local_steam_cache_is_on_without_setup()
    {
        var enabled = KnownProviders.All.Where(KnownProviders.IsEnabledByDefault).Select(p => p.ProviderId);

        Assert.Equal(["steam-local"], enabled);
    }

    [Fact]
    public void Provider_ids_are_unique()
    {
        var ids = KnownProviders.All.Select(p => p.ProviderId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
