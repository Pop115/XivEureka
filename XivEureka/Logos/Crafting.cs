using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>What a recipe gives with a stock: how many times it can be made, how many mnemes are missing.</summary>
public sealed record RecipeState(IReadOnlyList<uint> Recipe, IReadOnlyDictionary<uint, int> Needs, int Craftable, int Missing);

/// <summary>Where an action stands: learned or not, craftable how many times, and the closest recipe.</summary>
public sealed record ActionState(LogosAction Action, bool Learned, int Craftable, RecipeState Closest, IReadOnlyList<RecipeState> Recipes)
{
    /// <summary>Not learned yet and craftable right now: what we look for first.</summary>
    public bool ToLearn => !Learned && Craftable > 0;
}

/// <summary>The computation, with nothing from the game: a mneme stock and a set of learned actions.</summary>
public static class Crafting
{
    public static RecipeState Recipe(IReadOnlyList<uint> recipe, IReadOnlyDictionary<uint, int> stock)
    {
        var needs = recipe.GroupBy(m => m).ToDictionary(g => g.Key, g => g.Count());
        var craftable = int.MaxValue;
        var missing = 0;
        foreach (var (mneme, need) in needs)
        {
            var owned = stock.GetValueOrDefault(mneme);
            craftable = Math.Min(craftable, owned / need);
            missing += Math.Max(0, need - owned);
        }
        return new RecipeState(recipe, needs, craftable == int.MaxValue ? 0 : craftable, missing);
    }

    /// <summary>
    /// An action's recipes share the same stock: they cannot be added up, hence the maximum
    /// ("up to × N").
    /// </summary>
    public static ActionState Action(LogosAction action, IReadOnlyDictionary<uint, int> stock, IReadOnlySet<uint> learned)
    {
        var recipes = action.Recipes.Select(r => Recipe(r, stock)).ToList();
        var closest = recipes.OrderBy(r => r.Missing).ThenBy(r => r.Recipe.Count).First();
        return new ActionState(action, learned.Contains(action.Action), recipes.Max(r => r.Craftable), closest, recipes);
    }

    /// <summary>
    /// For each unidentified shard, how many not-yet-learned actions (not craftable right now) it
    /// would move forward: it can yield a mneme missing from one of their closest recipes (all
    /// those missing the fewest mnemes). Tells which NMs to hit.
    /// </summary>
    public static Dictionary<uint, int> UsefulShards(LogosCatalog catalog, IReadOnlyDictionary<uint, int> stock,
        IReadOnlySet<uint> learned)
    {
        var useful = new Dictionary<uint, int>();
        foreach (var action in catalog.Actions)
        {
            var state = Action(action, stock, learned);
            if (state.Learned || state.Craftable > 0) continue;
            // Every recipe as close as the closest one counts: with a + b owned toward [a, b, c]
            // and nothing toward [d], both c's and d's shards bring the action one mneme away.
            var missing = state.Recipes.Where(r => r.Missing == state.Closest.Missing)
                .SelectMany(r => r.Needs.Where(n => stock.GetValueOrDefault(n.Key) < n.Value).Select(n => n.Key))
                .ToHashSet();
            foreach (var shard in catalog.Shards.Where(s => s.Mnemes.Any(missing.Contains)))
                useful[shard.Item] = useful.GetValueOrDefault(shard.Item) + 1;
        }
        return useful;
    }

    /// <summary>For each mneme, how many not-yet-learned actions use it.</summary>
    public static Dictionary<uint, int> Usefulness(IEnumerable<LogosAction> actions, IReadOnlySet<uint> learned)
    {
        var usefulness = new Dictionary<uint, int>();
        foreach (var action in actions.Where(a => !learned.Contains(a.Action)))
            foreach (var mneme in action.Recipes.SelectMany(r => r).Distinct())
                usefulness[mneme] = usefulness.GetValueOrDefault(mneme) + 1;
        return usefulness;
    }
}
