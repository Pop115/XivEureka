using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using XivEureka.Zones;

namespace XivEureka.Tracker;

/// <summary>
/// Le tracker ffxiv-eureka.com de l'instance en cours, suivi en direct. Un tracker est temporaire
/// et lié à une instance : on suit celui crié dans le chat de la zone ; sans tracker au bout de
/// quelques minutes, le plugin en crée un (et le dit dans le chat, pour qu'on le crie). On écrit
/// seulement sur un tracker dont on a le mot de passe, c'est-à-dire créé par le plugin : les NM vus
/// tués y sont signalés. Le data center est aussi écouté, pour lister les trackers ouverts de la zone.
/// </summary>
public sealed class SuiviTracker : IDisposable
{
    public static readonly TimeSpan DelaiCreation = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RepriseMax = TimeSpan.FromHours(3);
    private static readonly HttpClient Http = CreerHttp();
    private const string Titre = "Xiv Eurêka";

    private readonly Configuration configuration;
    private readonly IClientState clientState;
    private readonly IObjectTable objets;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly CatalogueNm catalogueNm;
    private readonly Action sauver;
    private readonly ClientPhoenix client;
    private readonly ConcurrentQueue<Action> aFaire = new();
    private readonly Dictionary<string, InstanceTracker> duDataCenter = [];

    private uint territoire;
    private DateTime entreeZone = DateTime.UtcNow;
    private string? suiviId;
    private string? canalDataCenter;
    private bool creationEnCours;

    public SuiviTracker(Configuration configuration, IClientState clientState, IObjectTable objets, IFramework framework,
        IChatGui chat, IPluginLog log, CatalogueNm catalogueNm, Action sauver)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.objets = objets;
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        this.catalogueNm = catalogueNm;
        this.sauver = sauver;
        client = new ClientPhoenix(log);

        clientState.TerritoryChanged += SurChangementZone;
        framework.Update += SurTrame;
        Reprendre();
    }

    /// <summary>Le tracker suivi, tel que le serveur l'a envoyé ; null s'il n'y en a pas (encore).</summary>
    public InstanceTracker? Suivi { get; private set; }

    /// <summary>Ce qu'il se passe, en une phrase (bandeau de la carte).</summary>
    public string Statut { get; private set; } = "pas de tracker";

    /// <summary>Le plugin a le mot de passe du tracker suivi : il peut y écrire.</summary>
    public bool Proprietaire => suiviId != null && configuration.MotsDePasseTrackers.ContainsKey(suiviId);

    public string? IdSuivi => suiviId;

    /// <summary>Quand le plugin créera un tracker s'il n'y en a toujours pas (null : il n'en créera pas).</summary>
    public DateTime? CreationPrevue =>
        CreationAttendue(ZoneSite != null, suiviId != null, creationEnCours, configuration.CreerTrackerAuto)
            ? entreeZone + DelaiCreation : null;

    /// <summary>Les autres trackers ouverts sur le data center pour la zone en cours.</summary>
    public IReadOnlyList<InstanceTracker> AutresTrackers =>
        duDataCenter.Values.Where(t => t.Zone == ZoneSite && t.Id != suiviId).OrderByDescending(t => t.MajLe).ToList();

    private int? ZoneSite => FormatTracker.ZoneSite(territoire);

    public void Dispose()
    {
        framework.Update -= SurTrame;
        clientState.TerritoryChanged -= SurChangementZone;
        client.Dispose();
    }

    /// <summary>
    /// Le plugin créera-t-il un tracker ? À Eurêka, sans tracker suivi ni création en cours, si le
    /// réglage le permet ; il attend alors <see cref="DelaiCreation"/> dans la zone, le temps qu'on en crie un.
    /// </summary>
    public static bool CreationAttendue(bool aEureka, bool trackerSuivi, bool creationEnCours, bool reglage) =>
        aEureka && !trackerSuivi && !creationEnCours && reglage;

    // ---------- ce qui arrive du reste du plugin ----------

    /// <summary>Un lien de tracker vu dans le chat de la zone : on le suit (s'il est nouveau).</summary>
    public void TrackerCrie(string id)
    {
        if (ZoneSite == null || !configuration.SuivreTrackerCrie || id == suiviId) return;
        Suivre(id, "crié dans le chat");
    }

    /// <summary>Choix à la main dans la liste du data center.</summary>
    public void Choisir(string id) => Suivre(id, "choisi dans la liste");

    /// <summary>Créer un tracker tout de suite (bouton).</summary>
    public void CreerMaintenant()
    {
        if (ZoneSite is { } zone && !creationEnCours) Creer(zone);
    }

    /// <summary>Une FATE de NM réussie dans la zone : signalée sur notre tracker.</summary>
    public void FateReussie(ushort fate)
    {
        if (!Proprietaire || !configuration.SignalerMorts || suiviId == null) return;
        if (catalogueNm.Zone(territoire)?.Nm.FirstOrDefault(n => n.Fate == fate) is not { } nm) return;
        client.Envoyer($"instance:{suiviId}", "set_kill_time",
            new { id = nm.Site, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        log.Information("Tracker {Id} : NM {Site} signalé tué", suiviId, nm.Site);
    }

    /// <summary>Heures de mort (UTC) du tracker, par numéro de FATE, pour la carte.</summary>
    public IReadOnlyDictionary<ushort, DateTime> MortsParFate()
    {
        if (Suivi == null || catalogueNm.Zone(territoire) is not { } zone) return new Dictionary<ushort, DateTime>();
        return zone.Nm.Where(n => Suivi.Morts.ContainsKey(n.Site))
            .ToDictionary(n => n.Fate, n => Suivi.Morts[n.Site].UtcDateTime);
    }

    /// <summary>FATE dont le NM est marqué « préparé » sur le tracker.</summary>
    public IReadOnlySet<ushort> PreparesParFate()
    {
        if (Suivi == null || catalogueNm.Zone(territoire) is not { } zone) return new HashSet<ushort>();
        return zone.Nm.Where(n => Suivi.Prepares.Contains(n.Site)).Select(n => n.Fate).ToHashSet();
    }

    // ---------- zone, reprise ----------

    private void SurChangementZone(uint nouveau)
    {
        // Nouvelle zone = nouvelle instance : l'ancien tracker ne vaut plus.
        Oublier();
        territoire = nouveau;
        entreeZone = DateTime.UtcNow;
        configuration.TrackerEnCours = null;
        sauver();
        EcouterDataCenter();
    }

    /// <summary>Au démarrage du plugin : reprendre le tracker suivi si on est toujours dans sa zone.</summary>
    private void Reprendre()
    {
        territoire = clientState.TerritoryType;
        EcouterDataCenter();
        if (configuration.TrackerEnCours is { } t && t.Territoire == territoire && DateTime.UtcNow - t.Depuis < RepriseMax)
            Suivre(t.Id, "repris");
    }

    private void EcouterDataCenter()
    {
        if (canalDataCenter != null) client.Quitter(canalDataCenter);
        canalDataCenter = null;
        duDataCenter.Clear();
        if (ZoneSite == null || DataCenter() is not { } dc) return;
        canalDataCenter = $"datacenter:{dc}";
        client.Rejoindre(canalDataCenter, new { });
    }

    /// <summary>Numéro du site pour le data center où l'on joue (nom anglais du jeu).</summary>
    private int? DataCenter()
    {
        var nom = objets.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText();
        return nom != null && FormatTracker.DataCenters.TryGetValue(nom, out var id) ? id : null;
    }

    // ---------- suivre, créer ----------

    private void Suivre(string id, string pourquoi)
    {
        Oublier();
        suiviId = id;
        Statut = $"connexion au tracker {id}…";
        var charge = configuration.MotsDePasseTrackers.TryGetValue(id, out var mdp) ? (object)new { password = mdp } : new { };
        client.Rejoindre($"instance:{id}", charge);
        configuration.TrackerEnCours = new TrackerSauve { Id = id, Territoire = territoire, Depuis = DateTime.UtcNow };
        sauver();
        log.Information("Tracker {Id} suivi ({Pourquoi})", id, pourquoi);
    }

    private void Oublier()
    {
        if (suiviId != null) client.Quitter($"instance:{suiviId}");
        suiviId = null;
        Suivi = null;
        Statut = "pas de tracker";
    }

    private void Creer(int zone)
    {
        creationEnCours = true;
        Statut = "création d'un tracker…";
        var territoireCreation = territoire;
        _ = Task.Run(async () =>
        {
            try
            {
                using var contenu = new StringContent(FormatTracker.CorpsCreation(zone), Encoding.UTF8, "application/vnd.api+json");
                using var reponse = await Http.PostAsync("https://ffxiv-eureka.com/api/instances", contenu);
                reponse.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
                var cree = FormatTracker.LireInstance(doc.RootElement.GetProperty("data"));
                aFaire.Enqueue(() => TrackerCree(cree, territoireCreation));
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
            {
                log.Warning(e, "Création de tracker impossible");
                aFaire.Enqueue(() =>
                {
                    creationEnCours = false;
                    entreeZone = DateTime.UtcNow; // nouvel essai dans quelques minutes
                    Statut = "création du tracker impossible (site injoignable ?)";
                });
            }
        });
    }

    private void TrackerCree(InstanceTracker cree, uint territoireCreation)
    {
        creationEnCours = false;
        if (cree.MotDePasse is { } mdp) configuration.MotsDePasseTrackers[cree.Id] = mdp;
        sauver();
        if (territoireCreation != territoire) return; // on a changé de zone entre-temps
        if (suiviId != null) return;                  // un tracker a été crié entre-temps : on garde celui-là
        Suivre(cree.Id, "créé");
        if (DataCenter() is { } dc)
            client.Envoyer($"instance:{cree.Id}", "set_instance_information", new { instance_id = (int?)null, data_center_id = dc });
        chat.Print($"Tracker créé pour cette instance : https://ffxiv-eureka.com/{cree.Id} — crie-le pour le partager.", Titre);
    }

    // ---------- chaque image ----------

    private void SurTrame(IFramework _)
    {
        while (aFaire.TryDequeue(out var action)) action();
        while (client.Recus.TryDequeue(out var message)) Traiter(message);

        if (CreationAttendue(ZoneSite != null, suiviId != null, creationEnCours, configuration.CreerTrackerAuto)
            && DateTime.UtcNow - entreeZone >= DelaiCreation)
            Creer(ZoneSite!.Value);

        if (suiviId != null && Suivi != null)
            Statut = client.Connecte ? $"tracker {suiviId} en direct" + (Proprietaire ? " (le tien)" : string.Empty)
                : $"tracker {suiviId} : reconnexion…";
    }

    private void Traiter(MessagePhoenix m)
    {
        try
        {
            if (m.Sujet == $"instance:{suiviId}") TraiterInstance(m);
            else if (m.Sujet == canalDataCenter) TraiterDataCenter(m);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Un message inattendu ne doit pas casser le suivi.
            log.Warning(e, "Tracker : message illisible ({Evenement})", m.Evenement);
        }
    }

    private void TraiterInstance(MessagePhoenix m)
    {
        switch (m.Evenement)
        {
            case "initial_payload":
                Suivi = FormatTracker.LireInstance(m.Charge.GetProperty("data"));
                break;
            case "payload" when Suivi != null:
                Suivi = FormatTracker.Fusionner(Suivi, m.Charge.GetProperty("data"));
                break;
            case "phx_reply" when m.Charge.TryGetProperty("status", out var s) && s.GetString() == "error":
            case "instance_removed":
                var id = suiviId;
                Oublier();
                configuration.TrackerEnCours = null;
                sauver();
                Statut = $"tracker {id} introuvable (expiré ?)";
                entreeZone = DateTime.UtcNow; // la création attendra de nouveau quelques minutes
                break;
        }
    }

    private void TraiterDataCenter(MessagePhoenix m)
    {
        switch (m.Evenement)
        {
            case "initial_payload":
                duDataCenter.Clear();
                foreach (var t in m.Charge.GetProperty("data").EnumerateArray().Select(FormatTracker.LireInstance))
                    duDataCenter[t.Id] = t;
                break;
            case "instance_created":
            case "payload":
                if (!m.Charge.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var idElement)) break;
                var id = idElement.GetString() ?? string.Empty;
                duDataCenter[id] = duDataCenter.TryGetValue(id, out var avant)
                    ? FormatTracker.Fusionner(avant, data) : FormatTracker.LireInstance(data);
                break;
            case "instance_removed":
                if (m.Charge.TryGetProperty("id", out var retire)) duDataCenter.Remove(retire.ToString());
                break;
        }
    }

    private static HttpClient CreerHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");
        http.DefaultRequestHeaders.Add("User-Agent", "XivEureka (plugin Dalamud privé)");
        return http;
    }
}
