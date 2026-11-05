// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using GameHome.Core.Library;
using GameHome.Metadata;

namespace GameHome.Core.Tests;

public class TitleGuessTests
{
    [Fact]
    public void An_abbreviated_exe_uses_the_folder_name()
    {
        var candidates = TitleGuess.Candidates(
            @"C:\Users\chris\Downloads\AnkerGames\Games\Assassin's Creed Rogue-273\Assassins Creed Rogue\ACC.exe", productName: null);

        Assert.Equal("Assassins Creed Rogue", candidates[0].Title);
        Assert.Equal(TitleOrigin.Folder, candidates[0].Origin);
        Assert.Contains(candidates, c => c.Title == "ACC" && c.Origin == TitleOrigin.FileName);
    }

    [Fact]
    public void The_product_name_in_the_file_wins_when_it_is_a_real_name()
    {
        var candidates = TitleGuess.Candidates(@"C:\Games\x\ACC.exe", "Assassin's Creed® Rogue");
        Assert.Equal("Assassin's Creed Rogue", candidates[0].Title);
        Assert.Equal(TitleOrigin.ProductName, candidates[0].Origin);
    }

    [Fact]
    public void A_normal_exe_name_still_comes_first()
    {
        Assert.Equal("Broforce", TitleGuess.Candidates(@"C:\Games\BroForce v3148\Broforce.exe", null)[0].Title);
        Assert.Equal("Doom", TitleGuess.Candidates(@"C:\Games\DOOM\Doom.exe", null)[0].Title);
    }

    [Theory]
    [InlineData("Assassin's Creed Rogue-273", "Assassin's Creed Rogue")]
    [InlineData("BroForce v3148", "BroForce")]
    [InlineData("Mega Man 11", "Mega Man 11")]
    [InlineData("Hollow_Knight (GOG) [1.5.78]", "Hollow Knight")]
    [InlineData("Game 1.0.2", "Game")]
    public void Names_are_cleaned_without_losing_real_numbers(string raw, string expected) =>
        Assert.Equal(expected, TitleGuess.Clean(raw));

    [Theory]
    [InlineData("Assassins Creed Rogue", "Assassin's Creed® Rogue")]
    [InlineData("Ratchet and Clank", "Ratchet & Clank")]
    [InlineData("Final Fantasy 7", "FINAL FANTASY VII")]
    [InlineData("Pokemon", "Pokémon")]
    public void Matching_forgives_punctuation_trademarks_and_numerals(string ours, string steams) =>
        Assert.Equal(TitleGuess.MatchKey(steams), TitleGuess.MatchKey(ours));

    [Fact]
    public void Matching_still_requires_every_word()
    {
        Assert.NotEqual(TitleGuess.MatchKey("Assassin's Creed"), TitleGuess.MatchKey("Assassin's Creed Rogue"));
        const string json = """{"items":[{"type":"app","name":"Assassin's Creed® Rogue Deluxe","id":1},{"type":"app","name":"Assassin's Creed® Rogue","id":311560}]}""";
        Assert.Equal(311560, SteamStoreClient.PickAppId(json, "Assassins Creed Rogue"));
        Assert.Equal("Assassin's Creed Rogue", SteamStoreClient.PickMatch(json, "Assassins Creed Rogue")!.Name);
    }
}
