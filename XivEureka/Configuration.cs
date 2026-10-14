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

/// <summary>Le tracker suivi : son identifiant, la zone où on l'a suivi, et depuis quand (UTC).</summary>
[Serializable]
public sealed class TrackerSauve
{
    public string Id { get; set; } = string.Empty;
    public uint Territoire { get; set; }
    public DateTime Depuis { get; set; }
}

/// <summary>Ce que l'onglet Carte affiche (cases « Calques »).</summary>
[Serializable]
public sealed class CalquesCarte
{
    public bool Reperes { get; set; } = true;
    public bool NomsDeZone { get; set; } = true;
    public bool Groupe { get; set; } = true;
    public bool Fates { get; set; } = true;
    public bool Pops { get; set; } = true;
    public bool Nm { get; set; } = true;
    public bool Prepa { get; set; } = true;
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Un état par personnage (ContentId), pour les joueurs qui en ont plusieurs à Eurêka.</summary>
    public Dictionary<ulong, EtatPersonnage> Personnages { get; set; } = [];

    /// <summary>Ouvrir la fenêtre Logos quand on ouvre le manipulateur Logos en jeu.</summary>
    public bool OuvrirAvecLeManipulateur { get; set; } = true;

    /// <summary>Annoncer (toast, chat avec lien de carte) chaque nouvelle FATE dans une zone d'Eurêka.</summary>
    public bool AlerterFates { get; set; } = true;

    /// <summary>Poser le drapeau de la carte du jeu sur une nouvelle FATE.</summary>
    public bool DrapeauSurFate { get; set; } = true;

    /// <summary>Lire le chat d'Eurêka (cris, LS, CWLS…) pour les annonces de pop et les liens de tracker.</summary>
    public bool EcouterChat { get; set; } = true;

    /// <summary>Poser le drapeau sur la position d'un pop annoncé dans le chat.</summary>
    public bool DrapeauSurChat { get; set; } = true;

    /// <summary>Son du jeu avec les alertes.</summary>
    public bool SonAlertes { get; set; } = true;

    /// <summary>Calques de l'onglet Carte.</summary>
    public CalquesCarte Calques { get; set; } = new();

    /// <summary>Zone d'Eurêka montrée par l'onglet Carte hors d'Eurêka : la dernière visitée (Pyros au départ).</summary>
    public uint DerniereZone { get; set; } = 795;

    /// <summary>Suivre en direct le tracker ffxiv-eureka.com crié dans le chat de la zone.</summary>
    public bool SuivreTrackerCrie { get; set; } = true;

    /// <summary>Sans tracker crié ni choisi après quelques minutes dans la zone, en créer un.</summary>
    public bool CreerTrackerAuto { get; set; } = true;

    /// <summary>Sur un tracker dont on a le mot de passe (créé par le plugin), signaler les NM tués.</summary>
    public bool SignalerMorts { get; set; } = true;

    /// <summary>Tracker suivi dans la zone en cours, pour le reprendre si le plugin redémarre sur place.</summary>
    public TrackerSauve? TrackerEnCours { get; set; }

    /// <summary>Mots de passe des trackers créés par le plugin (identifiant → mot de passe), pour y écrire.</summary>
    public Dictionary<string, string> MotsDePasseTrackers { get; set; } = [];

    /// <summary>Dernier tracker ffxiv-eureka.com vu dans le chat (identifiant, ex. « bZFR-s »).</summary>
    public string? DernierTracker { get; set; }

    public EtatPersonnage Personnage(ulong contentId, string nom)
    {
        if (!Personnages.TryGetValue(contentId, out var etat))
            Personnages[contentId] = etat = new EtatPersonnage();
        if (!string.IsNullOrEmpty(nom)) etat.Nom = nom;
        return etat;
    }
}
