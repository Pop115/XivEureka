using System;
using XivEureka.Zones;

namespace XivEureka.Logos;

/// <summary>How hard a source is for a small party: from the level gap and the kind of mob.</summary>
public enum Difficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// Elemental experience, roughly, to rank the logogram sources when the player also wants to level
/// up (until the zone's max level). Units: the experience of one kill of a plain mob at the player's
/// level. What the community guides agree on, without exact numbers: mobs above you give more (up to
/// +6, the "try-hard" groups' target), mobs a few levels below give almost nothing, mutated and
/// adapted mobs give 2.5 times, sprites 1.5 times, an NM a big bonus, its 30 prep kills too (the prep
/// mob is 5 levels above the NM); bunny FATEs give little. The factors below are guesses on that
/// basis, meant to rank, like the drop chances.
/// </summary>
public static class HuntXp
{
    /// <summary>The reference rhythm (share 100 %): a duo grinding plain mobs 3 levels above them.</summary>
    public const double ReferenceRate = 1.0;

    public const double SpriteFactor = 1.5;
    /// <summary>Mutating or adapting mobs farmed under their condition: not all of them change (2.5 when they do).</summary>
    public const double ChangingFactor = 1.75;
    /// <summary>An NM kill, in plain kills at its level.</summary>
    public const double NmBonus = 25;
    /// <summary>A bunny FATE, in plain kills at its level.</summary>
    public const double BunnyFate = 5;

    /// <summary>Experience of a kill against the level gap: +15 % a level above you (up to twice), almost nothing 3 or more below.</summary>
    public static double LevelFactor(int mobLevel, int? you)
    {
        if (you is not { } level) return 1;
        var gap = mobLevel - level;
        return gap <= -3 ? 0.1 : Math.Clamp(1 + (0.15 * gap), 0.5, 2.0);
    }

    /// <summary>Experience per minute while farming this source (per player: each one gets the kill's experience).</summary>
    public static double ForFarm(Farm farm, TimeSpan tryTime, int? level)
    {
        if (tryTime <= TimeSpan.Zero) return 0;
        var perTry = farm.Kind switch
        {
            FarmKind.Bunny => BunnyFate,
            FarmKind.Chain => 30 * 1.5, // 30 kills, the chain bonus on top
            FarmKind.Sprite => farm.Adapted ? SpriteFactor * ChangingFactor : SpriteFactor,
            _ => farm.Mutated || farm.Adapted ? ChangingFactor : 1,
        };
        return perTry * LevelFactor(farm.Level, level) / tryTime.TotalMinutes;
    }

    /// <summary>Experience per minute of an NM try: its prep kills (unless the FATE runs) and the NM bonus.</summary>
    public static double ForNm(EurekaNm nm, TimeSpan firstTry, bool running, int? level)
    {
        if (firstTry <= TimeSpan.Zero) return 0;
        var xp = NmBonus * LevelFactor(nm.Level, level);
        if (!running) xp += HuntOptions.PrepKills * LevelFactor(nm.Prep.Level, level);
        return xp / firstTry.TotalMinutes;
    }

    /// <summary>Share of the reference rhythm, 0 to 1.</summary>
    public static double Share(double rate) => Math.Clamp(rate / ReferenceRate, 0, 1);

    /// <summary>How much experience counts in the ranking: the setting, or nothing at the zone's max level.</summary>
    public static double Weight(double importance, uint territory, int? level) =>
        level is { } l && l >= MaxLevel(territory) ? 0 : Math.Clamp(importance, 0, 1);

    /// <summary>The max elemental level reachable in a zone.</summary>
    public static int MaxLevel(uint territory) => territory switch
    {
        732 => 20,
        763 => 35,
        795 => 50,
        827 => 60,
        _ => 0,
    };

    /// <summary>Hard: 4+ levels above you (an NM at or above your level); medium: above you or a mob that changes; else easy.</summary>
    public static Difficulty Of(int mobLevel, int? you, bool changes, bool nm)
    {
        if (you is not { } level) return Difficulty.Medium;
        var gap = mobLevel - level;
        if (nm) return gap >= 0 ? Difficulty.Hard : gap >= -4 ? Difficulty.Medium : Difficulty.Easy;
        return gap >= 4 ? Difficulty.Hard : gap >= 1 || changes ? Difficulty.Medium : Difficulty.Easy;
    }

    /// <summary>The time the ranking counts: the estimate, less the part that also levels you up.</summary>
    public static TimeSpan? Counted(TimeSpan? eta, double share, double weight) =>
        eta is { } e ? TimeSpan.FromMinutes(e.TotalMinutes * (1 - (weight * share))) : null;
}
