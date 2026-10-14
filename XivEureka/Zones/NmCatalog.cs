using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Zones;

/// <summary>
/// What makes an NM or its prep mob appear: a weather (English name, key of the
/// <see cref="EurekaWeather"/> slices) or the Eorzea night (no NM needs both). Neither: no condition.
/// </summary>
public sealed record SpawnCondition(string? Weather, bool Night)
{
    public bool None => Weather == null && !Night;
}

/// <summary>The mob to kill to make the NM appear, its level, its condition and where it lives (map coordinates).</summary>
public sealed record PrepMob(uint Npc, int Level, SpawnCondition Condition, IReadOnlyList<Vector2> Places);

/// <summary>
/// A Eureka NM. <see cref="Npc"/>: BNpcName row (name in the client language); <see cref="Site"/>:
/// its id on ffxiv-eureka.com; <see cref="Fate"/>: its FATE; <see cref="Position"/>: map
/// coordinates of the FATE; <see cref="Shard"/>: the logogram (unidentified Logos shard) it drops;
/// <see cref="Items"/>: its rare items.
/// </summary>
public sealed record EurekaNm(uint Npc, int Site, ushort Fate, int Level, string Element, Vector2 Position, uint Shard,
    IReadOnlyList<uint> Items, SpawnCondition Condition, PrepMob Prep);

/// <summary>A zone's NMs and their respawn delay.</summary>
public sealed record ZoneNms(uint Territory, int RespawnMinutes, IReadOnlyList<EurekaNm> Nms);

/// <summary>The known NMs, zone by zone, read from <c>Data/nm.json</c> (sources in the file).</summary>
public sealed class NmCatalog(IReadOnlyList<ZoneNms> zones)
{
    public ZoneNms? Zone(uint territory) => zones.FirstOrDefault(z => z.Territory == territory);

    public static NmCatalog Read(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<RawFile>(json, options) ?? throw new InvalidDataException("nm.json is empty");
        return new NmCatalog(raw.Zones.Select(z => new ZoneNms(z.Territory, z.RespawnMinutes, z.Nm.Select(n => new EurekaNm(
            n.Npc, n.Site, n.Fate, n.Level, n.Element, new Vector2(n.X, n.Y), n.Shard, n.Items,
            new SpawnCondition(n.Weather, n.Night),
            new PrepMob(n.Prep.Npc, n.Prep.Level, new SpawnCondition(n.Prep.Weather, n.Prep.Night),
                n.Prep.Places.Select(p => new Vector2(p[0], p[1])).ToList()))).ToList())).ToList());
    }

    public static NmCatalog Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XivEureka.Data.nm.json")
            ?? throw new FileNotFoundException("nm.json resource missing from the DLL");
        return Read(stream);
    }

    private sealed record RawFile(List<RawZone> Zones);
    private sealed record RawZone(uint Territory, int RespawnMinutes, List<RawNm> Nm);
    private sealed record RawNm(uint Npc, int Site, ushort Fate, int Level, string Element, float X, float Y, uint Shard,
        List<uint> Items, string? Weather, bool Night, RawPrep Prep);
    private sealed record RawPrep(uint Npc, int Level, string? Weather, bool Night, List<float[]> Places);
}
