using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Logos;

/// <summary>Name, icon and description of an item or action, as the game shows them.</summary>
public sealed record GameText(string Name, string ShortName, uint Icon, string Description);

/// <summary>
/// Texts of mnemes, shards, actions and NPCs, read once from the game sheets (in the client
/// language) and cached.
/// </summary>
public sealed class GameTexts(IDataManager data)
{
    private readonly Dictionary<uint, GameText> items = [];
    private readonly Dictionary<uint, GameText> actions = [];
    private readonly Dictionary<uint, string> npcs = [];

    public GameText Item(uint id)
    {
        if (items.TryGetValue(id, out var text)) return text;
        var row = data.GetExcelSheet<Item>().GetRowOrDefault(id);
        var name = row?.Name.ExtractText() ?? $"Objet {id}";
        return items[id] = new GameText(name, Shorten(name), row?.Icon ?? 0u, string.Empty);
    }

    public GameText Action(uint id)
    {
        if (actions.TryGetValue(id, out var text)) return text;
        var row = data.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id);
        var name = row?.Name.ExtractText() ?? $"Action {id}";
        var description = data.GetExcelSheet<ActionTransient>().GetRowOrDefault(id)?.Description.ExtractText() ?? string.Empty;
        return actions[id] = new GameText(name, Shorten(name), row?.Icon ?? 0u, description);
    }

    /// <summary>Name of an NPC or monster (BNpcName sheet), capitalized: "shuck Pyros" → "Shuck Pyros".</summary>
    public string Npc(uint id)
    {
        if (npcs.TryGetValue(id, out var name)) return name;
        var raw = data.GetExcelSheet<BNpcName>().GetRowOrDefault(id)?.Singular.ExtractText();
        return npcs[id] = string.IsNullOrEmpty(raw) ? $"Monstre {id}"
            : char.ToUpper(raw[0], CultureInfo.CurrentCulture) + raw[1..];
    }

    /// <summary>Drops the French client's " non identifié" suffix from an unidentified shard's name.</summary>
    public static string WithoutUnidentified(string name) =>
        name.EndsWith(" non identifié", StringComparison.Ordinal) ? name[..^" non identifié".Length] : name;

    /// <summary>
    /// "Évocation Logos : Bouclier" → "Bouclier": the shared prefix does not help tell the cells
    /// apart. Names without a colon stay whole.
    /// </summary>
    public static string Shorten(string name)
    {
        var i = name.LastIndexOf(':');
        return i >= 0 && i < name.Length - 1 ? name[(i + 1)..].Trim() : name;
    }
}
