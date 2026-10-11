using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka.Logos;

/// <summary>
/// Lit les actions Logos apprises dans le journal des actions Logos (<c>EurekaMagiaActionNotebook</c>,
/// ouvert en parlant à Drake). Relevé en jeu le 2026-10-11 : les 56 premières valeurs de la fenêtre
/// sont une par action, son numéro (1 à 56) si elle est apprise, 0 sinon ; viennent ensuite les icônes.
/// </summary>
public static unsafe class LecteurJournal
{
    public const string Fenetre = "EurekaMagiaActionNotebook";

    /// <summary>Les actions apprises (lignes de la feuille Action), ou null si la fenêtre est fermée ou incohérente.</summary>
    public static HashSet<uint>? Lire(nint adresseFenetre, CatalogueLogos catalogue)
    {
        var fenetre = (AtkUnitBase*)adresseFenetre;
        if (fenetre == null || !fenetre->IsVisible) return null;
        var valeurs = new List<long>();
        foreach (var v in fenetre->AtkValuesSpan)
            valeurs.Add(v.Type switch { AtkValueType.UInt => v.UInt, AtkValueType.Int => v.Int, _ => -1 });
        return Decoder(valeurs, catalogue);
    }

    public static HashSet<uint>? Decoder(IReadOnlyList<long> valeurs, CatalogueLogos catalogue)
    {
        var nombre = catalogue.Actions.Count;
        if (valeurs.Count < nombre) return null;
        var parNumero = catalogue.Actions.ToDictionary(a => (long)a.Numero, a => a.Action);
        var apprises = new HashSet<uint>();
        for (var i = 0; i < nombre; i++)
        {
            var v = valeurs[i];
            if (v == 0) continue;
            if (!parNumero.TryGetValue(v, out var action)) return null;
            apprises.Add(action);
        }
        return apprises;
    }
}
