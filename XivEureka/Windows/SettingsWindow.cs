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

    public SettingsWindow(Configuration configuration, Action save)
        : base("Réglages · Xiv Eurêka###XivEurekaReglages", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.configuration = configuration;
        this.save = save;
        Size = new Vector2(380, 0);
    }

    public override void Draw()
    {
        var c = configuration;
        ImGui.TextDisabled("Actions Logos");
        Checkbox("Ouvrir avec le manipulateur Logos", () => c.OpenWithManipulator, v => c.OpenWithManipulator = v);

        ImGui.Spacing();
        ImGui.TextDisabled("En zone d'Eurêka");
        Checkbox("Annoncer les nouvelles FATE (sauf les FATE lapin)", () => c.AlertFates, v => c.AlertFates = v);
        Checkbox("Drapeau sur la carte pour une nouvelle FATE", () => c.FlagOnFate, v => c.FlagOnFate = v);
        Checkbox("Lire le chat (pops annoncés, liens de tracker)", () => c.ListenToChat, v => c.ListenToChat = v);
        Checkbox("Drapeau sur la carte pour un pop annoncé", () => c.FlagOnChatPop, v => c.FlagOnChatPop = v);
        Checkbox("Son avec les alertes", () => c.AlertSound, v => c.AlertSound = v);

        ImGui.Spacing();
        ImGui.TextDisabled("Tracker ffxiv-eureka.com");
        Checkbox("Suivre le tracker crié dans le chat", () => c.FollowShoutedTracker, v => c.FollowShoutedTracker = v);
        Checkbox("Créer un tracker si aucun n'est crié en 3 min", () => c.AutoCreateTracker, v => c.AutoCreateTracker = v);
        Checkbox("Signaler les NM tués sur mon tracker", () => c.ReportKills, v => c.ReportKills = v);

        if (c.LastTracker is { } tracker)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Dernier tracker vu dans le chat : ffxiv-eureka.com/{tracker}");
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
