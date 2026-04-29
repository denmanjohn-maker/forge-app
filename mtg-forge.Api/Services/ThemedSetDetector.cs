using System.Text;

namespace MtgForge.Api.Services;

/// <summary>
/// Detects mentions of Magic: The Gathering Universes Beyond / themed product lines
/// (Avatar: The Last Airbender, Warhammer 40K, TMNT, LOTR, etc.) inside free-form
/// user notes, and produces a prompt addendum that asks the deck-generation
/// pipeline to incorporate cards from those sets.
/// </summary>
public static class ThemedSetDetector
{
    public sealed record ThemedSet(string DisplayName, IReadOnlyList<string> Triggers, string PromptFragment);

    public static readonly IReadOnlyList<ThemedSet> KnownSets =
    [
        new ThemedSet(
            "Avatar: The Last Airbender",
            ["airbender", "air bender", "last airbender", "atla", "avatar the last", "avatar the air", "aang", "katara", "sokka", "zuko", "toph", "korra"],
            "the Universes Beyond product 'Avatar: The Last Airbender'. Lean into the four-nations flavor (Air/Water/Earth/Fire) by including legendary characters such as Aang, Katara, Sokka, Zuko, Toph Beifong, and Iroh, plus themed support cards from the set, where they fit the requested color identity."),

        new ThemedSet(
            "Warhammer 40,000",
            ["warhammer", "wh40k", "imperium of man", "primaris", "space marine", "tyranid", "necron", "abaddon", "marneus calgar", "the swarmlord"],
            "the Universes Beyond Commander product 'Warhammer 40,000'. Include iconic 40K characters such as Abaddon the Despoiler, Marneus Calgar, Szarekh the Silent King, The Swarmlord, and Magus Lucea Kane when colors permit, and pull themed support cards from the four preconstructed decks (Imperium of Man, Necron Dynasties, The Ruinous Powers, Tyranid Swarm)."),

        new ThemedSet(
            "Teenage Mutant Ninja Turtles",
            ["tmnt", "ninja turtles", "teenage mutant", "leonardo", "donatello", "raphael", "michelangelo", "splinter", "shredder"],
            "the Universes Beyond Secret Lair / Commander product 'Teenage Mutant Ninja Turtles'. Feature the four turtles (Leonardo, Donatello, Raphael, Michelangelo), Master Splinter, and Shredder where the color identity allows."),

        new ThemedSet(
            "The Lord of the Rings: Tales of Middle-earth",
            ["lord of the rings", "lotr", "tales of middle-earth", "middle-earth", "middle earth", "frodo", "gandalf", "aragorn", "sauron", "gollum", "the one ring"],
            "the Universes Beyond set 'The Lord of the Rings: Tales of Middle-earth'. Incorporate marquee cards such as The One Ring, Orcish Bowmasters, Sauron the Dark Lord, Gandalf the White, Aragorn and Arwen Wed, and Frodo Baggins where they fit."),

        new ThemedSet(
            "Doctor Who",
            ["doctor who", "dr who", "tardis", "the tardis", "dalek", "cyberman", "time lord"],
            "the Universes Beyond Commander product 'Doctor Who'. Pull themed cards from the four Doctor Who Commander decks, including The Tenth Doctor, The Eleventh Doctor, The Thirteenth Doctor, and Davros, Dalek Creator."),

        new ThemedSet(
            "Fallout",
            ["fallout", "vault dweller", "wasteland survivor", "nuka-cola", "nuka cola", "brotherhood of steel"],
            "the Universes Beyond Commander product 'Fallout'. Feature themed cards from the four Fallout Commander decks (Mutant Menace, Hail, Caesar!, Science!, Scrappy Survivors), e.g. The Wise Mothman, Caesar, Legion's Emperor, Dogmeat, Ever Loyal, Vault 101: Birthplace."),

        new ThemedSet(
            "Final Fantasy",
            ["final fantasy", "ffvii", "ff7", "ffvi", "ff6", "cloud strife", "sephiroth", "tidus", "yuna", "cecil harvey", "y'shtola"],
            "the Universes Beyond set 'Final Fantasy'. Include iconic Final Fantasy characters and locations across the series where the color identity allows."),

        new ThemedSet(
            "Marvel",
            ["marvel", "spider-man", "spiderman", "x-men", "avengers", "wolverine", "iron man", "captain america"],
            "the Universes Beyond 'Marvel' product line. Feature Marvel super-hero and super-villain cards that align with the requested colors and strategy."),

        new ThemedSet(
            "Stranger Things",
            ["stranger things", "demogorgon", "the upside down", "hawkins"],
            "the Universes Beyond Secret Lair 'Stranger Things' drop, e.g. Eleven, the Mage, Hopper, Chief of Police, Demogorgon, Killer of Kin."),

        new ThemedSet(
            "The Walking Dead",
            ["walking dead", "rick grimes", "negan"],
            "the Universes Beyond Secret Lair 'The Walking Dead' drop, e.g. Rick, Steadfast Leader, Negan, the Cold-Blooded, Glenn, the Voice of Calm."),

        new ThemedSet(
            "Street Fighter",
            ["street fighter", "ryu apprentice", "chun-li", "ken master", "m. bison", "akuma"],
            "the Universes Beyond Secret Lair 'Street Fighter' drop, e.g. Ryu, World Warrior, Chun-Li, Countless Kicks, Ken, Burning Brawler."),

        new ThemedSet(
            "Jurassic World",
            ["jurassic park", "jurassic world"],
            "the Universes Beyond Jurassic World Collection. Include themed dinosaur and Jurassic Park / Jurassic World cards across the requested colors."),

        new ThemedSet(
            "Transformers",
            ["transformers", "optimus prime", "megatron", "autobot", "decepticon"],
            "the Universes Beyond 'Transformers' Secret Lair. Feature Optimus Prime, Hero of Cybertron, Megatron, Tyrant, and other Autobot/Decepticon cards that fit the requested colors."),

        new ThemedSet(
            "Assassin's Creed",
            ["assassin's creed", "assassins creed", "ezio", "altair", "altaïr", "edward kenway", "bayek", "kassandra"],
            "the Universes Beyond Beyond Booster set 'Assassin's Creed'. Pull themed Assassin and historical-figure cards across the requested colors."),

        new ThemedSet(
            "SpongeBob SquarePants",
            ["spongebob", "sponge bob", "squarepants", "bikini bottom", "patrick star", "squidward"],
            "the Universes Beyond Secret Lair 'SpongeBob SquarePants' drop, e.g. SpongeBob SquarePants, Patrick Star, Squidward Tentacles."),
    ];

    /// <summary>
    /// Returns the themed sets referenced by <paramref name="notes"/>, if any.
    /// Matching is case-insensitive substring matching against curated trigger
    /// phrases for each set.
    /// </summary>
    public static IReadOnlyList<ThemedSet> Detect(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return [];

        var lowered = notes.ToLowerInvariant();
        return KnownSets
            .Where(s => s.Triggers.Any(t => lowered.Contains(t)))
            .ToList();
    }

    /// <summary>
    /// Builds a multi-line prompt addendum describing the matched themed sets,
    /// or returns null when no themed set is referenced in the notes.
    /// </summary>
    public static string? BuildPromptAddendum(string? notes)
    {
        var matches = Detect(notes);
        if (matches.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("THEMED SET REQUEST: The user's notes reference the following Magic: The Gathering Universes Beyond / themed product line(s). Prioritize including cards from these sets where they are legal in the chosen format and color identity:");
        foreach (var set in matches)
            sb.AppendLine($"- {set.DisplayName}: {set.PromptFragment}");
        sb.Append("Aim to incorporate at least 8-12 themed cards from the matched set(s), and prefer a themed legendary creature as the commander when one fits the requested color identity.");
        return sb.ToString();
    }
}
