using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace XivEureka.Progress;

/// <summary>
/// What the progress tab reads from the game at a given instant: the relic items in the containers
/// the client has loaded, the relic materials in the bags, the Eureka access quests done, and the
/// current job (English abbreviation, the key of <see cref="RelicItemInfo.Jobs"/>).
/// </summary>
public sealed record ProgressSnapshot(IReadOnlySet<uint> Relics, IReadOnlyDictionary<uint, int> Materials,
    IReadOnlySet<uint> QuestsDone, string? Job)
{
    public static readonly ProgressSnapshot Empty = new(new HashSet<uint>(), new Dictionary<uint, int>(), new HashSet<uint>(), null);
}

/// <summary>
/// Reads the <see cref="ProgressSnapshot"/> (main thread only) and what each relic item fits, from
/// the game sheets. Retainers and the glamour dresser are not read: the plugin remembers the relic
/// items it has seen (character state), so a weapon put away still counts.
/// </summary>
public sealed class ProgressReader(IGameInventory inventory, IDataManager data, IClientState clientState, RelicCatalog catalog)
{
    private static readonly GameInventoryType[] RelicContainers =
    [
        GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4,
        GameInventoryType.EquippedItems, GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand,
        GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody, GameInventoryType.ArmoryHands, GameInventoryType.ArmoryLegs,
        GameInventoryType.ArmoryFeets, GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2,
        GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2,
    ];

    private static readonly GameInventoryType[] Bags =
        [GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4];

    private readonly Dictionary<uint, RelicItemInfo> infos = [];
    private readonly Dictionary<string, string> jobNames = [];

    public unsafe ProgressSnapshot Read()
    {
        if (!clientState.IsLoggedIn) return ProgressSnapshot.Empty;

        var relics = new HashSet<uint>();
        foreach (var container in RelicContainers)
            foreach (var item in inventory.GetInventoryItems(container))
                if (!item.IsEmpty && catalog.IsRelic(item.BaseItemId)) relics.Add(item.BaseItemId);

        var materials = new Dictionary<uint, int>();
        foreach (var bag in Bags)
            foreach (var item in inventory.GetInventoryItems(bag))
                if (!item.IsEmpty && catalog.Materials.Contains(item.BaseItemId))
                    materials[item.BaseItemId] = materials.GetValueOrDefault(item.BaseItemId) + item.Quantity;

        var quests = catalog.Story.Select(z => z.AccessQuest).Where(QuestManager.IsQuestComplete).ToHashSet();

        string? job = null;
        var player = PlayerState.Instance();
        if (player != null && data.GetExcelSheet<ClassJob>(ClientLanguage.English).GetRowOrDefault(player->CurrentClassJobId) is { } row)
            job = row.Abbreviation.ExtractText();

        return new ProgressSnapshot(relics, materials, quests, job);
    }

    /// <summary>The jobs (English abbreviations) and slot of an item, from the Item sheet; cached.</summary>
    public RelicItemInfo Info(uint item)
    {
        if (infos.TryGetValue(item, out var info)) return info;
        var row = data.GetExcelSheet<Item>(ClientLanguage.English).GetRowOrDefault(item);
        var jobs = row?.ClassJobCategory.ValueNullable?.Name.ExtractText().Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet() ?? [];
        RelicSlot? slot = row?.EquipSlotCategory.RowId switch
        {
            1 or 13 => RelicSlot.MainHand,
            2 => RelicSlot.OffHand,
            3 => RelicSlot.Head,
            4 => RelicSlot.Body,
            5 => RelicSlot.Hands,
            7 => RelicSlot.Legs,
            8 => RelicSlot.Feet,
            _ => null,
        };
        return infos[item] = new RelicItemInfo(jobs, slot);
    }

    /// <summary>A job's abbreviation in the client language, from its English one ("PLD" → "PAL" on a French client).</summary>
    public string JobName(string english)
    {
        if (jobNames.TryGetValue(english, out var name)) return name;
        var row = data.GetExcelSheet<ClassJob>(ClientLanguage.English).FirstOrDefault(j => j.Abbreviation.ExtractText() == english);
        var local = row.RowId == 0 ? null : data.GetExcelSheet<ClassJob>().GetRowOrDefault(row.RowId)?.Abbreviation.ExtractText();
        return jobNames[english] = string.IsNullOrEmpty(local) ? english : local;
    }
}
