using System.Globalization;
using System.Numerics;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;
using Xunit.Abstractions;

namespace XivEureka.Tests;

/// <summary>
/// "Quoi taper" in XP mode, on the plugin's real data: what levels you up fastest in the zone. Each
/// scenario prints its ranking (dotnet test --logger "console;verbosity=detailed").
/// </summary>
public class XpScenarioTests(ITestOutputHelper output)
{
    private const uint Pyros = 795;
    private const uint Hydatos = 827;
    private const uint Obscure = 24809;

    private static readonly NmCatalog Nms = NmCatalog.Embedded();
    private static readonly FarmCatalog Farms = FarmCatalog.Embedded();
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);
    private static readonly HashSet<uint> Nothing = [];

    private static DateTimeOffset Under(uint territory, string weather) =>
        EurekaWeather.Periods(EurekaWeather.Zone(territory)!, Start).Take(2000).First(p => p.Weather == weather).Start.AddMinutes(1);

    private static HuntContext Context(uint territory, DateTimeOffset now, int level, int gap = HuntOptions.DefaultGap, ushort? runningFate = null)
    {
        var fates = runningFate is { } f
            ? new List<MapFate> { new(f, "FATE", Vector3.Zero, 10, 30, TimeSpan.FromMinutes(12), 0, false) }
            : [];
        var statuses = NmStatuses.Compute(EurekaWeather.Zone(territory)!, Nms.Zone(territory)!, fates,
            new Dictionary<ushort, DateTime>(), new HashSet<ushort>(), now);
        return new HuntContext(territory, statuses, new HashSet<ushort>(), level, now, gap);
    }

    private IReadOnlyList<XpOption> Rank(string title, HuntContext context, IReadOnlySet<uint>? learned = null, IReadOnlySet<uint>? useful = null)
    {
        var options = XpOptions.For(Farms, context, learned ?? Nothing, useful ?? Nothing);
        output.WriteLine($"== {title}");
        foreach (var o in options.Take(12))
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"   x{o.Relative,4:0.00} (hour x{o.Score,4:0.00})  {o.Kind} {o.Npc} lv {o.Level}{(o.Window is { } w && w.Start > context.Now ? $", in {(w.Start - context.Now).TotalMinutes:0} min" : "")}{(o.UsefulShards.Count > 0 ? " +logogram" : "")}"));
        return options;
    }

    [Fact]
    public void Pyros_lv_45_gap_3_only_mobs_from_43_to_48_best_hour_first()
    {
        var options = Rank("Pyros lv 45, gap +3", Context(Pyros, Under(Pyros, "Fair Skies"), 45));
        Assert.NotEmpty(options);
        Assert.All(options, o => Assert.InRange(o.Level, 43, 48));
        Assert.Equal(options.OrderByDescending(o => o.Score).Select(o => o.Score), options.Select(o => o.Score));
        Assert.Equal(48, options.Single(o => o.Kind == XpKind.Chain).Level);
        // One line per mob.
        Assert.Equal(options.Count, options.Select(o => (o.Kind == XpKind.Nm, o.Kind == XpKind.Chain, o.Npc, o.Level)).Distinct().Count());
    }

    [Fact]
    public void At_gap_6_the_chain_aims_six_levels_above_where_experience_stops_growing()
    {
        var options = Rank("Pyros lv 44, gap +6", Context(Pyros, Under(Pyros, "Fair Skies"), 44, gap: 6));
        var chain = options.Single(o => o.Kind == XpKind.Chain);
        Assert.Equal(50, chain.Level);
        Assert.All(options, o => Assert.True(o.Level <= 50));
        Assert.True(chain.Rate > Rank("Pyros lv 44, gap +3", Context(Pyros, Under(Pyros, "Fair Skies"), 44)).Single(o => o.Kind == XpKind.Chain).Rate);
    }

    [Fact]
    public void Counting_a_party_of_two_doubles_the_experience_per_minute_of_kills()
    {
        var now = Under(Pyros, "Fair Skies");
        var alone = Rank("Pyros lv 45, alone", Context(Pyros, now, 45));
        var duo = Rank("Pyros lv 45, party of 2 counted", Context(Pyros, now, 45) with { PartySize = 2 });
        var chain = alone.Single(o => o.Kind == XpKind.Chain);
        Assert.Equal(chain.Rate * 2, duo.Single(o => o.Kind == XpKind.Chain).Rate, 6);
    }

    [Fact]
    public void Reflect_L_makes_the_sprites_faster()
    {
        var context = Context(Pyros, Under(Pyros, "Fair Skies"), 45);
        var without = Rank("Pyros lv 45, no Reflect L", context).Where(o => o.Kind == XpKind.Sprite).ToList();
        var with = Rank("Pyros lv 45, Reflect L", context, new HashSet<uint> { XpOptions.ReflectL }).Where(o => o.Kind == XpKind.Sprite).ToList();
        Assert.NotEmpty(without);
        foreach (var sprite in without)
            Assert.True(with.Single(o => o.Npc == sprite.Npc && o.Level == sprite.Level).Rate > sprite.Rate);
        Assert.All(with, o => Assert.Contains(o.Notes, n => n.Contains("Reflect L")));
    }

    [Fact]
    public void An_NM_more_than_one_level_above_is_not_listed_one_at_your_level_is()
    {
        // Hydatos NMs are lv 50 to 60, one per level: at 52, the lv 52 and 53 are worth it, not the lv 54.
        var zone = Nms.Zone(Hydatos)!;
        var at53 = zone.Nms.Single(n => n.Level == 53);
        var at54 = zone.Nms.Single(n => n.Level == 54);
        var now = Under(Hydatos, "Fair Skies");
        Assert.Contains(Rank("Hydatos lv 52, NM lv 53 running", Context(Hydatos, now, 52, runningFate: at53.Fate)), o => o.Nm == at53);
        Assert.DoesNotContain(Rank("Hydatos lv 52, NM lv 54 running", Context(Hydatos, now, 52, runningFate: at54.Fate)), o => o.Nm == at54);
    }

    [Fact]
    public void A_running_NM_at_your_level_beats_the_plain_chain()
    {
        // The guides: chain mobs, unless an NM you get full rewards for spawns nearby. Mobs that
        // mutate or adapt, under their condition, stay as good.
        var nm = Nms.Zone(Hydatos)!.Nms.Single(n => n.Level == 55);
        var options = Rank("Hydatos lv 55, NM lv 55 running", Context(Hydatos, Under(Hydatos, "Fair Skies"), 55, runningFate: nm.Fate)).ToList();
        var at = options.FindIndex(o => o.Nm == nm);
        Assert.InRange(at, 0, 4);
        Assert.True(at < options.FindIndex(o => o.Kind == XpKind.Chain));
    }

    [Fact]
    public void A_useful_logogram_only_from_mobs_at_or_above_your_level()
    {
        var context = Context(Hydatos, Under(Hydatos, "Snow"), 55);
        var useful = new HashSet<uint>(Farms.Zone(Hydatos).SelectMany(f => f.Drops).Select(d => d.Shard)) { Obscure };
        var options = Rank("Hydatos lv 55, every logogram useful", context, useful: useful);
        Assert.Contains(options, o => o.UsefulShards.Count > 0);
        Assert.All(options.Where(o => o.Level < 55 && o.Kind != XpKind.Nm), o => Assert.Empty(o.UsefulShards));
    }

    [Fact]
    public void A_source_an_hour_away_counts_for_nothing_now()
    {
        var rate = 1.0;
        var now = Start;
        Assert.Equal(1, XpOptions.Score(rate, null, now));
        Assert.Equal(0.5, XpOptions.Score(rate, new TimeWindow(now.AddMinutes(30), now.AddMinutes(60)), now), 6);
        Assert.Equal(0, XpOptions.Score(rate, new TimeWindow(now.AddMinutes(90), now.AddMinutes(120)), now));
    }

    [Fact]
    public void In_Logos_mode_an_NM_more_than_one_level_above_gives_no_logogram()
    {
        // Obscure comes from three Hydatos NMs; at 50 only those at 51 or below count.
        var obscure = Nms.Zone(Hydatos)!.Nms.Where(n => n.Shard == Obscure).ToList();
        var shard = new HuntShard(Obscure, [new HuntMneme(1, 0, [new HuntNeed(new LogosAction(1, 1, "T", null, []), 1)])],
            [], obscure.Select(n => new HuntSource(Hydatos, n)).ToList());
        var options = HuntOptions.For(shard, Farms, Context(Hydatos, Under(Hydatos, "Fair Skies"), 50));
        Assert.All(options.Where(o => o.Nm != null), o => Assert.True(o.Nm!.Nm.Level <= 51));
    }
}
