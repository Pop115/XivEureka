using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace XivEureka;

/// <summary>Ce que le plugin retient d'un personnage : son dernier stock lu et ses actions apprises.</summary>
[Serializable]
public sealed class EtatPersonnage
{
    public string Nom { get; set; } = string.Empty;

    /// <summary>Mnemes possédées (numéro d'objet → quantité), au dernier passage au manipulateur.</summary>
    public Dictionary<uint, int> Stock { get; set; } = [];

    /// <summary>Quand <see cref="Stock"/> a été lu dans le jeu ; null tant qu'il ne l'a jamais été.</summary>
    public DateTime? StockLuLe { get; set; }

    /// <summary>
    /// Actions Logos apprises (ligne de la feuille Action). Lues dans le journal des actions Logos
    /// quand il est ouvert ; cochables à la main entre deux passages.
    /// </summary>
    public HashSet<uint> Apprises { get; set; } = [];

    /// <summary>Quand <see cref="Apprises"/> a été lu dans le journal ; null s'il ne l'a jamais été.</summary>
    public DateTime? ApprisesLuesLe { get; set; }
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Un état par personnage (ContentId), pour les joueurs qui en ont plusieurs à Eurêka.</summary>
    public Dictionary<ulong, EtatPersonnage> Personnages { get; set; } = [];

    /// <summary>Ouvrir la fenêtre Logos quand on ouvre le manipulateur Logos en jeu.</summary>
    public bool OuvrirAvecLeManipulateur { get; set; } = true;

    public EtatPersonnage Personnage(ulong contentId, string nom)
    {
        if (!Personnages.TryGetValue(contentId, out var etat))
            Personnages[contentId] = etat = new EtatPersonnage();
        if (!string.IsNullOrEmpty(nom)) etat.Nom = nom;
        return etat;
    }
}
