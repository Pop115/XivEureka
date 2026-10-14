using System.Numerics;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

public class EurekaWeatherTests
{
    private static readonly EurekaZone Pyros = EurekaWeather.Zone(795)!;

    [Fact]
    public void Known_value_at_the_unix_epoch()
    {
        // By hand: hour 0 → increment 8, day 0 → seed 8; (8<<11)^8 = 16392; (16392>>8)^16392 = 16456.
        Assert.Equal(56, EurekaWeather.Value(DateTimeOffset.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public void Weather_only_changes_at_period_boundaries()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L / 1400 * 1400);
        var value = EurekaWeather.Value(start);
        Assert.Equal(value, EurekaWeather.Value(start.AddSeconds(1399)));
        var periods = EurekaWeather.Periods(Pyros, start.AddSeconds(700)).Take(3).ToList();
        Assert.Equal(start, periods[0].Start);
        Assert.Equal(periods[0].End, periods[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(1400), periods[2].End - periods[2].Start);
    }

    [Fact]
    public void Every_weather_of_every_zone_comes_back_within_a_week()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);
        foreach (var zone in EurekaWeather.Zones)
            foreach (var (_, weather) in zone.Slices)
                Assert.Equal(weather, EurekaWeather.Next(zone, weather, now)!.Weather);
    }

    [Fact]
    public void Slices_at_the_bounds()
    {
        Assert.Equal("Fair Skies", Pyros.WeatherFor(0));
        Assert.Equal("Fair Skies", Pyros.WeatherFor(9));
        Assert.Equal("Heat Waves", Pyros.WeatherFor(10));
        Assert.Equal("Snow", Pyros.WeatherFor(99));
    }

    [Fact]
    public void Eorzea_time_and_night()
    {
        // 175 real seconds = one Eorzea hour; the unix epoch falls at midnight.
        Assert.Equal((0, 0), EurekaWeather.EorzeaTime(DateTimeOffset.FromUnixTimeSeconds(0)));
        Assert.Equal((6, 0), EurekaWeather.EorzeaTime(DateTimeOffset.FromUnixTimeSeconds(6 * 175)));
        Assert.True(EurekaWeather.IsNight(DateTimeOffset.FromUnixTimeSeconds(5 * 175)));
        Assert.False(EurekaWeather.IsNight(DateTimeOffset.FromUnixTimeSeconds(6 * 175)));
        Assert.True(EurekaWeather.IsNight(DateTimeOffset.FromUnixTimeSeconds(19 * 175)));
    }

    [Theory]
    [InlineData(0, false, 6)]         // midnight → day at 6:00
    [InlineData(6 * 175, true, 19)]   // 6:00 sharp → night at 19:00
    [InlineData(19 * 175, false, 30)] // 19:00 → day at 6:00 the next day
    public void Next_day_night_switch(long seconds, bool night, int eorzeaHour)
    {
        var (n, when) = EurekaWeather.NextDayNightSwitch(DateTimeOffset.FromUnixTimeSeconds(seconds));
        Assert.Equal(night, n);
        Assert.Equal(eorzeaHour * 175L, when.ToUnixTimeSeconds());
    }
}

public class ChatParserTests
{
    private static readonly string[] NoFate = [];

    [Theory]
    [InlineData("tracker https://ffxiv-eureka.com/bZFR-s merci", "bZFR-s")]
    [InlineData("ffxiv-eureka.com/v/aB3_x9 ", "aB3_x9")]
    [InlineData("pas de lien ici", null)]
    public void Finds_tracker_links(string text, string? expected) =>
        Assert.Equal(expected, ChatParser.Parse(text, NoFate).Tracker);

    [Theory]
    [InlineData("Skoll pop X: 12.3 Y: 14.5", 12.3f, 14.5f)]
    [InlineData("pop (21.4, 26.1)", 21.4f, 26.1f)]
    [InlineData("pop x 8,5 y 30", 8.5f, 30f)]
    public void Reads_coordinates(string text, float x, float y)
    {
        var info = ChatParser.Parse(text, NoFate);
        Assert.True(info.Pop);
        Assert.Equal(new Vector2(x, y), info.Position);
    }

    [Fact]
    public void Ignores_off_map_coordinates_and_the_tracker_link()
    {
        Assert.Null(ChatParser.Parse("pop (80, 12)", NoFate).Position);
        Assert.Null(ChatParser.Parse("ffxiv-eureka.com/X12Y14", NoFate).Position);
    }

    [Fact]
    public void Recognizes_a_running_fate_by_its_name()
    {
        var info = ChatParser.Parse("go go Ice Scream !!", ["Ice Scream", "Eternity"]);
        Assert.True(info.Pop);
        Assert.Equal("Ice Scream", info.Fate);
    }

    [Theory]
    [InlineData("level up enfin")]
    [InlineData("population de lapins")]
    public void No_false_pop(string text) => Assert.False(ChatParser.Parse(text, NoFate).Pop);
}

public class FateTrackerTests
{
    private static SeenFate Fate(ushort id, float x = 0, float z = 0) => new(id, $"F{id}", new Vector3(x, 10, z));

    [Fact]
    public void Only_reports_new_ones_and_not_those_already_there_on_arrival()
    {
        var tracker = new FateTracker();
        Assert.Empty(tracker.Update([Fate(1)]));
        Assert.Empty(tracker.Update([Fate(1)]));
        Assert.Equal([2], tracker.Update([Fate(1), Fate(2)]).Select(f => (int)f.Id));
        Assert.Empty(tracker.Update([Fate(2)]));

        tracker.Reset();
        Assert.Empty(tracker.Update([Fate(3)]));
    }

    [Fact]
    public void Finds_the_nearest_fate_in_the_plane()
    {
        var tracker = new FateTracker();
        tracker.Update([Fate(1, 0, 0), Fate(2, 100, 0)]);
        Assert.Equal(2, tracker.Nearest(new Vector3(90, -50, 5), 60)!.Id);
        Assert.Null(tracker.Nearest(new Vector3(50, 0, 200), 60));
    }

    [Theory]
    [InlineData(4, true)]   // We're All Mad Here, Uncommon Nonsense… (Fate sheet, Rule column)
    [InlineData(1, false)]  // NM FATE
    [InlineData(5, false)]  // Color My World (Anemos)
    public void Recognizes_bunny_fates_by_their_rule(byte rule, bool bunny) =>
        Assert.Equal(bunny, FateTracker.IsBunnyFate(rule));

    [Fact]
    public void Remembers_fates_seen_ending_and_forgets_them_when_they_come_back()
    {
        var tracker = new FateTracker();
        var t0 = new DateTime(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc);
        tracker.Update([Fate(1), Fate(2)], t0);           // already there on arrival
        tracker.Update([Fate(2)], t0.AddSeconds(5));        // #1 ends
        Assert.Equal(t0.AddSeconds(5), tracker.Ended[1]);
        Assert.False(tracker.Ended.ContainsKey(2));

        tracker.Update([Fate(1), Fate(2)], t0.AddHours(2)); // #1 comes back
        Assert.False(tracker.Ended.ContainsKey(1));

        tracker.Reset();
        Assert.Empty(tracker.Ended);
    }
}

public class MapGeometryTests
{
    private static readonly MapGeometry Pyros = new(100, 0, 0);

    [Fact]
    public void A_game_point_lands_on_the_wiki_coordinates()
    {
        // Firing Chamber aetheryte: MapMarker 335 (pixels 1151, 827); the wiki says X 23.8 Y 17.0.
        var coordinates = Pyros.Coordinates(new Vector2(1151, 827));
        Assert.Equal(24.02f, coordinates.X, 2);
        Assert.Equal(17.54f, coordinates.Y, 2);
    }

    [Fact]
    public void The_world_center_is_the_texture_center()
    {
        Assert.Equal(new Vector2(1024, 1024), Pyros.Pixel(Vector3.Zero));
        Assert.Equal(new Vector2(1024, 1024), new MapGeometry(200, 0, 0).Pixel(Vector3.Zero));
        Assert.Equal(new Vector2(1024, 1024 + 475), new MapGeometry(100, 0, 475).Pixel(Vector3.Zero)); // Hydatos
    }

    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(200, 0, -754)]
    public void Round_trip_world_pixel_coordinates(ushort scale, short dx, short dy)
    {
        var g = new MapGeometry(scale, dx, dy);
        var world = new Vector3(-123.5f, 40, 287.25f);
        var pixel = g.Pixel(world);
        var back = g.World(pixel);
        Assert.Equal(world.X, back.X, 3);
        Assert.Equal(world.Z, back.Z, 3);
        Assert.True(Vector2.Distance(pixel, g.PixelFromCoordinates(g.Coordinates(pixel))) < 1e-3f);
    }

    [Fact]
    public void Same_formula_as_the_game_map_link()
    {
        // Dalamud's MapUtil: 0.02 × offset + 2048 / scale + 0.02 × value + 1.
        var g = new MapGeometry(200, 0, -754);
        var world = new Vector3(310f, 0, -95f);
        var expected = (0.02f * -754) + (2048f / 200) + (0.02f * -95f) + 1f;
        Assert.Equal(expected, g.Coordinates(g.Pixel(world)).Y, 3);
    }
}
