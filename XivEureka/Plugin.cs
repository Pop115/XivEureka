using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka;

/// <summary>
/// Point d'entrée Dalamud. Pour l'instant : la commande de relevé qui écrit ce que le jeu expose
/// autour du manipulateur Logos, pour savoir où lire les mnemes et les actions apprises.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string Command = "/eureka";

    /// <summary>Fenêtres du jeu liées aux Logos (noms d'addon).</summary>
    private static readonly string[] LogosAddons =
    [
        "EurekaMagiciteItemShardList",  // liste des mnemes (éclats identifiés)
        "EurekaMagiciteItemSynthesis",  // manipulateur Logos
        "EurekaMagiaActionNotebook",    // journal des actions Logos
        "EurekaMagiaAction",            // actions Logos préparées
        "EurekaElementalHud",
    ];

    public Plugin()
    {
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "/eureka releve : écrit les données Logos lues dans le jeu (fenêtre des éclats ouverte).",
        });
    }

    public void Dispose() => CommandManager.RemoveHandler(Command);

    private void OnCommand(string command, string args)
    {
        if (!args.Trim().Equals("releve", StringComparison.OrdinalIgnoreCase))
        {
            ChatGui.Print("Usage : /eureka releve");
            return;
        }
        try
        {
            var path = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "releve.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Releve(), new JsonSerializerOptions { WriteIndented = true }));
            ChatGui.Print($"Relevé écrit : {path}");
        }
        catch (Exception e)
        {
            Log.Error(e, "Relevé impossible");
            ChatGui.PrintError($"Relevé impossible : {e.Message}");
        }
    }

    private static unsafe Dictionary<string, object> Releve()
    {
        var holder = &RaptureAtkModule.Instance()->AtkArrayDataHolder;
        var result = new Dictionary<string, object> { ["date"] = DateTime.Now.ToString("s") };

        foreach (var (name, index) in new[] { ("NumberArray EurekaLogosShardList", 137), ("NumberArray EurekaLogosAetherList", 138), ("NumberArray 139", 139) })
        {
            var a = holder->GetNumberArrayData(index);
            result[name] = a == null ? "absent" : a->Span.ToArray();
        }
        foreach (var (name, index) in new[] { ("StringArray EurekaLogosShardList", 116) })
        {
            var a = holder->GetStringArrayData(index);
            if (a == null) { result[name] = "absent"; continue; }
            var texts = new List<string>();
            for (var i = 0; i < a->AtkArrayData.Size; i++)
                texts.Add(a->StringArray[i].HasValue ? a->StringArray[i].ToString() : "");
            result[name] = texts;
        }
        foreach (var addon in LogosAddons)
        {
            var unit = (AtkUnitBase*)GameGui.GetAddonByName(addon, 1).Address;
            if (unit == null) { result["Addon " + addon] = "fermé"; continue; }
            var values = new List<string>();
            foreach (var v in unit->AtkValuesSpan)
                values.Add(Valeur(v));
            result["Addon " + addon] = new Dictionary<string, object> { ["visible"] = unit->IsVisible, ["valeurs"] = values };
        }
        return result;
    }

    private static unsafe string Valeur(AtkValue v) => v.Type switch
    {
        AtkValueType.Bool => "b:" + v.Bool,
        AtkValueType.Int => "i:" + v.Int,
        AtkValueType.UInt => "u:" + v.UInt,
        AtkValueType.Int64 => "l:" + v.Int64,
        AtkValueType.Float => "f:" + v.Float,
        AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString => "s:" + v.String.ToString(),
        _ => "?" + (int)v.Type,
    };
}
