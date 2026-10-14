using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivEureka.Zones;

/// <summary>A running FATE as the map sees it: where, how far along, how long left.</summary>
public sealed record MapFate(ushort Id, string Name, Vector3 Position, float Radius, byte Progress, TimeSpan Remaining,
    uint Icon, bool Preparing);

/// <summary>A pop announced in the chat, kept a few minutes on the map.</summary>
public sealed record ChatPop(string Title, Vector3 Position, DateTime When);

/// <summary>
/// What the plugin map shows of the game at a given instant: the current map, the player (position
/// and rotation), the party, the game flag, the running FATEs and the chat pops.
/// </summary>
public sealed record MapState(uint Map, uint Territory, Vector3? Me, float Rotation,
    IReadOnlyList<Vector3> Party, Vector3? Flag, IReadOnlyList<MapFate> Fates, IReadOnlyList<ChatPop> Pops)
{
    public static readonly MapState Empty = new(0, 0, null, 0, [], null, [], []);
}

/// <summary>Reads the <see cref="MapState"/> from Dalamud services and the game map agent.</summary>
public sealed class MapStateReader(IClientState clientState, IObjectTable objects, IPartyList party, IFateTable fates,
    Func<IReadOnlyList<ChatPop>> pops)
{
    public MapState Read()
    {
        if (!clientState.IsLoggedIn) return MapState.Empty;
        var territory = clientState.TerritoryType;
        var map = clientState.MapId;
        var me = objects.LocalPlayer;

        var members = party
            .Where(m => m.Territory.RowId == territory && (me == null || m.EntityId != me.EntityId))
            .Select(m => m.Position)
            .ToList();

        var running = fates
            .Where(f => f.State is FateState.Preparing or FateState.Running)
            .Where(f => !FateTracker.IsBunnyFate(f.GameData.Value.Rule))
            .Select(f => new MapFate(f.FateId, f.Name.TextValue, f.Position, f.Radius, f.Progress,
                TimeSpan.FromSeconds(Math.Max(0, f.TimeRemaining)), f.MapIconId, f.State == FateState.Preparing))
            .ToList();

        return new MapState(map, territory, me?.Position, me?.Rotation ?? 0, members, Flag(map), running, pops());
    }

    /// <summary>The flag set on the game map, if it is on this map (world X and Z).</summary>
    private static unsafe Vector3? Flag(uint map)
    {
        var agent = AgentMap.Instance();
        if (agent == null || agent->FlagMarkerCount == 0) return null;
        var flag = agent->FlagMapMarkers[0];
        return flag.MapId == map ? new Vector3(flag.XFloat, 0, flag.YFloat) : null;
    }
}
