using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace XivEureka;

/// <summary>What the plugin remembers about a character: its last read stock and its learned actions.</summary>
[Serializable]
public sealed class CharacterState
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Owned mnemes (item id → quantity), as of the last visit to the manipulator.</summary>
    public Dictionary<uint, int> Stock { get; set; } = [];

    /// <summary>When <see cref="Stock"/> was read in game; null while it never was.</summary>
    public DateTime? StockReadAt { get; set; }

    /// <summary>
    /// Learned Logos actions (Action sheet rows). Read from the Logos action notebook when it is
    /// open; can be ticked by hand between two reads.
    /// </summary>
    public HashSet<uint> Learned { get; set; } = [];

    /// <summary>When <see cref="Learned"/> was read from the notebook; null if it never was.</summary>
    public DateTime? LearnedReadAt { get; set; }
}

/// <summary>The followed tracker: its id, the zone it was followed in, and since when (UTC).</summary>
[Serializable]
public sealed class SavedTracker
{
    public string Id { get; set; } = string.Empty;
    public uint Territory { get; set; }
    public DateTime Since { get; set; }
}

/// <summary>What the Map tab shows ("Calques" checkboxes).</summary>
[Serializable]
public sealed class MapLayers
{
    public bool Points { get; set; } = true;
    public bool AreaNames { get; set; } = true;
    public bool Party { get; set; } = true;
    public bool Fates { get; set; } = true;
    public bool Pops { get; set; } = true;
    public bool Nms { get; set; } = true;
    public bool Prep { get; set; } = true;
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>2 since the code moved to English names (version 1 is migrated by <see cref="ConfigurationMigration"/>).</summary>
    public int Version { get; set; } = 2;

    /// <summary>One state per character (ContentId), for players with several in Eureka.</summary>
    public Dictionary<ulong, CharacterState> Characters { get; set; } = [];

    /// <summary>Show the short "Eurêka" entry in the server info bar (click opens the plugin).</summary>
    public bool ServerInfoEntry { get; set; } = true;

    /// <summary>Open the Logos window when the Logos manipulator opens in game.</summary>
    public bool OpenWithManipulator { get; set; } = true;

    /// <summary>Announce (toast, chat with map link) every new FATE in a Eureka zone.</summary>
    public bool AlertFates { get; set; } = true;

    /// <summary>Set the game map flag on a new FATE.</summary>
    public bool FlagOnFate { get; set; } = true;

    /// <summary>Read the Eureka chat (shouts, LS, CWLS…) for pop announcements and tracker links.</summary>
    public bool ListenToChat { get; set; } = true;

    /// <summary>Set the flag on the position of a pop announced in the chat.</summary>
    public bool FlagOnChatPop { get; set; } = true;

    /// <summary>Game sound with the alerts.</summary>
    public bool AlertSound { get; set; } = true;

    /// <summary>Map tab layers.</summary>
    public MapLayers Layers { get; set; } = new();

    /// <summary>Eureka zone shown by the Map tab outside Eureka: the last one visited (Pyros at first).</summary>
    public uint LastZone { get; set; } = 795;

    /// <summary>Follow live the ffxiv-eureka.com tracker shouted in the zone chat.</summary>
    public bool FollowShoutedTracker { get; set; } = true;

    /// <summary>With no tracker shouted or picked after a few minutes in the zone, create one.</summary>
    public bool AutoCreateTracker { get; set; } = true;

    /// <summary>On a tracker whose password we hold (created by the plugin), report the NMs killed.</summary>
    public bool ReportKills { get; set; } = true;

    /// <summary>Tracker followed in the current zone, to resume it if the plugin restarts on the spot.</summary>
    public SavedTracker? CurrentTracker { get; set; }

    /// <summary>Passwords of the trackers created by the plugin (id → password), to write to them.</summary>
    public Dictionary<string, string> TrackerPasswords { get; set; } = [];

    /// <summary>Last ffxiv-eureka.com tracker seen in the chat (id, e.g. "bZFR-s").</summary>
    public string? LastTracker { get; set; }

    public CharacterState Character(ulong contentId, string name)
    {
        if (!Characters.TryGetValue(contentId, out var state))
            Characters[contentId] = state = new CharacterState();
        if (!string.IsNullOrEmpty(name)) state.Name = name;
        return state;
    }
}
