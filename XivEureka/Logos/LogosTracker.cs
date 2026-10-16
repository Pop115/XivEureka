using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>
/// What the Logos windows show at one moment; null for a window that is closed.
/// </summary>
/// <param name="ShardList">Stock read from the shard list (array 137).</param>
/// <param name="Manipulator">Content of the Logos manipulator.</param>
/// <param name="Notebook">Learned actions read from the Logos action notebook.</param>
public sealed record LogosSnapshot(
    IReadOnlyDictionary<uint, int>? ShardList = null,
    ManipulatorView? Manipulator = null,
    IReadOnlySet<uint>? Notebook = null);

/// <summary>
/// Keeps a character's mneme stock and learned actions up to date from what the game windows show,
/// with nothing from the game itself so whole sessions can be replayed in tests.
/// <list type="bullet">
/// <item>Stock = shard list + mnemes placed in the manipulator: a placed mneme is only spent when
/// the extraction empties the slots.</item>
/// <item>An action shown as extracted is learned (any extracted action is).</item>
/// <item>The notebook, when open, is authoritative: it replaces the learned set.</item>
/// </list>
/// </summary>
public sealed class LogosTracker(LogosCatalog catalog)
{
    /// <summary>Applies one snapshot; true when the character changed (to be saved).</summary>
    public bool Update(CharacterState state, LogosSnapshot snapshot, DateTime now)
    {
        var changed = false;
        if (snapshot.Notebook is { } notebook && (!notebook.SetEquals(state.Learned) || state.LearnedReadAt == null))
        {
            state.Learned = [.. notebook];
            state.LearnedReadAt = now;
            changed = true;
        }
        if (snapshot.Manipulator?.Extracted is { } number
            && catalog.Actions.FirstOrDefault(a => a.Number == number) is { } extracted
            && state.Learned.Add(extracted.Action))
            changed = true;
        if (snapshot.ShardList is { } list)
        {
            var stock = Stock(list, snapshot.Manipulator);
            if (!SameStock(stock, state.Stock) || state.StockReadAt == null)
            {
                state.Stock = stock;
                state.StockReadAt = now;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Shard list plus the mnemes placed in the manipulator.</summary>
    public static Dictionary<uint, int> Stock(IReadOnlyDictionary<uint, int> list, ManipulatorView? manipulator)
    {
        var stock = new Dictionary<uint, int>(list);
        foreach (var item in manipulator?.Placed ?? [])
            stock[item] = stock.GetValueOrDefault(item) + 1;
        return stock;
    }

    /// <summary>Same quantities, a missing mneme counting as 0.</summary>
    public static bool SameStock(IReadOnlyDictionary<uint, int> a, IReadOnlyDictionary<uint, int> b) =>
        a.Keys.Union(b.Keys).All(k => a.GetValueOrDefault(k) == b.GetValueOrDefault(k));
}
