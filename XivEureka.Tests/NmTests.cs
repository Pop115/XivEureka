using System.Numerics;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

public class NmCatalogTests
{
    private static readonly ZoneNms Pyros = NmCatalog.Embedded().Zone(795)!;

    [Fact]
    public void The_17_Pyros_NMs_with_their_FATE()
    {
        Assert.Equal(17, Pyros.Nms.Count);
        Assert.Equal(120, Pyros.RespawnMinutes);
        // FATEs 1388 to 1404 of the Fate sheet, one per NM; the bunny FATEs (1407, 1408) are not there.
        Assert.Equal(Enumerable.Range(1388, 17).Select(i => (ushort)i), Pyros.Nms.Select(n => n.Fate).Order());
        Assert.Equal(Enumerable.Range(38, 17), Pyros.Nms.Select(n => n.Site).Order());
        Assert.All(Pyros.Nms, n => Assert.NotEmpty(n.Prep.Places));
    }

    [Fact]
    public void Each_NM_drops_a_shard_known_to_the_Logos_catalog()
    {
        var shards = LogosCatalog.Embedded().Shards.Select(s => s.Item).ToHashSet();
        Assert.All(Pyros.Nms, n => Assert.Contains(n.Shard, shards));
    }

    [Fact]
    public void Conditions_as_on_the_tracker()
    {
        var skoll = Pyros.Nms.Single(n => n.Npc == 7739);
        Assert.Equal("Blizzards", skoll.Condition.Weather);
        Assert.True(skoll.Prep.Condition.None);

        var leucosia = Pyros.Nms.Single(n => n.Npc == 7759);
        Assert.True(leucosia.Condition.None);
        Assert.True(leucosia.Prep.Condition.Night);

        // The weathers used exist in the Pyros slices (otherwise their window would never be found).
        var weathers = EurekaWeather.Zone(795)!.Slices.Select(s => s.Weather).ToHashSet();
        Assert.All(Pyros.Nms.SelectMany(n => new[] { n.Condition.Weather, n.Prep.Condition.Weather }).OfType<string>(),
            w => Assert.Contains(w, weathers));
    }
}

public class NmStatusesTests
{
    private static readonly EurekaZone Pyros = EurekaWeather.Zone(795)!;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    private static EurekaNm Nm(uint npc, ushort fate, int level = 40) =>
        new(npc, 0, fate, level, "fire", new Vector2(20, 20), 24009, [], new SpawnCondition(null, false),
            new PrepMob(1, level + 5, new SpawnCondition(null, false), [new Vector2(21, 21)]));

    [Fact]
    public void A_weather_window_spans_consecutive_periods()
    {
        foreach (var (_, weather) in Pyros.Slices)
        {
            var w = NmStatuses.NextWindow(Pyros, new SpawnCondition(weather, false), Now)!;
            var periods = EurekaWeather.Periods(Pyros, w.Start).TakeWhile(p => p.Start < w.End).ToList();
            Assert.All(periods, p => Assert.Equal(weather, p.Weather));
            Assert.NotEqual(weather, EurekaWeather.Periods(Pyros, w.End).First().Weather);
            Assert.True(w.End > Now);
        }
    }

    [Fact]
    public void Night_window()
    {
        var midnight = DateTimeOffset.FromUnixTimeSeconds(0);
        var open = NmStatuses.NextWindow(Pyros, new SpawnCondition(null, true), midnight)!;
        Assert.True(open.IsOpen(midnight));
        Assert.Equal(6 * 175, open.End.ToUnixTimeSeconds());

        var noon = DateTimeOffset.FromUnixTimeSeconds(12 * 175);
        var coming = NmStatuses.NextWindow(Pyros, new SpawnCondition(null, true), noon)!;
        Assert.Equal(19 * 175, coming.Start.ToUnixTimeSeconds());
        Assert.Equal((24 + 6) * 175, coming.End.ToUnixTimeSeconds());
    }

    [Fact]
    public void Respawn_two_hours_after_the_FATE_ends()
    {
        var zone = new ZoneNms(795, 120, [Nm(10, 500)]);
        var end = Now.UtcDateTime.AddMinutes(-30);
        var s = NmStatuses.Compute(Pyros, zone, [], new Dictionary<ushort, DateTime> { [500] = end }, new HashSet<ushort> { 500 }, Now).Single();
        Assert.True(s.IsRespawning(Now));
        Assert.Equal(Now.AddMinutes(90), s.RespawnNotBefore);
        Assert.False(s.IsRespawning(Now.AddMinutes(91)));
        Assert.True(s.Prepped);
    }
}

public class SuggestionsTests
{
    private static readonly GameNames Names = new(n => $"NM{n}", w => w, i => $"Item{i}");

    private static EurekaNm Nm(uint npc, int level, uint shard = 24009, SpawnCondition? condition = null) =>
        new(npc, 0, (ushort)(1000 + npc), level, "fire", new Vector2(npc, 10), shard, [], condition ?? new SpawnCondition(null, false),
            new PrepMob(npc + 100, level + 5, new SpawnCondition(null, false), [new Vector2(npc, 12)]));

    private static NmStatus Free(EurekaNm nm) => new(nm, null, null, null, null);

    [Fact]
    public void A_running_FATE_comes_first()
    {
        var now = DateTimeOffset.UtcNow;
        var fate = new MapFate(1001, "F", Vector3.Zero, 20, 35, TimeSpan.FromMinutes(24), 0, false);
        var suggestions = Suggestions.Compute([new NmStatus(Nm(1, 40), fate, null, null, null), Free(Nm(2, 40))],
            now, 40, new Dictionary<uint, int>(), Names);
        Assert.Equal("NM1 – FATE en cours", suggestions[0].Title);
        Assert.Contains("35 %", suggestions[0].Text);
    }

    [Fact]
    public void A_weather_within_half_an_hour_gives_the_prep()
    {
        var now = DateTimeOffset.UtcNow;
        var in20 = new TimeWindow(now.AddMinutes(20), now.AddMinutes(43));
        var in50 = new TimeWindow(now.AddMinutes(50), now.AddMinutes(73));
        var suggestions = Suggestions.Compute(
            [new NmStatus(Nm(1, 50, condition: new SpawnCondition("Blizzards", false)), null, null, in20, null),
             new NmStatus(Nm(2, 46, condition: new SpawnCondition("Thunder", false)), null, null, in50, null)],
            now, null, new Dictionary<uint, int>(), Names);
        var s = Assert.Single(suggestions);
        Assert.Equal("NM1 – Blizzards dans 20 min", s.Title);
        Assert.Equal("Prépa : NM101 (Nv 55) vers (1.0, 12.0).", s.Text);
        Assert.Equal(new Vector2(1, 12), s.Coordinates);
    }

    [Fact]
    public void Level_and_shards_skip_respawning_or_waiting_NMs()
    {
        var now = DateTimeOffset.UtcNow;
        var respawning = new NmStatus(Nm(1, 40), null, now.AddMinutes(30), null, null);
        var waiting = new NmStatus(Nm(2, 41, condition: new SpawnCondition("Thunder", false)), null, null,
            new TimeWindow(now.AddHours(2), now.AddHours(3)), null);
        var free = Free(Nm(3, 42, shard: 24011));
        var suggestions = Suggestions.Compute([respawning, waiting, free], now, 40,
            new Dictionary<uint, int> { [24009] = 5, [24011] = 2 }, Names);

        var level = suggestions.Single(s => s.Title.StartsWith("À ton niveau"));
        Assert.Equal("Disponibles : NM3 (Nv 42).", level.Text);
        var logos = Assert.Single(suggestions, s => s.Title.StartsWith("Logos"));
        Assert.Equal("Logos – Item24011", logos.Title); // 24009 has no available source
    }
}

public class NmDoableTests
{
    private static EurekaNm Nm(SpawnCondition? condition = null) =>
        new(1, 0, 1001, 50, "fire", new Vector2(10, 10), 24009, [], condition ?? new SpawnCondition(null, false),
            new PrepMob(2, 55, new SpawnCondition(null, false), [new Vector2(11, 11)]));

    private static readonly DateTimeOffset Now = new(2026, 10, 17, 20, 0, 0, TimeSpan.Zero);
    private static readonly MapFate Running = new(1001, "F", System.Numerics.Vector3.Zero, 20, 35, TimeSpan.FromMinutes(24), 0, false);

    [Fact]
    public void Doable_states()
    {
        var snow = new SpawnCondition("Snow", false);
        Assert.True(new NmStatus(Nm(), null, null, null, null).IsDoable(Now));
        Assert.True(new NmStatus(Nm(), Running, Now.AddHours(1), null, null).IsDoable(Now));
        Assert.True(new NmStatus(Nm(snow), null, null, new TimeWindow(Now.AddHours(1), Now.AddHours(2)), null, Prepped: true).IsDoable(Now));
        Assert.True(new NmStatus(Nm(snow), null, null, new TimeWindow(Now.AddMinutes(-5), Now.AddMinutes(20)), null).IsDoable(Now));
        Assert.True(new NmStatus(Nm(), null, Now.AddMinutes(-1), null, null).IsDoable(Now)); // respawned
        Assert.False(new NmStatus(Nm(), null, Now.AddMinutes(30), null, null).IsDoable(Now));
        Assert.False(new NmStatus(Nm(snow), null, null, new TimeWindow(Now.AddMinutes(10), Now.AddMinutes(40)), null).IsDoable(Now));
    }

    [Fact]
    public void Describes_the_state_in_a_few_words()
    {
        var snow = new SpawnCondition("Snow", false);
        string Of(NmStatus s) => s.Describe(Now, w => w == "Snow" ? "Neige" : w);
        Assert.Equal("disponible", Of(new NmStatus(Nm(), null, null, null, null)));
        Assert.Equal("FATE en cours", Of(new NmStatus(Nm(), Running, null, null, null)));
        Assert.Equal("respawn ≥ 30 min", Of(new NmStatus(Nm(), null, Now.AddMinutes(30), null, null)));
        Assert.Equal("Neige dans 10 min", Of(new NmStatus(Nm(snow), null, null, new TimeWindow(Now.AddMinutes(10), Now.AddMinutes(40)), null)));
        Assert.Equal("Neige en cours", Of(new NmStatus(Nm(snow), null, null, new TimeWindow(Now.AddMinutes(-1), Now.AddMinutes(40)), null)));
        Assert.Equal("nuit dans 8 min", Of(new NmStatus(Nm(new SpawnCondition(null, true)), null, null, new TimeWindow(Now.AddMinutes(8), Now.AddMinutes(30)), null)));
    }
}
