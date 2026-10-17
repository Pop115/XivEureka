using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Zones;

/// <summary>A real-time window where a condition holds: open now, or to come.</summary>
public sealed record TimeWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public bool IsOpen(DateTimeOffset now) => Start <= now && now < End;
}

/// <summary>
/// Where an NM stands now: its FATE if running, when it can come back if it was seen killed (here or
/// on the tracker), whether it is prepped, and when its own condition and its prep mob's condition
/// hold (or will).
/// </summary>
public sealed record NmStatus(EurekaNm Nm, MapFate? Fate, DateTimeOffset? RespawnNotBefore,
    TimeWindow? NmWindow, TimeWindow? PrepWindow, bool Prepped = false)
{
    public bool IsRespawning(DateTimeOffset now) => Fate == null && RespawnNotBefore > now;

    /// <summary>The first window to wait for (NM or prep); null if no condition.</summary>
    public TimeWindow? Wait => NmWindow ?? PrepWindow;

    /// <summary>
    /// Can be done now: its FATE runs, it is prepped, or it is not respawning and its condition
    /// (if any) holds.
    /// </summary>
    public bool IsDoable(DateTimeOffset now) =>
        Fate != null || Prepped || (!IsRespawning(now) && (Wait == null || Wait.IsOpen(now)));

    /// <summary>
    /// Short state for lists, in the plugin language: "FATE en cours" / "FATE running", "préparé" /
    /// "prepped", "respawn ≥ 12 min", "Neige en cours" / "Snow now", "nuit dans 8 min" / "night in 8
    /// min", "disponible" / "available". <paramref name="weatherName"/> names a weather.
    /// </summary>
    public string Describe(DateTimeOffset now, Func<string, string> weatherName)
    {
        if (Fate != null) return Loc.T("FATE en cours", "FATE running");
        if (Prepped) return Loc.T("préparé", "prepped");
        if (IsRespawning(now)) return $"respawn ≥ {Formatting.Duration(RespawnNotBefore!.Value - now)}";
        if (Wait is not { } window) return Loc.T("disponible", "available");
        var condition = NmWindow != null ? Nm.Condition : Nm.Prep.Condition;
        var name = condition.Weather is { } w ? weatherName(w) : Loc.T("nuit", "night");
        return window.IsOpen(now) ? Loc.T($"{name} en cours", $"{name} now")
            : Loc.T($"{name} dans", $"{name} in") + $" {Formatting.Duration(window.Start - now)}";
    }
}

/// <summary>Computes NM statuses with nothing from the game: running FATEs and known kill times.</summary>
public static class NmStatuses
{
    public static IReadOnlyList<NmStatus> Compute(EurekaZone zone, ZoneNms nms, IReadOnlyList<MapFate> fates,
        IReadOnlyDictionary<ushort, DateTime> ended, IReadOnlySet<ushort> prepped, DateTimeOffset now) =>
        nms.Nms.Select(nm =>
        {
            var fate = fates.FirstOrDefault(f => f.Id == nm.Fate);
            DateTimeOffset? respawn = ended.TryGetValue(nm.Fate, out var end)
                ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)).AddMinutes(nms.RespawnMinutes)
                : null;
            return new NmStatus(nm, fate, respawn,
                NextWindow(zone, nm.Condition, now), NextWindow(zone, nm.Prep.Condition, now),
                prepped.Contains(nm.Fate));
        }).ToList();

    /// <summary>The open or next window where the condition holds; null without a condition.</summary>
    public static TimeWindow? NextWindow(EurekaZone zone, SpawnCondition condition, DateTimeOffset now)
    {
        if (condition.Weather is { } weather)
        {
            // Several periods in a row can share the weather: the window runs to the last one.
            var periods = EurekaWeather.Periods(zone, now).TakeWhile(p => p.Start < now.AddDays(7))
                .SkipWhile(p => p.Weather != weather).TakeWhile(p => p.Weather == weather).ToList();
            return periods.Count == 0 ? null : new TimeWindow(periods[0].Start, periods[^1].End);
        }
        if (condition.Night)
        {
            var (nightNext, at) = EurekaWeather.NextDayNightSwitch(now);
            if (!nightNext) return new TimeWindow(now, at); // night until dawn
            var (_, dawn) = EurekaWeather.NextDayNightSwitch(at);
            return new TimeWindow(at, dawn);
        }
        return null;
    }
}
