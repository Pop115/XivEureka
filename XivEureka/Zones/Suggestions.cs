using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace XivEureka.Zones;

/// <summary>A "where to go now" line: what, why, and where on the map (map coordinates).</summary>
public sealed record Suggestion(int Priority, string Title, string Text, Vector2 Coordinates, uint? Npc);

/// <summary>Names in the client language, provided by the caller (game sheets).</summary>
public sealed record GameNames(Func<uint, string> Npc, Func<string, string> Weather, Func<uint, string> Item);

/// <summary>
/// "Where to go now", with nothing from the game: from the NM statuses, the elemental level and the
/// useful Logos shards, a short ranked list. Rules, most to least urgent: a running NM FATE; a
/// condition (weather, night) opening within half an hour, with the prep to do; available NMs at
/// the player's level; NMs dropping the shards the player is missing. The texts are user-facing
/// (French for now).
/// </summary>
public static class Suggestions
{
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<Suggestion> Compute(IReadOnlyList<NmStatus> statuses, DateTimeOffset now,
        int? level, IReadOnlyDictionary<uint, int> usefulShards, GameNames names, int max = 5)
    {
        var suggestions = new List<Suggestion>();

        foreach (var s in statuses.Where(s => s.Fate != null))
        {
            var f = s.Fate!;
            var text = f.Preparing ? Loc.T("En préparation.", "Preparing.")
                : Loc.T($"{f.Progress} %, encore {Formatting.Duration(f.Remaining)}.", $"{f.Progress}%, {Formatting.Duration(f.Remaining)} left.");
            suggestions.Add(new Suggestion(100 + f.Progress, $"{names.Npc(s.Nm.Npc)} – " + Loc.T("FATE en cours", "FATE running"), text,
                s.Nm.Position, s.Nm.Npc));
        }

        foreach (var s in statuses.Where(s => s.Fate == null && !s.IsRespawning(now)))
        {
            if (s.Wait is not { } window || window.Start - now > Horizon) continue;
            var condition = s.NmWindow != null ? s.Nm.Condition : s.Nm.Prep.Condition;
            var conditionName = condition.Weather is { } w ? names.Weather(w) : Loc.T("Nuit", "Night");
            var when = window.IsOpen(now)
                ? Loc.T($"{conditionName} en cours (encore {Formatting.Duration(window.End - now)})",
                    $"{conditionName} now ({Formatting.Duration(window.End - now)} left)")
                : Loc.T($"{conditionName} dans", $"{conditionName} in") + $" {Formatting.Duration(window.Start - now)}";
            var minutes = (int)Math.Max(0, (window.Start - now).TotalMinutes);
            suggestions.Add(new Suggestion(80 - minutes, $"{names.Npc(s.Nm.Npc)} – {when}", PrepText(s.Nm, names),
                s.Nm.Prep.Places.FirstOrDefault(s.Nm.Position), s.Nm.Npc));
        }

        var available = statuses.Where(s => IsAvailable(s, now)).ToList();

        if (level is { } l)
        {
            var atLevel = available.Where(s => s.Nm.Level >= l - 1 && s.Nm.Level <= l + 2)
                .OrderBy(s => Math.Abs(s.Nm.Level - l)).Take(3).ToList();
            if (atLevel.Count > 0)
                suggestions.Add(new Suggestion(40, Loc.T($"À ton niveau ({l})", $"At your level ({l})"),
                    Loc.T("Disponibles : ", "Available: ")
                    + string.Join(", ", atLevel.Select(s => $"{names.Npc(s.Nm.Npc)} ({Loc.T("Nv", "lv")} {s.Nm.Level})")) + ".",
                    atLevel[0].Nm.Position, atLevel[0].Nm.Npc));
        }

        foreach (var (shard, actions) in usefulShards.Where(u => u.Value > 0).OrderByDescending(u => u.Value))
        {
            var sources = available.Where(s => s.Nm.Shard == shard).OrderBy(s => s.Nm.Level).Take(4).ToList();
            if (sources.Count == 0) continue;
            suggestions.Add(new Suggestion(30 + Math.Min(actions, 9), $"Logos – {names.Item(shard)}",
                Loc.T($"Sert à {actions} action(s) pas encore apprise(s). Disponibles : ",
                    $"Used by {actions} action(s) not learned yet. Available: ")
                + string.Join(", ", sources.Select(s => names.Npc(s.Nm.Npc))) + ".",
                sources[0].Nm.Position, sources[0].Nm.Npc));
        }

        return suggestions.OrderByDescending(s => s.Priority).Take(max).ToList();
    }

    /// <summary>No FATE, not respawning, and its condition (if any) holds right now.</summary>
    public static bool IsAvailable(NmStatus s, DateTimeOffset now) =>
        s.Fate == null && !s.IsRespawning(now) && (s.Wait == null || s.Wait.IsOpen(now));

    public static string PrepText(EurekaNm nm, GameNames names)
    {
        var places = string.Join(Loc.T(" ou ", " or "), nm.Prep.Places.Select(Formatting.Coordinates));
        return Loc.T($"Prépa : {names.Npc(nm.Prep.Npc)} (Nv {nm.Prep.Level})", $"Prep: {names.Npc(nm.Prep.Npc)} (lv {nm.Prep.Level})")
               + (places.Length > 0 ? Loc.T($" vers {places}.", $" around {places}.") : ".");
    }
}

/// <summary>Formatting shared by the map and the suggestions.</summary>
public static class Formatting
{
    /// <summary>"12 min", "1 h 05", "40 s".</summary>
    public static string Duration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00}"
        : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min"
        : $"{Math.Max(0, (int)d.TotalSeconds)} s";

    /// <summary>"(16.8, 33.4)", as on the game map.</summary>
    public static string Coordinates(Vector2 c) =>
        string.Create(CultureInfo.InvariantCulture, $"({c.X:0.0}, {c.Y:0.0})");
}
