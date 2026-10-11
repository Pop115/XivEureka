using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka.Logos;

/// <summary>
/// Lit le stock de mnemes que le jeu prépare pour la fenêtre des éclats Logos
/// (<c>EurekaMagiciteItemShardList</c>). Le tableau n'est rempli que pendant que cette fenêtre
/// est ouverte ; hors de là il garde au mieux un ancien contenu, d'où <see cref="FenetreOuverte"/>.
///
/// Disposition relevée en jeu le 2026-10-11 : case 0 = nombre de mnemes différentes possédées,
/// puis 4 cases par mneme à partir de la case 3 : icône, quantité, numéro d'objet, ligne
/// EurekaMagiciteItem.
/// </summary>
public static unsafe class LecteurEclats
{
    public const string Fenetre = "EurekaMagiciteItemShardList";

    public static bool FenetreOuverte(nint adresseFenetre) =>
        adresseFenetre != 0 && ((AtkUnitBase*)adresseFenetre)->IsVisible;

    /// <summary>Le stock (objet → quantité), ou null si le tableau est absent ou incohérent.</summary>
    public static Dictionary<uint, int>? Lire()
    {
        var module = RaptureAtkModule.Instance();
        if (module == null) return null;
        var tableau = module->AtkArrayDataHolder.GetNumberArrayData((int)NumberArrayType.EurekaLogosShardList);
        if (tableau == null || tableau->IntArray == null) return null;
        return Decoder(tableau->Span);
    }

    /// <summary>Séparé de la lecture mémoire pour pouvoir le vérifier sur un relevé.</summary>
    public static Dictionary<uint, int>? Decoder(System.ReadOnlySpan<int> cases)
    {
        if (cases.Length < 1) return null;
        var nombre = cases[0];
        if (nombre < 0 || 3 + (4 * nombre) > cases.Length) return null;
        var stock = new Dictionary<uint, int>();
        for (var i = 0; i < nombre; i++)
        {
            var quantite = cases[4 + (4 * i)];
            var objet = cases[5 + (4 * i)];
            if (objet <= 0 || quantite < 0) return null;
            stock[(uint)objet] = quantite;
        }
        return stock;
    }
}
