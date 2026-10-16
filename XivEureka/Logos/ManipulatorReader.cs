using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka.Logos;

/// <summary>
/// What the Logos manipulator shows: the mnemes placed in it (still owned until the extraction
/// spends them) and, right after "Lancer l'extraction", the action obtained.
/// </summary>
/// <param name="Placed">Item ids of the placed mnemes, one per slot used.</param>
/// <param name="Extracted">Notebook number (1 to 56) of the action just extracted, else null.</param>
public sealed record ManipulatorView(IReadOnlyList<uint> Placed, int? Extracted)
{
    public static readonly ManipulatorView Empty = new([], null);
}

/// <summary>
/// Reads the Logos manipulator (<c>EurekaMagiciteItemSynthesis</c>). Captured in game on
/// 2026-10-16: value 0 is the window state (2 = "Extraction réussie." shown), values 3 to 8 the
/// icons and 15 to 20 the item ids of the 6 slots (0 = empty), value 27 the notebook number of the
/// action obtained (kept after the result is gone, so only read in state 2). The game takes a
/// mneme out of the shard list as soon as it is placed, and empties the slots once it is spent.
/// </summary>
public static unsafe class ManipulatorReader
{
    public const string Window = "EurekaMagiciteItemSynthesis";

    private const int State = 0;
    private const int ResultShown = 2;
    private const int FirstSlot = 15;
    private const int Slots = 6;
    private const int Result = 27;

    /// <summary>The manipulator's content, or null if the window is closed or inconsistent.</summary>
    public static ManipulatorView? Read(nint windowAddress)
    {
        var window = (AtkUnitBase*)windowAddress;
        if (window == null || !window->IsVisible) return null;
        var values = new List<long>();
        foreach (var v in window->AtkValuesSpan)
            values.Add(v.Type switch { AtkValueType.UInt => v.UInt, AtkValueType.Int => v.Int, _ => -1 });
        return Decode(values);
    }

    /// <summary>Kept apart from the memory read so it can be checked against a capture.</summary>
    public static ManipulatorView? Decode(IReadOnlyList<long> values)
    {
        if (values.Count <= Result) return null;
        var placed = new List<uint>();
        for (var i = FirstSlot; i < FirstSlot + Slots; i++)
            if (values[i] > 0) placed.Add((uint)values[i]);
        var extracted = values[State] == ResultShown && values[Result] is >= 1 and <= 56 ? (int)values[Result] : (int?)null;
        return new ManipulatorView(placed, extracted);
    }
}
