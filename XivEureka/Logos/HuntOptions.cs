using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using XivEureka.Zones;

namespace XivEureka.Logos;

/// <summary>What the ranking needs from the moment: zone shown, its NM statuses, running bunny FATEs, level.</summary>
public sealed record HuntContext(uint Territory, IReadOnlyList<NmStatus> Statuses, IReadOnlySet<ushort> BunnyFates,
    int? Level, DateTimeOffset Now);

/// <summary>
/// One way to get a shard: a farm (sprite, mob, lockbox, bunny) or an NM, with a rough time to the
/// first logogram (<see cref="Eta"/>: wait for the weather, respawn or FATE, plus the effort of the
/// kind of source; null: unknown, another zone), the window to aim for, and short user-facing notes
/// (French for now).
/// </summary>
public sealed record HuntOption(uint Shard, uint Territory, Farm? Farm, FarmDrop? Drop, HuntSource? Nm, NmStatus? Status,
    TimeSpan? Eta, TimeWindow? Window, IReadOnlyList<string> Notes)
{
    public IReadOnlyList<Vector2> Places => Farm?.Places ?? (Nm != null ? [Nm.Nm.Position] : []);

    /// <summary>Doable right now: no window to wait for, or the window is open.</summary>
    public bool IsOpen(DateTimeOffset now) => Window == null || Window.IsOpen(now);
}

/// <summary>
/// Ranks the sources of a shard by how fast they give one. The efforts are rough guesses (minutes
/// once the source is there), meant to order, not to promise: a sprite under its weather dies in a
/// minute or two and drops often; an adapted one must adapt first; a running NM FATE is a few
/// minutes, an NM to prep about twenty; a bunny FATE must pop, then the right chest tier be found;
/// lockboxes and random mob drops are rare. The effort is then divided by the chance that what drops
/// gives a useful mneme (<see cref="HuntShard.UsefulChance"/>, raised for drops of several logograms):
/// a shard with 1 useful mneme out of 7 takes about 7 of them. An NM drops one per kill, so its
/// extra tries are other NMs of the train (<see cref="NmRepeat"/> each).
/// </summary>
public static class HuntOptions
{
    public static readonly TimeSpan Sprite = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan AdaptedSprite = TimeSpan.FromMinutes(8);
    public static readonly TimeSpan TooStrong = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RandomMob = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan Lockbox = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan NmRunning = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NmToPrep = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan BunnyRunning = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BunnyToWait = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan NmRepeat = TimeSpan.FromMinutes(30);

    /// <summary>Levels above the player from which a mob counts as hard (slower).</summary>
    public const int HardAbove = 3;

    /// <summary>All the ways to get the shard, fastest first; unknown times last.</summary>
    public static IReadOnlyList<HuntOption> For(HuntShard shard, FarmCatalog farms, HuntContext context)
    {
        var options = new List<HuntOption>();
        foreach (var zone in farms.Zones)
            foreach (var farm in zone.Farms)
                foreach (var drop in farm.Drops.Where(d => d.Shard == shard.Shard))
                    if (FromFarm(shard, zone.Territory, farm, drop, context) is { } option)
                        options.Add(option);
        options.AddRange(shard.Sources.Select(s => FromNm(shard, s, context)));
        return options.OrderBy(o => o.Eta ?? TimeSpan.MaxValue).ThenBy(o => o.Farm?.Kind ?? FarmKind.Bunny + 1).ToList();
    }

    /// <summary>For each useful shard, its fastest known way, the fastest first (then the most useful shard).</summary>
    public static IReadOnlyList<HuntOption> Fastest(HuntPlan plan, FarmCatalog farms, HuntContext context) =>
        plan.Shards
            .Select((shard, rank) => (Best: For(shard, farms, context).FirstOrDefault(o => o.Eta != null), rank))
            .Where(x => x.Best != null)
            .OrderBy(x => x.Best!.Eta).ThenBy(x => x.rank)
            .Select(x => x.Best!)
            .ToList();

    /// <summary>Chance that a drop of <paramref name="count"/> logograms holds at least one useful mneme.</summary>
    public static double DropChance(double usefulChance, double count = 1) => 1 - Math.Pow(1 - usefulChance, count);

    /// <summary>Average of a "1-2" count; 1 when missing or unreadable.</summary>
    public static double AverageCount(string? count)
    {
        var parts = (count ?? "").Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var low) && int.TryParse(parts[1], out var high)
            ? (low + high) / 2.0
            : int.TryParse(count, out var single) ? single : 1;
    }

    private static TimeSpan Scaled(TimeSpan effort, double chance) =>
        chance <= 0 ? TimeSpan.MaxValue / 4 : TimeSpan.FromMinutes(effort.TotalMinutes / chance);

    private static HuntOption? FromFarm(HuntShard shard, uint territory, Farm farm, FarmDrop drop, HuntContext context)
    {
        if (shard.UsefulChance <= 0) return null;
        var notes = new List<string>();
        if (drop.IfAtOrAboveYou)
        {
            if (context.Level is { } level && farm.Level < level) return null; // drops nothing below the player's level
            if (context.Level == null) notes.Add("seulement si son niveau ≥ le tien");
        }

        TimeWindow? window = null;
        if (farm.Weathers.Count > 0 && EurekaWeather.Zone(territory) is { } zone)
        {
            window = farm.Weathers
                .Select(w => NmStatuses.NextWindow(zone, new SpawnCondition(w, false), context.Now))
                .OfType<TimeWindow>()
                .OrderBy(w => w.Start)
                .FirstOrDefault();
            if (window == null) return null; // weather not coming within a week: not worth listing
        }

        var effort = farm.Kind switch
        {
            FarmKind.Sprite => farm.Adapted ? AdaptedSprite : Sprite,
            FarmKind.Mob => RandomMob,
            FarmKind.Lockbox => Lockbox,
            _ => context.BunnyFates.Contains(farm.Fate) ? BunnyRunning : BunnyToWait,
        };
        if (farm.Kind == FarmKind.Bunny)
        {
            effort += drop.Chest switch { "gold" => TimeSpan.FromMinutes(30), "silver" => TimeSpan.FromMinutes(15), _ => TimeSpan.Zero };
            notes.Add($"coffre {Chest(drop.Chest)}" + (drop.Count != null ? $" ({drop.Count})" : ""));
            if (context.BunnyFates.Contains(farm.Fate)) notes.Add("FATE lapin en cours");
        }
        if (farm.Adapted) notes.Add("seulement une fois adapté (ne pas l'étourdir avant)");
        if (farm.Kind == FarmKind.Mob) notes.Add("au hasard, plus souvent en chaîne de 25-30");
        if (farm.Kind == FarmKind.Lockbox) notes.Add("dans les coffres de mutation (tout mob muté ou adapté peut en lâcher)");
        if (context.Level is { } you && farm.Kind != FarmKind.Bunny && farm.Level > you + HardAbove)
        {
            effort += TooStrong;
            notes.Add($"Nv {farm.Level} : costaud seul");
        }

        effort = Scaled(effort, DropChance(shard.UsefulChance, AverageCount(drop.Count)));
        TimeSpan? eta = territory != context.Territory ? null
            : effort + (window != null && window.Start > context.Now ? window.Start - context.Now : TimeSpan.Zero);
        return new HuntOption(shard.Shard, territory, farm, drop, null, null, eta, window, notes);
    }

    private static HuntOption FromNm(HuntShard huntShard, HuntSource source, HuntContext context)
    {
        var shard = huntShard.Shard;
        var status = source.Territory == context.Territory
            ? context.Statuses.FirstOrDefault(s => s.Nm.Npc == source.Nm.Npc)
            : null;
        if (status == null) return new HuntOption(shard, source.Territory, null, null, source, null, null, null, []);

        var now = context.Now;
        TimeSpan eta;
        TimeWindow? window = null;
        if (status.Fate != null || status.Prepped) eta = NmRunning;
        else
        {
            var wait = TimeSpan.Zero;
            if (status.IsRespawning(now)) wait = status.RespawnNotBefore!.Value - now;
            if (status.Wait is { } w)
            {
                window = w;
                if (w.Start - now > wait) wait = w.Start - now;
            }
            eta = NmToPrep + wait;
        }
        if (huntShard.UsefulChance is > 0 and < 1)
            eta += TimeSpan.FromMinutes(NmRepeat.TotalMinutes * ((1 / huntShard.UsefulChance) - 1));
        return new HuntOption(shard, source.Territory, null, null, source, status, eta, window, []);
    }

    private static string Chest(string? chest) => chest switch
    {
        "bronze" => "de bronze",
        "silver" => "d'argent",
        "gold" => "d'or",
        _ => chest ?? "",
    };
}
