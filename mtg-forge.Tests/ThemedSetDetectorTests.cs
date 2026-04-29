using MtgForge.Api.Services;

namespace MtgForge.Tests;

public class ThemedSetDetectorTests
{
    [Theory]
    [InlineData("avatar the air bender",                "Avatar: The Last Airbender")]
    [InlineData("Build me an Avatar: The Last Airbender deck", "Avatar: The Last Airbender")]
    [InlineData("aang and katara please",               "Avatar: The Last Airbender")]
    [InlineData("warhammer 40k imperium of man",        "Warhammer 40,000")]
    [InlineData("TMNT please",                          "Teenage Mutant Ninja Turtles")]
    [InlineData("teenage mutant ninja turtles",         "Teenage Mutant Ninja Turtles")]
    [InlineData("LOTR with Frodo and Gandalf",          "The Lord of the Rings: Tales of Middle-earth")]
    [InlineData("Tales of Middle-earth flavor",         "The Lord of the Rings: Tales of Middle-earth")]
    [InlineData("doctor who tardis",                    "Doctor Who")]
    [InlineData("fallout vault dweller",                "Fallout")]
    [InlineData("final fantasy ffvii sephiroth",        "Final Fantasy")]
    [InlineData("marvel x-men",                         "Marvel")]
    [InlineData("stranger things demogorgon",           "Stranger Things")]
    [InlineData("walking dead negan",                   "The Walking Dead")]
    [InlineData("street fighter chun-li",               "Street Fighter")]
    [InlineData("jurassic park dinos",                  "Jurassic World")]
    [InlineData("transformers optimus prime",           "Transformers")]
    [InlineData("assassin's creed ezio",                "Assassin's Creed")]
    [InlineData("spongebob bikini bottom",              "SpongeBob SquarePants")]
    public void Detect_ReturnsMatchingSet(string notes, string expectedDisplayName)
    {
        var matches = ThemedSetDetector.Detect(notes);

        Assert.Contains(matches, m => m.DisplayName == expectedDisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("just a normal aggro deck please")]
    [InlineData("budget under $40")]
    public void Detect_ReturnsEmpty_ForNoMatch(string? notes)
    {
        var matches = ThemedSetDetector.Detect(notes);

        Assert.Empty(matches);
    }

    [Fact]
    public void Detect_FindsMultipleSets_WhenNotesReferenceSeveral()
    {
        var matches = ThemedSetDetector.Detect("crossover between avatar the airbender and warhammer 40k");

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.DisplayName == "Avatar: The Last Airbender");
        Assert.Contains(matches, m => m.DisplayName == "Warhammer 40,000");
    }

    [Fact]
    public void BuildPromptAddendum_ReturnsNull_ForNoMatch()
    {
        Assert.Null(ThemedSetDetector.BuildPromptAddendum(null));
        Assert.Null(ThemedSetDetector.BuildPromptAddendum(""));
        Assert.Null(ThemedSetDetector.BuildPromptAddendum("standard goblin tribal"));
    }

    [Fact]
    public void BuildPromptAddendum_IncludesMatchedSetNamesAndGuidance()
    {
        var addendum = ThemedSetDetector.BuildPromptAddendum("avatar the air bender please");

        Assert.NotNull(addendum);
        Assert.Contains("THEMED SET REQUEST", addendum);
        Assert.Contains("Avatar: The Last Airbender", addendum);
        Assert.Contains("8-12 themed cards", addendum);
    }

    [Fact]
    public void Detect_IsCaseInsensitive()
    {
        Assert.NotEmpty(ThemedSetDetector.Detect("AVATAR THE LAST AIRBENDER"));
        Assert.NotEmpty(ThemedSetDetector.Detect("WaRhAmMeR 40K"));
    }
}
