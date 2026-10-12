using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace XivEureka.Zones;

/// <summary>
/// Ce que le plugin fait en jeu sans qu'on lui demande rien : l'entrée de la barre d'infos serveur
/// (zone, météo, heure d'Eorzéa, actions Logos à apprendre ; clic = fenêtre Logos), l'alerte des
/// nouvelles FATE avec drapeau sur la carte du jeu, et la lecture du chat d'Eurêka (annonces de pop,
/// liens de tracker).
/// </summary>
public sealed class CompagnonZone : IDisposable
{
    private const string Titre = "Xiv Eurêka";
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AntiDoublon = TimeSpan.FromSeconds(45);
    private const float DistanceFate = 60f;

    // Canaux où se donnent les pops : cris, zone, groupe, alliance, LS, CWLS. Pas les messages système.
    private static readonly HashSet<XivChatType> CanauxEcoutes =
    [
        XivChatType.Say, XivChatType.Shout, XivChatType.Yell, XivChatType.Party, XivChatType.CrossParty,
        XivChatType.Alliance, XivChatType.FreeCompany,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    private readonly Configuration configuration;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IFateTable fateTable;
    private readonly IChatGui chat;
    private readonly IToastGui toasts;
    private readonly IDtrBar dtrBar;
    private readonly IPluginLog log;
    private readonly NomsMeteo nomsMeteo;
    private readonly Func<int?> aApprendre;
    private readonly Action ouvrirLogos;
    private readonly Action sauver;
    private readonly IDtrBarEntry entree;
    private readonly SuiviFates fates = new();
    private readonly Dictionary<string, DateTime> dernieresAlertes = [];
    private DateTime prochainPassage = DateTime.MinValue;

    public CompagnonZone(Configuration configuration, IClientState clientState, IFramework framework, IFateTable fateTable,
        IChatGui chat, IToastGui toasts, IDtrBar dtrBar, IPluginLog log, NomsMeteo nomsMeteo, Func<int?> aApprendre, Action ouvrirLogos, Action sauver)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.framework = framework;
        this.fateTable = fateTable;
        this.chat = chat;
        this.toasts = toasts;
        this.dtrBar = dtrBar;
        this.log = log;
        this.nomsMeteo = nomsMeteo;
        this.aApprendre = aApprendre;
        this.ouvrirLogos = ouvrirLogos;
        this.sauver = sauver;

        entree = dtrBar.Get(Titre);
        entree.Shown = false;
        entree.OnClick = e =>
        {
            if (e.ClickType == MouseClickType.Left) ouvrirLogos();
        };

        framework.Update += SurTrame;
        clientState.TerritoryChanged += SurChangementZone;
        chat.ChatMessage += SurMessage;
    }

    public void Dispose()
    {
        chat.ChatMessage -= SurMessage;
        clientState.TerritoryChanged -= SurChangementZone;
        framework.Update -= SurTrame;
        entree.Remove();
    }

    private ZoneEureka? ZoneCourante => Meteo.Zone(clientState.TerritoryType);

    private void SurChangementZone(uint territoire) => fates.Reinitialiser();

    private void SurTrame(IFramework _)
    {
        if (DateTime.UtcNow < prochainPassage) return;
        prochainPassage = DateTime.UtcNow + Intervalle;

        var zone = ZoneCourante;
        MettreAJourBarre(zone);
        if (zone == null) return;

        var nouvelles = fates.MettreAJour(fateTable
            .Where(f => f.State is FateState.Preparing or FateState.Running)
            .Select(f => new FateVue(f.FateId, f.Name.TextValue, f.Position)));
        foreach (var fate in nouvelles) AnnoncerFate(fate);
    }

    private void MettreAJourBarre(ZoneEureka? zone)
    {
        var logos = aApprendre() ?? 0;
        var logosTexte = logos > 0 ? $"Logos : {logos} à apprendre" : null;
        if (zone == null)
        {
            entree.Shown = configuration.BarreHorsEureka && logosTexte != null;
            if (entree.Shown) SetTexte(logosTexte!, "Clic : actions Logos");
            return;
        }

        var maintenant = DateTimeOffset.UtcNow;
        var periodes = Meteo.Periodes(zone, maintenant).Take(7).ToList();
        var (h, m) = Meteo.HeureEorzea(maintenant);
        var suivante = periodes[1];
        var texte = $"{nomsMeteo[periodes[0].Meteo]} → {nomsMeteo[suivante.Meteo]} {Duree(suivante.Debut - maintenant)} · ET {h:00}:{m:00}"
                    + (logosTexte != null ? $" · {logosTexte}" : string.Empty);
        var infobulle = $"{zone.Nom} — météo à venir (heure locale) :\n"
                        + string.Join("\n", periodes.Skip(1).Select(p => $"{p.Debut.ToLocalTime():HH:mm}  {nomsMeteo[p.Meteo]}"))
                        + "\nClic : actions Logos";
        entree.Shown = true;
        SetTexte(texte, infobulle);
    }

    private void SetTexte(string texte, string infobulle)
    {
        // Ne réécrire que si le texte change : chaque affectation reconstruit la barre.
        if (entree.Text?.TextValue != texte) entree.Text = texte;
        if (entree.Tooltip?.TextValue != infobulle) entree.Tooltip = infobulle;
    }

    private static string Duree(TimeSpan d) => d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min" : $"{Math.Max(0, (int)d.TotalSeconds)} s";

    private void AnnoncerFate(FateVue fate)
    {
        if (!configuration.AlerterFates || DejaAnnonce($"fate:{fate.Id}")) return;
        Alerter($"FATE : {fate.Nom}", fate.Position, configuration.DrapeauSurFate);
    }

    private void SurMessage(IHandleableChatMessage message)
    {
        var zone = ZoneCourante;
        if (zone == null || !configuration.EcouterChat || !CanauxEcoutes.Contains(message.LogKind)) return;
        try
        {
            Analyser(message.Message);
        }
        catch (Exception e)
        {
            // Un message mal formé ne doit jamais casser le chat du jeu.
            log.Error(e, "Lecture d'un message du chat impossible");
        }
    }

    private void Analyser(SeString texte)
    {
        var lu = AnalyseChat.Analyser(texte.TextValue, fates.EnCours.Select(f => f.Nom));

        if (lu.Tracker != null && lu.Tracker != configuration.DernierTracker)
        {
            configuration.DernierTracker = lu.Tracker;
            sauver();
            chat.Print($"Tracker repéré dans le chat : https://ffxiv-eureka.com/{lu.Tracker}", Titre);
        }

        if (!lu.Pop) return;
        var territoire = clientState.TerritoryType;
        var carte = clientState.MapId;
        Vector3? position = texte.Payloads.OfType<MapLinkPayload>().FirstOrDefault() is { } lien
            ? CarteJeu.Monde(lien)
            : lu.Position is { } p ? CarteJeu.Monde(territoire, carte, p) : null;

        var fate = lu.Fate != null
            ? fates.EnCours.FirstOrDefault(f => f.Nom == lu.Fate)
            : position is { } pos ? fates.PlusProche(pos, DistanceFate) : null;
        position ??= fate?.Position;
        if (position == null) return;

        var cle = fate != null ? $"fate:{fate.Id}" : $"pos:{(int)(position.Value.X / 20)}:{(int)(position.Value.Z / 20)}";
        if (DejaAnnonce(cle)) return;
        Alerter(fate != null ? $"Pop annoncé : {fate.Nom}" : "Pop annoncé dans le chat", position.Value, configuration.DrapeauSurChat);
    }

    private bool DejaAnnonce(string cle)
    {
        var maintenant = DateTime.UtcNow;
        if (dernieresAlertes.TryGetValue(cle, out var quand) && maintenant - quand < AntiDoublon) return true;
        dernieresAlertes[cle] = maintenant;
        return false;
    }

    private void Alerter(string titre, Vector3 position, bool drapeau)
    {
        var territoire = clientState.TerritoryType;
        var carte = clientState.MapId;
        toasts.ShowQuest(titre, new QuestToastOptions { PlaySound = configuration.SonAlertes });
        chat.Print(new SeStringBuilder().AddText(titre + " ").Append(CarteJeu.Lien(territoire, carte, position)).Build(), Titre);
        if (drapeau) CarteJeu.Drapeau(territoire, carte, position, ouvrir: false);
    }
}
