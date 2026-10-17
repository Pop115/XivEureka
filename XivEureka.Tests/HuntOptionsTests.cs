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
    private static readonly IReadOnlyList<Farm> Pyros = FarmCatalog.Embedded().Zone(795);

    [Fact]
    public void Every_Pyros_logogram_has_a_source()
    {
        var shards = LogosCatalog.Embedded().Shards.Select(s => s.Item).Where(i => i != 24809); // Obscure: Hydatos only
        var nms = NmCatalog.Embedded().Zone(795)!.Nms.Select(n => n.Shard);
        var dropped = Pyros.SelectMany(f => f.Drops).Select(d => d.Shard).Concat(nms).ToHashSet();
        Assert.All(shards, s => Assert.Contains(s, dropped));
    }

    [Fact]
    public void Weathers_exist_and_places_are_on_the_map()
    {
        var weathers = EurekaWeather.Zone(795)!.Slices.Select(s => s.Weather).ToHashSet();
        Assert.All(Pyros.SelectMany(f => f.Weathers), w => Assert.Contains(w, weathers));
        Assert.All(Pyros, f => Assert.NotEmpty(f.Places));
        Assert.All(Pyros.SelectMany(f => f.Places), p => Assert.InRange(p.X, 5, 42));
        Assert.All(Pyros.SelectMany(f => f.Places), p => Assert.InRange(p.Y, 5, 42));
    }

    [Fact]
    public void Sprites_and_bunnies_as_read()
    {
        var thunder41 = Pyros.Single(f => f is { Kind: FarmKind.Sprite, Npc: 7772, Level: 41 });
        Assert.Equal(["Thunder"], thunder41.Weathers);
        Assert.Equal(24008u, thunder41.Drops.Single().Shard);
        Assert.All(Pyros.Where(f => f.Adapted), f => Assert.DoesNotContain(f.Drops, d => d.Shard == 24007));
        Assert.Equal([1407, 1408], Pyros.Where(f => f.Kind == FarmKind.Bunny).Select(f => (int)f.Fate));
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
        [new FarmDrop(24007, false, "bronze", "1-2"), new FarmDrop(24010, false, "gold", "5-11")]);

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
        Assert.Equal(HuntOptions.Sprite, options[0].Eta);
        Assert.True(options[0].IsOpen(now));
    }

    [Fact]
    public void Without_its_weather_the_sprite_waits_for_it()
    {
        var now = When("Heat Waves", false);
        var sprite = HuntOptions.For(Shard(24007), Farms(EmberSprite), Context(now)).Single();
        Assert.False(sprite.IsOpen(now));
        Assert.Equal(HuntOptions.Sprite + (sprite.Window!.Start - now), sprite.Eta);
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
    public void A_running_bunny_FATE_beats_waiting_for_one_and_gold_is_slower()
    {
        var now = Start;
        var waiting = HuntOptions.For(Shard(24007), Farms(Bunny), Context(now)).Single();
        var running = HuntOptions.For(Shard(24007), Farms(Bunny), Context(now, bunnies: 1407)).Single();
        Assert.True(running.Eta < waiting.Eta);
        var gold = HuntOptions.For(Shard(24010), Farms(Bunny), Context(now, bunnies: 1407)).Single();
        Assert.True(gold.Eta > running.Eta);
        Assert.Contains(gold.Notes, n => n.Contains("d'or"));
    }

    [Fact]
    public void A_running_NM_FATE_beats_a_sprite_whose_weather_is_far()
    {
        var now = When("Heat Waves", false);
        var nm = new EurekaNm(7759, 0, 1388, 35, "water", new Vector2(26, 26), 24007, [], new SpawnCondition(null, false),
            new PrepMob(1, 40, new SpawnCondition(null, false), [new Vector2(27, 27)]));
        var fate = new MapFate(1388, "Leucosia", Vector3.Zero, 10, 20, TimeSpan.FromMinutes(10), 0, false);
        var status = new NmStatus(nm, fate, null, null, null);
        var options = HuntOptions.For(Shard(24007, new HuntSource(Pyros, nm)), Farms(EmberSprite), Context(now, statuses: [status]));
        // Heat Waves is off: its next window is at least one weather period (23 min) away.
        Assert.True(options.Single(o => o.Farm != null).Eta > HuntOptions.NmRunning);
        Assert.NotNull(options[0].Nm);
        Assert.Equal(HuntOptions.NmRunning, options[0].Eta);
    }

    [Fact]
    public void An_NM_of_another_zone_has_no_time_and_comes_last()
    {
        var nm = new EurekaNm(1, 0, 1, 60, "fire", new Vector2(10, 10), 24007, [], new SpawnCondition(null, false),
            new PrepMob(2, 60, new SpawnCondition(null, false), []));
        var options = HuntOptions.For(Shard(24007, new HuntSource(827, nm)), Farms(EmberSprite), Context(When("Heat Waves", true)));
        Assert.Null(options[^1].Eta);
        Assert.NotNull(options[^1].Nm);
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
    public void A_mob_well_above_you_is_slower()
    {
        var now = When("Heat Waves", true);
        var strong = EmberSprite with { Level = 53 };
        var option = HuntOptions.For(Shard(24007), Farms(strong), Context(now, level: 45)).Single();
        Assert.Equal(HuntOptions.Sprite + HuntOptions.TooStrong, option.Eta);
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
        // No weather: the effort alone, divided by the chance (1 useful out of 4 → 4 times).
        var sprite = new Farm(FarmKind.Sprite, 7773, 0, 45, false, [], [new Vector2(20, 20)], [new FarmDrop(24007, false, null, null)]);
        var farms = new FarmCatalog([new ZoneFarms(Pyros, [sprite])]);
        Assert.Equal(HuntOptions.Sprite * 4, HuntOptions.For(Shard(1, 4), farms, Context()).Single().Eta);
        Assert.Equal(HuntOptions.Sprite, HuntOptions.For(Shard(4, 4), farms, Context()).Single().Eta);
    }

    [Fact]
    public void An_NM_needs_other_NMs_of_the_train_for_the_extra_tries()
    {
        var nm = new EurekaNm(7759, 0, 1388, 35, "water", new Vector2(26, 26), 24007, [], new SpawnCondition(null, false),
            new PrepMob(1, 40, new SpawnCondition(null, false), [new Vector2(27, 27)]));
        var fate = new MapFate(1388, "Leucosia", Vector3.Zero, 10, 20, TimeSpan.FromMinutes(10), 0, false);
        var status = new NmStatus(nm, fate, null, null, null);
        var option = HuntOptions.For(Shard(1, 2, new HuntSource(Pyros, nm)), new FarmCatalog([]), Context([status])).Single();
        Assert.Equal(HuntOptions.NmRunning + HuntOptions.NmRepeat, option.Eta);
    }
}
