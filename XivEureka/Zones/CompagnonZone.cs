using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace XivEureka.Zones;

/// <summary>
/// Ce que le plugin fait en jeu sans qu'on lui demande rien : l'alerte des nouvelles FATE de NM
/// avec drapeau sur la carte du jeu, et la lecture du chat d'Eurêka (annonces de pop, liens de
/// tracker). Les FATE lapin sont ignorées : on ne les fait pas.
/// </summary>
public sealed class CompagnonZone : IDisposable
{
    private const string Titre = "Xiv Eurêka";
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AntiDoublon = TimeSpan.FromSeconds(45);
    private const float DistanceFate = 60f;
    private static readonly TimeSpan DureePop = TimeSpan.FromMinutes(10);

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
    private readonly IPluginLog log;
    private readonly Action sauver;
    private readonly SuiviFates fates = new();
    private readonly Dictionary<string, DateTime> dernieresAlertes = [];
    private readonly List<PopAnnonce> pops = [];
    private readonly HashSet<ushort> reussiesSignalees = [];
    private DateTime prochainPassage = DateTime.MinValue;

    public CompagnonZone(Configuration configuration, IClientState clientState, IFramework framework, IFateTable fateTable,
        IChatGui chat, IToastGui toasts, IPluginLog log, Action sauver)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.framework = framework;
        this.fateTable = fateTable;
        this.chat = chat;
        this.toasts = toasts;
        this.log = log;
        this.sauver = sauver;

        framework.Update += SurTrame;
        clientState.TerritoryChanged += SurChangementZone;
        chat.ChatMessage += SurMessage;
    }

    /// <summary>Un lien ffxiv-eureka.com/xxxx vu dans le chat d'Eurêka (identifiant du tracker).</summary>
    public event Action<string>? TrackerCrie;

    /// <summary>Une FATE de NM vue réussie (état « Ended » du jeu, à valider en jeu), une fois par passage.</summary>
    public event Action<ushort>? FateReussie;

    public void Dispose()
    {
        chat.ChatMessage -= SurMessage;
        clientState.TerritoryChanged -= SurChangementZone;
        framework.Update -= SurTrame;
    }

    private ZoneEureka? ZoneCourante => Meteo.Zone(clientState.TerritoryType);

    /// <summary>Les FATE vues finir dans cette instance, et quand (UTC) : pour le respawn des NM sur la carte.</summary>
    public IReadOnlyDictionary<ushort, DateTime> FatesTerminees() => fates.Terminees;

    /// <summary>Les pops annoncés dans le chat depuis moins de dix minutes, pour la carte du plugin.</summary>
    public IReadOnlyList<PopAnnonce> PopsRecents()
    {
        pops.RemoveAll(p => DateTime.UtcNow - p.Quand > DureePop);
        return pops.ToList();
    }

    private void SurChangementZone(uint territoire)
    {
        fates.Reinitialiser();
        pops.Clear();
        reussiesSignalees.Clear();
    }

    private void SurTrame(IFramework _)
    {
        if (DateTime.UtcNow < prochainPassage) return;
        prochainPassage = DateTime.UtcNow + Intervalle;

        if (ZoneCourante == null) return;

        var nouvelles = fates.MettreAJour(fateTable
            .Where(f => f.State is FateState.Preparing or FateState.Running)
            .Where(f => !SuiviFates.EstFateLapin(f.GameData.Value.Rule))
            .Select(f => new FateVue(f.FateId, f.Name.TextValue, f.Position)));
        foreach (var fate in nouvelles) AnnoncerFate(fate);

        foreach (var f in fateTable.Where(f => !SuiviFates.EstFateLapin(f.GameData.Value.Rule)))
        {
            if (f.State == FateState.Running) reussiesSignalees.Remove(f.FateId); // elle revient : nouveau passage
            else if (f.State == FateState.Ended && reussiesSignalees.Add(f.FateId)) FateReussie?.Invoke(f.FateId);
        }
    }

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

        if (lu.Tracker != null) TrackerCrie?.Invoke(lu.Tracker);
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
        var titre = fate != null ? $"Pop annoncé : {fate.Nom}" : "Pop annoncé dans le chat";
        pops.Add(new PopAnnonce(titre, position.Value, DateTime.UtcNow));
        Alerter(titre, position.Value, configuration.DrapeauSurChat);
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
