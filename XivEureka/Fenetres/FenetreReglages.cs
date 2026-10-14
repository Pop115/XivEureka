using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XivEureka.Fenetres;

/// <summary>Les réglages, ouverts par le bouton de configuration de /xlplugins.</summary>
public sealed class FenetreReglages : Window
{
    private readonly Configuration configuration;
    private readonly Action sauver;

    public FenetreReglages(Configuration configuration, Action sauver)
        : base("Réglages · Xiv Eurêka###XivEurekaReglages", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.configuration = configuration;
        this.sauver = sauver;
        Size = new Vector2(380, 0);
    }

    public override void Draw()
    {
        ImGui.TextDisabled("Actions Logos");
        Case("Ouvrir avec le manipulateur Logos", () => configuration.OuvrirAvecLeManipulateur, v => configuration.OuvrirAvecLeManipulateur = v);

        ImGui.Spacing();
        ImGui.TextDisabled("En zone d'Eurêka");
        Case("Annoncer les nouvelles FATE (sauf les FATE lapin)", () => configuration.AlerterFates, v => configuration.AlerterFates = v);
        Case("Drapeau sur la carte pour une nouvelle FATE", () => configuration.DrapeauSurFate, v => configuration.DrapeauSurFate = v);
        Case("Lire le chat (pops annoncés, liens de tracker)", () => configuration.EcouterChat, v => configuration.EcouterChat = v);
        Case("Drapeau sur la carte pour un pop annoncé", () => configuration.DrapeauSurChat, v => configuration.DrapeauSurChat = v);
        Case("Son avec les alertes", () => configuration.SonAlertes, v => configuration.SonAlertes = v);

        ImGui.Spacing();
        ImGui.TextDisabled("Tracker ffxiv-eureka.com");
        Case("Suivre le tracker crié dans le chat", () => configuration.SuivreTrackerCrie, v => configuration.SuivreTrackerCrie = v);
        Case("Créer un tracker si aucun n'est crié en 3 min", () => configuration.CreerTrackerAuto, v => configuration.CreerTrackerAuto = v);
        Case("Signaler les NM tués sur mon tracker", () => configuration.SignalerMorts, v => configuration.SignalerMorts = v);

        if (configuration.DernierTracker is { } tracker)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Dernier tracker vu dans le chat : ffxiv-eureka.com/{tracker}");
        }
    }

    private void Case(string libelle, Func<bool> lire, Action<bool> ecrire)
    {
        var valeur = lire();
        if (!ImGui.Checkbox(libelle, ref valeur)) return;
        ecrire(valeur);
        sauver();
    }
}
