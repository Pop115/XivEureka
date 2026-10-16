using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>
/// The Logos action board of a character, as the Logos tab shows it: each action's state, how many
/// are learned, how many can be learned right now, and the shards worth hunting ("where to go now").
/// </summary>
public sealed record LogosBoard(IReadOnlyDictionary<int, ActionState> Actions, int Learned, int Total, int ToLearn,
    IReadOnlyDictionary<uint, int> UsefulShards)
{
    public static LogosBoard Of(LogosCatalog catalog, CharacterState state)
    {
        var actions = catalog.Actions.ToDictionary(a => a.Number, a => Crafting.Action(a, state.Stock, state.Learned));
        // Without a stock read, every shard would look useful: better to suggest none.
        var useful = state.StockReadAt == null ? new Dictionary<uint, int>()
            : Crafting.UsefulShards(catalog, state.Stock, state.Learned);
        return new LogosBoard(actions, state.Learned.Count, catalog.Actions.Count, actions.Values.Count(a => a.ToLearn), useful);
    }
}
