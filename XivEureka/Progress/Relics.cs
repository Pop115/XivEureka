using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Progress;

/// <summary>An item and how many of it.</summary>
public sealed record RelicCost(uint Item, int Count);

/// <summary>
/// One stage of a relic line: a block of consecutive item ids (one per job or role and slot), its item
/// level, the zone where it is made (0: none, from the job quest), what it costs to reach from the
/// previous stage (<see cref="BodyLegsCost"/> for chest and legs when it differs) and the Logos
/// actions to have learned.
/// </summary>
public sealed record RelicStage(string Key, uint First, int Count, int ItemLevel, uint Zone, IReadOnlyList<RelicCost> Cost,
    IReadOnlyList<RelicCost>? BodyLegsCost, int Logos)
{
    public bool Contains(uint item) => item >= First && item < First + Count;

    public IEnumerable<uint> Items => Enumerable.Range(0, Count).Select(i => First + (uint)i);

    public IReadOnlyList<RelicCost> CostFor(RelicSlot slot) =>
        BodyLegsCost != null && slot is RelicSlot.Body or RelicSlot.Legs ? BodyLegsCost : Cost;
}

/// <summary>Where a relic piece goes.</summary>
public enum RelicSlot
{
    MainHand,
    OffHand,
    Head,
    Body,
    Hands,
    Legs,
    Feet,
}

/// <summary>A step of a zone's story: the elemental level it needs, and what it opens (French, English).</summary>
public sealed record StoryStep(int Level, string? French, string? English)
{
    public string? Note => French == null ? null : Loc.T(French, English ?? French);
}

/// <summary>A zone's story: the quest that opens the zone, then the steps given by Krile by level.</summary>
public sealed record StoryZone(uint Territory, uint AccessQuest, IReadOnlyList<StoryStep> Steps);

/// <summary>The relic lines, the materials and the story, read from <c>Data/relics.json</c> (sources in the file).</summary>
public sealed class RelicCatalog(IReadOnlyList<RelicStage> weapon, IReadOnlyList<RelicStage> armor, IReadOnlyList<uint> materials,
    IReadOnlyList<StoryZone> story)
{
    public IReadOnlyList<RelicStage> Weapon => weapon;
    public IReadOnlyList<RelicStage> Armor => armor;
    public IReadOnlyList<uint> Materials => materials;
    public IReadOnlyList<StoryZone> Story => story;

    /// <summary>Every relic item id (to look for in the inventories).</summary>
    public bool IsRelic(uint item) => weapon.Any(s => s.Contains(item)) || armor.Any(s => s.Contains(item));

    public static RelicCatalog Read(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<RawFile>(json, options) ?? throw new InvalidDataException("relics.json is empty");
        static IReadOnlyList<RelicCost> Costs(List<int[]>? c) => (c ?? []).Select(x => new RelicCost((uint)x[0], x[1])).ToList();
        static IReadOnlyList<RelicStage> Stages(List<RawStage> s) => s.Select(x => new RelicStage(x.Key, x.First, x.Count, x.ItemLevel,
            x.Zone, Costs(x.Cost), x.BodyLegsCost == null ? null : Costs(x.BodyLegsCost), x.Logos)).ToList();
        return new RelicCatalog(Stages(raw.Weapon), Stages(raw.Armor), raw.Materials,
            raw.Story.Select(z => new StoryZone(z.Zone, z.AccessQuest, z.Steps.Select(s => new StoryStep(s.Level, s.Fr, s.En)).ToList())).ToList());
    }

    public static RelicCatalog Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XivEureka.Data.relics.json")
            ?? throw new FileNotFoundException("relics.json resource missing from the DLL");
        return Read(stream);
    }

    private sealed record RawFile(List<RawStage> Weapon, List<RawStage> Armor, List<uint> Materials, List<RawZone> Story);
    private sealed record RawStage(string Key, uint First, int Count, int ItemLevel, uint Zone, List<int[]>? Cost,
        List<int[]>? BodyLegsCost, int Logos);
    private sealed record RawZone(uint Zone, uint AccessQuest, List<RawStep> Steps);
    private sealed record RawStep(int Level, string? Fr, string? En);
}

/// <summary>What a relic item fits, from the game sheets: the jobs (English abbreviations) and the slot.</summary>
public sealed record RelicItemInfo(IReadOnlySet<string> Jobs, RelicSlot? Slot);

/// <summary>
/// Where a job stands on one slot of a relic line: the highest stage owned (null: none), the item of
/// each stage for that job and slot, and the next stage with its cost.
/// </summary>
public sealed record SlotProgress(RelicSlot Slot, int? Stage, IReadOnlyList<uint?> ItemByStage, int? NextStage,
    IReadOnlyList<RelicCost> NextCost, int NextLogos)
{
    public bool Done => NextStage == null && Stage != null;
    public uint? Owned => Stage is { } s ? ItemByStage[s] : null;
    public uint? NextItem => NextStage is { } n ? ItemByStage[n] : null;
}

/// <summary>
/// Relic progress with nothing from the game: which stage of each line a job has reached on each
/// slot, from the relic items seen in the inventories, and what the next stage costs.
/// </summary>
public static class RelicProgress
{
    /// <summary>The slots of a line for a job (weapons: main hand, plus off hand for a shield job; armor: the five pieces).</summary>
    public static IReadOnlyList<SlotProgress> For(IReadOnlyList<RelicStage> stages, string job, IReadOnlySet<uint> owned,
        Func<uint, RelicItemInfo> info)
    {
        var bySlot = new Dictionary<RelicSlot, uint?[]>();
        for (var i = 0; i < stages.Count; i++)
            foreach (var item in stages[i].Items)
            {
                var fit = info(item);
                if (fit.Slot is not { } slot || !fit.Jobs.Contains(job)) continue;
                if (!bySlot.TryGetValue(slot, out var items)) bySlot[slot] = items = new uint?[stages.Count];
                items[i] ??= item;
            }

        return bySlot.OrderBy(kv => kv.Key).Select(kv =>
        {
            var items = kv.Value;
            int? stage = null;
            for (var i = items.Length - 1; i >= 0; i--)
                if (items[i] is { } it && owned.Contains(it))
                {
                    stage = i;
                    break;
                }
            int? next = null;
            for (var i = (stage ?? -1) + 1; i < items.Length; i++)
                if (items[i] != null)
                {
                    next = i;
                    break;
                }
            var cost = next is { } n ? stages[n].CostFor(kv.Key) : [];
            return new SlotProgress(kv.Key, stage, items, next, cost, next is { } nl ? stages[nl].Logos : 0);
        }).ToList();
    }

    /// <summary>What is still needed of each material to take the slots from their stage to the end of the line.</summary>
    public static IReadOnlyDictionary<uint, int> RemainingToEnd(IReadOnlyList<RelicStage> stages, IReadOnlyList<SlotProgress> slots)
    {
        var total = new Dictionary<uint, int>();
        foreach (var slot in slots)
            for (var i = (slot.Stage ?? -1) + 1; i < stages.Count; i++)
                if (slot.ItemByStage[i] != null)
                    foreach (var c in stages[i].CostFor(slot.Slot))
                        total[c.Item] = total.GetValueOrDefault(c.Item) + c.Count;
        return total;
    }

    /// <summary>Jobs that have a line (the jobs of its first stage), in the sheet order.</summary>
    public static IReadOnlyList<string> Jobs(IReadOnlyList<RelicStage> stages, Func<uint, RelicItemInfo> info) =>
        stages.Count == 0 ? [] : stages[0].Items.SelectMany(i => info(i).Jobs).Distinct().ToList();
}

/// <summary>Where the player stands in a zone's story.</summary>
public enum StoryState
{
    Locked,
    InProgress,
    Done,
}

/// <summary>
/// The Eureka story with nothing from the game: a zone is open once its access quest is done, and
/// its story is finished once the next zone's access quest is done (it needs the last step). The
/// in-zone steps are not ordinary quests: the player ticks them (<paramref name="ticked"/>, "territory:level").
/// </summary>
public static class StoryProgress
{
    public static StoryState State(IReadOnlyList<StoryZone> story, int index, IReadOnlySet<uint> questsDone, IReadOnlySet<string> ticked)
    {
        var zone = story[index];
        if (!questsDone.Contains(zone.AccessQuest)) return StoryState.Locked;
        var nextOpen = index + 1 < story.Count && questsDone.Contains(story[index + 1].AccessQuest);
        var lastTicked = zone.Steps.Count > 0 && ticked.Contains(Key(zone, zone.Steps[^1]));
        return nextOpen || lastTicked ? StoryState.Done : StoryState.InProgress;
    }

    /// <summary>A step is done when its zone is done or the player ticked it (or a later step).</summary>
    public static bool StepDone(IReadOnlyList<StoryZone> story, int index, StoryStep step, IReadOnlySet<uint> questsDone, IReadOnlySet<string> ticked)
    {
        var zone = story[index];
        if (State(story, index, questsDone, ticked) == StoryState.Done) return true;
        return zone.Steps.SkipWhile(s => s != step).Any(s => ticked.Contains(Key(zone, s)));
    }

    public static string Key(StoryZone zone, StoryStep step) => $"{zone.Territory}:{step.Level}";
}
