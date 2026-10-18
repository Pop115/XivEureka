using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using XivEureka.Zones;

namespace XivEureka.Logos;

/// <summary>What an XP source is: a sprite, a mob that mutates or adapts, a plain mob, an NM prep mob, a chain of 30, an NM.</summary>
public enum XpKind
{
    Sprite,
    Changing,
    Mob,
    Prep,
    Chain,
    Nm,
}

/// <summary>
/// One way to level up in the zone shown: <see cref="Rate"/> (experience per minute, <see cref="HuntXp"/>
/// units), <see cref="Score"/> (the experience it gives over the next <see cref="XpOptions.Horizon"/>,
/// its wait taken off), when it is there, how hard, short user-facing notes, and the useful
/// logograms it drops on the way. <see cref="PrepFor"/>: the NM a prep mob makes appear.
/// </summary>
public sealed record XpOption(XpKind Kind, uint Npc, int Level, IReadOnlyList<Vector2> Places, TimeWindow? Window,
    double Rate, double Score, Difficulty Difficulty, IReadOnlyList<string> Notes)
{
    public Farm? Farm { get; init; }
    public EurekaNm? Nm { get; init; }
    public NmStatus? Status { get; init; }
    public EurekaNm? PrepFor { get; init; }
    public IReadOnlyList<uint> UsefulShards { get; init; } = [];

    /// <summary>Against the reference rhythm (plain mobs 3 levels above you): 1.0 = as fast.</summary>
    public double Relative => Rate / HuntXp.ReferenceRate;
}

/// <summary>
/// Ranks what to hit to level up in the zone shown, by the experience it gives over the next hour.
/// From the guides (icy-veins Eureka overview, gamecraftgather Eureka guide, 2026-10-18): mobs give
/// more up to 6 levels above you; a chain of 30 gives
/// a bonus every 5 kills; mutated and adapted mobs give much more (kill them on a multiple of 5 of the
/// chain); with Reflect L, sprites kill themselves (their attacks are magic); an NM more than one
/// level above you gives reduced experience. Mobs 3 or more levels below you give almost nothing,
/// mobs beyond your level gap (<see cref="HuntContext.Gap"/>) are not hit: both left out. Sources: the logogram farms (sprites, changing mobs, plain mobs), the NM prep mobs (one
/// per NM, with its places), the running or prepped NMs, and a chain on any mobs at the right level.
/// A kill of a plain mob takes <see cref="HuntOptions.PrepKill"/>, longer for a mob that changes,
/// shared by the party when it is counted. The chain's bonus applies to every kill in a row:
/// it does not change the ranking.
/// </summary>
public static class XpOptions
{
    /// <summary>Reflect L (Action sheet).</summary>
    public const uint ReflectL = 13000;

    /// <summary>Statuses (Status sheet): Potion of Harmony (+10 %), and the elementals' (fairies') experience blessing.</summary>
    public const uint HarmonyStatus = 1587;
    public const uint FairyStatus = 1461;

    /// <summary>A sprite under Reflect L: it kills itself, about twice as fast as fighting it.</summary>
    public static readonly TimeSpan ReflectKill = TimeSpan.FromMinutes(1);

    /// <summary>The ranking counts the experience over this time: a source an hour away counts for nothing.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(60);

    /// <summary>Mobs this many levels below you or more give almost nothing: not listed.</summary>
    public const int BelowYou = 3;

    /// <summary>A mob that mutates or adapts takes a bit longer once changed.</summary>
    public const double ChangingKill = 1.25;

    /// <summary>The sources of the zone shown, best first (experience over the next hour, then sooner).</summary>
    public static IReadOnlyList<XpOption> For(FarmCatalog farms, HuntContext context, IReadOnlySet<uint> learned,
        IReadOnlySet<uint> usefulShards)
    {
        var options = new List<XpOption>();
        if (Chain(context) is { } chain) options.Add(chain);
        foreach (var farm in farms.Zone(context.Territory))
            if (FromFarm(farm, context, learned, usefulShards) is { } option)
                options.Add(option);
        foreach (var status in context.Statuses)
        {
            if (FromPrep(status.Nm, context) is { } prep) options.Add(prep);
            if (FromNm(status, context, usefulShards) is { } nm) options.Add(nm);
        }
        // One line per mob: a sprite listed for two logograms, a prep mob that is also a farm.
        return options
            .GroupBy(o => (o.Kind == XpKind.Nm, o.Kind == XpKind.Chain, o.Npc, o.Level))
            .Select(Merge)
            .OrderByDescending(o => o.Score)
            .ThenBy(o => o.Window?.Start ?? context.Now)
            .ToList();
    }

    private static XpOption Merge(IEnumerable<XpOption> same)
    {
        var all = same.ToList();
        var best = all.MaxBy(o => o.Score)!;
        return best with
        {
            PrepFor = best.PrepFor ?? all.Select(o => o.PrepFor).FirstOrDefault(n => n != null),
            UsefulShards = all.SelectMany(o => o.UsefulShards).Distinct().ToList(),
        };
    }

    /// <summary>Experience over the horizon: the rate, less the part of the hour spent waiting.</summary>
    public static double Score(double rate, TimeWindow? window, DateTimeOffset now)
    {
        var wait = window != null && window.Start > now ? window.Start - now : TimeSpan.Zero;
        return rate * Math.Max(0, 1 - (wait / Horizon));
    }

    private static bool InRange(int level, HuntContext context) =>
        context.Level is not { } you || (level > you - BelowYou && level <= you + context.Gap);


    private static XpOption? Chain(HuntContext context)
    {
        if (context.Level is not { } you) return null;
        var level = you + context.Gap;
        var tryTime = HuntOptions.Shared(HuntOptions.PrepKill, context);
        var rate = HuntXp.LevelFactor(level, you) / tryTime.TotalMinutes;
        var notes = new List<string>
        {
            Loc.T($"n'importe quels mobs Nv {level} (+{level - you}) : l'XP monte jusqu'à +6",
                $"any lv {level} mobs (+{level - you}): experience grows up to +6"),
            Loc.T("bonus tous les 5 kills ; la chaîne repart à 0 après 30", "bonus every 5 kills; the chain restarts after 30"),
        };
        return new XpOption(XpKind.Chain, 0, level, [], null, rate, rate,
            HuntXp.Of(level, you, changes: false, nm: false), notes);
    }

    private static XpOption? FromFarm(Farm farm, HuntContext context, IReadOnlySet<uint> learned, IReadOnlySet<uint> usefulShards)
    {
        // Bunny FATEs give little; the chains of the data are the generic chain above.
        if (farm.Kind is FarmKind.Bunny or FarmKind.Chain) return null;
        if (!InRange(farm.Level, context)) return null;
        if (!HuntOptions.TryWindow(farm, context.Territory, context.Now, out var window)) return null;

        var changes = farm.Adapted || farm.Mutated;
        var kind = farm.Kind == FarmKind.Sprite ? XpKind.Sprite : changes ? XpKind.Changing : XpKind.Mob;
        var notes = new List<string>();
        var reflect = farm.Kind == FarmKind.Sprite && learned.Contains(ReflectL);
        TimeSpan tryTime;
        if (reflect)
        {
            tryTime = HuntOptions.Shared(ReflectKill, context);
            notes.Add(Loc.T("avec Reflect L : ses attaques magiques le tuent (à relancer toutes les 5-6 s)",
                "with Reflect L: its magic attacks kill it (recast every 5-6 s)"));
        }
        else
        {
            var alone = farm.Kind == FarmKind.Sprite ? HuntOptions.SpriteKill : HuntOptions.PrepKill;
            tryTime = HuntOptions.Shared(changes ? alone * ChangingKill : alone, context);
            if (farm.Kind == FarmKind.Sprite)
                notes.Add(Loc.T("Reflect L (pas apprise) les ferait se tuer eux-mêmes", "Reflect L (not learned) would make them kill themselves"));
        }
        if (changes)
            notes.Add(Loc.T("à tuer une fois " + (farm.Adapted ? "adapté" : "muté") + ", sur un multiple de 5 de la chaîne",
                "kill it once " + (farm.Adapted ? "adapted" : "mutated") + ", on a multiple of 5 of the chain"));

        var rate = HuntXp.ForFarm(farm, tryTime, context.Level);
        // Logograms only drop from mobs at or above your level.
        IReadOnlyList<uint> useful = context.Level is { } level && farm.Level < level
            ? []
            : farm.Drops.Select(d => d.Shard).Where(usefulShards.Contains).Distinct().ToList();
        return new XpOption(kind, farm.Npc, farm.Level, farm.Places, window, rate, Score(rate, window, context.Now),
            HuntXp.Of(farm.Level, context.Level, changes, nm: false), notes)
        {
            Farm = farm,
            UsefulShards = useful,
        };
    }

    private static XpOption? FromPrep(EurekaNm nm, HuntContext context)
    {
        var prep = nm.Prep;
        if (!InRange(prep.Level, context)) return null;
        TimeWindow? window = null;
        if (!prep.Condition.None && EurekaWeather.Zone(context.Territory) is { } zone)
        {
            window = NmStatuses.NextWindow(zone, prep.Condition, context.Now);
            if (window == null) return null;
        }
        var tryTime = HuntOptions.Shared(HuntOptions.PrepKill, context);
        var rate = HuntXp.LevelFactor(prep.Level, context.Level) / tryTime.TotalMinutes;
        var notes = new List<string>();
        return new XpOption(XpKind.Prep, prep.Npc, prep.Level, prep.Places, window, rate, Score(rate, window, context.Now),
            HuntXp.Of(prep.Level, context.Level, changes: false, nm: false), notes)
        {
            PrepFor = nm,
        };
    }

    /// <summary>A running NM (its fight), or a prepped one (the spawn roll, then the fight); the others come through their prep.</summary>
    private static XpOption? FromNm(NmStatus status, HuntContext context, IReadOnlySet<uint> usefulShards)
    {
        var nm = status.Nm;
        var running = status.Fate != null;
        if (!running && !status.Prepped) return null;
        if (context.Level is { } you && (nm.Level <= you - BelowYou || nm.Level > you + HuntXp.NmRewardAbove)) return null;
        var time = running ? HuntOptions.NmFight(nm, context) : HuntOptions.NmPrepped(nm, context);
        var rate = HuntXp.ForNm(nm, time, running: true, context.Level);
        IReadOnlyList<uint> useful = nm.Shard != 0 && usefulShards.Contains(nm.Shard) ? [nm.Shard] : [];
        var window = running ? null : status.Wait;
        return new XpOption(XpKind.Nm, nm.Npc, nm.Level, [nm.Position], window, rate, Score(rate, window, context.Now),
            HuntXp.Of(nm.Level, context.Level, changes: false, nm: true), [])
        {
            Nm = nm,
            Status = status,
            UsefulShards = useful,
        };
    }
}
