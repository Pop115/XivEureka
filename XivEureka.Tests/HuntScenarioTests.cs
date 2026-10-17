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

    private static HuntContext Context(uint territory, DateTimeOffset now, int level, IReadOnlyList<NmStatus>? statuses = null,
        params ushort[] bunnies) => new(territory, statuses ?? Statuses(territory, now), bunnies.ToHashSet(), level, now);

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

    // ---------- Conceptual (7 mnemes; 2 still missing: 29 % per logogram) ----------

    [Fact]
    public void Conceptual_under_Heat_Waves_the_level_53_ember_sprite_beats_waiting_for_other_weathers()
    {
        // Level 45: Conceptual only drops from sprites at or above 45. Under Heat Waves that is the
        // lv 53 Ember sprite, 8 levels above: hard, so 4 min a kill. 30 % drop x 29 % useful = 9 % a
        // kill, about 12 kills: about 47 min. Typhoon sprites (lv 45) need Umbral Wind, later.
        var options = Rank("Conceptual, lv 45, Pyros, Heat Waves", Shard(Conceptual, 2), Context(Pyros, Under(Pyros, "Heat Waves"), 45));
        var first = options[0];
        Assert.Equal((FarmKind.Sprite, 7773u, 53), (first.Farm!.Kind, first.Farm.Npc, first.Farm.Level));
        Assert.Equal(TimeSpan.Zero, first.Wait);
        Assert.InRange(Minutes(first), 40, 55);
        Assert.DoesNotContain(options, o => o.Farm is { Kind: FarmKind.Sprite, Level: < 45 } && o.Drop!.IfAtOrAboveYou);
    }

    [Fact]
    public void Conceptual_from_bunny_coffers_takes_hours_even_with_the_FATE_running()
    {
        // Bronze coffer: 28 % of bunny FATEs give Conceptual (1-2 of them), so about 9 FATEs; each
        // full cycle is 28 min (respawn 8, travel 5, FATE 5, treasure hunt 10). A FATE already
        // running only saves the start of the first cycle.
        var now = Under(Pyros, "Heat Waves");
        var waiting = Rank("Conceptual, bunny not running", Shard(Conceptual, 2), Context(Pyros, now, 45))
            .First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1407 });
        var running = Rank("Conceptual, bunny 1407 running", Shard(Conceptual, 2), Context(Pyros, now, 45, null, 1407))
            .First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1407 });
        Assert.Equal(HuntOptions.BunnyCycle, waiting.FirstTry);
        Assert.Equal(HuntOptions.BunnyRunning, running.FirstTry);
        Assert.Equal(Minutes(waiting) - Minutes(running), (HuntOptions.BunnyCycle - HuntOptions.BunnyRunning).TotalMinutes, 3);
        Assert.True(Minutes(running) > 180);
    }

    [Fact]
    public void Conceptual_with_no_sprite_weather_up_waits_for_the_next_one()
    {
        // Fair Skies: no sprite at all. The next sprite weather here is Snow (lv 47 Snowstorm
        // sprite): its wait is counted, then the kills.
        var options = Rank("Conceptual, lv 45, Pyros, Fair Skies", Shard(Conceptual, 2), Context(Pyros, Under(Pyros, "Fair Skies"), 45));
        var first = options[0];
        Assert.Equal(FarmKind.Sprite, first.Farm!.Kind);
        Assert.True(first.Wait > TimeSpan.Zero);
        Assert.False(first.IsOpen(Under(Pyros, "Fair Skies")));
        Assert.Equal(Minutes(first), (first.Wait + first.FirstTry).TotalMinutes + ((first.Tries - 1) * first.TryTime.TotalMinutes), 3);
    }

    // ---------- Curative (2 mnemes; 1 missing: 50 %) ----------

    [Fact]
    public void Curative_with_nothing_up_an_NM_to_prep_costs_about_as_much_as_silver_coffers()
    {
        // An NM not prepped: ~30 kills of its prep mob (30 min with others), the game's spawn roll
        // (~15 min), the fight (5): 50 min, then 50 % useful, so a second NM later (30 min): 80 min.
        // Silver coffer: 36 % of bunny FATEs, 1-5 Curative: 31 % a FATE, ~3 FATEs of 28 min: 90 min.
        var now = Under(Pyros, "Fair Skies");
        var options = Rank("Curative, lv 45, Pyros, nothing up", Shard(Curative, 1), Context(Pyros, now, 45));
        var nm = options[0];
        Assert.NotNull(nm.Nm);
        Assert.Equal(HuntOptions.NmToPrep, nm.FirstTry);
        Assert.Contains(nm.Notes, n => n.Contains("préparer"));
        var silver = options.First(o => o.Farm is { Kind: FarmKind.Bunny } && o.Drop!.Chest == "silver");
        Assert.InRange(Minutes(silver) - Minutes(nm), 0, 20);
    }

    [Fact]
    public void Curative_a_running_NM_FATE_goes_first()
    {
        // Leucosia's FATE (1388) is up: 5 min to get there and kill it.
        var now = Under(Pyros, "Fair Skies");
        var options = Rank("Curative, Leucosia FATE running", Shard(Curative, 1),
            Context(Pyros, now, 45, Statuses(Pyros, now, runningFate: 1388)));
        Assert.Equal(7759u, options[0].Nm!.Nm.Npc);
        Assert.Equal(HuntOptions.NmRunning, options[0].FirstTry);
        Assert.Equal(35, Minutes(options[0]), 3);
    }

    [Fact]
    public void A_prepped_NM_only_waits_for_the_spawn_roll_and_a_respawning_one_for_its_respawn()
    {
        var now = Under(Pyros, "Fair Skies");
        var prepped = Rank("Curative, Graffiacane prepped (tracker)", Shard(Curative, 1),
                Context(Pyros, now, 45, Statuses(Pyros, now, prepped: 1391)))
            .First(o => o.Nm?.Nm.Npc == 7736);
        Assert.Equal(HuntOptions.NmPrepped, prepped.FirstTry);

        var killed = Rank("Curative, Graffiacane killed 30 min ago", Shard(Curative, 1),
                Context(Pyros, now, 45, Statuses(Pyros, now, killed: (1391, now.UtcDateTime.AddMinutes(-30)))))
            .First(o => o.Nm?.Nm.Npc == 7736);
        Assert.Equal(90, killed.Wait.TotalMinutes, 3); // 120 min respawn, 30 gone
        Assert.Equal(HuntOptions.NmToPrep, killed.FirstTry);
    }

    // ---------- Inimical (2 mnemes; 1 missing: 50 %) ----------

    [Fact]
    public void Inimical_under_Thunder_the_adapted_level_54_sprite_is_minutes_away()
    {
        // It "always adapts" (80 %) and drops 5 Inimical: 97 % that one is useful, so 78 % a kill.
        // Hard at lv 45 (8 min a kill): about 10 min. The NMs need preparing: 80 min.
        var options = Rank("Inimical, lv 45, Thunder", Shard(Inimical, 1), Context(Pyros, Under(Pyros, "Thunder"), 45));
        var first = options[0];
        Assert.Equal((7772u, 54, true), (first.Farm!.Npc, first.Farm.Level, first.Farm.Adapted));
        Assert.InRange(Minutes(first), 8, 12);
        Assert.True(options.Where(o => o.Nm != null && o.Eta != null).All(o => Minutes(o) >= 80));
    }

    // ---------- Offensive, Protective: the rare ones ----------

    [Fact]
    public void Offensive_in_Pyros_only_comes_from_gold_coffers_about_twelve_hours()
    {
        // 8 % of bunny coffers are gold, half of them Offensive: 4 % a FATE, ~26 FATEs of 28 min.
        // The Hydatos sources (adapted lv 64 Snowstorm sprite, its gold coffers) are another zone.
        var options = Rank("Offensive, Pyros", Shard(Offensive, 1), Context(Pyros, Under(Pyros, "Fair Skies"), 45));
        var pyros = options.Where(o => o.Eta != null).ToList();
        Assert.All(pyros, o => Assert.Equal(("gold", FarmKind.Bunny), (o.Drop!.Chest, o.Farm!.Kind)));
        Assert.InRange(Minutes(pyros[0]), 600, 800);
        Assert.All(options.Where(o => o.Territory == Hydatos), o => Assert.Null(o.Eta));
    }

    [Fact]
    public void Protective_in_Pyros_NMs_beat_gold_coffers_by_far()
    {
        // Both Protective mnemes missing: every logogram is useful, one NM is enough (50 min);
        // gold coffers would take about 11 hours.
        var options = Rank("Protective, Pyros", Shard(Protective, 2), Context(Pyros, Under(Pyros, "Fair Skies"), 45));
        Assert.NotNull(options[0].Nm);
        Assert.Equal(HuntOptions.NmToPrep.TotalMinutes, Minutes(options[0]), 3);
        Assert.True(Minutes(options.First(o => o.Farm is { Kind: FarmKind.Bunny })) > 600);
    }

    // ---------- Hydatos ----------

    [Fact]
    public void Obscure_in_Hydatos_three_NMs_before_the_random_drops_of_prep_mobs()
    {
        // 4 mnemes, 1 missing: 25 %. An NM gives one: about 4 NMs of the train (140 min). The lv 62+
        // prep mobs drop it at random (5 %, guessed), hard at lv 55: about 8 hours.
        var options = Rank("Obscure, Hydatos lv 55", Shard(Obscure, 1), Context(Hydatos, Under(Hydatos, "Fair Skies"), 55));
        Assert.Equal([8063u, 8069u, 8072u], options.Take(3).Select(o => o.Nm!.Nm.Npc).Order());
        Assert.All(options.Skip(3), o => Assert.Equal(FarmKind.Mob, o.Farm!.Kind));
        Assert.True(Minutes(options[3]) > 3 * Minutes(options[0]));
    }

    [Fact]
    public void Offensive_in_Hydatos_under_Snow_the_adapted_level_64_sprite_beats_gold_coffers()
    {
        // 20 at once but rarely (5 %, at a 30 chain): about 20 kills of 8 min, 2 h 40; gold coffers
        // (10 % of FATEs): about 9 hours.
        var options = Rank("Offensive, Hydatos lv 60, Snow", Shard(Offensive, 1), Context(Hydatos, Under(Hydatos, "Snow"), 60));
        Assert.Equal((7469u, 64), (options[0].Farm!.Npc, options[0].Farm!.Level));
        Assert.InRange(Minutes(options[0]), 140, 180);
        Assert.True(Minutes(options.First(o => o.Farm is { Kind: FarmKind.Bunny, Fate: 1425 })) > 3 * Minutes(options[0]));
    }
}
