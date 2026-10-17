using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XivEureka.Windows;

/// <summary>The settings, opened by the configuration button in /xlplugins.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action save;
    private readonly Action languageChanged;

    public SettingsWindow(Configuration configuration, Action save, Action languageChanged)
        : base(Loc.T("Réglages", "Settings") + " · Xiv Eurêka###XivEurekaReglages", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.configuration = configuration;
        this.save = save;
        this.languageChanged = languageChanged;
        Size = new Vector2(380, 0);
    }

    public override void Draw()
    {
        var c = configuration;
        var language = (int)c.Language;
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo(Loc.T("Langue du plugin", "Plugin language") + "##language", ref language,
                [Loc.T("Celle du jeu", "The game's"), "Français", "English"]))
        {
            c.Language = (TextLanguage)language;
            save();
            languageChanged();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Celle du jeu : français pour un client français, anglais sinon.\nLes noms du jeu (objets, monstres, météo) restent dans la langue du client.",
                "The game's: French for a French client, English otherwise.\nNames from the game (items, monsters, weather) stay in the client's language."));
        Checkbox(Loc.T("« Eurêka » dans la barre d'infos serveur (clic : ouvrir)", "\"Eurêka\" in the server info bar (click: open)"),
            () => c.ServerInfoEntry, v => c.ServerInfoEntry = v);

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("Actions Logos", "Logos actions"));
        Checkbox(Loc.T("Ouvrir avec le manipulateur Logos", "Open with the Logos manipulator"), () => c.OpenWithManipulator, v => c.OpenWithManipulator = v);

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("Quoi taper", "What to hit"));
        var party = c.FarmPartySize;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt(Loc.T("Joueurs pour farmer", "Players farming") + "##party", ref party, 1, 8))
        {
            c.FarmPartySize = party;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Combien vous êtes à farmer les éclats, sans compter sur un groupe public.\nLe groupe réel compte s'il est plus grand.",
                "How many of you farm the logograms, without counting on a public group.\nThe actual party counts when it is bigger."));

        var xp = c.XpImportance;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt(Loc.T("Importance de l'XP", "Weight of experience") + "##xp", ref xp, 0, 100, "%d %%"))
        {
            c.XpImportance = xp;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Une source qui fait aussi monter de niveau compte pour moins de temps dans le classement.\nÀ 50 % : une heure qui rapporte autant d'XP qu'un farm de mobs à +3 niveaux compte pour une demi-heure.\nRien au niveau max de la zone.",
                "A source that also levels you up counts for less time in the ranking.\nAt 50%: an hour giving as much experience as grinding mobs 3 levels above counts as half an hour.\nNothing at the zone's max level."));

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("En zone d'Eurêka", "In Eureka"));
        Checkbox(Loc.T("Annoncer les nouvelles FATE (sauf les FATE lapin)", "Announce new FATEs (except bunny FATEs)"), () => c.AlertFates, v => c.AlertFates = v);
        Checkbox(Loc.T("Drapeau sur la carte pour une nouvelle FATE", "Map flag on a new FATE"), () => c.FlagOnFate, v => c.FlagOnFate = v);
        Checkbox(Loc.T("Lire le chat (pops annoncés, liens de tracker)", "Read the chat (called pops, tracker links)"), () => c.ListenToChat, v => c.ListenToChat = v);
        Checkbox(Loc.T("Drapeau sur la carte pour un pop annoncé", "Map flag on a called pop"), () => c.FlagOnChatPop, v => c.FlagOnChatPop = v);
        Checkbox(Loc.T("Son avec les alertes", "Sound with alerts"), () => c.AlertSound, v => c.AlertSound = v);

        ImGui.Spacing();
        ImGui.TextDisabled("Tracker ffxiv-eureka.com");
        Checkbox(Loc.T("Suivre le tracker crié dans le chat", "Follow the tracker called in chat"), () => c.FollowShoutedTracker, v => c.FollowShoutedTracker = v);
        Checkbox(Loc.T("Signaler les NM tués sur mon tracker", "Report NM kills on my tracker"), () => c.ReportKills, v => c.ReportKills = v);

        if (c.LastTracker is { } tracker)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(Loc.T($"Dernier tracker vu dans le chat : ffxiv-eureka.com/{tracker}", $"Last tracker seen in chat: ffxiv-eureka.com/{tracker}"));
        }
    }

    private void Checkbox(string label, Func<bool> read, Action<bool> write)
    {
        var value = read();
        if (!ImGui.Checkbox(label, ref value)) return;
        write(value);
        save();
    }
}
