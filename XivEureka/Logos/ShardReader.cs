using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka.Logos;

/// <summary>
/// Reads the mneme stock the game prepares for the Logos shard window
/// (<c>EurekaMagiciteItemShardList</c>). The array is only filled while that window is open;
/// otherwise it holds stale data at best, hence <see cref="IsWindowOpen"/>.
///
/// Layout captured in game on 2026-10-11: slot 0 = number of distinct mnemes owned, then 4 slots
/// per mneme from slot 3: icon, quantity, item id, EurekaMagiciteItem row.
/// </summary>
public static unsafe class ShardReader
{
    public const string Window = "EurekaMagiciteItemShardList";

    public static bool IsWindowOpen(nint windowAddress) =>
        windowAddress != 0 && ((AtkUnitBase*)windowAddress)->IsVisible;

    /// <summary>The stock (item → quantity), or null if the array is missing or inconsistent.</summary>
    public static Dictionary<uint, int>? Read()
    {
        var module = RaptureAtkModule.Instance();
        if (module == null) return null;
        var array = module->AtkArrayDataHolder.GetNumberArrayData((int)NumberArrayType.EurekaLogosShardList);
        if (array == null || array->IntArray == null) return null;
        return Decode(array->Span);
    }

    /// <summary>Kept apart from the memory read so it can be checked against a capture.</summary>
    public static Dictionary<uint, int>? Decode(ReadOnlySpan<int> slots)
    {
        if (slots.Length < 1) return null;
        var count = slots[0];
        if (count < 0 || 3 + (4 * count) > slots.Length) return null;
        var stock = new Dictionary<uint, int>();
        for (var i = 0; i < count; i++)
        {
            var quantity = slots[4 + (4 * i)];
            var item = slots[5 + (4 * i)];
            if (item <= 0 || quantity < 0) return null;
            stock[(uint)item] = quantity;
        }
        return stock;
    }
}
