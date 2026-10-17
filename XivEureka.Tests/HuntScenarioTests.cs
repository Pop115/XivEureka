using System.Globalization;
using System.Numerics;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;
using Xunit.Abstractions;

namespace XivEureka.Tests;

/// <summary>
/// "Quoi taper", case by case, on the plugin's real data (logos.json, nm.json, farms.json): what
/// comes first and roughly how long it takes. Each scenario prints its ranking (dotnet test
/// --logger "console;verbosity=detailed"), so the logic can be read as well as checked. The times
/// are the model's (HuntOptions), not measured play.
/// </summary>
public class HuntScenarioTests(ITestOutputHelper output)
{
    private const uint Pyros = 795;
    private const uint Hydatos = 827;
    private const uint Conceptual = 24007, Curative = 24009, Offensive = 24010, Protective = 24011, Inimical = 24014,
        Obscure = 24809;

    private static readonly LogosCatalog Logos = LogosCatalog.Embedded();
    private static readonly NmCatalog Nms = NmCatalog.Embedded();
    private static readonly FarmCatalog Farms = FarmCatalog.Embedded();
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);
    private static readonly LogosAction SomeAction = new(1, 1, "T", null, []);

    /// <summary>The shard as the hunt plan would give it: <paramref name="useful"/> of its mnemes still missing.</summary>
    private static HuntShard Shard(uint shard, int useful)
    {
        var mnemes = Logos.Shards.Single(s => s.Item == shard).Mnemes
            .Select((m, i) => new HuntMneme(m, 0, i < useful ? [new HuntNeed(SomeAction, 1)] : []))
            .ToList();
        var sources = Nms.Zones.SelectMany(z => z.Nms.Where(n => n.Shard == shard).Select(n => new HuntSource(z.Territory, n))).ToList();
        return new HuntShard(shard, mnemes, [SomeAction], sources);
    }

    /// <summary>First instant from Start (+1 min) where the zone's weather is <paramref name="weather"/>.</summary>
    private static DateTimeOffset Under(uint territory, string weather) =>
        EurekaWeather.Periods(EurekaWeather.Zone(territory)!, Start).Take(2000).First(p => p.Weather == weather).Start.AddMinutes(1);

    /// <summary>NM statuses with nothing special going on (no FATE, no kill seen), unless told.</summary>
    private static IReadOnlyList<NmStatus> Statuses(uint territory, DateTimeOffset now, ushort? runningFate = null,
        ushort? prepped = null, (ushort Fate, DateTime At)? killed = null)
    {
        var zone = EurekaWeather.Zone(territory)!;
        var fates = runningFate is { } f
            ? new List<MapFate> { new(f, "FATE", Vector3.Zero, 10, 30, TimeSpan.FromMinutes(12), 0, false) }
            : [];
        var ended = killed is { } k ? new Dictionary<ushort, DateTime> { [k.Fate] = k.At } : [];
        var preps = prepped is { } p ? new HashSet<ushort> { p } : [];
        return NmStatuses.Compute(zone, Nms.Zone(territory)!, fates, ended, preps, now);
    }

    /// <summary>Two players farming on their own, as Pop and their partner do.</summary>
    private const int Duo = 2;

    private static HuntContext Context(uint territory, DateTimeOffset now, int level, IReadOnlyList<NmStatus>? statuses = null,
        params ushort[] bunnies) => new(territory, statuses ?? Statuses(territory, now), bunnies.ToHashSet(), level, now, Duo);

    /// <summary>The same, leveling up counting for half (the default setting).</summary>
    private static HuntContext Leveling(HuntContext context) =>
        context with { XpWeight = HuntXp.Weight(0.5, context.Territory, context.Level) };

    /// <summary>The ranking, printed, then returned.</summary>
    private IReadOnlyList<HuntOption> Rank(string title, HuntShard shard, HuntContext context)
    {
        var options = HuntOptions.For(shard, Farms, context);
        output.WriteLine($"== {title}");
        output.WriteLine($"   useful mneme chance {shard.UsefulChance:P0}, level {context.Level}");
        foreach (var o in options.Take(8)) output.WriteLine("   " + Describe(o));
        return options;
    }

    private static string Describe(HuntOption o)
    {
        var what = o.Nm is { } nm ? $"NM {nm.Nm.Npc} (lv {nm.Nm.Level}{(o.Status?.Fate != null ? ", FATE running" : "")})"
            : o.Farm!.Kind == FarmKind.Bunny ? $"bunny FATE {o.Farm.Fate}, {o.Drop!.Chest} coffer"
            : $"{o.Farm.Kind} {o.Farm.Npc} lv {o.Farm.Level}{(o.Farm.Adapted ? " adapted" : "")}";
        var eta = o.Eta is { } e ? $"{e.TotalMinutes,6:0} min" : "     ?";
        return string.Create(CultureInfo.InvariantCulture,
            $"{eta}  {what}  [wait {o.Wait.TotalMinutes:0}, first {o.FirstTry.TotalMinutes:0}, {o.Tries:0.#} tries x {o.TryTime.TotalMinutes:0} min, {o.TryChance:P0}/try]");
    }

    private static double Minutes(HuntOption o) => o.Eta!.Value.TotalMinutes;

    // Pop and their partner: level 50, two players farming on their own (no public group, no train).

    // ---------- Conceptual (7 mnemes; 2 still missing: 29 % per logogram) ----------

    [Fact]
    public void Conceptual_under_a_sprite_weather_a_dozen_sprites_shared_by_two()
    {
        // Level 50: Conceptual only from sprites at 50 or more. Under Heat Waves, the lv 53 Ember
        // sprite (not "hard": 3 levels above). 2 min a kill alone, 1 for two; 30 % drop x 29 %
        // useful = 9 % a kill: about 12 kills, 12 min.
        var options = Rank("Conceptual, duo lv 50, Pyros, Heat Waves", Shard(Conceptual, 2), Context(Pyros, Under(Pyros, "Heat Waves"), 50));
        var first = options[0];
        Assert.Equal((FarmKind.Sprite, 7773u, 53), (first.Farm!.Kind, first.Farm.Npc, first.Farm.Level));
        Assert.Equal(HuntOptions.SpriteKill / 2, first.TryTime);
        Assert.InRange(Minutes(first), 11, 13);
        // Sprites below 50 give no Conceptual any more: none of them is proposed.
        Assert.DoesNotContain(options, o => o.Farm is { Kind: FarmKind.Sprite, Level: < 50 } && o.Drop!.IfAtOrAboveYou);
    }

    [Fact]
    public void Conceptual_with_no_sprite_weather_up_waits_for_the_next_one()
    {
        // Fair Skies: no sprite. The next sprite weather is counted as a wait, then the kills.
        var now = Under(Pyros, "Fair Skies");
        var first = Rank("Conceptual, duo lv 50, Pyros, Fair Skies", Shard(Conceptual, 2), Context(Pyros, now, 50))[0];
        Assert.Equal(FarmKind.Sprite, first.Farm!.Kind);
        Assert.True(first.Wait > TimeSpan.Zero);
        Assert.False(first.IsOpen(now));
        Assert.Equal(Minutes(first), (first.Wait + first.FirstTry).TotalMinutes + ((first.Tries - 1) * first.TryTime.TotalMinutes), 3);
    }

    [Fact]
    public void Conceptual_from_bunny_coffers_takes_hours_even_with_the_FATE_running()
    {
        // Bronze coffer: 28 % of bunny FATEs give Conceptual (1-2), so about 9 FATEs of 28 min
        // (respawn 8, travel 5, FATE 5, treasure hunt 10). Kills are shared, coffers are not: the
        // party size changes nothing here. A FATE already running only shortens the first one.
        var now = Under(Pyros, "Heat Waves");
        var waiting = Rank("Conceptual, bunny not running", Shard(Conceptual, 2), Context(Pyros, now, 50))
            .First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1407 });
        var running = Rank("Conceptual, bunny 1407 running", Shard(Conceptual, 2), Context(Pyros, now, 50, null, 1407))
            .First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1407 });
        Assert.Equal(HuntOptions.BunnyCycle, waiting.FirstTry);
        Assert.Equal(HuntOptions.BunnyRunning, running.FirstTry);
        Assert.Equal(Minutes(waiting) - Minutes(running), (HuntOptions.BunnyCycle - HuntOptions.BunnyRunning).TotalMinutes, 3);
        Assert.True(Minutes(running) > 180);
    }

    // ---------- Curative (2 mnemes; 1 missing: 50 %) ----------

    [Fact]
    public void Curative_with_nothing_up_silver_coffers_beat_prepping_NMs_ourselves()
    {
        // An NM to prep for two: 30 prep kills (2 min alone, 1 each for two: 30 min), the game's
        // spawn roll (15), travel and fight (3 + 5 for a lv 38 NM, far below 50): 53 min, and a
        // second NM half the time: 106 min. Silver coffer: 31 % a FATE, ~3 FATEs of 28 min: 90 min.
        var now = Under(Pyros, "Fair Skies");
        var options = Rank("Curative, duo lv 50, Pyros, nothing up", Shard(Curative, 1), Context(Pyros, now, 50));
        Assert.Equal(("silver", FarmKind.Bunny), (options[0].Drop!.Chest, options[0].Farm!.Kind));
        Assert.InRange(Minutes(options[0]), 85, 95);
        var graffiacane = options.First(o => o.Nm?.Nm.Npc == 7736);
        Assert.Equal(53, graffiacane.FirstTry.TotalMinutes, 3);
        Assert.Equal(106, Minutes(graffiacane), 3);
        Assert.Contains(graffiacane.Notes, n => n.Contains("préparer"));
    }

    [Fact]
    public void Curative_a_running_NM_FATE_goes_first()
    {
        // Leucosia's FATE (1388) is up: 3 min to get there, a lv 35 NM at lv 50 (half the fight,
        // shared by two: 5 min). Half the time its mneme is the wrong one: one NM to prep after.
        var now = Under(Pyros, "Fair Skies");
        var options = Rank("Curative, Leucosia FATE running", Shard(Curative, 1),
            Context(Pyros, now, 50, Statuses(Pyros, now, runningFate: 1388)));
        Assert.Equal(7759u, options[0].Nm!.Nm.Npc);
        Assert.Equal(8, options[0].FirstTry.TotalMinutes, 3);
        Assert.Equal(8 + 53, Minutes(options[0]), 3);
    }

    [Fact]
    public void A_prepped_NM_only_waits_for_the_spawn_roll_and_a_respawning_one_for_its_respawn()
    {
        var now = Under(Pyros, "Fair Skies");
        var prepped = Rank("Curative, Graffiacane prepped (tracker)", Shard(Curative, 1),
                Context(Pyros, now, 50, Statuses(Pyros, now, prepped: 1391)))
            .First(o => o.Nm?.Nm.Npc == 7736);
        Assert.Equal(HuntOptions.NmRoll.TotalMinutes + 8, prepped.FirstTry.TotalMinutes, 3);

        var killed = Rank("Curative, Graffiacane killed 30 min ago", Shard(Curative, 1),
                Context(Pyros, now, 50, Statuses(Pyros, now, killed: (1391, now.UtcDateTime.AddMinutes(-30)))))
            .First(o => o.Nm?.Nm.Npc == 7736);
        Assert.Equal(90, killed.Wait.TotalMinutes, 3); // 120 min respawn, 30 gone
    }

    // ---------- Inimical (2 mnemes; 1 missing: 50 %) ----------

    [Fact]
    public void Inimical_under_Thunder_the_adapted_level_54_sprite_is_minutes_away()
    {
        // It "always adapts" (80 %) and drops 5 Inimical: 97 % that one is useful, 78 % a kill.
        // 4 levels above (hard): 8 min alone, 4 for two: about 5 min. NMs: over an hour and a half.
        var options = Rank("Inimical, duo lv 50, Thunder", Shard(Inimical, 1), Context(Pyros, Under(Pyros, "Thunder"), 50));
        var first = options[0];
        Assert.Equal((7772u, 54, true), (first.Farm!.Npc, first.Farm.Level, first.Farm.Adapted));
        Assert.InRange(Minutes(first), 4, 6);
        Assert.True(options.Where(o => o.Nm != null && o.Eta != null).All(o => Minutes(o) >= 100));
    }

    // ---------- Protective, Offensive: NMs or gold coffers ----------

    [Fact]
    public void Protective_one_NM_prepped_by_two_beats_gold_coffers_by_far()
    {
        // Both Protective mnemes missing: one NM is enough. Lesath (lv 42): 53 min for two. Skoll
        // (lv 50, prep mob lv 55, hard): 60 min of prep, a 10 min fight: 88 min, and Blizzards.
        var options = Rank("Protective, duo lv 50, Pyros", Shard(Protective, 2), Context(Pyros, Under(Pyros, "Fair Skies"), 50));
        Assert.NotNull(options[0].Nm);
        Assert.Equal(53, Minutes(options[0]), 3);
        var skoll = options.First(o => o.Nm?.Nm.Npc == 7739);
        Assert.Equal(88, skoll.FirstTry.TotalMinutes, 3);
        Assert.True(Minutes(options.First(o => o.Farm is { Kind: FarmKind.Bunny } && o.Drop!.Chest == "gold")) > 600);
    }

    [Fact]
    public void Offensive_from_Pyros_go_to_the_adapted_lv_53_sprite_of_Hydatos_not_the_gold_coffers()
    {
        // In Pyros, Offensive only comes from gold coffers: 8 % of them, half Offensive, ~26 FATEs of
        // 28 min, about 12 hours. The adapted lv 53 thunderstorm sprite of Hydatos is within reach at
        // 50: its weather wait and the zone change still make it the fastest by far.
        var options = Rank("Offensive, duo lv 50, Pyros", Shard(Offensive, 1), Context(Pyros, Under(Pyros, "Fair Skies"), 50));
        var first = options[0];
        Assert.Equal((Hydatos, 7470u, 53), (first.Territory, first.Farm!.Npc, first.Farm.Level));
        Assert.Contains(first.Notes, n => n.Contains("autre zone"));
        var pyrosGold = options.Where(o => o.Territory == Pyros).ToList();
        Assert.All(pyrosGold, o => Assert.Equal(("gold", FarmKind.Bunny), (o.Drop!.Chest, o.Farm!.Kind)));
        Assert.InRange(Minutes(pyrosGold[0]), 600, 800);
        Assert.True(Minutes(pyrosGold[0]) > 5 * Minutes(first));
    }

    // ---------- Hydatos at level 50: much is out of reach for two ----------

    [Fact]
    public void Hydatos_at_50_what_is_within_reach_beats_coffers()
    {
        // Offensive: the adapted lv 64 Snowstorm sprite is out of reach (14 levels above), but the lv 53
        // thunderstorm sprite gives Offensive once adapted: under Thunderstorms, about an hour and a half,
        // against about 9 hours of gold coffers.
        var offensive = Rank("Offensive, duo lv 50, Hydatos, Snow", Shard(Offensive, 1), Context(Hydatos, Under(Hydatos, "Snow"), 50));
        Assert.DoesNotContain(offensive, o => o.Farm is { Kind: FarmKind.Sprite, Level: 64 });
        Assert.Equal((FarmKind.Sprite, 7470u, 53), (offensive[0].Farm!.Kind, offensive[0].Farm!.Npc, offensive[0].Farm!.Level));
        var gold = offensive.First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1425 });
        Assert.True(Minutes(gold) > 5 * Minutes(offensive[0]));

        // Obscure: a chain of 30 on mobs above you, rather than one NM every two hours. Of the NMs,
        // only Khalamari is preppable (Xzomit lv 55); Stegodon's and Frostmane's prep mobs (56, 59)
        // are too strong for two. The lv 62+ prep mobs are not proposed.
        var obscure = Rank("Obscure, duo lv 50, Hydatos", Shard(Obscure, 1), Context(Hydatos, Under(Hydatos, "Fair Skies"), 50));
        Assert.Equal(FarmKind.Chain, obscure[0].Farm!.Kind);
        Assert.Equal(52, obscure[0].Farm!.Level);
        Assert.Contains(obscure, o => o.Nm?.Nm.Npc == 8063 && o.Eta != null);
        Assert.All(obscure.Where(o => o.Nm != null && o.Nm.Nm.Npc != 8063), o =>
        {
            Assert.Null(o.Eta);
            Assert.Contains(o.Notes, n => n.Contains("prépa trop forte"));
        });
        Assert.DoesNotContain(obscure, o => o.Farm is { Kind: FarmKind.Mob });
    }

    [Fact]
    public void Hydatos_at_50_Conceptual_from_the_level_51_snowmelt_sprites_under_Showers()
    {
        var first = Rank("Conceptual, duo lv 50, Hydatos, Showers", Shard(Conceptual, 2), Context(Hydatos, Under(Hydatos, "Showers"), 50))[0];
        Assert.Equal((7471u, 51), (first.Farm!.Npc, first.Farm.Level));
        Assert.InRange(Minutes(first), 11, 13);
    }

    // ---------- Tactical: mutating mobs ----------

    [Fact]
    public void Tactical_in_Hydatos_at_50_comes_from_mutating_mobs_within_reach()
    {
        // Any mutated or adapted mob can drop a moisture-warped lockbox. At 50, the lv 64-65 ones are
        // out of reach, but the lv 50-55 mutants are not: each under its own condition (night, a weather).
        var options = Rank("Tactical, duo lv 50, Hydatos", Shard(24012, 1), Context(Hydatos, Under(Hydatos, "Fair Skies"), 50));
        var reachable = options.Where(o => o.Eta != null).ToList();
        Assert.NotEmpty(reachable);
        Assert.All(reachable, o => Assert.True(o.Farm!.Mutated || o.Farm.Adapted));
        Assert.All(reachable, o => Assert.InRange(o.Farm!.Level, 50, 55));
        Assert.Contains(reachable, o => o.Farm!.Npc == 8014 && o.IsOpen(Under(Hydatos, "Fair Skies"))); // Val Chigoe: Fair Skies
        Assert.Contains(reachable[0].Notes, n => n.Contains("muté") || n.Contains("adapte"));
    }

    [Fact]
    public void Tactical_in_Pyros_at_50_mutants_instead_of_only_the_Val_gigantopithecus()
    {
        // Before, only the lv 55 Val Gigantopithecus (Umbral Wind). Now every Pyros mutant or adapting
        // mob within reach counts, each under its own condition.
        var options = Rank("Tactical, duo lv 50, Pyros", Shard(24012, 1), Context(Pyros, Under(Pyros, "Fair Skies"), 50));
        var reachable = options.Where(o => o.Eta != null).ToList();
        Assert.True(reachable.Count > 10);
        Assert.All(reachable, o => Assert.True(o.Farm!.Mutated || o.Farm.Adapted));
        Assert.All(reachable, o => Assert.InRange(o.Farm!.Level, 35, 55));
        Assert.Contains(reachable, o => o.Farm!.Npc == 7847);
        Assert.NotEqual(7847u, reachable[0].Farm!.Npc);
    }

    // ---------- Leveling up as well ----------

    [Fact]
    public void Leveling_in_Hydatos_favors_sources_at_your_level_over_bunny_coffers()
    {
        // At 50 in Hydatos (max 60) experience weighs half. Mobs around your level level you up at
        // the reference rhythm or better: their time counts half. Bunny FATEs give little: almost none off.
        var now = Under(Hydatos, "Showers");
        var conceptual = Rank("Conceptual, duo lv 50, Hydatos, Showers, leveling", Shard(Conceptual, 2), Leveling(Context(Hydatos, now, 50)));
        var sprite = conceptual[0];
        Assert.Equal(FarmKind.Sprite, sprite.Farm!.Kind);
        Assert.Equal(1, sprite.XpShare);
        Assert.Equal(sprite.Eta!.Value.TotalMinutes / 2, sprite.Counted!.Value.TotalMinutes, 3);

        var offensive = Rank("Offensive, duo lv 50, Hydatos, leveling", Shard(Offensive, 1), Leveling(Context(Hydatos, now, 50)));
        var bunny = offensive.First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1425 });
        Assert.True(bunny.XpShare < 0.3);
        Assert.True(bunny.Counted > bunny.Eta * 0.9);
    }

    [Fact]
    public void An_NM_at_your_level_levels_you_up_and_one_far_below_does_not()
    {
        var now = Under(Hydatos, "Fair Skies");
        var obscure = Rank("Obscure, duo lv 50, Hydatos, leveling", Shard(Obscure, 1), Leveling(Context(Hydatos, now, 50)));
        var khalamari = obscure.Single(o => o.Nm?.Nm.Npc == 8063);
        Assert.True(khalamari.XpShare >= 0.75); // "XP ++": 30 prep kills of a lv 55 mob plus the NM bonus, over its 88 min
        Assert.Equal(Difficulty.Hard, khalamari.Difficulty);

        // A lv 35 Pyros NM for a lv 50 player gives almost nothing (and Pyros is maxed at 50 anyway).
        var pyros = Rank("Curative, duo lv 50, Pyros, leveling", Shard(Curative, 1), Leveling(Context(Pyros, Under(Pyros, "Fair Skies"), 50)));
        Assert.All(pyros.Where(o => o.Eta != null), o => Assert.Equal(o.Eta, o.Counted));
        Assert.True(pyros.Single(o => o.Nm?.Nm.Npc == 7759).XpShare < 0.3);
    }
}
