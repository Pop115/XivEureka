using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using XivEureka.Logos;

namespace XivEureka.Fenetres;

/// <summary>
/// La page des éclats Logos, sur le modèle de ffxiv-eureka.com/logograms. À gauche, la grille du
/// journal (10 par ligne, dans l'ordre du jeu) : icône en couleur si l'action est apprise, case
/// sombre sinon, case claire si elle n'est pas apprise mais fabricable ; en jaune, combien on peut en
/// fabriquer. En dessous, les mnemes par éclat avec les actions où chacune sert. À droite, la fiche
/// de l'action choisie.
/// </summary>
public sealed class FenetreLogos : Window
{
    private const int Colonnes = 10;
    private const float TailleCase = 44;
    private const float Ecart = 6;
    private const float LargeurFiche = 330;

    private static readonly Vector4 Jaune = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Vert = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Rouge = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Gris = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Vector4 CaseVide = new(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Vector4 BordVide = new(0.3f, 0.3f, 0.34f, 1f);
    private static readonly Vector4 CaseClaire = new(0.92f, 0.92f, 0.95f, 1f);

    private static readonly (string Lettre, string Nom)[] Roles =
        [("T", "tank"), ("H", "soigneur"), ("M", "mêlée"), ("P", "distance physique"), ("C", "distance magique")];

    private readonly CatalogueLogos catalogue;
    private readonly TextesJeu textes;
    private readonly ITextureProvider icones;
    private readonly Func<EtatPersonnage?> etatCourant;
    private readonly Func<bool> stockEnDirect;
    private readonly Action sauver;

    private int? choisie;

    public FenetreLogos(CatalogueLogos catalogue, TextesJeu textes, ITextureProvider icones,
        Func<EtatPersonnage?> etatCourant, Func<bool> stockEnDirect, Action sauver)
        : base("Actions Logos · Xiv Eurêka###XivEurekaLogos")
    {
        this.catalogue = catalogue;
        this.textes = textes;
        this.icones = icones;
        this.etatCourant = etatCourant;
        this.stockEnDirect = stockEnDirect;
        this.sauver = sauver;
        Size = new Vector2(1000, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(600 + LargeurFiche, 420), MaximumSize = new Vector2(4000, 3000) };
    }

    public override void Draw()
    {
        var etat = etatCourant();
        if (etat == null)
        {
            ImGui.TextWrapped("Connecte-toi à un personnage pour voir ses mnemes.");
            return;
        }

        var etats = catalogue.Actions.ToDictionary(a => a.Numero, a => Fabrication.Action(a, etat.Stock, etat.Apprises));
        var hauteur = ImGui.GetContentRegionAvail().Y;
        using (var gauche = ImRaii.Child("##journal", new Vector2(ImGui.GetContentRegionAvail().X - LargeurFiche - Ecart, hauteur), false))
        {
            if (gauche)
            {
                DessinerEntete(etat, etats);
                DessinerGrille(etat, etats);
                ImGui.Spacing();
                ImGui.Separator();
                DessinerMnemes(etat, etats);
            }
        }
        ImGui.SameLine(0, Ecart);
        using (var droite = ImRaii.Child("##fiche", new Vector2(0, hauteur), true))
            if (droite) DessinerFiche(etat, etats);
    }

    // ---------- en-tête ----------

    private void DessinerEntete(EtatPersonnage etat, Dictionary<int, EtatAction> etats)
    {
        ImGui.TextUnformatted("ACTIONS LOGOS");
        ImGui.SameLine();
        ImGui.TextColored(Jaune, $"{etat.Apprises.Count}");
        ImGui.SameLine(0, 0);
        ImGui.TextUnformatted($"/{catalogue.Actions.Count}");
        var aApprendre = etats.Values.Count(e => e.AApprendre);
        if (aApprendre > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Jaune, $"· {aApprendre} à apprendre maintenant (cases qui clignotent)");
        }

        ImGui.TextColored(Gris, Provenance(etat));
        ImGui.TextColored(Gris, "Clic : recettes de l'action. Clic droit : apprise / pas apprise.");
        ImGui.Spacing();
    }

    private string Provenance(EtatPersonnage etat)
    {
        var stock = stockEnDirect() ? "Mnemes lues en direct"
            : etat.StockLuLe is { } s ? $"Mnemes du {s:dd/MM HH:mm}"
            : "Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur";
        var apprises = etat.ApprisesLuesLe is { } a ? $"apprises du {a:dd/MM HH:mm}"
            : "apprises jamais lues : ouvre le journal chez Drake";
        return $"{stock} · {apprises}";
    }

    // ---------- grille du journal ----------

    private void DessinerGrille(EtatPersonnage etat, Dictionary<int, EtatAction> etats)
    {
        var actions = catalogue.Actions.OrderBy(a => a.Numero).ToList();
        for (var i = 0; i < actions.Count; i++)
        {
            if (i % Colonnes != 0) ImGui.SameLine(0, Ecart);
            CaseAction(etat, etats[actions[i].Numero], TailleCase);
        }
    }

    /// <summary>Une case d'action, réutilisée en petit dans la liste des mnemes.</summary>
    private void CaseAction(EtatPersonnage etat, EtatAction e, float taille, string suffixe = "")
    {
        var texte = textes.Action(e.Action.Action);
        var coin = ImGui.GetCursorScreenPos();
        var fin = coin + new Vector2(taille);
        if (ImGui.InvisibleButton($"##a{e.Action.Numero}{suffixe}", new Vector2(taille))) choisie = e.Action.Numero;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) BasculerApprise(etat, e.Action.Action);
        var survolee = ImGui.IsItemHovered();

        var dessin = ImGui.GetWindowDrawList();
        var arrondi = taille / 8;
        if (e.Apprise)
        {
            if (TextureIcone(texte.Icone) is { } image) dessin.AddImageRounded(image, coin, fin, Vector2.Zero, Vector2.One, 0xFFFFFFFF, arrondi);
        }
        else if (e.Possibles > 0)
        {
            // À apprendre : icône en couleur dans un halo jaune qui pulse, pour sauter aux yeux.
            var pulsation = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 4f));
            for (var halo = 1; halo <= 4; halo++)
                dessin.AddRect(coin - new Vector2(halo * 1.5f), fin + new Vector2(halo * 1.5f),
                    ImGui.GetColorU32(Jaune with { W = pulsation * (0.9f - (halo * 0.18f)) }), arrondi + halo, ImDrawFlags.None, 2);
            dessin.AddRectFilled(coin, fin, ImGui.GetColorU32(CaseClaire), arrondi);
            if (TextureIcone(texte.Icone) is { } image) dessin.AddImageRounded(image, coin, fin, Vector2.Zero, Vector2.One, 0xFFFFFFFF, arrondi);
            if (taille >= 32)
            {
                var pastille = coin + new Vector2(7, 7);
                dessin.AddCircleFilled(pastille, 7, ImGui.GetColorU32(Jaune));
                dessin.AddText(pastille - (ImGui.CalcTextSize("!") / 2), 0xFF000000, "!");
            }
        }
        else
        {
            dessin.AddRectFilled(coin, fin, ImGui.GetColorU32(CaseVide), arrondi);
            dessin.AddRect(coin, fin, ImGui.GetColorU32(BordVide), arrondi);
        }
        if (choisie == e.Action.Numero) dessin.AddRect(coin - new Vector2(2), fin + new Vector2(2), ImGui.GetColorU32(Jaune), arrondi, ImDrawFlags.None, 2);

        if (e.Possibles > 0 && taille >= 32)
        {
            var nombre = e.Possibles.ToString(CultureInfo.InvariantCulture);
            var position = fin - ImGui.CalcTextSize(nombre) - new Vector2(2, 0);
            dessin.AddText(position + new Vector2(1, 1), 0xFF000000, nombre);
            dessin.AddText(position + new Vector2(-1, 1), 0xFF000000, nombre);
            dessin.AddText(position, ImGui.GetColorU32(Jaune), nombre);
        }

        if (survolee)
        {
            var etatTexte = e.Apprise ? "apprise" : "pas apprise";
            var detail = e.Possibles > 0 ? $"fabricable ×{e.Possibles}" : $"il manque {e.PlusProche.Manquantes} mneme(s)";
            ImGui.SetTooltip($"{texte.Nom}\n{etatTexte} · {detail}");
        }
    }

    // ---------- mnemes ----------

    private void DessinerMnemes(EtatPersonnage etat, Dictionary<int, EtatAction> etats)
    {
        ImGui.TextUnformatted("MNEMES");
        ImGui.SameLine();
        ImGui.TextColored(Gris, "par éclat Logos · actions qui s'en servent");
        foreach (var eclat in catalogue.Eclats)
        {
            ImGui.Spacing();
            var nomEclat = textes.Objet(eclat.Objet);
            Icone(nomEclat.Icone, new Vector2(22));
            ImGui.SameLine();
            AligneTexte(22);
            ImGui.TextColored(Jaune, NomEclat(nomEclat.Nom));
            foreach (var objet in eclat.Mnemes) LigneMneme(etat, etats, objet);
        }
    }

    private void LigneMneme(EtatPersonnage etat, Dictionary<int, EtatAction> etats, uint objet)
    {
        const float hauteur = 28;
        var texte = textes.Objet(objet);
        var quantite = etat.Stock.GetValueOrDefault(objet);

        ImGui.Dummy(new Vector2(12, hauteur));
        ImGui.SameLine(0, 0);
        Icone(texte.Icone, new Vector2(hauteur));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(texte.Nom);
        ImGui.SameLine();
        var debutNom = ImGui.GetCursorPosX();
        AligneTexte(hauteur);
        if (quantite > 0) ImGui.TextColored(Jaune, quantite.ToString(CultureInfo.InvariantCulture));
        else ImGui.TextColored(Gris, "0");
        ImGui.SameLine(debutNom + 34);
        AligneTexte(hauteur);
        if (quantite > 0) ImGui.TextUnformatted(texte.NomCourt);
        else ImGui.TextColored(Gris, texte.NomCourt);

        var utilisees = catalogue.Actions.Where(a => a.Recettes.Any(r => r.Contains(objet))).OrderBy(a => a.Numero);
        ImGui.SameLine(debutNom + 230);
        var premiere = true;
        foreach (var action in utilisees)
        {
            if (!premiere) ImGui.SameLine(0, 3);
            premiere = false;
            CaseAction(etat, etats[action.Numero], hauteur, $"m{objet}");
        }
    }

    private static void AligneTexte(float hauteurLigne) =>
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((hauteurLigne - ImGui.GetTextLineHeight()) / 2));

    /// <summary>« Éclat Logos d'apprenti non identifié » → « Éclat Logos d'apprenti ».</summary>
    private static string NomEclat(string nom) =>
        nom.EndsWith(" non identifié", StringComparison.Ordinal) ? nom[..^" non identifié".Length] : nom;

    // ---------- fiche de l'action ----------

    private void DessinerFiche(EtatPersonnage etat, Dictionary<int, EtatAction> etats)
    {
        if (choisie is not { } numero || !etats.TryGetValue(numero, out var e))
        {
            ImGui.TextWrapped("Clique une action pour voir sa description et ses recettes, avec ce que tu as de chaque mneme.");
            return;
        }
        var texte = textes.Action(e.Action.Action);
        Icone(texte.Icone, new Vector2(48));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(texte.NomCourt);
        ImGui.TextColored(Gris, $"n° {e.Action.Numero} · {NomsRoles(e.Action.Roles)}");
        if (e.Action.Utilisations is { } utilisations) ImGui.TextColored(Gris, $"{utilisations} utilisation(s)");
        ImGui.EndGroup();

        var apprise = e.Apprise;
        if (ImGui.Checkbox("Apprise", ref apprise)) BasculerApprise(etat, e.Action.Action);

        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(texte.Description);
        ImGui.PopTextWrapPos();
        ImGui.Separator();
        ImGui.TextUnformatted("Recettes");
        foreach (var recette in e.Recettes)
        {
            ImGui.Spacing();
            if (recette.Possibles > 0) ImGui.TextColored(Vert, $"×{recette.Possibles} possible(s)");
            else ImGui.TextColored(Gris, $"il manque {recette.Manquantes}");
            foreach (var (objet, besoin) in recette.Besoins)
            {
                var mneme = textes.Objet(objet);
                var dispo = etat.Stock.GetValueOrDefault(objet);
                Icone(mneme.Icone, new Vector2(22));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(mneme.Nom);
                ImGui.SameLine();
                var ligne = $"{(besoin > 1 ? $"{besoin}× " : "")}{mneme.NomCourt}  {dispo}/{besoin}";
                if (dispo >= besoin) ImGui.TextUnformatted(ligne);
                else ImGui.TextColored(Rouge, ligne);
            }
        }
    }

    private static string NomsRoles(string lettres) =>
        lettres.Length == Roles.Length ? "tous les rôles"
            : string.Join(", ", Roles.Where(r => lettres.Contains(r.Lettre, StringComparison.Ordinal)).Select(r => r.Nom));

    private void BasculerApprise(EtatPersonnage etat, uint action)
    {
        if (!etat.Apprises.Remove(action)) etat.Apprises.Add(action);
        sauver();
    }

    // ---------- icônes ----------

    private ImTextureID? TextureIcone(uint icone)
    {
        if (icone != 0 && icones.TryGetFromGameIcon(new GameIconLookup(icone), out var partagee) && partagee.TryGetWrap(out var image, out _))
            return image.Handle;
        return null;
    }

    private void Icone(uint icone, Vector2 taille)
    {
        if (TextureIcone(icone) is { } texture) ImGui.Image(texture, taille);
        else ImGui.Dummy(taille);
    }
}
