using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka.Logos;

/// <summary>
/// Reads the learned Logos actions from the Logos action notebook (<c>EurekaMagiaActionNotebook</c>,
/// opened by talking to Drake). Captured in game on 2026-10-11: the window's first 56 values are one
/// per action, its number (1 to 56) if learned, 0 otherwise; the icons come after.
/// </summary>
public static unsafe class NotebookReader
{
    public const string Window = "EurekaMagiaActionNotebook";

    /// <summary>Learned actions (Action sheet rows), or null if the window is closed or inconsistent.</summary>
    public static HashSet<uint>? Read(nint windowAddress, LogosCatalog catalog)
    {
        var window = (AtkUnitBase*)windowAddress;
        if (window == null || !window->IsVisible) return null;
        var values = new List<long>();
        foreach (var v in window->AtkValuesSpan)
            values.Add(v.Type switch { AtkValueType.UInt => v.UInt, AtkValueType.Int => v.Int, _ => -1 });
        return Decode(values, catalog);
    }

    public static HashSet<uint>? Decode(IReadOnlyList<long> values, LogosCatalog catalog)
    {
        var count = catalog.Actions.Count;
        if (values.Count < count) return null;
        var byNumber = catalog.Actions.ToDictionary(a => (long)a.Number, a => a.Action);
        var learned = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var v = values[i];
            if (v == 0) continue;
            if (!byNumber.TryGetValue(v, out var action)) return null;
            learned.Add(action);
        }
        return learned;
    }
}
