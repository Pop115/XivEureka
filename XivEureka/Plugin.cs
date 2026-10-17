using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivEureka.Logos;
using XivEureka.Tracker;
using XivEureka.Windows;
using XivEureka.Zones;

namespace XivEureka;

/// <summary>
/// Dalamud entry point: wires the windows, the command, the mneme stock reading, the zone companion
/// and the tracker session. What to craft is computed in <see cref="Crafting"/>, the memory read is
/// in <see cref="ShardReader"/>.
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
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;

    private const string Command = "/xiveureka";
    private const string ManipulatorWindow = ManipulatorReader.Window;
    private const string LogosAetherWindow = "EurekaMagiciteItemAtherList";
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(500);

    private readonly Configuration configuration;
    private readonly LogosCatalog catalog;
    private readonly GameTexts texts;
    private readonly WindowSystem windows = new("XivEureka");
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly ZoneCompanion companion;
    private readonly TrackerSession tracker;
    private readonly ServerInfoEntry serverInfoEntry;
    private readonly LogosTracker logosTracker;
    private DateTime nextRead = DateTime.MinValue;
    private bool liveStock;

    public Plugin()
    {
        configuration = LoadConfiguration();
        // The client language is static data (no game state): safe off the main thread.
        Loc.Apply(configuration.Language, DataManager.Language);
        catalog = LogosCatalog.Embedded();
        logosTracker = new LogosTracker(catalog);
        companion = new ZoneCompanion(configuration, ClientState, ObjectTable, Framework, FateTable, ChatGui, ToastGui, Log, Save);
        var nmCatalog = NmCatalog.Embedded();
        tracker = new TrackerSession(configuration, ClientState, ObjectTable, Framework, ChatGui, Log, nmCatalog, Save);
        companion.TrackerShouted += tracker.OnTrackerShouted;
        companion.FateSucceeded += tracker.OnFateSucceeded;

        texts = new GameTexts(DataManager);
        var weatherNames = new WeatherNames(DataManager);
        var logosTab = new LogosTab(catalog, texts, TextureProvider, CurrentCharacter, () => liveStock, Save);
        var mapTab = new MapTab(configuration, new MapReader(DataManager),
            new MapStateReader(ClientState, ObjectTable, PartyList, FateTable, companion.RecentPops),
            nmCatalog,
            new GameNames(texts.Npc, w => weatherNames[w], i => texts.Item(i).Name),
            TextureProvider,
            new MapSources(EndedFates, tracker.PreppedByFate, ElementalLevel, UsefulShards, tracker,
                (territory, map, position) => InGameMap.SetFlag(territory, map, position, open: false)),
            Save);
        var huntTab = new HuntTab(catalog, nmCatalog, FarmCatalog.Embedded(), texts, TextureProvider, CurrentCharacter,
            mapTab.HuntContextNow, w => weatherNames[w],
            nm =>
            {
                mapTab.ShowNm(nm);
                mainWindow.Open(MainWindow.Tab.Map);
            },
            (label, places) =>
            {
                mapTab.ShowFarm(label, places);
                mainWindow.Open(MainWindow.Tab.Map);
            },
            mapTab.FlagAt);
        var relics = Progress.RelicCatalog.Embedded();
        var progressTab = new ProgressTab(relics, new Progress.ProgressReader(GameInventory, DataManager, ClientState, relics), texts,
            TextureProvider, CurrentCharacter, ElementalLevel, Save);
        mainWindow = new MainWindow(logosTab, huntTab, mapTab, progressTab);
        windows.AddWindow(mainWindow);
        settingsWindow = new SettingsWindow(configuration, Save, () => Loc.Apply(configuration.Language, DataManager.Language));
        windows.AddWindow(settingsWindow);
        serverInfoEntry = new ServerInfoEntry(configuration, DtrBar, ClientState, Framework, weatherNames, mainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.T("Ouvre la fenêtre du plugin. « /xiveureka carte » : sur la carte. « /xiveureka releve » : relevé de développement. « /xiveureka niveau » : valeurs de contenu.",
                "Opens the plugin window. \"/xiveureka map\": on the map. \"/xiveureka releve\": development capture. \"/xiveureka niveau\": content values."),
        });

        Framework.Update += OnFrameworkUpdate;
        ChatGui.LogMessage += OnLogMessage;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ManipulatorWindow, OnManipulatorOpened);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnWindowOpened);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += settingsWindow.Toggle;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= settingsWindow.Toggle;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ManipulatorWindow, OnManipulatorOpened);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnWindowOpened);
        Framework.Update -= OnFrameworkUpdate;
        ChatGui.LogMessage -= OnLogMessage;
        companion.TrackerShouted -= tracker.OnTrackerShouted;
        companion.FateSucceeded -= tracker.OnFateSucceeded;
        serverInfoEntry.Dispose();
        tracker.Dispose();
        companion.Dispose();
        CommandManager.RemoveHandler(Command);
        windows.RemoveAllWindows();
    }

    /// <summary>
    /// Loads the configuration, migrating a version 1 file first (Dalamud could not read it: the
    /// class names it stores have changed). The old file is kept next to it as <c>.v1.bak</c>.
    /// </summary>
    private static Configuration LoadConfiguration()
    {
        var file = PluginInterface.ConfigFile;
        if (file.Exists)
        {
            var json = File.ReadAllText(file.FullName);
            if (ConfigurationMigration.IsLegacy(json))
            {
                var migrated = ConfigurationMigration.FromLegacy(json);
                File.Copy(file.FullName, file.FullName + ".v1.bak", overwrite: true);
                PluginInterface.SavePluginConfig(migrated);
                Log.Information("Configuration migrated from version 1 ({Count} character(s))", migrated.Characters.Count);
                return migrated;
            }
        }
        return PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    }

    private CharacterState? CurrentCharacter() =>
        PlayerState.IsLoaded && PlayerState.ContentId != 0
            ? configuration.Character(PlayerState.ContentId, PlayerState.CharacterName)
            : null;

    /// <summary>
    /// Ended NM FATEs and when (UTC): those seen ending in the instance and those from the followed
    /// tracker; for a given FATE, the latest.
    /// </summary>
    private IReadOnlyDictionary<ushort, DateTime> EndedFates()
    {
        var ended = new Dictionary<ushort, DateTime>(companion.EndedFates());
        foreach (var (fate, when) in tracker.KillsByFate())
            if (!ended.TryGetValue(fate, out var seen) || when > seen) ended[fate] = when;
        return ended;
    }

    /// <summary>Logos shards that would move not-yet-learned actions forward (empty without a read stock).</summary>
    private IReadOnlyDictionary<uint, int> UsefulShards()
    {
        var state = CurrentCharacter();
        return state == null ? new Dictionary<uint, int>() : LogosBoard.Of(catalog, state).UsefulShards;
    }

    /// <summary>
    /// Eureka elemental level: the player's content value #2 (step-0 research, to confirm in game).
    /// Null if the value is not a plausible level.
    /// </summary>
    private static unsafe int? ElementalLevel()
    {
        var state = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (state == null) return null;
        var level = (int)state->GetContentValue(2);
        return level is >= 1 and <= 60 ? level : null;
    }

    /// <summary>Development: the player's content values 0 to 9, to check that #2 is the elemental level.</summary>
    private static unsafe void PrintContentValues()
    {
        var state = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        if (state == null) return;
        var values = Enumerable.Range(0, 10).Select(i => $"{i}={state->GetContentValue((uint)i)}");
        ChatGui.Print(Loc.T($"Valeurs de contenu : {string.Join(" ", values)} (niveau élémentaire attendu en 2)",
            $"Content values: {string.Join(" ", values)} (elemental level expected at 2)"), "Xiv Eurêka");
    }

    private void Save() => PluginInterface.SavePluginConfig(configuration);

    private void ToggleMainWindow() => mainWindow.Toggle();

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim();
        if (argument.StartsWith("rel", StringComparison.OrdinalIgnoreCase))
        {
            WriteCapture(ShardReader.Window);
            WriteCapture(ManipulatorWindow);
            WriteCapture(LogosAetherWindow);
            return;
        }
        if (argument.StartsWith("niv", StringComparison.OrdinalIgnoreCase))
        {
            PrintContentValues();
            return;
        }
        if (argument.StartsWith("carte", StringComparison.OrdinalIgnoreCase) || argument.StartsWith("map", StringComparison.OrdinalIgnoreCase))
        {
            mainWindow.Open(MainWindow.Tab.Map);
            return;
        }
        ToggleMainWindow();
    }

    /// <summary>
    /// Twice a second: what the shard list, the manipulator and the notebook show goes to the
    /// <see cref="LogosTracker"/>, saved when it changed. So the plugin window stays right even away
    /// from the manipulator.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (DateTime.UtcNow < nextRead) return;
        nextRead = DateTime.UtcNow + ReadInterval;

        var state = CurrentCharacter();
        if (state == null) return;
        liveStock = ShardReader.IsWindowOpen(GameGui.GetAddonByName(ShardReader.Window, 1).Address);
        var snapshot = new LogosSnapshot(
            liveStock ? ShardReader.Read() : null,
            ManipulatorReader.Read(GameGui.GetAddonByName(ManipulatorReader.Window, 1).Address),
            NotebookReader.Read(GameGui.GetAddonByName(NotebookReader.Window, 1).Address, catalog));
        if (logosTracker.Update(state, snapshot, DateTime.Now)) Save();
    }

    /// <summary>
    /// An action just learned at the manipulator is added at once, without waiting for the
    /// notebook to be opened again (which stays authoritative).
    /// </summary>
    private void OnLogMessage(ILogMessage message)
    {
        if (message.LogMessageId != LearnedMessage.Id) return;
        var state = CurrentCharacter();
        if (state == null) return;
        var names = catalog.Actions.Select(a => (a.Action, texts.Action(a.Action).Name));
        for (var i = 0; i < message.ParameterCount; i++)
        {
            if (!message.TryGetStringParameter(i, out var text)) continue;
            if (LearnedMessage.Match(text.ExtractText(), names) is not { } action) continue;
            if (state.Learned.Add(action)) Save();
            return;
        }
        Log.Warning("Learned Logos action not recognised: {Message}", message.FormatLogMessageForDebugging());
    }

    private void OnManipulatorOpened(AddonEvent type, AddonArgs args)
    {
        if (configuration.OpenWithManipulator) mainWindow.Open(MainWindow.Tab.Logos);
    }

    /// <summary>Automatic capture of every Eureka window that opens (development).</summary>
    private void OnWindowOpened(AddonEvent type, AddonArgs args)
    {
        if (args.AddonName.StartsWith("Eureka", StringComparison.Ordinal)) WriteCapture(args.AddonName);
    }

    private unsafe void WriteCapture(string window)
    {
        try
        {
            var path = Diagnostic.Write(PluginInterface.GetPluginConfigDirectory(), window,
                (AtkUnitBase*)GameGui.GetAddonByName(window, 1).Address);
            Log.Information("Capture written: {Path}", path);
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not capture {Window}", window);
        }
    }
}
