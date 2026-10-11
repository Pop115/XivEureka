using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>Ce qu'une recette donne avec un stock : combien de fois on peut la faire, combien de mnemes manquent.</summary>
public sealed record EtatRecette(IReadOnlyList<uint> Recette, IReadOnlyDictionary<uint, int> Besoins, int Possibles, int Manquantes);

/// <summary>Où en est une action : apprise ou non, fabricable combien de fois, et la recette la plus proche.</summary>
public sealed record EtatAction(ActionLogos Action, bool Apprise, int Possibles, EtatRecette PlusProche, IReadOnlyList<EtatRecette> Recettes)
{
    /// <summary>Pas encore apprise et fabricable tout de suite : ce qu'on cherche en priorité.</summary>
    public bool AApprendre => !Apprise && Possibles > 0;
}

/// <summary>Le calcul, sans rien du jeu : un stock de mnemes et une liste d'actions apprises.</summary>
public static class Fabrication
{
    public static EtatRecette Recette(IReadOnlyList<uint> recette, IReadOnlyDictionary<uint, int> stock)
    {
        var besoins = recette.GroupBy(m => m).ToDictionary(g => g.Key, g => g.Count());
        var possibles = int.MaxValue;
        var manquantes = 0;
        foreach (var (mneme, besoin) in besoins)
        {
            var dispo = stock.GetValueOrDefault(mneme);
            possibles = Math.Min(possibles, dispo / besoin);
            manquantes += Math.Max(0, besoin - dispo);
        }
        return new EtatRecette(recette, besoins, possibles == int.MaxValue ? 0 : possibles, manquantes);
    }

    /// <summary>
    /// Les recettes d'une action se partagent le même stock : on ne peut pas les additionner, d'où
    /// le maximum (« jusqu'à × N »).
    /// </summary>
    public static EtatAction Action(ActionLogos action, IReadOnlyDictionary<uint, int> stock, IReadOnlySet<uint> apprises)
    {
        var recettes = action.Recettes.Select(r => Recette(r, stock)).ToList();
        var plusProche = recettes.OrderBy(r => r.Manquantes).ThenBy(r => r.Recette.Count).First();
        return new EtatAction(action, apprises.Contains(action.Action), recettes.Max(r => r.Possibles), plusProche, recettes);
    }

    /// <summary>Pour chaque mneme, combien d'actions pas encore apprises s'en servent.</summary>
    public static Dictionary<uint, int> Utilite(IEnumerable<ActionLogos> actions, IReadOnlySet<uint> apprises)
    {
        var utilite = new Dictionary<uint, int>();
        foreach (var action in actions.Where(a => !apprises.Contains(a.Action)))
            foreach (var mneme in action.Recettes.SelectMany(r => r).Distinct())
                utilite[mneme] = utilite.GetValueOrDefault(mneme) + 1;
        return utilite;
    }
}
