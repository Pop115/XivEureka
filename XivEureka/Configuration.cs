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

    /// <summary>
    /// Relic items (Eurekan weapons and armor) seen in the character's containers, kept so that one
    /// put away (retainer, glamour dresser) still counts. The highest stage seen is what counts.
    /// </summary>
    public HashSet<uint> RelicItems { get; set; } = [];

    /// <summary>
    /// "Quoi acheter": crafts wanted on top of learning the board (action → how many times), for
    /// the actions worth crafting again (Bloodbath L, Spirit of the Remembered…).
    /// </summary>
    public Dictionary<uint, int> ExtraCrafts { get; set; } = [];

    /// <summary>In-zone story steps ticked by the player ("territory:level"): the game does not expose them.</summary>
    public HashSet<string> StorySteps { get; set; } = [];
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

    /// <summary>Also NMs that cannot be done now (respawning, or waiting for a weather or the night).</summary>
    public bool UnavailableNms { get; set; }
}

/// <summary>What "Quoi taper" ranks by: the Logos actions only, both, or the experience only.</summary>
public enum HuntMode
{
    Logos,
    Mix,
    Xp,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>2 since the code moved to English names (version 1 is migrated by <see cref="ConfigurationMigration"/>).</summary>
    public int Version { get; set; } = 2;

    /// <summary>"Quoi taper": how many levels above you you hit (0 to 6; experience grows up to +6).</summary>
    public int LevelGap { get; set; } = Logos.HuntOptions.DefaultGap;

    /// <summary>"Quoi taper": share the kill times with the party (<see cref="FarmPartySize"/>, or the actual party when bigger).</summary>
    public bool CountParty { get; set; }

    /// <summary>Players farming together when <see cref="CountParty"/> is ticked (no public group counted on).</summary>
    public int FarmPartySize { get; set; } = 2;

    /// <summary>What "Quoi taper" ranks by (the tab's buttons).</summary>
    public HuntMode HuntMode { get; set; } = HuntMode.Mix;

    /// <summary>
    /// In <see cref="HuntMode.Mix"/>: how much leveling up counts (percent: 25 mostly Logos, 50
    /// balanced, 75 mostly experience; none at the zone's max level).
    /// </summary>
    public int XpImportance { get; set; } = 50;

    /// <summary>"Quoi acheter": also plan the actions not learned yet (complete the board).</summary>
    public bool ShopCompleteBoard { get; set; } = true;

    /// <summary>
    /// "Quoi acheter": data centers left out of the prices (those of the home world's region not
    /// listed here are read; all of them by default, as one can travel to any).
    /// </summary>
    public HashSet<string> ShopSkippedDataCenters { get; set; } = [];

    /// <summary>Plugin text language: the game client's (French or English), or forced.</summary>
    public TextLanguage Language { get; set; } = TextLanguage.Auto;

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
