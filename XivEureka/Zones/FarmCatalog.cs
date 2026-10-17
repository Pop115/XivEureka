using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Zones;

/// <summary>
/// A logogram source other than an NM: an elemental (sprite), a plain mob that drops it at random, a
/// mob whose lockbox holds it, or a bunny FATE whose chests hold it.
/// </summary>
public enum FarmKind
{
    Sprite,
    Mob,
    Lockbox,
    Bunny,
}

/// <summary>
/// A logogram a farm can give. <see cref="IfAtOrAboveYou"/>: only from mobs at or above the player's
/// elemental level. <see cref="Chest"/>: bunny chest tier. <see cref="Count"/>: how many when it
/// drops ("1-2"). <see cref="Chance"/>: chance per try (kill, bunny FATE) that it drops, chest tier
/// and adaptation included; null: the default of the farm kind (<see cref="Logos.HuntOptions"/>).
/// </summary>
public sealed record FarmDrop(uint Shard, bool IfAtOrAboveYou, string? Chest, string? Count, double? Chance = null);

/// <summary>
/// One farm: <see cref="Npc"/> (BNpcName row; 0 for a bunny FATE) or <see cref="Fate"/> (bunny FATE),
/// its level, whether it drops only once adapted, the weathers it needs (any of them; none: always
/// there), the Eorzea time it needs (<see cref="Time"/>: "day", "night" or null), where (map
/// coordinates), what it drops, a user-facing note (French for now), the minutes one try takes
/// (null: the default of the farm kind), and whether it drops once mutated (<see cref="Mutated"/>;
/// <see cref="Adapted"/> for an adaptation): the weather or time is then when the change happens.
/// </summary>
public sealed record Farm(FarmKind Kind, uint Npc, ushort Fate, int Level, bool Adapted, IReadOnlyList<string> Weathers,
    IReadOnlyList<Vector2> Places, IReadOnlyList<FarmDrop> Drops, string? Time = null, string? Note = null,
    double? Minutes = null, bool Mutated = false);

public sealed record ZoneFarms(uint Territory, IReadOnlyList<Farm> Farms);

/// <summary>The known logogram farms besides NMs, zone by zone, read from <c>Data/farms.json</c> (sources in the file).</summary>
public sealed class FarmCatalog(IReadOnlyList<ZoneFarms> zones)
{
    public IReadOnlyList<ZoneFarms> Zones => zones;

    public IReadOnlyList<Farm> Zone(uint territory) => zones.FirstOrDefault(z => z.Territory == territory)?.Farms ?? [];

    public static FarmCatalog Read(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<RawFile>(json, options) ?? throw new InvalidDataException("farms.json is empty");
        return new FarmCatalog(raw.Zones.Select(z => new ZoneFarms(z.Territory, z.Farms.Select(f => new Farm(
            Enum.Parse<FarmKind>(f.Kind, ignoreCase: true), f.Npc, f.Fate, f.Level, f.Adapted, f.Weathers ?? [],
            f.Places.Select(p => new Vector2(p[0], p[1])).ToList(),
            f.Drops.Select(d => new FarmDrop(d.Shard, d.IfAtOrAboveYou, d.Chest, d.Count, d.Chance)).ToList(), f.Time, f.Note,
            f.Minutes, f.Mutated)).ToList())).ToList());
    }

    public static FarmCatalog Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XivEureka.Data.farms.json")
            ?? throw new FileNotFoundException("farms.json resource missing from the DLL");
        return Read(stream);
    }

    private sealed record RawFile(List<RawZone> Zones);
    private sealed record RawZone(uint Territory, List<RawFarm> Farms);
    private sealed record RawFarm(string Kind, uint Npc, ushort Fate, int Level, bool Adapted, List<string>? Weathers,
        List<float[]> Places, List<RawDrop> Drops, string? Time, string? Note, double? Minutes, bool Mutated);
    private sealed record RawDrop(uint Shard, bool IfAtOrAboveYou, string? Chest, string? Count, double? Chance);
}
