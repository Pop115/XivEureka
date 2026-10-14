using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Zones;

/// <summary>
/// A fixed point of the game map (MapMarker sheet): either an icon (aetheryte, NPC, landmark, with
/// its name if any) or an area name written on the map (<see cref="Icon"/> = 0).
/// </summary>
public sealed record MapPoint(uint Icon, Vector2 Pixel, string? Name)
{
    public bool IsAreaName => Icon == 0;
}

/// <summary>A game map ready to draw: its texture, geometry and fixed points.</summary>
public sealed record GameMap(uint Map, uint Territory, string Name, string Texture, MapGeometry Geometry,
    IReadOnlyList<MapPoint> Points);

/// <summary>Reads maps from the game sheets (Map, MapMarker), once per map.</summary>
public sealed class MapReader(IDataManager data)
{
    private readonly Dictionary<uint, GameMap?> cache = [];

    /// <summary>The map of a Map row; null if the row does not exist or has no texture.</summary>
    public GameMap? Map(uint map)
    {
        if (cache.TryGetValue(map, out var known)) return known;
        return cache[map] = Read(map);
    }

    /// <summary>A territory's main map (the first Map row pointing at it).</summary>
    public GameMap? MainMap(uint territory)
    {
        var row = data.GetExcelSheet<Map>().FirstOrDefault(m => m.TerritoryType.RowId == territory && !m.Id.IsEmpty);
        return row.RowId == 0 ? null : Map(row.RowId);
    }

    private GameMap? Read(uint rowId)
    {
        if (data.GetExcelSheet<Map>().GetRowOrDefault(rowId) is not { } map || map.Id.IsEmpty) return null;

        // "z3fc/00" → ui/map/z3fc/00/z3fc00_m.tex
        var id = map.Id.ExtractText();
        var texture = $"ui/map/{id}/{id.Replace("/", string.Empty)}_m.tex";
        if (!data.FileExists(texture)) return null;

        var points = new List<MapPoint>();
        if (data.GetSubrowExcelSheet<MapMarker>().GetRowOrDefault(map.MapMarkerRange) is { } markers)
        {
            foreach (var m in markers)
            {
                if (m.X == 0 && m.Y == 0) continue;
                var name = m.PlaceNameSubtext.ValueNullable?.Name.ExtractText();
                if (m.Icon == 0 && string.IsNullOrEmpty(name)) continue;
                points.Add(new MapPoint(m.Icon, new Vector2(m.X, m.Y), string.IsNullOrEmpty(name) ? null : name));
            }
        }

        var mapName = map.PlaceName.ValueNullable?.Name.ExtractText() ?? id;
        return new GameMap(rowId, map.TerritoryType.RowId, mapName, texture,
            new MapGeometry(map.SizeFactor, map.OffsetX, map.OffsetY), points);
    }
}
