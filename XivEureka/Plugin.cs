using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivEureka.Fenetres;
using XivEureka.Logos;
using XivEureka.Zones;

namespace XivEureka;

/// <summary>
/// Point d'entrée Dalamud : branche les fenêtres, la commande et la lecture du stock de mnemes.
/// Le calcul (quoi fabriquer) est dans <see cref="Fabrication"/>, la lecture mémoire dans
/// <see cref="LecteurEclats"/>.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;

    private const string Commande = "/xiveureka";
    private const string FenetreManipulateur = "EurekaMagiciteItemSynthesis";
    private static readonly TimeSpan IntervalleLecture = TimeSpan.FromMilliseconds(500);

    private readonly Configuration configuration;
    private readonly CatalogueLogos catalogue;
    private readonly WindowSystem fenetres = new("XivEureka");
    private readonly FenetrePrincipale fenetre;
    private readonly FenetreReglages fenetreReglages;
    private readonly CompagnonZone compagnon;
    private DateTime prochaineLecture = DateTime.MinValue;
    private bool stockEnDirect;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        catalogue = CatalogueLogos.Integre();
        compagnon = new CompagnonZone(configuration, ClientState, Framework, FateTable, ChatGui, ToastGui, Log, Sauver);
        var textes = new TextesJeu(DataManager);
        var nomsMeteo = new NomsMeteo(DataManager);
        var logos = new OngletLogos(catalogue, textes, TextureProvider, EtatCourant, () => stockEnDirect, Sauver);
        var carte = new OngletCarte(configuration, new LecteurCartes(DataManager),
            new LecteurEtatCarte(ClientState, ObjectTable, PartyList, FateTable, compagnon.PopsRecents),
            CatalogueNm.Integre(),
            new NomsJeu(textes.Pnj, m => nomsMeteo[m], o => textes.Objet(o).Nom),
            TextureProvider,
            new SourcesCarte(compagnon.FatesTerminees, NiveauElementaire, EclatsUtiles,
                (territoire, map, position) => CarteJeu.Drapeau(territoire, map, position, ouvrir: false)),
            Sauver);
        fenetre = new FenetrePrincipale(logos, carte);
        fenetres.AddWindow(fenetre);
        fenetreReglages = new FenetreReglages(configuration, Sauver);
        fenetres.AddWindow(fenetreReglages);

        CommandManager.AddHandler(Commande, new CommandInfo(SurCommande)
        {
            HelpMessage = "Ouvre la fenêtre du plugin. « /xiveureka carte » : sur la carte. « /xiveureka releve » : relevé de développement.",
        });

        Framework.Update += SurTrame;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, FenetreManipulateur, SurManipulateurOuvert);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SurFenetreOuverte);

        PluginInterface.UiBuilder.Draw += fenetres.Draw;
        PluginInterface.UiBuilder.OpenMainUi += BasculerFenetre;
        PluginInterface.UiBuilder.OpenConfigUi += fenetreReglages.Toggle;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= fenetres.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= BasculerFenetre;
        PluginInterface.UiBuilder.OpenConfigUi -= fenetreReglages.Toggle;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, FenetreManipulateur, SurManipulateurOuvert);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SurFenetreOuverte);
        Framework.Update -= SurTrame;
        compagnon.Dispose();
        CommandManager.RemoveHandler(Commande);
        fenetres.RemoveAllWindows();
    }

    private EtatPersonnage? EtatCourant() =>
        PlayerState.IsLoaded && PlayerState.ContentId != 0
            ? configuration.Personnage(PlayerState.ContentId, PlayerState.CharacterName)
            : null;

    /// <summary>Éclats Logos qui feraient avancer des actions pas encore apprises (vide sans stock lu).</summary>
    private IReadOnlyDictionary<uint, int> EclatsUtiles()
    {
        var etat = EtatCourant();
        return etat?.StockLuLe == null ? new Dictionary<uint, int>() : Fabrication.EclatsUtiles(catalogue, etat.Stock, etat.Apprises);
    }

    /// <summary>
    /// Niveau élémentaire d'Eurêka : valeur de contenu n° 2 du joueur (recherche de l'étape 0, à
    /// valider en jeu). Null si la valeur n'est pas un niveau plausible.
    /// </summary>
    private static unsafe int? NiveauElementaire()
    {
        var etat = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (etat == null) return null;
        var niveau = (int)etat->GetContentValue(2);
        return niveau is >= 1 and <= 60 ? niveau : null;
    }

    /// <summary>Développement : les valeurs de contenu 0 à 9 du joueur, pour vérifier que la n° 2 est le niveau élémentaire.</summary>
    private static unsafe void AfficherValeursContenu()
    {
        var etat = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (etat == null) return;
        var valeurs = Enumerable.Range(0, 10).Select(i => $"{i}={etat->GetContentValue((uint)i)}");
        ChatGui.Print($"Valeurs de contenu : {string.Join(" ", valeurs)} (niveau élémentaire attendu en 2)", "Xiv Eurêka");
    }

    private void Sauver() => PluginInterface.SavePluginConfig(configuration);

    private void BasculerFenetre() => fenetre.Toggle();

    private void SurCommande(string commande, string arguments)
    {
        if (arguments.Trim().StartsWith("rel", StringComparison.OrdinalIgnoreCase))
        {
            EcrireReleve(LecteurEclats.Fenetre);
            return;
        }
        if (arguments.Trim().StartsWith("niv", StringComparison.OrdinalIgnoreCase))
        {
            AfficherValeursContenu();
            return;
        }
        if (arguments.Trim().StartsWith("carte", StringComparison.OrdinalIgnoreCase))
        {
            fenetre.Ouvrir(FenetrePrincipale.Onglet.Carte);
            return;
        }
        BasculerFenetre();
    }

    /// <summary>
    /// Deux fois par seconde : si la fenêtre des éclats est ouverte, relit le stock et l'enregistre
    /// quand il a changé. Ainsi la fenêtre du plugin reste juste même loin du manipulateur.
    /// </summary>
    private void SurTrame(IFramework trame)
    {
        if (DateTime.UtcNow < prochaineLecture) return;
        prochaineLecture = DateTime.UtcNow + IntervalleLecture;

        var etat = EtatCourant();
        if (etat == null) return;
        stockEnDirect = LecteurEclats.FenetreOuverte(GameGui.GetAddonByName(LecteurEclats.Fenetre, 1).Address);
        if (stockEnDirect) LireStock(etat);
        LireApprises(etat);
    }

    private void LireStock(EtatPersonnage etat)
    {
        var stock = LecteurEclats.Lire();
        if (stock == null) return;
        var change = stock.Count != etat.Stock.Count || stock.Any(s => etat.Stock.GetValueOrDefault(s.Key, -1) != s.Value);
        if (!change && etat.StockLuLe != null) return;
        etat.Stock = stock;
        etat.StockLuLe = DateTime.Now;
        Sauver();
    }

    /// <summary>Le journal des actions Logos fait foi : il remplace ce qui avait été coché à la main.</summary>
    private void LireApprises(EtatPersonnage etat)
    {
        var apprises = LecteurJournal.Lire(GameGui.GetAddonByName(LecteurJournal.Fenetre, 1).Address, catalogue);
        if (apprises == null) return;
        if (apprises.SetEquals(etat.Apprises) && etat.ApprisesLuesLe != null) return;
        etat.Apprises = apprises;
        etat.ApprisesLuesLe = DateTime.Now;
        Sauver();
    }

    private void SurManipulateurOuvert(AddonEvent type, AddonArgs args)
    {
        if (configuration.OuvrirAvecLeManipulateur) fenetre.Ouvrir(FenetrePrincipale.Onglet.Logos);
    }

    /// <summary>Relevé automatique de toute fenêtre d'Eurêka qui s'ouvre (développement).</summary>
    private void SurFenetreOuverte(AddonEvent type, AddonArgs args)
    {
        if (args.AddonName.StartsWith("Eureka", StringComparison.Ordinal)) EcrireReleve(args.AddonName);
    }

    private unsafe void EcrireReleve(string fenetre)
    {
        try
        {
            var chemin = Diagnostic.Ecrire(PluginInterface.GetPluginConfigDirectory(), fenetre,
                (AtkUnitBase*)GameGui.GetAddonByName(fenetre, 1).Address);
            Log.Information("Relevé écrit : {Chemin}", chemin);
        }
        catch (Exception e)
        {
            Log.Error(e, "Relevé impossible pour {Fenetre}", fenetre);
        }
    }
}
