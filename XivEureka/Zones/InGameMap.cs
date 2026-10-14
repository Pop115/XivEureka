using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivEureka.Zones;

/// <summary>
/// What the plugin does with the game's own map: flag, opening the map, map link in the chat.
/// Recipe from KangasZ/EurekaHelper (Apache-2.0): AgentMap.SetFlagMapMarker expects world
/// coordinates (X and Z), not the coordinates displayed on the map.
/// </summary>
public static unsafe class InGameMap
{
    /// <summary>Sets the flag at a world position; opens the map if asked.</summary>
    public static void SetFlag(uint territory, uint map, Vector3 world, bool open)
    {
        var agent = AgentMap.Instance();
        if (agent == null) return;
        agent->FlagMarkerCount = 0;
        agent->SetFlagMapMarker(territory, map, world.X, world.Z);
        if (open) agent->OpenMap(map, territory, null, MapType.FlagMarker);
    }

    /// <summary>World position (Y = 0) of a map link.</summary>
    public static Vector3 World(MapLinkPayload link) => new(link.RawX / 1000f, 0, link.RawY / 1000f);

    /// <summary>World position of map coordinates ("X: 12.3 Y: 14.5").</summary>
    public static Vector3 World(uint territory, uint map, Vector2 coordinates) =>
        World(new MapLinkPayload(territory, map, coordinates.X, coordinates.Y));

    /// <summary>Clickable map link for the chat, at a world position.</summary>
    public static SeString Link(uint territory, uint map, Vector3 world) =>
        SeString.CreateMapLink(territory, map, (int)(world.X * 1000), (int)(world.Z * 1000));
}
