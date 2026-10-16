using XivEureka.Logos;
using Xunit;

namespace XivEureka.Tests;

/// <summary>
/// Randomized tests: plausible stocks, notebooks and whole manipulator sessions drawn from a
/// seeded <see cref="Random"/>. Each seed is its own test case, so a failure names the seed that
/// replays it exactly.
/// </summary>
public class LogosRandomTests
{
    private static readonly LogosCatalog Catalog = LogosCatalog.Embedded();
    private static readonly uint[] Mnemes = [.. Catalog.Mnemes.Keys.Order()];

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 100)];

    /// <summary>A plausible stock: a few kinds of mnemes, mostly small quantities, a big one now and then.</summary>
    private static Dictionary<uint, int> RandomStock(Random rng)
    {
        var stock = new Dictionary<uint, int>();
        foreach (var item in Mnemes.OrderBy(_ => rng.Next()).Take(rng.Next(0, Mnemes.Length + 1)))
            stock[item] = rng.Next(10) == 0 ? rng.Next(10, 40) : rng.Next(1, 6);
        return stock;
    }

    private static HashSet<int> RandomNotebook(Random rng) =>
        [.. Catalog.Actions.Select(a => a.Number).Where(_ => rng.Next(3) == 0)];

    private static Dictionary<uint, int> Positive(IReadOnlyDictionary<uint, int> s) =>
        s.Where(e => e.Value > 0).ToDictionary(e => e.Key, e => e.Value);

    // ---------- readers ----------

    [Theory, MemberData(nameof(Seeds))]
    public void Shard_list_round_trip(int seed)
    {
        var stock = RandomStock(new Random(seed));
        var array = new FakeLogosGame(Catalog, stock).ShardArray();
        Assert.Equal(Positive(stock), ShardReader.Decode(array));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Notebook_round_trip(int seed)
    {
        var learned = RandomNotebook(new Random(seed));
        var game = new FakeLogosGame(Catalog, [], learned);
        Assert.Equal(game.TrueLearned, NotebookReader.Decode(game.NotebookValues(), Catalog));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Manipulator_result_only_counts_while_shown(int seed)
    {
        var rng = new Random(seed);
        var values = Enumerable.Repeat(-1L, 38).ToArray();
        var placed = Enumerable.Range(0, rng.Next(7)).Select(_ => Mnemes[rng.Next(Mnemes.Length)]).ToList();
        for (var s = 0; s < 6; s++) values[15 + s] = s < placed.Count ? placed[s] : 0;
        values[0] = rng.Next(6);
        values[27] = rng.Next(57);

        var view = ManipulatorReader.Decode(values)!;

        Assert.Equal(placed, view.Placed);
        Assert.Equal(values[0] == 2 && values[27] > 0 ? (int)values[27] : null, view.Extracted);
    }

    // ---------- crafting ----------

    [Theory, MemberData(nameof(Seeds))]
    public void Craftable_means_a_whole_recipe_is_owned(int seed)
    {
        var rng = new Random(seed);
        var stock = RandomStock(rng);
        var learned = new HashSet<uint>(Catalog.Actions.Where(_ => rng.Next(2) == 0).Select(a => a.Action));
        foreach (var action in Catalog.Actions)
        {
            var state = Crafting.Action(action, stock, learned);
            var affordable = action.Recipes.Any(r => r.GroupBy(i => i).All(g => stock.GetValueOrDefault(g.Key) >= g.Count()));
            Assert.Equal(affordable, state.Craftable > 0);
            Assert.Equal(state.Craftable, Crafting.Action(action, stock, new HashSet<uint>()).Craftable);
            Assert.False(state.ToLearn && state.Learned);
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void One_more_mneme_never_makes_less_craftable(int seed)
    {
        var rng = new Random(seed);
        var stock = RandomStock(rng);
        var more = new Dictionary<uint, int>(stock);
        var item = Mnemes[rng.Next(Mnemes.Length)];
        more[item] = more.GetValueOrDefault(item) + 1;
        foreach (var action in Catalog.Actions)
            Assert.True(Crafting.Action(action, more, new HashSet<uint>()).Craftable >= Crafting.Action(action, stock, new HashSet<uint>()).Craftable);
    }

    [Fact]
    public void No_two_actions_share_a_recipe()
    {
        var recipes = Catalog.Actions.SelectMany(a => a.Recipes.Select(r => (a.Number, Key: string.Join(",", r.Order()))));
        Assert.All(recipes.GroupBy(r => r.Key), g => Assert.Single(g.Select(r => r.Number).Distinct()));
    }

    // ---------- hunt plan ----------

    /// <summary>
    /// The "Quoi taper" chain agrees with the board for any stock and notebook: same useful shards
    /// with the same counts, craftable actions apart, each needed mneme really missing and given by
    /// its shard, and every action not learned nor craftable reachable through some shard.
    /// </summary>
    [Theory, MemberData(nameof(Seeds))]
    public void Hunt_plan_agrees_with_the_board(int seed)
    {
        var rng = new Random(seed);
        var stock = RandomStock(rng);
        var learned = new HashSet<uint>(Catalog.Actions.Where(a => rng.Next(2) == 0).Select(a => a.Action));
        var zones = XivEureka.Zones.NmCatalog.Embedded().Zones;

        var plan = Hunt.Compute(Catalog, zones, stock, learned);

        var useful = Crafting.UsefulShards(Catalog, stock, learned);
        Assert.Equal(useful.OrderBy(u => u.Key), plan.Shards.ToDictionary(s => s.Shard, s => s.Actions.Count).OrderBy(u => u.Key));
        Assert.Equal(Catalog.Actions.Count(a => !learned.Contains(a.Action)), plan.NotLearned);
        Assert.Equal(Catalog.Actions.Where(a => Crafting.Action(a, stock, learned).ToLearn).Select(a => a.Number),
            plan.CraftableNow.Select(a => a.Number));
        foreach (var shard in plan.Shards)
        {
            var gives = Catalog.Shards.Single(s => s.Item == shard.Shard).Mnemes;
            Assert.Equal(gives.Order(), shard.Mnemes.Select(m => m.Mneme).Order());
            Assert.All(shard.Sources, src => Assert.Equal(shard.Shard, src.Nm.Shard));
            foreach (var need in shard.Mnemes.SelectMany(m => m.NeededBy.Select(n => (m, n))))
            {
                Assert.InRange(need.n.Missing, 1, 3);
                Assert.True(need.n.Action.Recipes.Any(r => r.Count(x => x == need.m.Mneme) >= stock.GetValueOrDefault(need.m.Mneme) + need.n.Missing));
                Assert.DoesNotContain(need.n.Action.Action, learned);
            }
        }
        var reachable = plan.Shards.SelectMany(s => s.Actions).Select(a => a.Number).ToHashSet();
        foreach (var action in Catalog.Actions.Where(a => !learned.Contains(a.Action) && Crafting.Action(a, stock, learned).Craftable == 0))
            Assert.Contains(action.Number, reachable);
    }

    // ---------- learned message ----------

    [Theory, MemberData(nameof(Seeds))]
    public void Learned_message_finds_the_action_back_whatever_the_casing(int seed)
    {
        var rng = new Random(seed);
        var names = Catalog.Actions.Select(a => (a.Action, Name: $"Évocation Logos : Nom{a.Number}x{rng.Next(1000)}")).ToList();
        var (action, name) = names[rng.Next(names.Count)];
        var shown = new string([.. name.Select(c => rng.Next(2) == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c))]);
        if (rng.Next(2) == 0) shown = $"“{shown}”";
        if (rng.Next(2) == 0) shown = GameTexts.Shorten(shown.Trim('“', '”'));
        Assert.Equal(action, LearnedMessage.Match(shown, names));
    }

    // ---------- whole sessions ----------

    /// <summary>
    /// A random session at the manipulator: loot and appraise shards, open, place, take back, place a whole recipe, extract,
    /// go through "Remplacer", dismiss, close, glance at the notebook. After every tick:
    /// the plugin's stock is what the character really owns (shard list + placed), it never
    /// believes an action learned that is not, an action is learned as soon as its result shows,
    /// and an open notebook gives the exact learned set.
    /// </summary>
    [Theory, MemberData(nameof(Seeds))]
    public void Random_session_keeps_stock_and_learned_right(int seed)
    {
        var rng = new Random(seed);
        var game = new FakeLogosGame(Catalog, RandomStock(rng), RandomNotebook(rng));
        var tracker = new LogosTracker(Catalog);
        var state = new CharacterState();
        var now = new DateTime(2026, 10, 16, 20, 0, 0);
        var log = new List<string>();

        for (var step = 0; step < 80; step++)
        {
            var move = Move(rng, game);
            log.Add(move);
            var shardListOpen = game.ManipulatorOpen || rng.Next(3) == 0;
            var snapshot = game.Snapshot(shardListOpen);
            tracker.Update(state, snapshot, now = now.AddMilliseconds(500));

            var where = $"seed {seed}, step {step}: {string.Join(" → ", log.TakeLast(6))}";
            if (snapshot.ShardList != null)
                Assert.True(LogosTracker.SameStock(game.TrueStock, state.Stock), $"stock wrong after {where}");
            Assert.True(state.Learned.IsSubsetOf(game.TrueLearned), $"invented a learned action after {where}");
            if (game.NotebookOpen) Assert.True(game.TrueLearned.SetEquals(state.Learned), $"notebook not applied after {where}");
            if (game.ShowingResult && snapshot.Manipulator?.Extracted is { } n)
                Assert.Contains(Catalog.Actions.Single(a => a.Number == n).Action, state.Learned);
        }
    }

    /// <summary>One plausible move, among those the game allows right now.</summary>
    private static string Move(Random rng, FakeLogosGame game)
    {
        if (game.ShowingResult)
        {
            if (!game.AskingToReplace && rng.Next(3) == 0) { game.ShowReserveFull(); return "reserve full"; }
            game.DismissResult();
            return "dismiss";
        }
        if (!game.ManipulatorOpen)
        {
            switch (rng.Next(3))
            {
                case 0: game.NotebookOpen = !game.NotebookOpen; return game.NotebookOpen ? "notebook open" : "notebook closed";
                case 1: return "wait";
                default: game.OpenManipulator(); return "open";
            }
        }
        var affordable = Catalog.Actions
            .SelectMany(a => a.Recipes.Select((r, i) => (Action: a, Index: i, Recipe: r)))
            .Where(x => x.Recipe.Count + game.Slots.Count <= 6
                        && x.Recipe.GroupBy(i => i).All(g => game.ShardList.GetValueOrDefault(g.Key) >= g.Count()))
            .ToList();
        var matches = Catalog.Actions.Any(a => a.Recipes.Any(r => r.Order().SequenceEqual(game.Slots.Order())));
        switch (rng.Next(8))
        {
            case 0 when game.Slots.Count < 6 && game.ShardList.Count > 0:
                var item = game.ShardList.Keys.ElementAt(rng.Next(game.ShardList.Count));
                game.Place(item);
                return $"place {item}";
            case 1 when game.Slots.Count > 0:
                var back = game.Slots[rng.Next(game.Slots.Count)];
                game.TakeBack(back);
                return $"take back {back}";
            case 2 or 3 when game.Slots.Count == 0 && affordable.Count > 0:
                var pick = affordable[rng.Next(affordable.Count)];
                game.PlaceRecipe(pick.Action, pick.Index);
                return $"place recipe of {pick.Action.Number}";
            case 4 or 5 when matches && game.Slots.Count > 0:
                return $"extract {game.Extract().Number}";
            case 6:
                game.CloseManipulator();
                return "close";
            case 7:
                var shard = Catalog.Shards[rng.Next(Catalog.Shards.Count)];
                var mneme = shard.Mnemes[rng.Next(shard.Mnemes.Count)];
                game.Loot(shard.Item);
                game.Appraise(shard.Item, mneme);
                return $"loot {shard.Item}, appraised into {mneme}";
            default:
                return "wait";
        }
    }
}
