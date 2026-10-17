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
/// first useful mneme (<see cref="Eta"/>: wait for the weather, time of day, respawn or FATE, then
/// <see cref="Tries"/> tries of <see cref="TryTime"/> each; null: unknown, another zone), the chance
/// per try that it gives a useful mneme (<see cref="TryChance"/>), the window to aim for, and short
/// user-facing notes (French for now).
/// </summary>
public sealed record HuntOption(uint Shard, uint Territory, Farm? Farm, FarmDrop? Drop, HuntSource? Nm, NmStatus? Status,
    TimeSpan? Eta, TimeWindow? Window, IReadOnlyList<string> Notes, double TryChance = 1, double Tries = 1,
    TimeSpan TryTime = default)
{
    public IReadOnlyList<Vector2> Places => Farm?.Places ?? (Nm != null ? [Nm.Nm.Position] : []);

    /// <summary>Doable right now: no window to wait for, or the window is open.</summary>
    public bool IsOpen(DateTimeOffset now) => Window == null || Window.IsOpen(now);
}

/// <summary>
/// Ranks the sources of a shard by the expected time to a useful mneme:
/// wait + time per try / chance per try. The chance per try is the drop chance (chest tier,
/// adaptation, drop rate) times the chance that the logograms dropped (several from some sources)
/// hold at least one mneme still missing (<see cref="HuntShard.UsefulChance"/>; mnemes do come out
/// evenly: community record of 700,000 appraisals). Measured where known (bunny chests, in
/// <c>farms.json</c>); elsewhere the defaults below are guesses, meant to order, not to promise.
/// An NM drops one logogram per kill: its extra tries are other NMs of the train (<see cref="NmRepeat"/>).
/// </summary>
public static class HuntOptions
{
    // Time per try, once the source is there.
    public static readonly TimeSpan SpriteKill = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AdaptedKill = TimeSpan.FromMinutes(4); // bigger, and it must adapt first
    public static readonly TimeSpan MobKill = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan LockboxKill = TimeSpan.FromMinutes(4); // a mutated or adapted mob
    /// <summary>A running bunny FATE: finish it, then follow the bunny to the buried coffer.</summary>
    public static readonly TimeSpan BunnyRunning = TimeSpan.FromMinutes(15);
    /// <summary>A whole bunny cycle: it comes back 7-8 min after the last coffer, FATE, treasure hunt, travel.</summary>
    public static readonly TimeSpan BunnyCycle = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan NmRunning = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NmToPrep = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan NmRepeat = TimeSpan.FromMinutes(30);

    // Chance per try that the logogram drops, when farms.json does not say (guesses).
    public const double SpriteChance = 0.3;
    public const double AdaptedChance = 0.4; // adapts at random, then drops
    public const double MobChance = 0.05;
    public const double LockboxChance = 0.03; // lockbox drop, then the logogram in it
    public const double BunnyChance = 0.3;

    /// <summary>Levels above the player from which a mob counts as hard: each kill takes twice as long.</summary>
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

    /// <summary>Default chance per try for the farm kind.</summary>
    public static double DefaultChance(Farm farm) => farm.Kind switch
    {
        FarmKind.Sprite => farm.Adapted ? AdaptedChance : SpriteChance,
        FarmKind.Mob => MobChance,
        FarmKind.Lockbox => LockboxChance,
        _ => BunnyChance,
    };

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
        // A farm needs a weather or a time of day, never both (checked by a test on the data).
        if (farm.Time is { } time) window = TimeOfDayWindow(time, context.Now);

        var running = farm.Kind == FarmKind.Bunny && context.BunnyFates.Contains(farm.Fate);
        var tryTime = farm.Minutes is { } minutes ? TimeSpan.FromMinutes(minutes) : farm.Kind switch
        {
            FarmKind.Sprite => farm.Adapted ? AdaptedKill : SpriteKill,
            FarmKind.Mob => MobKill,
            FarmKind.Lockbox => LockboxKill,
            _ => running ? BunnyRunning : BunnyCycle,
        };
        if (context.Level is { } you && farm.Kind != FarmKind.Bunny && farm.Level > you + HardAbove)
        {
            tryTime *= 2;
            notes.Add($"Nv {farm.Level} : costaud seul");
        }

        var dropChance = drop.Chance ?? DefaultChance(farm);
        var tryChance = dropChance * DropChance(shard.UsefulChance, AverageCount(drop.Count));
        var tries = 1 / tryChance;

        if (farm.Kind == FarmKind.Bunny)
        {
            notes.Add($"coffre {Chest(drop.Chest)}");
            notes.Add(running ? "FATE lapin en cours" : "le lapin revient 7-8 min après le dernier coffre, puis chasse au trésor");
        }
        if (farm.Note != null) notes.Add(farm.Note);
        if (farm.Adapted) notes.Add("seulement s'il s'adapte (au hasard ; ne pas l'étourdir avant)");
        if (farm.Kind == FarmKind.Mob) notes.Add("au hasard, plus souvent en chaîne de 25-30");
        if (farm.Kind == FarmKind.Lockbox) notes.Add("dans les coffres de mutation (tout mob muté ou adapté peut en lâcher)");

        TimeSpan? eta = territory != context.Territory ? null
            : TimeSpan.FromMinutes(tryTime.TotalMinutes * tries)
              + (window != null && window.Start > context.Now ? window.Start - context.Now : TimeSpan.Zero);
        return new HuntOption(shard.Shard, territory, farm, drop, null, null, eta, window, notes, tryChance, tries, tryTime);
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
        var tries = huntShard.UsefulChance > 0 ? 1 / huntShard.UsefulChance : 1;
        eta += TimeSpan.FromMinutes(NmRepeat.TotalMinutes * (tries - 1));
        return new HuntOption(shard, source.Territory, null, null, source, status, eta, window, [], huntShard.UsefulChance,
            tries, NmRepeat);
    }

    /// <summary>The open or next Eorzea day (6:00-19:00) or night window.</summary>
    private static TimeWindow? TimeOfDayWindow(string time, DateTimeOffset now) =>
        time switch
        {
            "night" => NmStatuses.NextWindow(EurekaWeather.Zones[0], new SpawnCondition(null, true), now),
            "day" => DayWindow(now),
            _ => null,
        };

    private static TimeWindow DayWindow(DateTimeOffset now)
    {
        var (nightNext, at) = EurekaWeather.NextDayNightSwitch(now);
        if (nightNext) return new TimeWindow(now, at); // day until dusk
        var (_, dusk) = EurekaWeather.NextDayNightSwitch(at);
        return new TimeWindow(at, dusk);
    }

    private static string Chest(string? chest) => chest switch
    {
        "bronze" => "de bronze",
        "silver" => "d'argent",
        "gold" => "d'or",
        _ => chest ?? "",
    };
}
