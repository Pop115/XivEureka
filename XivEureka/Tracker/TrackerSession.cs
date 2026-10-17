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
/// The ffxiv-eureka.com tracker of the current instance, followed live. A tracker is temporary and
/// tied to one instance: we follow the one shouted in the zone chat, or one picked from the data
/// center list; the player can create one with a button (never automatically: it once looped). We
/// only write to a tracker whose password we hold, i.e. one the plugin created: the NMs seen killed
/// are reported there. The data center channel is also joined, to list the zone's open trackers.
/// </summary>
public sealed class TrackerSession : IDisposable
{
    private static readonly TimeSpan MaxResumeAge = TimeSpan.FromHours(3);
    private static readonly HttpClient Http = CreateHttp();
    private const string ChatTitle = "Xiv Eurêka";

    private readonly Configuration configuration;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly NmCatalog nmCatalog;
    private readonly Action save;
    private readonly PhoenixClient client;
    private readonly ConcurrentQueue<Action> pending = new();
    private readonly Dictionary<string, TrackerInstance> dataCenterTrackers = [];

    private uint territory;
    private string? followedId;
    private string? dataCenterTopic;
    private bool creating;
    private bool resumed;

    public TrackerSession(Configuration configuration, IClientState clientState, IObjectTable objects, IFramework framework,
        IChatGui chat, IPluginLog log, NmCatalog nmCatalog, Action save)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.objects = objects;
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        this.nmCatalog = nmCatalog;
        this.save = save;
        client = new PhoenixClient(log);

        // Nothing from the game here: the constructor may run off the main thread (enabling the
        // plugin from /xlplugins), where Dalamud refuses game state. Resume() waits for the first frame.
        clientState.TerritoryChanged += OnTerritoryChanged;
        framework.Update += OnFrameworkUpdate;
    }

    /// <summary>The followed tracker, as the server sent it; null if there is none (yet).</summary>
    public TrackerInstance? Current { get; private set; }

    /// <summary>What is going on, in one line (map header). User-facing.</summary>
    public string Status { get; private set; } = Loc.T("pas de tracker", "no tracker");

    /// <summary>The plugin holds the followed tracker's password: it can write to it.</summary>
    public bool IsOwned => followedId != null && configuration.TrackerPasswords.ContainsKey(followedId);

    public string? FollowedId => followedId;

    /// <summary>The other open trackers on the data center for the current zone.</summary>
    public IReadOnlyList<TrackerInstance> OtherTrackers =>
        dataCenterTrackers.Values.Where(t => t.Zone == SiteZone && t.Id != followedId).OrderByDescending(t => t.UpdatedAt).ToList();

    private int? SiteZone => TrackerFormat.SiteZone(territory);

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        client.Dispose();
    }

    /// <summary>
    /// Does this message mean the followed tracker is gone? Removed by the site, or the join itself
    /// refused (its reply carries the join's ref). An error answering any other push (kill report,
    /// data center) leaves the tracker followed: treating those as "gone" made the plugin drop its
    /// own tracker and create another one, again and again.
    /// </summary>
    public static bool LosesTracker(PhoenixMessage m) =>
        m.Event == "instance_removed"
        || (m.Event == "phx_reply" && m.Ref != null && m.Ref == m.JoinRef
            && m.Payload.TryGetProperty("status", out var s) && s.GetString() == "error");

    // ---------- events from the rest of the plugin ----------

    /// <summary>A tracker link seen in the zone chat: follow it (if new).</summary>
    public void OnTrackerShouted(string id)
    {
        if (SiteZone == null || !configuration.FollowShoutedTracker || id == followedId) return;
        Follow(id, "shouted in chat");
    }

    /// <summary>Picked by hand from the data center list.</summary>
    public void Pick(string id) => Follow(id, "picked from the list");

    /// <summary>Create a tracker right now (button).</summary>
    public void CreateNow()
    {
        if (SiteZone is { } zone && !creating) Create(zone);
    }

    /// <summary>An NM FATE succeeded in the zone: report it on our tracker.</summary>
    public void OnFateSucceeded(ushort fate)
    {
        if (!IsOwned || !configuration.ReportKills || followedId == null) return;
        if (nmCatalog.Zone(territory)?.Nms.FirstOrDefault(n => n.Fate == fate) is not { } nm) return;
        client.Push($"instance:{followedId}", "set_kill_time",
            new { id = nm.Site, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        log.Information("Tracker {Id}: NM {Site} reported killed", followedId, nm.Site);
    }

    /// <summary>The tracker's kill times (UTC), by FATE id, for the map.</summary>
    public IReadOnlyDictionary<ushort, DateTime> KillsByFate()
    {
        if (Current == null || nmCatalog.Zone(territory) is not { } zone) return new Dictionary<ushort, DateTime>();
        return zone.Nms.Where(n => Current.Kills.ContainsKey(n.Site))
            .ToDictionary(n => n.Fate, n => Current.Kills[n.Site].UtcDateTime);
    }

    /// <summary>FATEs whose NM is marked "prepped" on the tracker.</summary>
    public IReadOnlySet<ushort> PreppedByFate()
    {
        if (Current == null || nmCatalog.Zone(territory) is not { } zone) return new HashSet<ushort>();
        return zone.Nms.Where(n => Current.Prepped.Contains(n.Site)).Select(n => n.Fate).ToHashSet();
    }

    // ---------- zone, resume ----------

    private void OnTerritoryChanged(uint newTerritory)
    {
        resumed = true; // a zone change supersedes resuming
        // New zone = new instance: the old tracker no longer applies.
        Forget();
        territory = newTerritory;
        configuration.CurrentTracker = null;
        save();
        ListenToDataCenter();
    }

    /// <summary>On the first frame after plugin start: resume the followed tracker if we are still in its zone.</summary>
    private void Resume()
    {
        territory = clientState.TerritoryType;
        ListenToDataCenter();
        if (configuration.CurrentTracker is { } t && t.Territory == territory && DateTime.UtcNow - t.Since < MaxResumeAge)
            Follow(t.Id, "resumed");
    }

    private void ListenToDataCenter()
    {
        if (dataCenterTopic != null) client.Leave(dataCenterTopic);
        dataCenterTopic = null;
        dataCenterTrackers.Clear();
        if (SiteZone == null || DataCenter() is not { } dc) return;
        dataCenterTopic = $"datacenter:{dc}";
        client.Join(dataCenterTopic, new { });
    }

    /// <summary>The site id of the data center being played on (English game name).</summary>
    private int? DataCenter()
    {
        var name = objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText();
        return name != null && TrackerFormat.DataCenters.TryGetValue(name, out var id) ? id : null;
    }

    // ---------- follow, create ----------

    private void Follow(string id, string reason)
    {
        Forget();
        followedId = id;
        Status = Loc.T($"connexion au tracker {id}…", $"connecting to tracker {id}…");
        var payload = configuration.TrackerPasswords.TryGetValue(id, out var pw) ? (object)new { password = pw } : new { };
        client.Join($"instance:{id}", payload);
        configuration.CurrentTracker = new SavedTracker { Id = id, Territory = territory, Since = DateTime.UtcNow };
        save();
        log.Information("Tracker {Id} followed ({Reason})", id, reason);
    }

    private void Forget()
    {
        if (followedId != null) client.Leave($"instance:{followedId}");
        followedId = null;
        Current = null;
        Status = Loc.T("pas de tracker", "no tracker");
    }

    private void Create(int zone)
    {
        creating = true;
        Status = Loc.T("création d'un tracker…", "creating a tracker…");
        var creationTerritory = territory;
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(TrackerFormat.CreationBody(zone), Encoding.UTF8, "application/vnd.api+json");
                using var response = await Http.PostAsync("https://ffxiv-eureka.com/api/instances", content);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var created = TrackerFormat.ReadInstance(doc.RootElement.GetProperty("data"));
                pending.Enqueue(() => OnCreated(created, creationTerritory));
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
            {
                log.Warning(e, "Could not create a tracker");
                pending.Enqueue(() =>
                {
                    creating = false;
                    Status = Loc.T("création du tracker impossible (site injoignable ?)", "could not create the tracker (site unreachable?)");
                });
            }
        });
    }

    private void OnCreated(TrackerInstance created, uint creationTerritory)
    {
        creating = false;
        if (created.Password is { } pw) configuration.TrackerPasswords[created.Id] = pw;
        save();
        if (creationTerritory != territory) return; // zone changed in the meantime
        if (followedId != null) return;              // someone shouted one in the meantime: keep that one
        Follow(created.Id, "created");
        if (DataCenter() is { } dc)
            client.Push($"instance:{created.Id}", "set_instance_information", new { instance_id = (int?)null, data_center_id = dc });
        chat.Print(Loc.T($"Tracker créé pour cette instance : https://ffxiv-eureka.com/{created.Id} — crie-le pour le partager.",
            $"Tracker created for this instance: https://ffxiv-eureka.com/{created.Id} — shout it to share it."), ChatTitle);
    }

    // ---------- every frame ----------

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!resumed)
        {
            resumed = true;
            Resume();
        }
        while (pending.TryDequeue(out var action)) action();
        while (client.Received.TryDequeue(out var message)) Handle(message);

        // On arrival the player may not be loaded yet, so the data center was unknown: try again.
        if (SiteZone != null && dataCenterTopic == null && DataCenter() != null) ListenToDataCenter();

        if (followedId != null && Current != null)
            Status = client.IsConnected
                ? Loc.T($"tracker {followedId} en direct", $"tracker {followedId} live") + (IsOwned ? Loc.T(" (le tien)", " (yours)") : string.Empty)
                : Loc.T($"tracker {followedId} : reconnexion…", $"tracker {followedId}: reconnecting…");
    }

    private void Handle(PhoenixMessage m)
    {
        try
        {
            if (m.Topic == $"instance:{followedId}") HandleInstance(m);
            else if (m.Topic == dataCenterTopic) HandleDataCenter(m);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // An unexpected message must not break the follow-up.
            log.Warning(e, "Tracker: unreadable message ({Event})", m.Event);
        }
    }

    private void HandleInstance(PhoenixMessage m)
    {
        switch (m.Event)
        {
            case "initial_payload":
                Current = TrackerFormat.ReadInstance(m.Payload.GetProperty("data"));
                break;
            case "payload" when Current != null:
                Current = TrackerFormat.Merge(Current, m.Payload.GetProperty("data"));
                break;
            case var _ when LosesTracker(m):
                var id = followedId;
                Forget();
                configuration.CurrentTracker = null;
                save();
                Status = Loc.T($"tracker {id} introuvable (expiré ?)", $"tracker {id} not found (expired?)");
                break;
            case "phx_reply" when m.Payload.TryGetProperty("status", out var s) && s.GetString() == "error":
                log.Warning("Tracker {Id}: the site refused a message ({Payload})", followedId, m.Payload.ToString());
                break;
        }
    }

    private void HandleDataCenter(PhoenixMessage m)
    {
        switch (m.Event)
        {
            case "initial_payload":
                dataCenterTrackers.Clear();
                foreach (var t in m.Payload.GetProperty("data").EnumerateArray().Select(TrackerFormat.ReadInstance))
                    dataCenterTrackers[t.Id] = t;
                break;
            case "instance_created":
            case "payload":
                if (!m.Payload.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var idElement)) break;
                var id = idElement.GetString() ?? string.Empty;
                dataCenterTrackers[id] = dataCenterTrackers.TryGetValue(id, out var before)
                    ? TrackerFormat.Merge(before, data) : TrackerFormat.ReadInstance(data);
                break;
            case "instance_removed":
                if (m.Payload.TryGetProperty("id", out var removed)) dataCenterTrackers.Remove(removed.ToString());
                break;
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");
        http.DefaultRequestHeaders.Add("User-Agent", "XivEureka (private Dalamud plugin)");
        return http;
    }
}
