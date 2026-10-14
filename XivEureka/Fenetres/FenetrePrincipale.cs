using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XivEureka.Fenetres;

/// <summary>La fenêtre du plugin : onglets « Actions Logos » et « Carte ».</summary>
public sealed class FenetrePrincipale : Window
{
    public enum Onglet
    {
        Logos,
        Carte,
    }

    private readonly OngletLogos logos;
    private readonly OngletCarte carte;
    private Onglet? aSelectionner;

    public FenetrePrincipale(OngletLogos logos, OngletCarte carte)
        : base("Xiv Eurêka###XivEurekaLogos")
    {
        this.logos = logos;
        this.carte = carte;
        Size = new Vector2(1000, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600 + OngletLogos.LargeurFiche, 420),
            MaximumSize = new Vector2(4000, 3000),
        };
    }

    /// <summary>Ouvre la fenêtre sur l'onglet voulu.</summary>
    public void Ouvrir(Onglet onglet)
    {
        IsOpen = true;
        aSelectionner = onglet;
    }

    public override void Draw()
    {
        using var barre = ImRaii.TabBar("##onglets");
        if (!barre) return;
        DessinerOnglet("Actions Logos", Onglet.Logos, logos.Dessiner);
        DessinerOnglet("Carte", Onglet.Carte, carte.Dessiner);
        aSelectionner = null;
    }

    private void DessinerOnglet(string titre, Onglet onglet, System.Action dessiner)
    {
        var drapeaux = aSelectionner == onglet ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var item = ImRaii.TabItem(titre, drapeaux);
        if (item) dessiner();
    }
}
