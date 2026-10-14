using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace XivEureka.Zones;

/// <summary>A FATE as the plugin follows it: its id, name and world position (X, Y, Z).</summary>
public sealed record SeenFate(ushort Id, string Name, Vector3 Position);

/// <summary>
/// Compares the running FATEs from one tick to the next to announce new ones. In Eureka every FATE
/// is an NM except the bunny FATEs, filtered out beforehand (<see cref="IsBunnyFate"/>); so no NM
/// table is needed to alert, name and position come from the game.
/// </summary>
public sealed class FateTracker
{
    /// <summary>
    /// "Collect" rule of the Fate sheet. In the four Eureka zones only the five bunny FATEs have it
    /// (Down the Rabbit Hole, Curiouser and Curiouser, We're All Mad Here, Uncommon Nonsense, Drink
    /// Me); NMs have rule 1 (rows 1328-1427 checked on 2026-10-14).
    /// </summary>
    private const byte CollectRule = 4;

    /// <summary>True for a Eureka bunny FATE (not announced: we do not do them).</summary>
    public static bool IsBunnyFate(byte rule) => rule == CollectRule;

    private readonly Dictionary<ushort, SeenFate> running = [];
    private readonly Dictionary<ushort, DateTime> ended = [];
    private bool firstPass = true;

    public IReadOnlyCollection<SeenFate> Running => running.Values;

    /// <summary>
    /// FATEs seen ending in this instance (successful or not) and when (UTC): start of the NM's
    /// respawn delay. A FATE already over when arriving in the zone is not there.
    /// </summary>
    public IReadOnlyDictionary<ushort, DateTime> Ended => ended;

    /// <summary>
    /// The FATEs that appeared since the last call. The first call (arriving in the zone) returns
    /// nothing: the ones already there are not news.
    /// </summary>
    public IReadOnlyList<SeenFate> Update(IEnumerable<SeenFate> fates, DateTime? now = null)
    {
        var seen = fates.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        var appeared = firstPass ? [] : seen.Values.Where(f => !running.ContainsKey(f.Id)).ToList();
        foreach (var gone in running.Keys.Where(id => !seen.ContainsKey(id)))
            ended[gone] = now ?? DateTime.UtcNow;
        foreach (var back in appeared) ended.Remove(back.Id);
        running.Clear();
        foreach (var (id, f) in seen) running[id] = f;
        firstPass = false;
        return appeared;
    }

    /// <summary>Forgets everything (zone change): the next pass will report nothing.</summary>
    public void Reset()
    {
        running.Clear();
        ended.Clear();
        firstPass = true;
    }

    /// <summary>The running FATE nearest to a world position (X/Z plane), within <paramref name="maxDistance"/>.</summary>
    public SeenFate? Nearest(Vector3 position, float maxDistance) =>
        running.Values
            .Select(f => (Fate: f, D: Vector2.Distance(new Vector2(f.Position.X, f.Position.Z), new Vector2(position.X, position.Z))))
            .Where(p => p.D <= maxDistance)
            .OrderBy(p => p.D)
            .Select(p => p.Fate)
            .FirstOrDefault();
}
