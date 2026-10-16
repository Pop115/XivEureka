using XivEureka.Logos;

namespace XivEureka.Tests;

/// <summary>
/// A stand-in for the game's Logos windows, as captured in game on 2026-10-11 and 2026-10-16: it
/// keeps the true inventory and writes what the windows would show in their raw layout (array
/// 137, manipulator values, notebook values), so the readers are exercised along with the tracker.
/// <list type="bullet">
/// <item>Placing a mneme takes it out of the shard list; taking it back puts it back.</item>
/// <item>"Lancer l'extraction" shows the result (state 2) with the mnemes still in the slots; the
/// slots empty when the result is dismissed (that is when the mnemes are spent).</item>
/// <item>With a full action reserve, the window goes to state 5 ("Remplacer") before emptying.</item>
/// <item>Closing the manipulator without extracting puts the placed mnemes back.</item>
/// </list>
/// </summary>
public sealed class FakeLogosGame(LogosCatalog catalog, Dictionary<uint, int> stock, IEnumerable<int>? learned = null)
{
    private const int ResultShown = 2;
    private const int ReserveFull = 5;

    private readonly Dictionary<uint, int> list = new(stock.Where(s => s.Value > 0));
    private readonly List<uint> slots = [];
    private int windowState;
    private int lastResult;

    public HashSet<int> LearnedNumbers { get; } = [.. learned ?? []];
    public bool ManipulatorOpen { get; private set; }
    public bool NotebookOpen { get; set; }
    public IReadOnlyList<uint> Slots => slots;
    public bool ShowingResult => windowState is ResultShown or ReserveFull;
    public bool AskingToReplace => windowState == ReserveFull;
    public IReadOnlyDictionary<uint, int> ShardList => list;

    /// <summary>What the character really owns: the shard list and the placed mnemes.</summary>
    public Dictionary<uint, int> TrueStock => list.Keys.Union(slots)
        .ToDictionary(item => item, item => list.GetValueOrDefault(item) + slots.Count(s => s == item));

    public HashSet<uint> TrueLearned => [.. catalog.Actions.Where(a => LearnedNumbers.Contains(a.Number)).Select(a => a.Action)];

    public void OpenManipulator() => ManipulatorOpen = true;

    public void CloseManipulator()
    {
        foreach (var item in slots) list[item] = list.GetValueOrDefault(item) + 1;
        slots.Clear();
        windowState = 0;
        ManipulatorOpen = false;
    }

    public void Place(uint item)
    {
        if (!ManipulatorOpen) throw new InvalidOperationException("manipulator closed");
        if (slots.Count >= 6) throw new InvalidOperationException("6 slots at most");
        if (list.GetValueOrDefault(item) <= 0) throw new InvalidOperationException($"no mneme {item} left");
        if (--list[item] == 0) list.Remove(item);
        slots.Add(item);
        windowState = 0;
    }

    public void TakeBack(uint item)
    {
        if (!slots.Remove(item)) throw new InvalidOperationException($"mneme {item} not placed");
        list[item] = list.GetValueOrDefault(item) + 1;
    }

    /// <summary>Places one recipe of the action (it must be affordable).</summary>
    public void PlaceRecipe(LogosAction action, int recipe = 0)
    {
        foreach (var item in action.Recipes[recipe]) Place(item);
    }

    /// <summary>"Lancer l'extraction": the action whose recipe is exactly in the slots.</summary>
    public LogosAction Extract()
    {
        var placed = slots.Order().ToList();
        var action = catalog.Actions.FirstOrDefault(a => a.Recipes.Any(r => r.Order().SequenceEqual(placed)))
            ?? throw new InvalidOperationException("no recipe matches the slots");
        windowState = ResultShown;
        lastResult = action.Number;
        LearnedNumbers.Add(action.Number);
        return action;
    }

    /// <summary>The result is dismissed (or, reserve full, replaced): the mnemes are spent.</summary>
    public void DismissResult()
    {
        if (windowState is not (ResultShown or ReserveFull)) throw new InvalidOperationException("no result shown");
        slots.Clear();
        windowState = 0;
    }

    /// <summary>Only the reserve-full step, before the slots empty (as captured on 2026-10-16).</summary>
    public void ShowReserveFull()
    {
        if (windowState != ResultShown) throw new InvalidOperationException("no result shown");
        windowState = ReserveFull;
    }

    /// <summary>What the plugin would read now. The shard list is open alongside the manipulator.</summary>
    public LogosSnapshot Snapshot(bool shardListOpen = true)
    {
        var shards = shardListOpen || ManipulatorOpen ? ShardReader.Decode(ShardArray()) : null;
        var manipulator = ManipulatorOpen ? ManipulatorReader.Decode(ManipulatorValues()) : null;
        var notebook = NotebookOpen ? NotebookReader.Decode(NotebookValues(), catalog) : null;
        return new LogosSnapshot(shards, manipulator, notebook);
    }

    /// <summary>Array 137: count, 2 unused, then icon, quantity, item, EurekaMagiciteItem row.</summary>
    public int[] ShardArray()
    {
        var array = new int[3 + (4 * list.Count) + 16];
        array[0] = list.Count;
        var i = 0;
        foreach (var (item, quantity) in list.OrderBy(s => s.Key))
        {
            array[3 + (4 * i)] = 27950 + (int)(item % 7);
            array[4 + (4 * i)] = quantity;
            array[5 + (4 * i)] = (int)item;
            array[6 + (4 * i)] = i + 1;
            i++;
        }
        return array;
    }

    /// <summary>The manipulator's 38 values: state, icons 3-8, names 9-14, items 15-20, result 27.</summary>
    public long[] ManipulatorValues()
    {
        var values = Enumerable.Repeat(-1L, 38).ToArray();
        values[0] = windowState;
        for (var s = 0; s < 6; s++)
        {
            values[3 + s] = s < slots.Count ? 27950 : 0;
            values[15 + s] = s < slots.Count ? slots[s] : 0;
        }
        values[27] = lastResult;
        return values;
    }

    /// <summary>The notebook: one value per action, its number if learned, else 0; icons after.</summary>
    public long[] NotebookValues() =>
        [.. Enumerable.Range(1, catalog.Actions.Count).Select(n => LearnedNumbers.Contains(n) ? (long)n : 0L),
            .. Enumerable.Repeat(64600L, catalog.Actions.Count)];
}
