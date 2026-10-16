using System.Collections.Generic;
using System.Linq;
using XivEureka.Zones;

namespace XivEureka.Logos;

/// <summary>An action that needs a mneme, and how many of it are missing from its closest recipe.</summary>
public sealed record HuntNeed(LogosAction Action, int Missing);

/// <summary>A mneme a shard can give: owned, and which not-yet-learned actions miss it (none: not useful now).</summary>
public sealed record HuntMneme(uint Mneme, int Owned, IReadOnlyList<HuntNeed> NeededBy);

/// <summary>An NM that drops the shard, and its zone.</summary>
public sealed record HuntSource(uint Territory, EurekaNm Nm);

/// <summary>
/// An unidentified shard worth hunting: the mnemes it can turn into (one per appraisal), the
/// actions it moves forward, and the NMs that drop it.
/// </summary>
public sealed record HuntShard(uint Shard, IReadOnlyList<HuntMneme> Mnemes, IReadOnlyList<LogosAction> Actions,
    IReadOnlyList<HuntSource> Sources);

/// <summary>The whole chain for a character: what to craft now, and what to hunt for the rest.</summary>
public sealed record HuntPlan(int NotLearned, IReadOnlyList<LogosAction> CraftableNow, IReadOnlyList<HuntShard> Shards);

/// <summary>
/// "Which mob to hit": from the actions not learned yet, the mnemes missing from their closest
/// recipes (all those missing the fewest, as for <see cref="Crafting.UsefulShards"/>), the shards
/// that give those mnemes, and the NMs that drop those shards. Actions craftable right now need no
/// hunting: they are listed apart. Shards come most useful first.
/// </summary>
public static class Hunt
{
    public static HuntPlan Compute(LogosCatalog catalog, IEnumerable<ZoneNms> zones, IReadOnlyDictionary<uint, int> stock,
        IReadOnlySet<uint> learned)
    {
        var craftable = new List<LogosAction>();
        var needs = new Dictionary<uint, List<HuntNeed>>();
        var notLearned = 0;
        foreach (var action in catalog.Actions.OrderBy(a => a.Number))
        {
            var state = Crafting.Action(action, stock, learned);
            if (state.Learned) continue;
            notLearned++;
            if (state.Craftable > 0)
            {
                craftable.Add(action);
                continue;
            }
            var missing = new Dictionary<uint, int>();
            foreach (var recipe in state.Recipes.Where(r => r.Missing == state.Closest.Missing))
                foreach (var (mneme, need) in recipe.Needs)
                {
                    var lacking = need - stock.GetValueOrDefault(mneme);
                    if (lacking > 0) missing[mneme] = System.Math.Max(missing.GetValueOrDefault(mneme), lacking);
                }
            foreach (var (mneme, lacking) in missing)
            {
                if (!needs.TryGetValue(mneme, out var list)) needs[mneme] = list = [];
                list.Add(new HuntNeed(action, lacking));
            }
        }

        var sources = zones.SelectMany(z => z.Nms.Select(n => new HuntSource(z.Territory, n))).ToList();
        var shards = catalog.Shards
            .Select(shard =>
            {
                var mnemes = shard.Mnemes
                    .Select(m => new HuntMneme(m, stock.GetValueOrDefault(m), needs.GetValueOrDefault(m) ?? []))
                    .OrderByDescending(m => m.NeededBy.Count).ThenBy(m => m.Mneme).ToList();
                var actions = mnemes.SelectMany(m => m.NeededBy.Select(n => n.Action)).Distinct().OrderBy(a => a.Number).ToList();
                var from = sources.Where(s => s.Nm.Shard == shard.Item).OrderBy(s => s.Nm.Level).ToList();
                return new HuntShard(shard.Item, mnemes, actions, from);
            })
            .Where(s => s.Actions.Count > 0)
            .OrderByDescending(s => s.Actions.Count).ThenBy(s => s.Shard)
            .ToList();
        return new HuntPlan(notLearned, craftable, shards);
    }
}
