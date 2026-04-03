using System.Reflection;
using MtgDeckForge.Api.Controllers;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Tests;

public class DecksControllerCsvHelpersTests
{
    [Fact]
    public void DetectFormat_ReturnsExpectedFormats()
    {
        Assert.Equal("moxfield", InvokeDetectFormat(["count", "tradelist count", "name", "tags"]));
        Assert.Equal("archidekt", InvokeDetectFormat(["quantity", "name", "categories"]));
        Assert.Equal("deckbox", InvokeDetectFormat(["count", "tradelist count", "name", "card number"]));
        Assert.Equal("deckstats", InvokeDetectFormat(["amount", "card_name", "is_commander"]));
        Assert.Equal("default", InvokeDetectFormat(["count", "name", "category"]));
    }

    [Fact]
    public void ParseCsvLine_HandlesQuotedCommas_AndEscapedQuotes()
    {
        var fields = InvokeParseCsvLine("2,\"Atraxa, Praetors\"\" Voice\",Commander");

        Assert.Equal(3, fields.Count);
        Assert.Equal("2", fields[0]);
        Assert.Equal("Atraxa, Praetors\" Voice", fields[1]);
        Assert.Equal("Commander", fields[2]);
    }

    [Fact]
    public void ParseDefault_UsesFallbackColumns_AndDefaults()
    {
        var card = InvokeParseDefault(
            ["3", "\"Arcane Signet\""],
            ["count", "name"]);

        Assert.NotNull(card);
        Assert.Equal("Arcane Signet", card!.Name);
        Assert.Equal(3, card.Quantity);
        Assert.Equal("Mainboard", card.Category);
    }

    [Fact]
    public void ParseDeckstats_SetsCommanderAndSideboardCategories()
    {
        var headers = new List<string> { "amount", "card_name", "is_commander", "is_sideboard" };
        var commander = InvokeParseDeckstats(["1", "Atraxa, Praetors' Voice", "1", "0"], headers);
        var sideboard = InvokeParseDeckstats(["2", "Swords to Plowshares", "0", "1"], headers);
        var mainboard = InvokeParseDeckstats(["3", "Cultivate", "0", "0"], headers);

        Assert.Equal("Commander", commander!.Category);
        Assert.Equal("Sideboard", sideboard!.Category);
        Assert.Equal("Mainboard", mainboard!.Category);
    }

    [Fact]
    public void ParseMoxfield_UsesTagsToSetCommander()
    {
        var card = InvokeParseMoxfield(
            ["1", "", "Krenko, Mob Boss", "", "", "", "", "Commander"],
            ["count", "tradelist count", "name", "edition", "condition", "language", "foil", "tags"]);

        Assert.NotNull(card);
        Assert.Equal("Krenko, Mob Boss", card!.Name);
        Assert.Equal("Commander", card.Category);
        Assert.Equal(1, card.Quantity);
    }

    private static string InvokeDetectFormat(List<string> headers) =>
        (string)InvokePrivateStatic(nameof(DecksController), "DetectFormat", [headers])!;

    private static List<string> InvokeParseCsvLine(string line) =>
        (List<string>)InvokePrivateStatic(nameof(DecksController), "ParseCsvLine", [line])!;

    private static CardEntry? InvokeParseDefault(List<string> fields, List<string> headers) =>
        (CardEntry?)InvokePrivateStatic(nameof(DecksController), "ParseDefault", [fields, headers]);

    private static CardEntry? InvokeParseDeckstats(List<string> fields, List<string> headers) =>
        (CardEntry?)InvokePrivateStatic(nameof(DecksController), "ParseDeckstats", [fields, headers]);

    private static CardEntry? InvokeParseMoxfield(List<string> fields, List<string> headers) =>
        (CardEntry?)InvokePrivateStatic(nameof(DecksController), "ParseMoxfield", [fields, headers]);

    private static object? InvokePrivateStatic(string typeName, string methodName, object?[] args)
    {
        var type = typeof(DecksController);
        Assert.Equal(typeName, type.Name);
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }
}
