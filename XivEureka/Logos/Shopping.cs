using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>One market board listing: a stack sold whole, at a price per unit (tax not included).</summary>
public sealed record Listing(int PricePerUnit, int Quantity, string World, string DataCenter = "");

/// <summary>What the market board sells of one logogram, cheapest first.</summary>
public sealed record ShardMarket(uint Shard, IReadOnlyList<Listing> Listings)
{
    public int UnitsForSale => Listings.Sum(l => l.Quantity);

    /// <summary>
    /// Price of the <paramref name="units"/> cheapest units, as if stacks could be split (for the
    /// plan's comparisons; <see cref="Shopping.Stacks"/> picks the actual stacks). Past what is for
    /// sale, the extra units cost <see cref="Shopping.Unavailable"/>.
    /// </summary>
    public double Cost(double units)
    {
        var cost = 0.0;
        foreach (var listing in Listings)
        {
            if (units <= 0) break;
            var take = Math.Min(units, listing.Quantity);
            cost += take * listing.PricePerUnit;
            units -= take;
        }
        return units > 0 ? cost + (units * Shopping.Unavailable) : cost;
    }
}

/// <summary>One craft to plan: an action, made once more with one of its recipes.</summary>
public sealed record PlannedCraft(LogosAction Action, IReadOnlyList<uint> Recipe, bool Learn);

/// <summary>
/// One logogram to buy: the mnemes still missing from it, the expected number to appraise before
/// they are all out (<see cref="Expected"/>, more than <see cref="AtLeast"/> since they come out at
/// random) and what that costs.
/// </summary>
public sealed record ShopLine(uint Shard, IReadOnlyDictionary<uint, int> Missing, double Expected, int AtLeast, double Cost,
    bool NotEnoughForSale);

/// <summary>The cheapest way found: which recipe for each craft, and what to buy.</summary>
public sealed record ShopPlan(IReadOnlyList<PlannedCraft> Crafts, IReadOnlyList<ShopLine> Lines, double Cost,
    IReadOnlyDictionary<uint, int> Needed)
{
    public bool NothingToBuy => Lines.Count == 0;
}

/// <summary>
/// What to buy at the market board to craft a list of Logos actions for the least gil. Mnemes
/// cannot be traded, logograms can: each one appraised gives one of its mnemes, evenly (the same
/// assumption as "Quoi taper": community record of 700,000 appraisals), and every mneme comes from a
/// single logogram. So:
/// <list type="bullet">
/// <item>for a choice of recipes, the mnemes missing from the stock tell, logogram by logogram,
/// how many appraisals it takes on average to get them all (<see cref="ExpectedDraws"/>, a coupon
/// collector with quotas), hence the expected cost;</item>
/// <item>the recipes are chosen to make that cost the lowest: each craft starts on its cheapest
/// recipe given the others, then each one is changed while that lowers the total (two mnemes of
/// the same logogram cost little more than one: the plan takes that into account).</item>
/// </list>
/// Nothing from the game or the network: the stock, the crafts wanted and the listings are given.
/// </summary>
public static class Shopping
{
    /// <summary>Price of a unit that is not for sale: high enough for any recipe that avoids it to win.</summary>
    public const double Unavailable = 10_000_000;

    /// <summary>
    /// Expected appraisals of a logogram with <paramref name="pool"/> mnemes, coming out evenly,
    /// to get <c>quotas[i]</c> of each of them (the others, up to <paramref name="pool"/>, wanted 0).
    /// Exact through the Poisson form: E = pool × ∫₀^∞ (1 − Π P(Poisson(x) ≥ qᵢ)) dx.
    /// </summary>
    public static double ExpectedDraws(IReadOnlyList<int> quotas, int pool)
    {
        var wanted = quotas.Where(q => q > 0).ToArray();
        if (wanted.Length == 0) return 0;
        if (pool < wanted.Length) throw new ArgumentException("more mnemes wanted than the logogram has", nameof(pool));
        // Simpson over x, until every quota is reached with near certainty.
        const double step = 0.01;
        var sum = 0.0;
        var previous = Missing(wanted, 0);
        for (var i = 1; ; i++)
        {
            var x = i * step;
            var middle = Missing(wanted, x - (step / 2));
            var current = Missing(wanted, x);
            sum += step / 6 * (previous + (4 * middle) + current);
            previous = current;
            if (current < 1e-12 && x > wanted.Max()) break;
        }
        return pool * sum;
    }

    /// <summary>1 − Π P(Poisson(x) ≥ q): the chance some quota is not reached yet at time x.</summary>
    private static double Missing(int[] quotas, double x)
    {
        var all = 1.0;
        foreach (var quota in quotas) all *= 1 - PoissonBelow(quota, x);
        return 1 - all;
    }

    /// <summary>P(Poisson(x) &lt; q).</summary>
    private static double PoissonBelow(int q, double x)
    {
        var term = Math.Exp(-x);
        var sum = 0.0;
        for (var j = 0; j < q; j++)
        {
            sum += term;
            term *= x / (j + 1);
        }
        return Math.Min(1, sum);
    }

    /// <summary>
    /// The cheapest plan for <paramref name="crafts"/> (action → how many times to craft it;
    /// <paramref name="learned"/> tells which crafts learn it). Logograms with no listing cost
    /// <see cref="Unavailable"/>: a recipe that avoids them wins.
    /// </summary>
    public static ShopPlan Plan(LogosCatalog catalog, IReadOnlyDictionary<uint, int> stock,
        IReadOnlyDictionary<uint, int> crafts, IReadOnlySet<uint> learned, IReadOnlyDictionary<uint, ShardMarket> markets)
    {
        var shardOf = catalog.Shards.SelectMany(s => s.Mnemes.Select(m => (Mneme: m, Shard: s))).ToDictionary(x => x.Mneme, x => x.Shard);
        var slots = new List<LogosAction>();
        foreach (var action in catalog.Actions)
            for (var i = 0; i < crafts.GetValueOrDefault(action.Action); i++)
                slots.Add(action);
        // The actions with a single recipe first: they fix part of the need, the others adjust to it.
        slots = [.. slots.OrderBy(a => a.Recipes.Count).ThenBy(a => a.Number)];

        var choice = new int[slots.Count];
        var needed = new Dictionary<uint, int>();
        var costs = new Dictionary<uint, double>();

        double ShardCost(Shard shard)
        {
            var quotas = shard.Mnemes.Select(m => Math.Max(0, needed.GetValueOrDefault(m) - stock.GetValueOrDefault(m))).ToList();
            var draws = ExpectedDraws(quotas, shard.Mnemes.Count);
            if (draws == 0) return 0;
            return markets.TryGetValue(shard.Item, out var market) ? market.Cost(draws) : draws * Unavailable;
        }

        double Total() => costs.Values.Sum();

        // Cost after adding (sign +1) or removing (-1) a recipe's mnemes; only its logograms move.
        void Apply(IReadOnlyList<uint> recipe, int sign)
        {
            foreach (var mneme in recipe) needed[mneme] = needed.GetValueOrDefault(mneme) + sign;
            foreach (var shard in recipe.Select(m => shardOf[m]).Distinct()) costs[shard.Item] = ShardCost(shard);
        }

        int Best(LogosAction action, int current)
        {
            var best = current;
            var bestCost = double.MaxValue;
            for (var r = 0; r < action.Recipes.Count; r++)
            {
                Apply(action.Recipes[r], +1);
                var cost = Total();
                Apply(action.Recipes[r], -1);
                // Fewer mnemes on a tie: less to place at the manipulator.
                if (cost < bestCost - 1e-6 || (Math.Abs(cost - bestCost) <= 1e-6 && action.Recipes[r].Count < action.Recipes[best].Count))
                {
                    best = r;
                    bestCost = cost;
                }
            }
            return best;
        }

        for (var i = 0; i < slots.Count; i++)
        {
            choice[i] = Best(slots[i], 0);
            Apply(slots[i].Recipes[choice[i]], +1);
        }
        for (var pass = 0; pass < 10; pass++)
        {
            var improved = false;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].Recipes.Count < 2) continue;
                var before = Total();
                Apply(slots[i].Recipes[choice[i]], -1);
                var best = Best(slots[i], choice[i]);
                Apply(slots[i].Recipes[best], +1);
                if (best != choice[i] && Total() < before - 1e-6) improved = true;
                choice[i] = best;
            }
            if (!improved) break;
        }

        var planned = slots.Select((action, i) => (action, recipe: action.Recipes[choice[i]]))
            .OrderBy(x => x.action.Number)
            .ToList();
        // Learning crafts: the first of each action not learned yet.
        var seen = new HashSet<uint>();
        var list = planned.Select(x => new PlannedCraft(x.action, x.recipe, !learned.Contains(x.action.Action) && seen.Add(x.action.Action))).ToList();

        var lines = new List<ShopLine>();
        foreach (var shard in catalog.Shards)
        {
            var missing = shard.Mnemes.Select(m => (m, n: needed.GetValueOrDefault(m) - stock.GetValueOrDefault(m)))
                .Where(x => x.n > 0).ToDictionary(x => x.m, x => x.n);
            if (missing.Count == 0) continue;
            var expected = ExpectedDraws(shard.Mnemes.Select(m => missing.GetValueOrDefault(m)).ToList(), shard.Mnemes.Count);
            var market = markets.GetValueOrDefault(shard.Item);
            var notEnough = market == null || market.UnitsForSale < Math.Ceiling(expected);
            lines.Add(new ShopLine(shard.Item, missing, expected, missing.Values.Sum(),
                market?.Cost(expected) ?? expected * Unavailable, notEnough));
        }
        lines = [.. lines.OrderByDescending(l => l.Cost)];
        return new ShopPlan(list, lines, lines.Sum(l => l.Cost), needed);
    }

    /// <summary>
    /// The stacks to buy for at least <paramref name="units"/> units, as cheap as possible (a stack
    /// is bought whole: a small dearer stack can beat the rest of a big cheap one). Few listings
    /// reach that far: the best of "cheapest first" and "every subset of the first stacks" is kept.
    /// </summary>
    public static IReadOnlyList<Listing> Stacks(ShardMarket market, int units)
    {
        if (units <= 0) return [];
        var listings = market.Listings.OrderBy(l => l.PricePerUnit).ToList();
        // Exact over the cheapest 16 stacks (2^16 subsets), which is where the cheap units are.
        var head = listings.Take(16).ToList();
        List<Listing>? best = null;
        long bestCost = long.MaxValue;
        for (var mask = 1; mask < 1 << head.Count; mask++)
        {
            long quantity = 0, cost = 0;
            for (var i = 0; i < head.Count; i++)
                if ((mask & (1 << i)) != 0)
                {
                    quantity += head[i].Quantity;
                    cost += (long)head[i].Quantity * head[i].PricePerUnit;
                }
            if (quantity >= units && cost < bestCost)
            {
                bestCost = cost;
                best = [.. head.Where((_, i) => (mask & (1 << i)) != 0)];
            }
        }
        if (best != null) return best;
        // Not enough in the first 16: cheapest first, as far as it goes.
        var taken = new List<Listing>();
        var total = 0;
        foreach (var listing in listings)
        {
            if (total >= units) break;
            taken.Add(listing);
            total += listing.Quantity;
        }
        return taken;
    }
}
