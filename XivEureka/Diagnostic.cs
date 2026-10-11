using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka;

/// <summary>
/// Relevés bruts de ce que le jeu expose, pour trouver où lire une information avant de s'en
/// servir (ex. : les actions Logos apprises). Écrits dans le dossier de configuration du plugin,
/// sous <c>releves/</c>. Outil de développement : il ne sert à rien en usage normal.
/// </summary>
public static unsafe class Diagnostic
{
    private static readonly JsonSerializerOptions Indente = new() { WriteIndented = true };

    /// <summary>Écrit le relevé d'une fenêtre du jeu (ses valeurs) et des tableaux Logos connus.</summary>
    public static string Ecrire(string dossierConfig, string nom, AtkUnitBase* fenetre)
    {
        var releve = new Dictionary<string, object> { ["date"] = DateTime.Now.ToString("s"), ["fenetre"] = nom };
        if (fenetre != null)
        {
            var valeurs = new List<string>();
            foreach (var v in fenetre->AtkValuesSpan) valeurs.Add(Valeur(v));
            releve["visible"] = fenetre->IsVisible;
            releve["valeurs"] = valeurs;
        }
        var tableaux = &RaptureAtkModule.Instance()->AtkArrayDataHolder;
        foreach (var index in new[] { 137, 138 })
        {
            var a = tableaux->GetNumberArrayData(index);
            releve[$"NumberArray {index}"] = a == null || a->IntArray == null ? "absent" : a->Span.ToArray();
        }
        var dossier = Path.Combine(dossierConfig, "releves");
        Directory.CreateDirectory(dossier);
        var chemin = Path.Combine(dossier, $"{nom}.json");
        File.WriteAllText(chemin, JsonSerializer.Serialize(releve, Indente));
        return chemin;
    }

    private static string Valeur(AtkValue v) => v.Type switch
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
