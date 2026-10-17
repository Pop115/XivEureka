using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using XivEureka.Zones;

namespace XivEureka.Logos;

/// <summary>
/// What the ranking needs from the moment: zone shown, its NM statuses, running bunny FATEs, level,
/// how many players farm together (<see cref="PartySize"/>: no public group counted on), and how
/// much leveling up counts (<see cref="XpWeight"/>, 0 to 1; 0 at the zone's max level, see <see cref="HuntXp"/>).
/// </summary>
public sealed record HuntContext(uint Territory, IReadOnlyList<NmStatus> Statuses, IReadOnlySet<ushort> BunnyFates,
    int? Level, DateTimeOffset Now, int PartySize = 1, double XpWeight = 0);

/// <summary>
/// One way to get a shard: a farm (sprite, mob, lockbox, bunny) or an NM, with a rough time to the
/// first useful mneme, <see cref="Eta"/> = <see cref="Wait"/> (weather, time of day, respawn) +
/// <see cref="FirstTry"/> + (<see cref="Tries"/> - 1) x <see cref="TryTime"/> (null: unknown, another
/// zone); the chance per try that it gives a useful mneme (<see cref="TryChance"/>), the window to aim
/// for, and short user-facing notes (French for now). The first try can differ from the next ones:
/// a bunny FATE already running, an NM to prep before the train goes on.
/// </summary>
public sealed record HuntOption(uint Shard, uint Territory, Farm? Farm, FarmDrop? Drop, HuntSource? Nm, NmStatus? Status,
    TimeSpan? Eta, TimeWindow? Window, IReadOnlyList<string> Notes, double TryChance = 1, double Tries = 1,
    TimeSpan TryTime = default, TimeSpan Wait = default, TimeSpan FirstTry = default)
{
    /// <summary>Experience per minute while on it (<see cref="HuntXp"/> units).</summary>
    public double XpRate { get; init; }

    /// <summary>Share of the reference leveling rhythm, 0 to 1.</summary>
    public double XpShare => HuntXp.Share(XpRate);

    public Difficulty Difficulty { get; init; } = Difficulty.Medium;

    /// <summary>What the ranking counts: <see cref="Eta"/> less the part that also levels you up.</summary>
    public TimeSpan? Counted { get; init; }

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
/// An NM drops one logogram per kill: its extra tries are other NMs dropping it, prepped as well.
/// Times are for the party farming on its own (<see cref="HuntContext.PartySize"/>), no train counted on.
/// <c>HuntScenarioTests</c> walks through the ranking on real data, case by case.
/// </summary>
public static class HuntOptions
{
    // Time per try once the source is there, for one player alone; a party shares the kills
    // (see Shared). No public group or train is counted on.
    public static readonly TimeSpan SpriteKill = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AdaptedKill = TimeSpan.FromMinutes(4); // bigger, and it must adapt first
    public static readonly TimeSpan MobKill = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan LockboxKill = TimeSpan.FromMinutes(4); // a mutated or adapted mob
    /// <summary>A chain of 30 kills, without letting it drop.</summary>
    public static readonly TimeSpan ChainKills = MobKill * 30;
    /// <summary>Going to another Eureka zone (and finding an instance).</summary>
    public static readonly TimeSpan ZoneChange = TimeSpan.FromMinutes(15);
    /// <summary>Players beyond this add nothing to the kill speed (they get in each other's way).</summary>
    public const int MaxSharing = 4;

    // Bunny FATE: it comes back 7-8 min after the last coffer is opened; then the FATE itself, then
    // following the bunny until it finds the buried coffer.
    public static readonly TimeSpan BunnyRespawn = TimeSpan.FromMinutes(8);
    public static readonly TimeSpan BunnyTravel = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BunnyFate = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TreasureHunt = TimeSpan.FromMinutes(10);
    /// <summary>A running bunny FATE: finish it, then the treasure hunt.</summary>
    public static readonly TimeSpan BunnyRunning = BunnyFate + TreasureHunt;
    /// <summary>A whole bunny cycle: respawn, travel, FATE, treasure hunt.</summary>
    public static readonly TimeSpan BunnyCycle = BunnyRespawn + BunnyTravel + BunnyFate + TreasureHunt;

    // NM: its FATE pops only once its prep mob was killed enough (about 30 kills, the "prep"), and
    // then at a spawn roll (every ~11 min and after bunny FATEs, among the prepped NMs). An NM is
    // made for a group: alone, at your level, its fight is long.
    public const int PrepKills = 30;
    /// <summary>One prep kill alone (a normal mob 5 levels above the NM).</summary>
    public static readonly TimeSpan PrepKill = TimeSpan.FromMinutes(2);
    /// <summary>Average wait for the spawn roll to pick a prepped NM.</summary>
    public static readonly TimeSpan NmRoll = TimeSpan.FromMinutes(15);
    /// <summary>An NM comes back 2 hours after its kill.</summary>
    public static readonly TimeSpan NmRespawn = TimeSpan.FromMinutes(120);
    /// <summary>Getting to the FATE.</summary>
    public static readonly TimeSpan NmTravel = TimeSpan.FromMinutes(3);
    /// <summary>The NM fight alone, NM at your level (half if 5+ levels below you, twice if above).</summary>
    public static readonly TimeSpan NmFightAlone = TimeSpan.FromMinutes(20);

    // Chance per try that the logogram drops, when farms.json does not say (guesses).
    public const double SpriteChance = 0.3;
    public const double AdaptedChance = 0.4; // adapts at random, then drops
    public const double MobChance = 0.05;
    public const double LockboxChance = 0.03; // lockbox drop, then the logogram in it
    public const double BunnyChance = 0.3;
    public const double ChainChance = 0.5; // the logogram at the end of a 30 chain (guess)

    /// <summary>Levels above the player from which a mob counts as hard: each kill takes twice as long.</summary>
    public const int HardAbove = 3;

    /// <summary>Levels above the player beyond which a mob is out of reach for a small party: not proposed.</summary>
    public const int MaxAbove = 5;

    /// <summary>A solo time shared by the party (up to <see cref="MaxSharing"/> players).</summary>
    public static TimeSpan Shared(TimeSpan alone, HuntContext context) =>
        alone / Math.Clamp(context.PartySize, 1, MaxSharing);

    /// <summary>Getting to a running NM FATE and killing the NM, with the party.</summary>
    public static TimeSpan NmFight(EurekaNm nm, HuntContext context)
    {
        var factor = context.Level is not { } you ? 1.0 : nm.Level > you ? 2.0 : nm.Level <= you - 5 ? 0.5 : 1.0;
        return NmTravel + Shared(NmFightAlone * factor, context);
    }

    /// <summary>The prep: about 30 kills of the prep mob (twice as long if it is well above you), with the party.</summary>
    public static TimeSpan NmPrep(EurekaNm nm, HuntContext context)
    {
        var kill = context.Level is { } you && nm.Prep.Level > you + HardAbove ? PrepKill * 2 : PrepKill;
        return Shared(kill * PrepKills, context);
    }

    /// <summary>An NM prepped already (tracker): spawn roll, then the fight.</summary>
    public static TimeSpan NmPrepped(EurekaNm nm, HuntContext context) => NmRoll + NmFight(nm, context);

    /// <summary>An NM not prepped: prep kills, spawn roll, then the fight.</summary>
    public static TimeSpan NmToPrep(EurekaNm nm, HuntContext context) => NmPrep(nm, context) + NmPrepped(nm, context);

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
        return options.Select(o => o with { Counted = HuntXp.Counted(o.Eta, o.XpShare, context.XpWeight) })
            .OrderBy(o => o.Counted ?? TimeSpan.MaxValue).ThenBy(o => o.Farm?.Kind ?? FarmKind.Bunny + 1).ToList();
    }

    /// <summary>For each useful shard, its fastest known way, the fastest first (then the most useful shard).</summary>
    public static IReadOnlyList<HuntOption> Fastest(HuntPlan plan, FarmCatalog farms, HuntContext context) =>
        plan.Shards
            .Select((shard, rank) => (Best: For(shard, farms, context).FirstOrDefault(o => o.Eta != null), rank))
            .Where(x => x.Best != null)
            .OrderBy(x => x.Best!.Counted).ThenBy(x => x.rank)
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

    /// <summary>Wait, then the first try, then the expected extra tries.</summary>
    public static TimeSpan Total(TimeSpan wait, TimeSpan firstTry, double tries, TimeSpan tryTime) =>
        wait + firstTry + TimeSpan.FromMinutes(tryTime.TotalMinutes * Math.Max(0, tries - 1));

    /// <summary>Default chance per try for the farm kind.</summary>
    public static double DefaultChance(Farm farm) => farm.Kind switch
    {
        FarmKind.Sprite => farm.Adapted ? AdaptedChance : SpriteChance,
        FarmKind.Mob => MobChance,
        FarmKind.Lockbox => LockboxChance,
        FarmKind.Chain => ChainChance,
        _ => BunnyChance,
    };

    private static HuntOption? FromFarm(HuntShard shard, uint territory, Farm farm, FarmDrop drop, HuntContext context)
    {
        if (shard.UsefulChance <= 0) return null;
        if (farm.RelativeLevel is { } above && context.Level is { } own) farm = farm with { Level = own + above };
        if (farm.Kind != FarmKind.Bunny && context.Level is { } reach && farm.Level > reach + MaxAbove) return null;
        // Eureka mobs drop logograms (and lockboxes) only at or above the player's level: below, the
        // guides tell to level down or sync to a FATE. Bunny coffers and NM FATEs are not concerned.
        if (farm.Kind != FarmKind.Bunny && context.Level is { } level && farm.Level < level) return null;
        var notes = new List<string>();
        if (farm.Kind != FarmKind.Bunny && context.Level == null)
            notes.Add(Loc.T("seulement si son niveau ≥ le tien", "only if its level ≥ yours"));
        var otherZone = territory != context.Territory;
        if (otherZone)
            notes.Add(Loc.T($"autre zone : {EurekaWeather.Zone(territory)?.Name} (+{ZoneChange.TotalMinutes:0} min)",
                $"other zone: {EurekaWeather.Zone(territory)?.Name} (+{ZoneChange.TotalMinutes:0} min)"));

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
            FarmKind.Chain => ChainKills,
            _ => BunnyCycle,
        };
        if (context.Level is { } you && farm.Kind != FarmKind.Bunny && farm.Level > you + HardAbove)
        {
            tryTime *= 2;
            notes.Add(Loc.T($"Nv {farm.Level} : costaud", $"lv {farm.Level}: tough"));
        }
        if (farm.Kind != FarmKind.Bunny && farm.Minutes == null) tryTime = Shared(tryTime, context);

        var dropChance = drop.Chance ?? DefaultChance(farm);
        var tryChance = dropChance * DropChance(shard.UsefulChance, AverageCount(drop.Count));
        var tries = 1 / tryChance;

        if (farm.Kind == FarmKind.Bunny)
        {
            notes.Add(Chest(drop.Chest));
            notes.Add(running ? Loc.T("FATE lapin en cours", "bunny FATE running")
                : Loc.T("le lapin revient 7-8 min après le dernier coffre, puis chasse au trésor",
                    "the bunny comes back 7-8 min after the last coffer, then the treasure hunt"));
        }
        if (farm.Note != null) notes.Add(farm.Note);
        if (farm.Adapted)
            notes.Add(Loc.T("seulement s'il s'adapte (au hasard ; ne pas l'étourdir avant)", "only once it adapts (random; do not stun it before)"));
        if (farm.Mutated)
            notes.Add(Loc.T("seulement une fois muté (au hasard, dans la condition indiquée)", "only once mutated (random, under the condition shown)"));
        if (farm.Kind == FarmKind.Mob)
            notes.Add(Loc.T("au hasard, plus souvent en chaîne de 25-30", "at random, more often at a 25-30 chain"));
        if (farm.Kind == FarmKind.Chain)
            notes.Add(Loc.T($"30 kills enchaînés sur des mobs Nv {farm.Level} ou plus, sans laisser tomber la chaîne",
                $"30 kills in a row on lv {farm.Level}+ mobs, without letting the chain drop"));
        if (farm.Kind == FarmKind.Lockbox)
            notes.Add(Loc.T("dans les coffres de mutation (tout mob muté ou adapté peut en lâcher)",
                "in mutation lockboxes (any mutated or adapted mob can drop one)"));

        var firstTry = running ? BunnyRunning : tryTime;
        var wait = window != null && window.Start > context.Now ? window.Start - context.Now : TimeSpan.Zero;
        TimeSpan? eta = Total(wait, firstTry, tries, tryTime) + (otherZone ? ZoneChange : TimeSpan.Zero);
        var levelsUp = context.Level is not { } current || current < HuntXp.MaxLevel(territory);
        return new HuntOption(shard.Shard, territory, farm, drop, null, null, eta, window, notes, tryChance, tries, tryTime,
            wait, firstTry)
        {
            XpRate = levelsUp ? HuntXp.ForFarm(farm, tryTime, context.Level) : 0,
            Difficulty = HuntXp.Of(farm.Level, context.Level, farm.Mutated || farm.Adapted, nm: false),
        };
    }

    private static HuntOption FromNm(HuntShard huntShard, HuntSource source, HuntContext context)
    {
        var shard = huntShard.Shard;
        var status = source.Territory == context.Territory
            ? context.Statuses.FirstOrDefault(s => s.Nm.Npc == source.Nm.Npc)
            : null;
        if (status == null) return FromNmElsewhere(huntShard, source, context);

        var now = context.Now;
        var wait = TimeSpan.Zero;
        TimeWindow? window = null;
        TimeSpan firstTry;
        if (status.Fate != null) firstTry = NmFight(source.Nm, context);
        else
        {
            if (status.IsRespawning(now)) wait = status.RespawnNotBefore!.Value - now;
            if (status.Wait is { } w)
            {
                window = w;
                if (w.Start - now > wait) wait = w.Start - now;
            }
            firstTry = status.Prepped ? NmPrepped(source.Nm, context) : NmToPrep(source.Nm, context);
        }
        var tries = huntShard.UsefulChance > 0 ? 1 / huntShard.UsefulChance : 1;
        // Each extra try is another NM dropping the same logogram, to prep yourself as well; when this
        // NM is the only one within reach, the same one again, so not before its respawn.
        var repeat = NmToPrep(source.Nm, context);
        var others = huntShard.Sources.Count(s => s.Territory == source.Territory && s.Nm.Npc != source.Nm.Npc
            && (context.Level is not { } reach || s.Nm.Prep.Level <= reach + MaxAbove));
        if (others == 0 && repeat < NmRespawn) repeat = NmRespawn;
        var notes = new List<string>();
        if (status.Fate == null && !status.Prepped)
            notes.Add(Loc.T($"à préparer : ~{PrepKills} kills du mob de prépa (Nv {source.Nm.Prep.Level}), puis le tirage du jeu (toutes les ~11 min)",
                $"to prep: ~{PrepKills} kills of the prep mob (lv {source.Nm.Prep.Level}), then the game's spawn roll (every ~11 min)"));
        if (context.Level is { } level && source.Nm.Level > level)
            notes.Add(Loc.T($"NM Nv {source.Nm.Level} : long à {context.PartySize}", $"NM lv {source.Nm.Level}: long for {context.PartySize}"));
        // A prep mob out of reach: the NM only comes if someone else preps it.
        var outOfReach = status.Fate == null && !status.Prepped && context.Level is { } you && source.Nm.Prep.Level > you + MaxAbove;
        if (outOfReach)
            notes.Add(Loc.T($"prépa trop forte à {context.PartySize} (mob Nv {source.Nm.Prep.Level})",
                $"prep too strong for {context.PartySize} (mob lv {source.Nm.Prep.Level})"));
        TimeSpan? eta = outOfReach ? null : Total(wait, firstTry, tries, repeat);
        return new HuntOption(shard, source.Territory, null, null, source, status, eta, window,
            notes, huntShard.UsefulChance, tries, repeat, wait, firstTry)
        {
            XpRate = HuntXp.ForNm(source.Nm, firstTry, status.Fate != null, context.Level),
            Difficulty = HuntXp.Of(source.Nm.Level, context.Level, changes: false, nm: true),
        };
    }

    /// <summary>
    /// An NM of another zone: its state there is unknown, so it is counted to prep, after its
    /// condition window and the zone change.
    /// </summary>
    private static HuntOption FromNmElsewhere(HuntShard huntShard, HuntSource source, HuntContext context)
    {
        var nm = source.Nm;
        var zone = EurekaWeather.Zone(source.Territory);
        var window = zone == null ? null
            : NmStatuses.NextWindow(zone, nm.Condition, context.Now) ?? NmStatuses.NextWindow(zone, nm.Prep.Condition, context.Now);
        var wait = window != null && window.Start > context.Now ? window.Start - context.Now : TimeSpan.Zero;
        var firstTry = NmToPrep(nm, context);
        var tries = huntShard.UsefulChance > 0 ? 1 / huntShard.UsefulChance : 1;
        var outOfReach = context.Level is { } you && nm.Prep.Level > you + MaxAbove;
        var notes = new List<string>
        {
            Loc.T($"autre zone : {zone?.Name} (+{ZoneChange.TotalMinutes:0} min), état du NM inconnu",
                $"other zone: {zone?.Name} (+{ZoneChange.TotalMinutes:0} min), NM state unknown"),
        };
        if (outOfReach)
            notes.Add(Loc.T($"prépa trop forte à {context.PartySize} (mob Nv {nm.Prep.Level})", $"prep too strong for {context.PartySize} (mob lv {nm.Prep.Level})"));
        var levelsUp = context.Level is not { } current || current < HuntXp.MaxLevel(source.Territory);
        return new HuntOption(huntShard.Shard, source.Territory, null, null, source, null,
            outOfReach ? null : Total(wait, firstTry, tries, firstTry) + ZoneChange, window, notes, huntShard.UsefulChance, tries, firstTry,
            wait, firstTry)
        {
            XpRate = levelsUp ? HuntXp.ForNm(nm, firstTry, running: false, context.Level) : 0,
            Difficulty = HuntXp.Of(nm.Level, context.Level, changes: false, nm: true),
        };
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
        "bronze" => Loc.T("coffre de bronze", "bronze coffer"),
        "silver" => Loc.T("coffre d'argent", "silver coffer"),
        "gold" => Loc.T("coffre d'or", "gold coffer"),
        _ => chest ?? "",
    };
}
