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

    private const string Commande = "/xiveureka";
    private const string FenetreManipulateur = "EurekaMagiciteItemSynthesis";
    private static readonly TimeSpan IntervalleLecture = TimeSpan.FromMilliseconds(500);

    private readonly Configuration configuration;
    private readonly WindowSystem fenetres = new("XivEureka");
    private readonly FenetreLogos fenetreLogos;
    private DateTime prochaineLecture = DateTime.MinValue;
    private bool stockEnDirect;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        fenetreLogos = new FenetreLogos(CatalogueLogos.Integre(), new TextesJeu(DataManager), TextureProvider,
            EtatCourant, () => stockEnDirect, Sauver);
        fenetres.AddWindow(fenetreLogos);

        CommandManager.AddHandler(Commande, new CommandInfo(SurCommande)
        {
            HelpMessage = "Ouvre la fenêtre des actions Logos. « /xiveureka releve » : relevé de développement.",
        });

        Framework.Update += SurTrame;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, FenetreManipulateur, SurManipulateurOuvert);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SurFenetreOuverte);

        PluginInterface.UiBuilder.Draw += fenetres.Draw;
        PluginInterface.UiBuilder.OpenMainUi += BasculerFenetre;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= fenetres.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= BasculerFenetre;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, FenetreManipulateur, SurManipulateurOuvert);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SurFenetreOuverte);
        Framework.Update -= SurTrame;
        CommandManager.RemoveHandler(Commande);
        fenetres.RemoveAllWindows();
    }

    private EtatPersonnage? EtatCourant() =>
        PlayerState.IsLoaded && PlayerState.ContentId != 0
            ? configuration.Personnage(PlayerState.ContentId, PlayerState.CharacterName)
            : null;

    private void Sauver() => PluginInterface.SavePluginConfig(configuration);

    private void BasculerFenetre() => fenetreLogos.Toggle();

    private void SurCommande(string commande, string arguments)
    {
        if (arguments.Trim().StartsWith("rel", StringComparison.OrdinalIgnoreCase))
        {
            EcrireReleve(LecteurEclats.Fenetre);
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

        stockEnDirect = LecteurEclats.FenetreOuverte(GameGui.GetAddonByName(LecteurEclats.Fenetre, 1).Address);
        if (!stockEnDirect) return;
        var etat = EtatCourant();
        var stock = LecteurEclats.Lire();
        if (etat == null || stock == null) return;

        var change = stock.Count != etat.Stock.Count || stock.Any(s => etat.Stock.GetValueOrDefault(s.Key, -1) != s.Value);
        if (!change && etat.StockLuLe != null) return;
        etat.Stock = stock;
        etat.StockLuLe = DateTime.Now;
        Sauver();
    }

    private void SurManipulateurOuvert(AddonEvent type, AddonArgs args)
    {
        if (configuration.OuvrirAvecLeManipulateur) fenetreLogos.IsOpen = true;
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
