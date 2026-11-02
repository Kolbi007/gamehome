// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Metadata;
using GameHome.Metadata;

namespace GameHome.Core.Tests;

public class MetadataPolicyTests
{
    [Fact]
    public void Unofficial_sources_are_off_by_default_unless_the_product_owner_chose_them()
    {
        foreach (var provider in KnownProviders.All.Where(p => p.Terms != TermsStatus.Approved))
        {
            Assert.Equal(KnownProviders.EnabledByProductOwner.Contains(provider.ProviderId),
                KnownProviders.IsEnabledByDefault(provider));
        }
    }

    [Fact]
    public void Steam_local_cache_and_steam_store_are_on_without_setup()
    {
        var enabled = KnownProviders.All.Where(KnownProviders.IsEnabledByDefault).Select(p => p.ProviderId);

        Assert.Equal(["steam-local", "steam-store"], enabled);
    }

    [Fact]
    public void Steam_store_search_picks_only_an_exact_title_match()
    {
        var json = """
            {"total":3,"items":[
              {"type":"app","name":"007 First Light - Deluxe Edition Upgrade","id":3986520},
              {"type":"app","name":"007 First Light™","id":3768760},
              {"type":"app","name":"Something Else","id":5}]}
            """;

        Assert.Equal(3768760, SteamStoreClient.PickAppId(json, "007 First Light"));
        Assert.Null(SteamStoreClient.PickAppId(json, "Mega Man 11"));
        Assert.Null(SteamStoreClient.PickAppId("not json", "x"));
    }

    [Fact]
    public void Steam_store_image_urls_use_the_public_cdn()
    {
        var urls = SteamStoreClient.ImageUrls(3768760);

        Assert.EndsWith("/3768760/library_600x900_2x.jpg", urls.Cover, StringComparison.Ordinal);
        Assert.EndsWith("/3768760/library_hero.jpg", urls.Hero, StringComparison.Ordinal);
        Assert.EndsWith("/3768760/logo.png", urls.Logo, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_ids_are_unique()
    {
        var ids = KnownProviders.All.Select(p => p.ProviderId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
