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
/// What the plugin does in game without being asked: alerting new NM FATEs with a flag on the game
/// map, and reading the Eureka chat (pop announcements, tracker links). Bunny FATEs are ignored: we
/// do not do them.
/// </summary>
public sealed class ZoneCompanion : IDisposable
{
    private const string ChatTitle = "Xiv Eurêka";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(45);
    private const float FateDistance = 60f;
    private static readonly TimeSpan PopLifetime = TimeSpan.FromMinutes(10);

    // Channels where pops are called: shouts, zone, party, alliance, LS, CWLS. Not system messages.
    private static readonly HashSet<XivChatType> ListenedChannels =
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
    private readonly IObjectTable objects;
    private readonly IFramework framework;
    private readonly IFateTable fateTable;
    private readonly IChatGui chat;
    private readonly IToastGui toasts;
    private readonly IPluginLog log;
    private readonly Action save;
    private readonly FateTracker fates = new();
    private readonly Dictionary<string, DateTime> lastAlerts = [];
    private readonly List<ChatPop> pops = [];
    private readonly HashSet<ushort> reportedSuccesses = [];
    private DateTime nextPass = DateTime.MinValue;

    public ZoneCompanion(Configuration configuration, IClientState clientState, IObjectTable objects, IFramework framework,
        IFateTable fateTable, IChatGui chat, IToastGui toasts, IPluginLog log, Action save)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.objects = objects;
        this.framework = framework;
        this.fateTable = fateTable;
        this.chat = chat;
        this.toasts = toasts;
        this.log = log;
        this.save = save;

        framework.Update += OnFrameworkUpdate;
        clientState.TerritoryChanged += OnTerritoryChanged;
        chat.ChatMessage += OnChatMessage;
    }

    /// <summary>A ffxiv-eureka.com/xxxx link seen in the Eureka chat (tracker id).</summary>
    public event Action<string>? TrackerShouted;

    /// <summary>An NM FATE seen succeeding (game state "Ended", to confirm in game), once per run.</summary>
    public event Action<ushort>? FateSucceeded;

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        framework.Update -= OnFrameworkUpdate;
    }

    private EurekaZone? CurrentZone => EurekaWeather.Zone(clientState.TerritoryType);

    /// <summary>FATEs seen ending in this instance, and when (UTC): for NM respawns on the map.</summary>
    public IReadOnlyDictionary<ushort, DateTime> EndedFates() => fates.Ended;

    /// <summary>Pops announced in the chat less than ten minutes ago, for the plugin map.</summary>
    public IReadOnlyList<ChatPop> RecentPops()
    {
        pops.RemoveAll(p => DateTime.UtcNow - p.When > PopLifetime);
        return pops.ToList();
    }

    private void OnTerritoryChanged(uint territory)
    {
        fates.Reset();
        pops.Clear();
        reportedSuccesses.Clear();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextPass) return;
        nextPass = DateTime.UtcNow + Interval;

        if (CurrentZone == null) return;

        var appeared = fates.Update(fateTable
            .Where(f => f.State is FateState.Preparing or FateState.Running)
            .Where(f => !FateTracker.IsBunnyFate(f.GameData.Value.Rule))
            .Select(f => new SeenFate(f.FateId, f.Name.TextValue, f.Position)));
        foreach (var fate in appeared) AnnounceFate(fate);

        foreach (var f in fateTable.Where(f => !FateTracker.IsBunnyFate(f.GameData.Value.Rule)))
        {
            if (f.State == FateState.Running) reportedSuccesses.Remove(f.FateId); // it is back: a new run
            else if (f.State == FateState.Ended && reportedSuccesses.Add(f.FateId)) FateSucceeded?.Invoke(f.FateId);
        }
    }

    private void AnnounceFate(SeenFate fate)
    {
        if (!configuration.AlertFates || AlreadyAlerted($"fate:{fate.Id}")) return;
        Alert($"FATE : {fate.Name}", fate.Position, configuration.FlagOnFate);
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (CurrentZone == null || !configuration.ListenToChat || !ListenedChannels.Contains(message.LogKind)) return;
        try
        {
            Parse(message.Message, FromMe(message));
        }
        catch (Exception e)
        {
            // A malformed message must never break the game chat.
            log.Error(e, "Could not read a chat message");
        }
    }

    /// <summary>
    /// The message is the player's own: the game says so (relation kind), or, on channels where it
    /// does not (linkshells), the sender's name is the player's (it can carry a world suffix).
    /// </summary>
    private bool FromMe(IHandleableChatMessage message)
    {
        if (message.SourceKind == XivChatRelationKind.LocalPlayer) return true;
        var me = objects.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrEmpty(me) && message.Sender.TextValue.Contains(me, StringComparison.Ordinal);
    }

    private void Parse(SeString text, bool fromMe)
    {
        var info = ChatParser.Parse(text.TextValue, fates.Running.Select(f => f.Name));

        if (info.Tracker != null) TrackerShouted?.Invoke(info.Tracker);
        if (info.Tracker != null && info.Tracker != configuration.LastTracker)
        {
            configuration.LastTracker = info.Tracker;
            save();
            chat.Print($"Tracker repéré dans le chat : https://ffxiv-eureka.com/{info.Tracker}", ChatTitle);
        }

        // A pop called by the player: they know already, no alert (the tracker link above still counts).
        if (!info.Pop || fromMe) return;
        var territory = clientState.TerritoryType;
        var map = clientState.MapId;
        Vector3? position = text.Payloads.OfType<MapLinkPayload>().FirstOrDefault() is { } link
            ? InGameMap.World(link)
            : info.Position is { } p ? InGameMap.World(territory, map, p) : null;

        var fate = info.Fate != null
            ? fates.Running.FirstOrDefault(f => f.Name == info.Fate)
            : position is { } pos ? fates.Nearest(pos, FateDistance) : null;
        position ??= fate?.Position;
        if (position == null) return;

        var key = fate != null ? $"fate:{fate.Id}" : $"pos:{(int)(position.Value.X / 20)}:{(int)(position.Value.Z / 20)}";
        if (AlreadyAlerted(key)) return;
        var title = fate != null ? $"Pop annoncé : {fate.Name}" : "Pop annoncé dans le chat";
        pops.Add(new ChatPop(title, position.Value, DateTime.UtcNow));
        Alert(title, position.Value, configuration.FlagOnChatPop);
    }

    private bool AlreadyAlerted(string key)
    {
        var now = DateTime.UtcNow;
        if (lastAlerts.TryGetValue(key, out var when) && now - when < Debounce) return true;
        lastAlerts[key] = now;
        return false;
    }

    private void Alert(string title, Vector3 position, bool flag)
    {
        var territory = clientState.TerritoryType;
        var map = clientState.MapId;
        toasts.ShowQuest(title, new QuestToastOptions { PlaySound = configuration.AlertSound });
        chat.Print(new SeStringBuilder().AddText(title + " ").Append(InGameMap.Link(territory, map, position)).Build(), ChatTitle);
        if (flag) InGameMap.SetFlag(territory, map, position, open: false);
    }
}
