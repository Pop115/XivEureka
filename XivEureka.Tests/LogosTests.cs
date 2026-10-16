using System.Text.Json;
using XivEureka.Logos;
using Xunit;

namespace XivEureka.Tests;

public class ShardReaderTests
{
    private static int[] RealCapture() =>
        JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine("TestData", "shards-2026-10-11.json")))!;

    [Fact]
    public void Decodes_the_in_game_capture()
    {
        var stock = ShardReader.Decode(RealCapture())!;

        Assert.Equal(12, stock.Count);
        Assert.Equal(5, stock[24015]);   // Mémoire d'un prêtre guerrier
        Assert.Equal(16, stock[24031]);  // Tranquillisant
        Assert.Equal(1, stock[24034]);   // Extra Soin, last slot
    }

    [Fact]
    public void Rejects_an_inconsistent_array()
    {
        Assert.Null(ShardReader.Decode([]));
        Assert.Null(ShardReader.Decode([5, 0, 0, 1, 2, 3]));
    }

    [Fact]
    public void Every_captured_mneme_is_in_the_catalog()
    {
        var catalog = LogosCatalog.Embedded();
        Assert.All(ShardReader.Decode(RealCapture())!.Keys, item => Assert.Contains(item, catalog.Mnemes.Keys));
    }
}

public class LogosCatalogTests
{
    private readonly LogosCatalog catalog = LogosCatalog.Embedded();

    [Fact]
    public void Holds_the_56_actions_and_28_mnemes()
    {
        Assert.Equal(Enumerable.Range(1, 56), catalog.Actions.Select(a => a.Number));
        Assert.Equal(28, catalog.Mnemes.Count);
    }

    [Fact]
    public void Each_mneme_comes_from_a_single_shard()
    {
        var byShard = catalog.Shards.SelectMany(s => s.Mnemes).ToList();
        Assert.Equal(byShard.Count, byShard.Distinct().Count());
        Assert.Equal(catalog.Mnemes.Keys.Order(), byShard.Order());
    }

    [Fact]
    public void Recipes_only_use_known_mnemes()
    {
        Assert.All(catalog.Actions, a =>
        {
            Assert.NotEmpty(a.Recipes);
            Assert.All(a.Recipes.SelectMany(r => r), m => Assert.Contains(m, catalog.Mnemes.Keys));
        });
    }
}

public class CraftingTests
{
    private static readonly LogosAction Example = new(1, 100, "THMPC", 3, [[1, 1, 1], [1, 2]]);

    [Fact]
    public void A_repeated_mneme_counts_as_many_times()
    {
        var recipe = Crafting.Recipe([1, 1, 1], new Dictionary<uint, int> { [1] = 7 });
        Assert.Equal(2, recipe.Craftable);
        Assert.Equal(0, recipe.Missing);

        var short_ = Crafting.Recipe([1, 1, 1], new Dictionary<uint, int> { [1] = 1 });
        Assert.Equal(0, short_.Craftable);
        Assert.Equal(2, short_.Missing);
    }

    [Fact]
    public void An_action_takes_the_best_recipe_without_adding_them_up()
    {
        var state = Crafting.Action(Example, new Dictionary<uint, int> { [1] = 3, [2] = 5 }, new HashSet<uint>());
        Assert.Equal(3, state.Craftable);
        Assert.True(state.ToLearn);
    }

    [Fact]
    public void A_learned_action_is_no_longer_to_learn()
    {
        var state = Crafting.Action(Example, new Dictionary<uint, int> { [1] = 3 }, new HashSet<uint> { 100 });
        Assert.False(state.ToLearn);
        Assert.Equal(1, state.Craftable);
    }

    [Fact]
    public void The_closest_recipe_is_the_one_missing_the_least()
    {
        var state = Crafting.Action(Example, new Dictionary<uint, int> { [1] = 1 }, new HashSet<uint>());
        Assert.Equal(0, state.Craftable);
        Assert.Equal(1, state.Closest.Missing);
        Assert.Equal([1u, 2u], state.Closest.Recipe);
    }

    [Fact]
    public void Usefulness_ignores_learned_actions()
    {
        Assert.Equal(1, Crafting.Usefulness([Example], new HashSet<uint>())[2]);
        Assert.Empty(Crafting.Usefulness([Example], new HashSet<uint> { 100 }));
    }

    [Fact]
    public void Counts_the_actions_each_shard_would_move_forward()
    {
        var catalog = new LogosCatalog(
            [new Shard(1, [100, 101]), new Shard(2, [200])],
            [new Mneme(100, "a"), new Mneme(101, "b"), new Mneme(200, "c")],
            [
                new LogosAction(1, 9001, "T", null, [[100, 200]]), // missing 100 and 200 → shards 1 and 2
                new LogosAction(2, 9002, "T", null, [[101]]),      // craftable → ignored
                new LogosAction(3, 9003, "T", null, [[200, 200]]), // learned → ignored
            ]);
        var stock = new Dictionary<uint, int> { [101] = 1 };
        var useful = Crafting.UsefulShards(catalog, stock, new HashSet<uint> { 9003 });
        Assert.Equal(1, useful[1]);
        Assert.Equal(1, useful[2]);
    }

    [Theory]
    [InlineData("Évocation Logos : Bouclier", "Bouclier")]
    [InlineData("Mémoire d'un prêtre guerrier", "Mémoire d'un prêtre guerrier")]
    [InlineData("Protect L", "Protect L")]
    public void Shortens_prefixed_names(string name, string expected) =>
        Assert.Equal(expected, GameTexts.Shorten(name));
}

public class NotebookReaderTests
{
    [Fact]
    public void Decodes_the_notebook_captured_in_game()
    {
        var catalog = LogosCatalog.Embedded();
        var values = JsonSerializer.Deserialize<long[]>(File.ReadAllText(Path.Combine("TestData", "notebook-2026-10-11.json")))!;

        var learned = NotebookReader.Decode(values, catalog)!;

        var numbers = catalog.Actions.Where(a => learned.Contains(a.Action)).Select(a => a.Number);
        Assert.Equal([1, 2, 3, 4, 5, 6, 11, 12, 13, 14, 16, 17, 19, 20, 22, 23, 26, 27, 31, 32, 33, 34, 37, 38, 39], numbers);
    }

    [Fact]
    public void Rejects_a_notebook_too_short_or_unknown()
    {
        var catalog = LogosCatalog.Embedded();
        Assert.Null(NotebookReader.Decode([1, 2, 3], catalog));
        Assert.Null(NotebookReader.Decode(Enumerable.Repeat(99L, 56).ToList(), catalog));
    }
}

public class LearnedMessageTests
{
    // French names from the Action sheet (client of 2026-10-16).
    private static readonly (uint, string)[] Actions =
    [
        (12958, "Mémoire d'un prêtre guerrier"),
        (12974, "Évocation Logos : Méga Paralysie"),
        (12989, "Évocation Logos : Soin"),
    ];

    [Fact]
    public void Matches_the_full_name_ignoring_case_and_quotes()
    {
        Assert.Equal(12974u, LearnedMessage.Match("“Évocation Logos : méga paralysie”", Actions));
        Assert.Equal(12958u, LearnedMessage.Match("Mémoire d'un prêtre guerrier", Actions));
    }

    [Fact]
    public void Matches_the_short_name()
    {
        Assert.Equal(12989u, LearnedMessage.Match("Soin", Actions));
    }

    [Fact]
    public void Unknown_names_match_nothing()
    {
        Assert.Null(LearnedMessage.Match("Soin X", Actions));
        Assert.Null(LearnedMessage.Match("", Actions));
    }
}
