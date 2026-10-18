using System.Numerics;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

public class LogogramCatalogTests
{
    private static readonly LogosCatalog Catalog = LogosCatalog.Embedded();

    /// <summary>
    /// Logogram item ids and their mnemes, checked against the game Item sheet (names) and the wiki
    /// (pools) on 2026-10-17: before that, five logograms carried another one's mnemes.
    /// </summary>
    [Theory]
    [InlineData(24009u, new uint[] { 24019, 24034 })] // Curative: Wisdom of the Ordained, Cure L II
    [InlineData(24010u, new uint[] { 24020, 24032 })] // Offensive: Wisdom of the Skirmisher, Bloodbath L
    [InlineData(24011u, new uint[] { 24018, 24021 })] // Protective: Wisdom of the Guardian, Spirit of the Remembered
    [InlineData(24012u, new uint[] { 24025, 24029 })] // Tactical: Featherfoot L, Stealth L
    [InlineData(24013u, new uint[] { 24023, 24035 })] // Mitigative: Shell L, Stoneskin L
    [InlineData(24014u, new uint[] { 24026, 24027 })] // Inimical: Spirit Dart L, Dispel L
    public void Each_logogram_gives_its_own_mnemes(uint logogram, uint[] mnemes) =>
        Assert.Equal(mnemes.Order(), Catalog.Shards.Single(s => s.Item == logogram).Mnemes.Order());

    [Fact]
    public void Leucosia_drops_curative_and_Flauros_protective()
    {
        var pyros = NmCatalog.Embedded().Zone(795)!;
        Assert.Equal(24009u, pyros.Nms.Single(n => n.Npc == 7759).Shard);
        Assert.Equal(24011u, pyros.Nms.Single(n => n.Npc == 7753).Shard);
    }
}

public class FarmCatalogTests
{
    private static readonly FarmCatalog Catalog = FarmCatalog.Embedded();
    private static readonly IReadOnlyList<Farm> Pyros = Catalog.Zone(795);

    [Theory]
    [InlineData(795u, new uint[] { 24809 })] // Obscure: Hydatos only
    [InlineData(827u, new uint[0])]
    public void Every_logogram_has_a_source_in_the_zone(uint territory, uint[] absent)
    {
        var shards = LogosCatalog.Embedded().Shards.Select(s => s.Item).Except(absent);
        var nms = NmCatalog.Embedded().Zone(territory)!.Nms.Select(n => n.Shard);
        var dropped = Catalog.Zone(territory).SelectMany(f => f.Drops).Select(d => d.Shard).Concat(nms).ToHashSet();
        Assert.All(shards, s => Assert.Contains(s, dropped));
    }

    [Theory]
    [InlineData(795u)]
    [InlineData(827u)]
    public void Weathers_exist_and_places_are_on_the_map(uint territory)
    {
        var farms = Catalog.Zone(territory);
        var weathers = EurekaWeather.Zone(territory)!.Slices.Select(s => s.Weather).ToHashSet();
        Assert.All(farms.SelectMany(f => f.Weathers), w => Assert.Contains(w, weathers));
        Assert.All(farms.Where(f => f.Kind != FarmKind.Chain), f => Assert.NotEmpty(f.Places));
        Assert.All(farms.Where(f => f.Kind == FarmKind.Chain), f => Assert.NotNull(f.RelativeLevel));
        Assert.All(farms.SelectMany(f => f.Places), p => Assert.InRange(p.X, 3, 42));
        Assert.All(farms.SelectMany(f => f.Places), p => Assert.InRange(p.Y, 5, 42));
        // A weather or a time of day, never both (HuntOptions keeps one window).
        Assert.All(farms.Where(f => f.Time != null), f => Assert.Empty(f.Weathers));
        Assert.All(farms.Select(f => f.Time).OfType<string>(), t => Assert.Contains(t, new[] { "day", "night" }));
    }

    [Fact]
    public void Sprites_and_bunnies_as_read()
    {
        var thunder41 = Pyros.Single(f => f is { Kind: FarmKind.Sprite, Npc: 7772, Level: 41 });
        Assert.Equal(["Thunder"], thunder41.Weathers);
        Assert.Equal(24008u, thunder41.Drops.Single().Shard);
        Assert.All(Pyros.Where(f => f.Adapted), f => Assert.DoesNotContain(f.Drops, d => d.Shard == 24007));
        Assert.Equal([1407, 1408], Pyros.Where(f => f.Kind == FarmKind.Bunny).Select(f => (int)f.Fate));
        Assert.Equal([1425], Catalog.Zone(827).Where(f => f.Kind == FarmKind.Bunny).Select(f => (int)f.Fate));
        // Every Hydatos lockbox farm is a mutating or adapting mob; Val Nanka mutates at night.
        Assert.All(Catalog.Zone(827).Concat(Catalog.Zone(795)).Where(f => f.Kind == FarmKind.Lockbox), f => Assert.True(f.Mutated || f.Adapted));
        Assert.True(Catalog.Zone(795).Count(f => f.Kind == FarmKind.Lockbox) >= 50);
        var nanka = Catalog.Zone(827).Single(f => f.Npc == 8009);
        Assert.True(nanka.Mutated);
        Assert.Equal("night", nanka.Time);
    }
}

public class HydatosNmTests
{
    private static readonly ZoneNms Hydatos = NmCatalog.Embedded().Zone(827)!;

    [Fact]
    public void The_11_Hydatos_NMs_of_the_tracker()
    {
        Assert.Equal(11, Hydatos.Nms.Count);
        Assert.Equal(Enumerable.Range(55, 11), Hydatos.Nms.Select(n => n.Site).Order());
        // FATEs 1412 to 1421, then 1423 (1422 is the Baldesion Arsenal support FATE, 1424 Ovni).
        Assert.Equal(Enumerable.Range(1412, 10).Append(1423).Select(i => (ushort)i), Hydatos.Nms.Select(n => n.Fate).Order());
        Assert.Equal(Enumerable.Range(50, 11), Hydatos.Nms.Select(n => n.Level).Order());
        Assert.All(Hydatos.Nms, n => Assert.NotEmpty(n.Prep.Places));
    }

    [Fact]
    public void Obscure_from_Khalamari_Stegodon_and_Frostmane_and_Goldemar_needs_the_night()
    {
        Assert.Equal([8063u, 8069u, 8072u], Hydatos.Nms.Where(n => n.Shard == 24809).Select(n => n.Npc).Order());
        Assert.All(Hydatos.Nms, n => Assert.Contains(n.Shard, new uint[] { 0, 24809 }));
        Assert.True(Hydatos.Nms.Single(n => n.Npc == 7966).Prep.Condition.Night);
        Assert.All(Hydatos.Nms.Where(n => n.Npc != 7966), n => Assert.True(n.Condition.None && n.Prep.Condition.None));
    }
}

public class HuntOptionsTests
{
    private const uint Pyros = 795;
    private static readonly EurekaZone Zone = EurekaWeather.Zone(Pyros)!;
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    private static readonly Farm EmberSprite = new(FarmKind.Sprite, 7773, 0, 45, false, ["Heat Waves"], [new Vector2(20, 20)],
        [new FarmDrop(24007, true, null, null)]);

    private static readonly Farm Bunny = new(FarmKind.Bunny, 0, 1407, 35, false, [], [new Vector2(24, 26)],
        [new FarmDrop(24007, false, "bronze", "1-2", 0.278), new FarmDrop(24010, false, "gold", "5-11", 0.039)]);

    /// <summary>Time per kill / chance per kill, for a sprite with nothing else in the way.</summary>
    private static readonly TimeSpan SpriteEta = TimeSpan.FromMinutes(HuntOptions.SpriteKill.TotalMinutes / HuntOptions.SpriteChance);

    private static void Near(TimeSpan expected, TimeSpan? actual) => Assert.Equal(expected.TotalMinutes, actual!.Value.TotalMinutes, 3);

    private static FarmCatalog Farms(params Farm[] farms) => new([new ZoneFarms(Pyros, farms)]);

    private static HuntShard Shard(uint item, params HuntSource[] nms) => new(item, [], [], nms);

    private static HuntContext Context(DateTimeOffset now, int? level = 45, IReadOnlyList<NmStatus>? statuses = null,
        params ushort[] bunnies) => new(Pyros, statuses ?? [], bunnies.ToHashSet(), level, now);

    /// <summary>The first instant (from Start) where the weather is on, or off.</summary>
    private static DateTimeOffset When(string weather, bool on) =>
        EurekaWeather.Periods(Zone, Start).Take(500).First(p => (p.Weather == weather) == on).Start + TimeSpan.FromMinutes(1);

    [Fact]
    public void A_sprite_under_its_weather_comes_first()
    {
        var now = When("Heat Waves", true);
        var options = HuntOptions.For(Shard(24007), Farms(Bunny, EmberSprite), Context(now));
        Assert.Equal(FarmKind.Sprite, options[0].Farm!.Kind);
        Near(SpriteEta, options[0].Eta);
        Assert.True(options[0].IsOpen(now));
    }

    [Fact]
    public void Without_its_weather_the_sprite_waits_for_it()
    {
        var now = When("Heat Waves", false);
        var sprite = HuntOptions.For(Shard(24007), Farms(EmberSprite), Context(now)).Single();
        Assert.False(sprite.IsOpen(now));
        Near(SpriteEta + (sprite.Window!.Start - now), sprite.Eta);
    }

    [Fact]
    public void Conceptual_only_from_sprites_at_or_above_your_level()
    {
        var now = When("Heat Waves", true);
        Assert.Empty(HuntOptions.For(Shard(24007), Farms(EmberSprite), Context(now, level: 46)));
        Assert.Single(HuntOptions.For(Shard(24007), Farms(EmberSprite), Context(now, level: 45)));
        var unknown = HuntOptions.For(Shard(24007), Farms(EmberSprite), Context(now, level: null)).Single();
        Assert.Contains(unknown.Notes, n => n.Contains("niveau"));
    }

    [Fact]
    public void A_bunny_FATE_is_a_long_try_and_gold_is_rare()
    {
        var now = Start;
        var waiting = HuntOptions.For(Shard(24007), Farms(Bunny), Context(now)).Single();
        var running = HuntOptions.For(Shard(24007), Farms(Bunny), Context(now, bunnies: 1407)).Single();
        Assert.True(running.Eta < waiting.Eta);
        // Bronze: 27.8 % per FATE, 1.5 logograms on average, every Conceptual mneme useful here.
        Assert.Equal(0.278, waiting.TryChance, 6);
        Near(TimeSpan.FromMinutes(HuntOptions.BunnyCycle.TotalMinutes / 0.278), waiting.Eta);
        var gold = HuntOptions.For(Shard(24010), Farms(Bunny), Context(now, bunnies: 1407)).Single();
        Assert.True(gold.Eta > running.Eta);
        Assert.True(gold.Tries > 25);
        Assert.Contains(gold.Notes, n => n.Contains("d'or"));
        // Even running, a bunny coffer is slower than a sprite under its weather.
        Assert.True(running.Eta > SpriteEta);
    }

    [Fact]
    public void An_adapted_sprite_dropping_five_needs_fewer_kills_when_few_mnemes_are_useful()
    {
        var now = When("Heat Waves", true);
        var shard = new HuntShard(24014, [new HuntMneme(1, 0, [new HuntNeed(new LogosAction(1, 1, "T", null, []), 1)]), new HuntMneme(2, 0, [])],
            [new LogosAction(1, 1, "T", null, [])], []);
        var one = EmberSprite with { Adapted = true, Drops = [new FarmDrop(24014, false, null, null)] };
        var five = EmberSprite with { Adapted = true, Drops = [new FarmDrop(24014, false, null, "5")] };
        var single = HuntOptions.For(shard, Farms(one), Context(now)).Single();
        var batch = HuntOptions.For(shard, Farms(five), Context(now)).Single();
        Assert.Equal(HuntOptions.AdaptedChance * 0.5, single.TryChance, 6);
        Assert.Equal(HuntOptions.AdaptedChance * (1 - Math.Pow(0.5, 5)), batch.TryChance, 6);
        Assert.True(batch.Eta < single.Eta);
        Assert.Contains(single.Notes, n => n.Contains("s'adapte"));
    }

    [Fact]
    public void A_running_NM_FATE_beats_a_sprite_whose_weather_is_far()
    {
        var now = When("Heat Waves", false);
        var nm = new EurekaNm(7759, 0, 1388, 35, "water", new Vector2(26, 26), 24007, [], new SpawnCondition(null, false),
            new PrepMob(1, 40, new SpawnCondition(null, false), [new Vector2(27, 27)]));
        var fate = new MapFate(1388, "Leucosia", Vector3.Zero, 10, 20, TimeSpan.FromMinutes(10), 0, false);
        var status = new NmStatus(nm, fate, null, null, null);
        var context = Context(now, statuses: [status]);
        var options = HuntOptions.For(Shard(24007, new HuntSource(Pyros, nm)), Farms(EmberSprite), context);
        // Alone at lv 45 against a lv 35 NM: 3 min to get there, half of the 20 min fight.
        var fight = HuntOptions.NmFight(nm, context);
        Assert.Equal(13, fight.TotalMinutes, 3);
        // Heat Waves is off: its next window is at least one weather period (23 min) away.
        Assert.True(options.Single(o => o.Farm != null).Eta > fight);
        Assert.NotNull(options[0].Nm);
        Assert.Equal(fight, options[0].Eta);
    }

    [Fact]
    public void Sources_of_another_zone_are_left_out()
    {
        var nm = new EurekaNm(1, 0, 1, 60, "fire", new Vector2(10, 10), 24007, [], new SpawnCondition(null, false),
            new PrepMob(2, 60, new SpawnCondition(null, false), []));
        var farms = new FarmCatalog([new ZoneFarms(Pyros, [EmberSprite]), new ZoneFarms(827, [EmberSprite])]);
        var options = HuntOptions.For(Shard(24007, new HuntSource(827, nm)), farms, Context(When("Heat Waves", true)));
        Assert.All(options, o => Assert.Equal(Pyros, o.Territory));
        Assert.DoesNotContain(options, o => o.Nm != null);
    }

    [Fact]
    public void Fastest_gives_one_line_per_useful_shard_fastest_first()
    {
        var now = When("Heat Waves", true);
        var plan = new HuntPlan(2, [], [Shard(24010), Shard(24007)]);
        var fastest = HuntOptions.Fastest(plan, Farms(EmberSprite, Bunny), Context(now, bunnies: 1407));
        Assert.Equal([24007u, 24010u], fastest.Select(o => o.Shard));
        Assert.Equal(FarmKind.Sprite, fastest[0].Farm!.Kind);
    }

    [Fact]
    public void A_mob_beyond_your_level_gap_is_not_proposed()
    {
        // Within the gap (3 by default), a mob above you takes no longer: Logos actions make up for it.
        var now = When("Heat Waves", true);
        var option = HuntOptions.For(Shard(24007), Farms(EmberSprite with { Level = 48 }), Context(now, level: 45)).Single();
        Near(SpriteEta, option.Eta);
        Assert.Empty(HuntOptions.For(Shard(24007), Farms(EmberSprite with { Level = 49 }), Context(now, level: 45)));
        Assert.Single(HuntOptions.For(Shard(24007), Farms(EmberSprite with { Level = 49 }), Context(now, level: 45) with { Gap = 4 }));
    }
}

public class HuntChanceTests
{
    private const uint Pyros = 795;
    private static readonly LogosAction Action = new(1, 1, "T", null, []);
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    /// <summary>A shard with <paramref name="useful"/> useful mnemes out of <paramref name="total"/>.</summary>
    private static HuntShard Shard(int useful, int total, params HuntSource[] nms) =>
        new(24007, Enumerable.Range(0, total)
            .Select(i => new HuntMneme((uint)(30000 + i), 0, i < useful ? [new HuntNeed(Action, 1)] : []))
            .ToList(), [Action], nms);

    private static HuntContext Context(IReadOnlyList<NmStatus>? statuses = null) =>
        new(Pyros, statuses ?? [], new HashSet<ushort>(), 45, Now);

    [Fact]
    public void Useful_chance_counts_the_mnemes_still_missing()
    {
        Assert.Equal(2.0 / 7, Shard(2, 7).UsefulChance, 6);
        Assert.Equal(1, Shard(2, 2).UsefulChance);
    }

    [Theory]
    [InlineData("1-2", 1.5)]
    [InlineData("5-11", 8)]
    [InlineData("3", 3)]
    [InlineData(null, 1)]
    public void Average_count(string? count, double expected) => Assert.Equal(expected, HuntOptions.AverageCount(count));

    [Fact]
    public void Several_logograms_raise_the_chance() =>
        Assert.Equal(0.75, HuntOptions.DropChance(0.5, 2), 6);

    [Fact]
    public void A_sprite_takes_as_many_kills_as_logograms_needed()
    {
        // No weather: kills / chance per kill, and 1 useful mneme out of 4 → 4 times as many.
        var sprite = new Farm(FarmKind.Sprite, 7773, 0, 45, false, [], [new Vector2(20, 20)], [new FarmDrop(24007, false, null, null)]);
        var farms = new FarmCatalog([new ZoneFarms(Pyros, [sprite])]);
        var all = HuntOptions.For(Shard(4, 4), farms, Context()).Single();
        var quarter = HuntOptions.For(Shard(1, 4), farms, Context()).Single();
        Assert.Equal(HuntOptions.SpriteKill.TotalMinutes / HuntOptions.SpriteChance, all.Eta!.Value.TotalMinutes, 3);
        Assert.Equal(all.Eta.Value.TotalMinutes * 4, quarter.Eta!.Value.TotalMinutes, 3);
        Assert.Equal(4 / HuntOptions.SpriteChance, quarter.Tries, 6);
    }

    [Fact]
    public void An_NM_alone_waits_its_respawn_for_the_extra_tries()
    {
        var nm = new EurekaNm(7759, 0, 1388, 35, "water", new Vector2(26, 26), 24007, [], new SpawnCondition(null, false),
            new PrepMob(1, 40, new SpawnCondition(null, false), [new Vector2(27, 27)]));
        var fate = new MapFate(1388, "Leucosia", Vector3.Zero, 10, 20, TimeSpan.FromMinutes(10), 0, false);
        var status = new NmStatus(nm, fate, null, null, null);
        var context = Context([status]);
        var option = HuntOptions.For(Shard(1, 2, new HuntSource(Pyros, nm)), new FarmCatalog([]), context).Single();
        // 1 useful mneme out of 2: a second try on average. The only NM dropping it: the same one again,
        // so not before its 2 h respawn (longer than prepping it).
        var again = HuntOptions.NmToPrep(nm, context) > HuntOptions.NmRespawn ? HuntOptions.NmToPrep(nm, context) : HuntOptions.NmRespawn;
        Assert.Equal(HuntOptions.NmFight(nm, context) + again, option.Eta);
    }
}

public class HuntTimeOfDayTests
{
    [Fact]
    public void A_night_mob_waits_for_the_night()
    {
        var wyvern = new Farm(FarmKind.Lockbox, 8053, 0, 65, false, [], [new Vector2(30, 17)], [new FarmDrop(24012, false, null, null)], "night");
        var farms = new FarmCatalog([new ZoneFarms(827, [wyvern])]);
        var shard = new HuntShard(24012, [], [], []);
        var start = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);
        foreach (var minutes in Enumerable.Range(0, 70).Select(i => i * 1.0))
        {
            var now = start.AddMinutes(minutes);
            var option = HuntOptions.For(shard, farms, new HuntContext(827, [], new HashSet<ushort>(), 60, now, Gap: 5)).Single();
            Assert.Equal(EurekaWeather.IsNight(now), option.IsOpen(now));
            Assert.True(EurekaWeather.IsNight(option.Window!.Start.AddSeconds(1)));
        }
    }
}

public class HuntGapTests
{
    private static readonly EurekaNm Skoll = NmCatalog.Embedded().Zone(795)!.Nms.Single(n => n.Npc == 7739);
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    private static HuntContext Context(int level, int gap = HuntOptions.DefaultGap) =>
        new(795, [], new HashSet<ushort>(), level, Now, gap);

    [Fact]
    public void No_party_counted_an_NM_and_its_prep_take_the_same_time_for_everyone()
    {
        // Skoll (lv 50): a 20 min fight at your level, 40 above you; its prep, 30 kills of 2 min.
        Assert.Equal(3 + 20, HuntOptions.NmFight(Skoll, Context(50)).TotalMinutes, 3);
        Assert.Equal(3 + 40, HuntOptions.NmFight(Skoll, Context(45)).TotalMinutes, 3);
        Assert.Equal(60, HuntOptions.NmPrep(Skoll, Context(50)).TotalMinutes, 3);
    }

    [Fact]
    public void Kills_are_shared_by_the_party_only_when_it_is_counted_up_to_four()
    {
        Assert.Equal(HuntOptions.SpriteKill, HuntOptions.Shared(HuntOptions.SpriteKill, Context(50)));
        Assert.Equal(HuntOptions.SpriteKill / 2, HuntOptions.Shared(HuntOptions.SpriteKill, Context(50) with { PartySize = 2 }));
        Assert.Equal(HuntOptions.SpriteKill / 4, HuntOptions.Shared(HuntOptions.SpriteKill, Context(50) with { PartySize = 8 }));
        // Skoll for two: 60 min of prep become 30, the 20 min fight 10.
        var duo = Context(50) with { PartySize = 2 };
        Assert.Equal(30, HuntOptions.NmPrep(Skoll, duo).TotalMinutes, 3);
        Assert.Equal(3 + 10, HuntOptions.NmFight(Skoll, duo).TotalMinutes, 3);
    }

    [Fact]
    public void Bunny_coffers_are_not_shared()
    {
        var bunny = new Farm(FarmKind.Bunny, 0, 1407, 50, false, [], [new(1, 1)], [new FarmDrop(24007, false, "bronze", "1-2", 0.14)]);
        var farms = new FarmCatalog([new ZoneFarms(795, [bunny])]);
        var shard = new HuntShard(24007, [new HuntMneme(1, 0, [new HuntNeed(new LogosAction(1, 1, "T", null, []), 1)])], [], []);
        var alone = HuntOptions.For(shard, farms, Context(50)).Single();
        var four = HuntOptions.For(shard, farms, Context(50) with { PartySize = 4 }).Single();
        Assert.Equal(alone.Eta, four.Eta);
    }

    [Fact]
    public void The_level_gap_decides_which_mobs_are_proposed()
    {
        var sprite = new Farm(FarmKind.Sprite, 1, 0, 55, false, [], [new(1, 1)], [new FarmDrop(24007, true, null, null)]);
        var farms = new FarmCatalog([new ZoneFarms(795, [sprite])]);
        var shard = new HuntShard(24007, [new HuntMneme(1, 0, [new HuntNeed(new LogosAction(1, 1, "T", null, []), 1)])], [], []);
        Assert.Empty(HuntOptions.For(shard, farms, Context(50, gap: 4)));
        Assert.Single(HuntOptions.For(shard, farms, Context(50, gap: 5)));
        var xp = XpOptions.For(farms, Context(50, gap: 6), new HashSet<uint>(), new HashSet<uint>());
        Assert.Equal(56, xp.Single(o => o.Kind == XpKind.Chain).Level);
        Assert.Contains(xp, o => o.Farm == sprite);
        Assert.DoesNotContain(XpOptions.For(farms, Context(50, gap: 2), new HashSet<uint>(), new HashSet<uint>()), o => o.Farm == sprite);
    }
}

public class HuntXpTests
{
    [Fact]
    public void Mobs_above_you_give_more_and_mobs_well_below_almost_nothing()
    {
        Assert.Equal(1, HuntXp.LevelFactor(50, 50));
        Assert.Equal(1.45, HuntXp.LevelFactor(53, 50), 6);
        // No more beyond +6 (the guides' cap).
        Assert.Equal(1.9, HuntXp.LevelFactor(56, 50), 6);
        Assert.Equal(1.9, HuntXp.LevelFactor(60, 50), 6);
        Assert.Equal(0.1, HuntXp.LevelFactor(47, 50));
    }

    [Fact]
    public void Experience_counts_until_the_zone_max_level()
    {
        Assert.Equal(0.5, HuntXp.Weight(0.5, 827, 50));
        Assert.Equal(0, HuntXp.Weight(0.5, 827, 60));
        Assert.Equal(0, HuntXp.Weight(0.5, 795, 50));   // Pyros caps at 50
        Assert.Equal(60, HuntXp.MaxLevel(827));
    }

    [Fact]
    public void The_counted_time_drops_by_the_weight_of_the_experience_it_gives()
    {
        Assert.Equal(30, HuntXp.Counted(TimeSpan.FromMinutes(60), 1, 0.5)!.Value.TotalMinutes, 6);
        Assert.Equal(60, HuntXp.Counted(TimeSpan.FromMinutes(60), 1, 0)!.Value.TotalMinutes, 6);
        Assert.Null(HuntXp.Counted(null, 1, 0.5));
    }

    [Fact]
    public void Difficulty_from_the_level_gap_the_change_and_NMs()
    {
        Assert.Equal(Difficulty.Easy, HuntXp.Of(50, 50, changes: false, nm: false));
        Assert.Equal(Difficulty.Medium, HuntXp.Of(50, 50, changes: true, nm: false));
        Assert.Equal(Difficulty.Hard, HuntXp.Of(54, 50, changes: false, nm: false));
        Assert.Equal(Difficulty.Hard, HuntXp.Of(50, 50, changes: false, nm: true));
        Assert.Equal(Difficulty.Easy, HuntXp.Of(40, 50, changes: false, nm: true));
    }
}
