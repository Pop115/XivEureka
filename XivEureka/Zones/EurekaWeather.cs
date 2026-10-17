using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Zones;

/// <summary>A Eureka zone: its TerritoryType row, display name and weather slices (value 0-99 → weather).</summary>
public sealed record EurekaZone(uint Territory, string FrenchName, IReadOnlyList<(int UpTo, string Weather)> Slices)
{
    /// <summary>The zone's name in the plugin language (only Anemos differs).</summary>
    public string Name => Loc.T(FrenchName, FrenchName.Replace('é', 'e'));

    public string WeatherFor(int value) => Slices.First(s => value <= s.UpTo).Weather;
}

/// <summary>A weather period: from when to when (real time), and which weather.</summary>
public sealed record WeatherPeriod(DateTimeOffset Start, DateTimeOffset End, string Weather);

/// <summary>
/// Eorzea time and Eureka weather, computed locally. Public algorithm, the same in the
/// ffxiv-eureka.com JS and in EurekaHelper: an Eorzea hour lasts 175 s, a weather holds for 8 Eorzea
/// hours (1400 s). The slices are ffxiv-eureka.com's (English names: they are also the keys of the
/// NM spawn conditions).
/// </summary>
public static class EurekaWeather
{
    public const int SecondsPerHour = 175;
    public const int SecondsPerPeriod = 8 * SecondsPerHour;

    public static readonly IReadOnlyList<EurekaZone> Zones =
    [
        new(732, "Anémos", [(29, "Fair Skies"), (59, "Gales"), (89, "Showers"), (99, "Snow")]),
        new(763, "Pagos", [(9, "Fair Skies"), (27, "Fog"), (45, "Heat Waves"), (63, "Snow"), (81, "Thunder"), (99, "Blizzards")]),
        new(795, "Pyros", [(9, "Fair Skies"), (27, "Heat Waves"), (45, "Thunder"), (63, "Blizzards"), (81, "Umbral Wind"), (99, "Snow")]),
        new(827, "Hydatos", [(11, "Fair Skies"), (33, "Showers"), (55, "Gloom"), (77, "Thunderstorms"), (99, "Snow")]),
    ];

    public static EurekaZone? Zone(uint territory) => Zones.FirstOrDefault(z => z.Territory == territory);

    /// <summary>Eorzea hour (0-23) and minute at the given instant.</summary>
    public static (int Hour, int Minute) EorzeaTime(DateTimeOffset instant)
    {
        var seconds = instant.ToUnixTimeSeconds();
        var minutes = seconds * 60 / SecondsPerHour;
        return ((int)(minutes / 60 % 24), (int)(minutes % 60));
    }

    /// <summary>Eorzea night as NMs see it: outside 6:00-19:00, as on ffxiv-eureka.com.</summary>
    public static bool IsNight(DateTimeOffset instant)
    {
        var (hour, _) = EorzeaTime(instant);
        return hour < 6 || hour >= 19;
    }

    /// <summary>
    /// Next Eorzea switch after the given instant, day → night (19:00) or night → day (6:00);
    /// <c>Night</c> says what starts then.
    /// </summary>
    public static (bool Night, DateTimeOffset When) NextDayNightSwitch(DateTimeOffset instant)
    {
        const long day = 24 * SecondsPerHour;
        var seconds = instant.ToUnixTimeSeconds();
        var dayStart = seconds - (seconds % day);
        var intoDay = seconds % day;
        long dawn = 6 * SecondsPerHour, dusk = 19 * SecondsPerHour;
        var (night, when) = intoDay < dawn ? (false, dayStart + dawn)
            : intoDay < dusk ? (true, dayStart + dusk)
            : (false, dayStart + day + dawn);
        return (night, DateTimeOffset.FromUnixTimeSeconds(when));
    }

    /// <summary>The 0-99 value that picks the weather of the period containing the instant.</summary>
    public static int Value(DateTimeOffset instant)
    {
        var seconds = instant.ToUnixTimeSeconds();
        var hour = seconds / SecondsPerHour;
        var increment = (uint)((hour + 8 - (hour % 8)) % 24);
        var days = (uint)(seconds / 4200);
        var seed = days * 100 + increment;
        var step1 = (seed << 11) ^ seed;
        var step2 = (step1 >> 8) ^ step1;
        return (int)(step2 % 100);
    }

    /// <summary>The current period, then the following ones.</summary>
    public static IEnumerable<WeatherPeriod> Periods(EurekaZone zone, DateTimeOffset from)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(from.ToUnixTimeSeconds() / SecondsPerPeriod * SecondsPerPeriod);
        while (true)
        {
            var end = start.AddSeconds(SecondsPerPeriod);
            yield return new WeatherPeriod(start, end, zone.WeatherFor(Value(start)));
            start = end;
        }
    }

    /// <summary>Next period (current one included) with this weather, searched over one real week at most.</summary>
    public static WeatherPeriod? Next(EurekaZone zone, string weather, DateTimeOffset from) =>
        Periods(zone, from).TakeWhile(p => p.Start < from.AddDays(7)).FirstOrDefault(p => p.Weather == weather);
}
