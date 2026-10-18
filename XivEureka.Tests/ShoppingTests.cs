using System.Diagnostics;
using XivEureka.Logos;
using Xunit;
using Xunit.Abstractions;

namespace XivEureka.Tests;

/// <summary>"Quoi acheter": expected appraisals, the cheapest plan, the stacks to buy.</summary>
public class ShoppingTests(ITestOutputHelper output)
{
    private static readonly LogosCatalog Catalog = LogosCatalog.Embedded();
    private const uint Offensive = 24010;    // Wisdom of the Skirmisher 24020, Bloodbath L 24032
    private const uint BloodbathMneme = 24032;
    private const uint Bloodbath = 12985;    // action 28: [24032] or [24033, 24020] or [24016, 24016, 24033]

    [Theory]
    [InlineData(new[] { 1 }, 1, 1.0)]
    [InlineData(new[] { 1 }, 2, 2.0)]
    [InlineData(new[] { 1, 1 }, 2, 3.0)]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1, 1 }, 7, 18.15)] // coupon collector: 7 × H7
    [InlineData(new[] { 2 }, 2, 4.0)]                     // 2 of one of two: 2 / (1/2)
    [InlineData(new[] { 0, 0 }, 2, 0.0)]
    public void Expected_appraisals(int[] quotas, int pool, double expected) =>
        Assert.Equal(expected, Shopping.ExpectedDraws(quotas, pool), 2);

    [Fact]
    public void Two_mnemes_of_the_same_logogram_cost_less_than_twice_one() =>
        Assert.True(Shopping.ExpectedDraws([1, 1], 2) < 2 * Shopping.ExpectedDraws([1], 2));

    [Fact]
    public void A_cheap_stack_beats_buying_cheapest_first_when_stacks_are_whole()
    {
        // 3 wanted: the 99 stack at 100 costs 9,900; 1 + 2 at 500 cost 1,500.
        var market = new ShardMarket(Offensive, [new(100, 99, "A"), new(500, 1, "B"), new(500, 2, "C")]);
        var stacks = Shopping.Stacks(market, 3);
        Assert.Equal(1500, stacks.Sum(s => s.Quantity * s.PricePerUnit));
    }

    [Fact]
    public void Owned_mnemes_mean_nothing_to_buy()
    {
        var stock = new Dictionary<uint, int> { [BloodbathMneme] = 2 };
        var plan = Shopping.Plan(Catalog, stock, new Dictionary<uint, int> { [Bloodbath] = 2 }, new HashSet<uint> { Bloodbath }, Markets());
        Assert.True(plan.NothingToBuy);
        Assert.All(plan.Crafts, c => Assert.Equal([BloodbathMneme], c.Recipe));
        Assert.All(plan.Crafts, c => Assert.False(c.Learn));
    }

    [Fact]
    public void Bloodbath_comes_from_offensive_logograms_at_even_prices()
    {
        var even = Catalog.Shards.ToDictionary(s => s.Item, s => new ShardMarket(s.Item, [new(1000, 99, "A")]));
        var plan = Shopping.Plan(Catalog, new Dictionary<uint, int>(), new Dictionary<uint, int> { [Bloodbath] = 1 },
            new HashSet<uint>(), even);
        var line = Assert.Single(plan.Lines);
        Assert.Equal(Offensive, line.Shard);
        Assert.Equal(2.0, line.Expected, 2);
        Assert.True(plan.Crafts.Single().Learn);
    }

    [Fact]
    public void Cheap_conceptual_logograms_can_beat_the_single_mneme_recipe()
    {
        // Chaos + Light, 2026-10-18: conceptual at ~1,000-1,900, offensive at 12,000. Bloodbath L
        // through [24016, 24016, 24033] (≈ 21 conceptual) costs less than 2 offensive.
        var plan = Shopping.Plan(Catalog, new Dictionary<uint, int>(), new Dictionary<uint, int> { [Bloodbath] = 1 },
            new HashSet<uint>(), Markets());
        Assert.Equal(24007u, Assert.Single(plan.Lines).Shard);
    }

    [Fact]
    public void A_logogram_not_for_sale_is_avoided()
    {
        // Spirit of the Remembered's action 11: [24021] (protective logogram) or [24016, 24018] or
        // [24015, 24016, 24017]. Without protective logograms for sale, the conceptual recipe wins.
        var markets = Markets();
        markets.Remove(24011);
        var spirit = Catalog.Actions.Single(a => a.Number == 11);
        var plan = Shopping.Plan(Catalog, new Dictionary<uint, int>(), new Dictionary<uint, int> { [spirit.Action] = 1 },
            new HashSet<uint>(), markets);
        Assert.Equal([24015u, 24016, 24017], plan.Crafts.Single().Recipe);
        Assert.DoesNotContain(plan.Lines, l => l.Shard == 24011);
    }

    [Fact]
    public void Universalis_answers_are_read()
    {
        var (listings, upload) = Universalis.Parse(File.ReadAllText("TestData/universalis-Chaos.json"), "Chaos");
        Assert.Equal(9, listings.Count);
        Assert.NotNull(upload);
        Assert.All(listings.Values.SelectMany(l => l), l => Assert.True(l.PricePerUnit > 0 && l.Quantity > 0 && l.World.Length > 0));
    }

    /// <summary>
    /// Megumin Crimson's board on 2026-10-18 (39 learned of 56) with the Chaos + Light prices of the
    /// same day: the board completed plus one Bloodbath, under a second.
    /// </summary>
    [Fact]
    public void Megumin_completes_her_board()
    {
        var stock = new Dictionary<uint, int>
        {
            [24015] = 16, [24016] = 1, [24017] = 11, [24022] = 47, [24024] = 14, [24028] = 34, [24030] = 10,
            [24031] = 38, [24033] = 6, [24036] = 38, [24037] = 7, [24038] = 35, [24021] = 1, [24026] = 17,
            [24027] = 23, [24810] = 2, [24811] = 2,
        };
        var learned = new HashSet<uint>
        {
            12958, 12959, 12960, 12961, 12962, 12963, 12968, 12969, 12970, 12971, 12973, 12974, 12976, 12977, 12979,
            12980, 12983, 12984, 12988, 12989, 12990, 12991, 12992, 12993, 12994, 12995, 12996, 13003, 12978, 12982,
            12964, 12967, 12975, 12985, 12986, 12981, 12987, 14480, 14476,
        };
        var crafts = Catalog.Actions.Where(a => !learned.Contains(a.Action)).ToDictionary(a => a.Action, _ => 1);
        crafts[Bloodbath] = 1;

        var watch = Stopwatch.StartNew();
        var plan = Shopping.Plan(Catalog, stock, crafts, learned, Markets());
        watch.Stop();

        Assert.Equal(18, plan.Crafts.Count);
        Assert.Equal(17, plan.Crafts.Count(c => c.Learn));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"{watch.ElapsedMilliseconds} ms");
        // Every recipe is covered by the stock plus what is bought.
        foreach (var (mneme, need) in plan.Needed)
        {
            var bought = plan.Lines.SelectMany(l => l.Missing).Where(m => m.Key == mneme).Sum(m => m.Value);
            Assert.True(stock.GetValueOrDefault(mneme) + bought >= need, $"mneme {mneme}");
        }

        output.WriteLine($"{plan.Cost:N0} gil in {watch.ElapsedMilliseconds} ms");
        foreach (var line in plan.Lines)
            output.WriteLine($"{line.Shard}: ≈{line.Expected:0.0} (≥{line.AtLeast}) {line.Cost:N0} — {string.Join(", ", line.Missing.Select(m => $"{m.Key}×{m.Value}"))}");
        foreach (var craft in plan.Crafts)
            output.WriteLine($"#{craft.Action.Number} {(craft.Learn ? "learn" : "extra")}: {string.Join(" + ", craft.Recipe)}");
    }

    private static Dictionary<uint, ShardMarket> Markets()
    {
        var all = new Dictionary<uint, List<Listing>>();
        foreach (var dc in new[] { "Chaos", "Light" })
            foreach (var (item, listings) in Universalis.Parse(File.ReadAllText($"TestData/universalis-{dc}.json"), dc).Listings)
            {
                if (!all.TryGetValue(item, out var list)) all[item] = list = [];
                list.AddRange(listings);
            }
        return all.ToDictionary(e => e.Key, e => new ShardMarket(e.Key, [.. e.Value.OrderBy(l => l.PricePerUnit)]));
    }
}
