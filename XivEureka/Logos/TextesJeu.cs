using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Logos;

/// <summary>Nom, icône et description d'un objet ou d'une action, tels que le jeu les affiche.</summary>
public sealed record TexteJeu(string Nom, string NomCourt, uint Icone, string Description);

/// <summary>
/// Les textes des mnemes, éclats et actions, lus une fois dans les feuilles du jeu (dans la langue
/// du client) et gardés en cache.
/// </summary>
public sealed class TextesJeu(IDataManager donnees)
{
    private readonly Dictionary<uint, TexteJeu> objets = [];
    private readonly Dictionary<uint, TexteJeu> actions = [];

    public TexteJeu Objet(uint id)
    {
        if (objets.TryGetValue(id, out var texte)) return texte;
        var ligne = donnees.GetExcelSheet<Item>().GetRowOrDefault(id);
        var nom = ligne?.Name.ExtractText() ?? $"Objet {id}";
        return objets[id] = new TexteJeu(nom, Raccourcir(nom), ligne?.Icon ?? 0u, string.Empty);
    }

    public TexteJeu Action(uint id)
    {
        if (actions.TryGetValue(id, out var texte)) return texte;
        var ligne = donnees.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id);
        var nom = ligne?.Name.ExtractText() ?? $"Action {id}";
        var description = donnees.GetExcelSheet<ActionTransient>().GetRowOrDefault(id)?.Description.ExtractText() ?? string.Empty;
        return actions[id] = new TexteJeu(nom, Raccourcir(nom), ligne?.Icon ?? 0u, description);
    }

    /// <summary>
    /// « Évocation Logos : Bouclier » → « Bouclier » : le préfixe commun n'aide pas à distinguer
    /// les cases. Les noms sans deux-points restent entiers.
    /// </summary>
    public static string Raccourcir(string nom)
    {
        var i = nom.LastIndexOf(':');
        return i >= 0 && i < nom.Length - 1 ? nom[(i + 1)..].Trim() : nom;
    }
}
