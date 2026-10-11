using System;
using System.Collections.Generic;
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
/// La page des éclats Logos, sur le modèle de ffxiv-eureka.com/logograms : le stock de mnemes à
/// gauche (lu dans le jeu), les 56 actions au centre, la fiche de l'action choisie à droite. Les
/// actions pas encore apprises qu'on peut fabriquer tout de suite ressortent en violet.
/// </summary>
public sealed class FenetreLogos : Window
{
    private static readonly Vector4 Violet = new(0.68f, 0.58f, 1f, 1f);
    private static readonly Vector4 Vert = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Rouge = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Gris = new(0.6f, 0.6f, 0.65f, 1f);

    private static readonly (string Lettre, string Nom)[] Roles =
        [("", "Tous les rôles"), ("T", "Tank"), ("H", "Soigneur"), ("M", "Mêlée"), ("P", "Distance physique"), ("C", "Distance magique")];

    private static readonly Dictionary<string, Vector4> CouleursTypes = new()
    {
        ["offensive"] = new(0.93f, 0.42f, 0.38f, 1f),
        ["protective"] = new(0.43f, 0.64f, 0.95f, 1f),
        ["curative"] = new(0.34f, 0.77f, 0.51f, 1f),
        ["tactical"] = new(0.89f, 0.71f, 0.27f, 1f),
        ["inimical"] = new(0.72f, 0.53f, 0.93f, 1f),
        ["mitigative"] = new(0.3f, 0.76f, 0.8f, 1f),
    };

    private enum Affichage { Toutes, PasApprises, Fabricables }

    private readonly CatalogueLogos catalogue;
    private readonly TextesJeu textes;
    private readonly ITextureProvider icones;
    private readonly Func<EtatPersonnage?> etatCourant;
    private readonly Func<bool> stockEnDirect;
    private readonly Action sauver;

    private Affichage affichage = Affichage.Toutes;
    private int role;
    private string recherche = string.Empty;
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
        Size = new Vector2(1180, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(820, 460), MaximumSize = new Vector2(4000, 3000) };
    }

    public override void Draw()
    {
        var etat = etatCourant();
        if (etat == null)
        {
            ImGui.TextWrapped("Connecte-toi à un personnage pour voir ses mnemes.");
            return;
        }

        var etats = catalogue.Actions.Select(a => Fabrication.Action(a, etat.Stock, etat.Apprises)).ToList();
        DessinerEntete(etat, etats);
        ImGui.Separator();

        var hauteur = ImGui.GetContentRegionAvail().Y;
        using (var gauche = ImRaii.Child("##mnemes", new Vector2(290, hauteur), true))
            if (gauche) DessinerMnemes(etat);
        ImGui.SameLine();
        var largeurCentre = Math.Max(300, ImGui.GetContentRegionAvail().X - 340);
        using (var centre = ImRaii.Child("##actions", new Vector2(largeurCentre, hauteur), true))
            if (centre) DessinerActions(etat, etats);
        ImGui.SameLine();
        using (var droite = ImRaii.Child("##fiche", new Vector2(0, hauteur), true))
            if (droite) DessinerFiche(etat, etats);
    }

    // ---------- en-tête : état du stock, compteurs, actions à apprendre ----------

    private void DessinerEntete(EtatPersonnage etat, List<EtatAction> etats)
    {
        if (stockEnDirect())
            ImGui.TextColored(Vert, "Stock lu en direct dans la fenêtre des éclats Logos.");
        else if (etat.StockLuLe is { } date)
            ImGui.TextColored(Gris, $"Stock du {date:dd/MM à HH:mm}. Ouvre la fenêtre des éclats Logos au manipulateur pour l'actualiser.");
        else
            ImGui.TextColored(Rouge, "Stock encore jamais lu : ouvre la fenêtre des éclats Logos au manipulateur Logos.");

        var aApprendre = etats.Where(e => e.AApprendre).ToList();
        ImGui.TextUnformatted($"{etat.Apprises.Count} / {catalogue.Actions.Count} apprises");
        ImGui.SameLine();
        ImGui.TextColored(Violet, $"· {aApprendre.Count} à apprendre maintenant");
        ImGui.SameLine();
        ImGui.TextColored(Gris, etat.ApprisesLuesLe is { } lues
            ? $"· apprises lues dans le journal le {lues:dd/MM à HH:mm}"
            : "· pour lire les apprises : journal des actions Logos (Drake), ou clic droit sur une action");

        if (aApprendre.Count == 0) return;
        ImGui.TextUnformatted("Pas encore apprises, fabricables :");
        foreach (var e in aApprendre)
        {
            ImGui.SameLine();
            var texte = textes.Action(e.Action.Action);
            if (Icone(texte.Icone, new Vector2(28), $"##haut{e.Action.Numero}")) choisie = e.Action.Numero;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{texte.NomCourt}  ×{e.Possibles}");
        }
    }

    // ---------- mnemes ----------

    private void DessinerMnemes(EtatPersonnage etat)
    {
        var utilite = Fabrication.Utilite(catalogue.Actions, etat.Apprises);
        var possedees = etat.Stock.Count(s => s.Value > 0);
        ImGui.TextUnformatted($"Mes mnemes ({possedees} / {catalogue.Mnemes.Count} sortes)");
        foreach (var eclat in catalogue.Eclats)
        {
            ImGui.Spacing();
            var nomEclat = textes.Objet(eclat.Objet);
            Icone(nomEclat.Icone, new Vector2(20));
            ImGui.SameLine();
            ImGui.TextColored(Gris, NomEclat(nomEclat.Nom));
            foreach (var objet in eclat.Mnemes)
            {
                var texte = textes.Objet(objet);
                var quantite = etat.Stock.GetValueOrDefault(objet);
                var utile = utilite.GetValueOrDefault(objet);
                var debut = ImGui.GetCursorScreenPos();
                if (utile > 0)
                {
                    var fin = debut + new Vector2(ImGui.GetContentRegionAvail().X, 26);
                    ImGui.GetWindowDrawList().AddRectFilled(debut, fin, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.05f)), 4);
                }
                ImGui.Dummy(new Vector2(4, 26));
                ImGui.SameLine(0, 0);
                PastilleType(catalogue.Mnemes[objet].Type);
                ImGui.SameLine();
                Icone(texte.Icone, new Vector2(24));
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4);
                if (quantite > 0) ImGui.TextUnformatted(texte.NomCourt);
                else ImGui.TextColored(Gris, texte.NomCourt);
                var nombre = quantite.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(nombre).X - 4);
                if (quantite > 0) ImGui.TextUnformatted(nombre);
                else ImGui.TextColored(Gris, nombre);
                ImGui.SetCursorScreenPos(debut);
                ImGui.InvisibleButton($"##mn{objet}", new Vector2(ImGui.GetContentRegionAvail().X, 26));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(utile > 0
                        ? $"{texte.Nom}\nSert à {utile} action(s) pas encore apprise(s)."
                        : texte.Nom);
            }
        }
    }

    /// <summary>« Éclat Logos d'apprenti non identifié » → « Éclat Logos d'apprenti ».</summary>
    private static string NomEclat(string nom) =>
        nom.EndsWith(" non identifié", StringComparison.Ordinal) ? nom[..^" non identifié".Length] : nom;

    private static void PastilleType(string type)
    {
        var p = ImGui.GetCursorScreenPos() + new Vector2(5, 13);
        ImGui.GetWindowDrawList().AddCircleFilled(p, 4, ImGui.GetColorU32(CouleursTypes.GetValueOrDefault(type, Gris)));
        ImGui.Dummy(new Vector2(10, 26));
    }

    // ---------- grille des actions ----------

    private void DessinerActions(EtatPersonnage etat, List<EtatAction> etats)
    {
        ImGui.RadioButton("Toutes", ref Unsafe(ref affichage), (int)Affichage.Toutes);
        ImGui.SameLine();
        ImGui.RadioButton("Pas apprises", ref Unsafe(ref affichage), (int)Affichage.PasApprises);
        ImGui.SameLine();
        ImGui.RadioButton("Fabricables", ref Unsafe(ref affichage), (int)Affichage.Fabricables);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##role", Roles[role].Nom))
        {
            for (var i = 0; i < Roles.Length; i++)
                if (ImGui.Selectable(Roles[i].Nom, i == role)) role = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##recherche", "Chercher une action", ref recherche, 64);

        var visibles = etats.Where(Visible).ToList();
        if (visibles.Count == 0)
        {
            ImGui.TextColored(Gris, "Aucune action pour ces filtres.");
            return;
        }

        var cellule = new Vector2(58, 66);
        var espace = ImGui.GetStyle().ItemSpacing.X;
        var colonnes = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + espace) / (cellule.X + espace)));
        for (var i = 0; i < visibles.Count; i++)
        {
            if (i % colonnes != 0) ImGui.SameLine();
            CaseAction(etat, visibles[i], cellule);
        }

        ImGui.Spacing();
        Legende();
    }

    private bool Visible(EtatAction e)
    {
        if (affichage == Affichage.PasApprises && e.Apprise) return false;
        if (affichage == Affichage.Fabricables && e.Possibles == 0) return false;
        if (Roles[role].Lettre.Length > 0 && !e.Action.Roles.Contains(Roles[role].Lettre, StringComparison.Ordinal)) return false;
        if (recherche.Length == 0) return true;
        var nom = textes.Action(e.Action.Action).Nom;
        return nom.Contains(recherche, StringComparison.CurrentCultureIgnoreCase);
    }

    private void CaseAction(EtatPersonnage etat, EtatAction e, Vector2 cellule)
    {
        var texte = textes.Action(e.Action.Action);
        var debut = ImGui.GetCursorScreenPos();
        var cliquee = ImGui.InvisibleButton($"##action{e.Action.Numero}", cellule);
        var survolee = ImGui.IsItemHovered();
        if (cliquee) choisie = e.Action.Numero;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) BasculerApprise(etat, e.Action.Action);

        var dessin = ImGui.GetWindowDrawList();
        var taille = new Vector2(44);
        var coin = debut + new Vector2((cellule.X - taille.X) / 2, 2);
        var teinte = e.Apprise && e.Possibles == 0 ? new Vector4(1, 1, 1, 0.35f) : Vector4.One;
        if (TextureIcone(texte.Icone) is { } image)
            dessin.AddImage(image, coin, coin + taille, Vector2.Zero, Vector2.One, ImGui.GetColorU32(teinte));

        Vector4? bord = e.AApprendre ? Violet : e.Possibles > 0 ? Vert : null;
        if (bord is { } couleur) dessin.AddRect(coin - new Vector2(2), coin + taille + new Vector2(2), ImGui.GetColorU32(couleur), 4, ImDrawFlags.None, e.AApprendre ? 3 : 2);
        if (choisie == e.Action.Numero) dessin.AddRect(debut, debut + cellule, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.8f)), 4);
        if (e.Apprise) dessin.AddText(coin + new Vector2(taille.X - 12, -2), ImGui.GetColorU32(Vert), "✓");

        var (etiquette, couleurTexte) = e.Possibles > 0
            ? ($"×{e.Possibles}", e.AApprendre ? Violet : Vert)
            : ($"-{e.PlusProche.Manquantes}", Gris);
        var largeur = ImGui.CalcTextSize(etiquette).X;
        dessin.AddText(debut + new Vector2((cellule.X - largeur) / 2, taille.Y + 4), ImGui.GetColorU32(couleurTexte), etiquette);

        if (survolee)
        {
            var etatTexte = e.AApprendre ? "Pas apprise, fabricable" : e.Apprise ? "Apprise" : "Pas apprise";
            var detail = e.Possibles > 0 ? $"jusqu'à ×{e.Possibles}" : $"il manque {e.PlusProche.Manquantes} mneme(s)";
            ImGui.SetTooltip($"{texte.Nom}\n{etatTexte} · {detail}");
        }
    }

    private static void Legende()
    {
        ImGui.TextColored(Violet, "■ pas apprise, fabricable");
        ImGui.SameLine();
        ImGui.TextColored(Vert, "■ apprise, fabricable");
        ImGui.SameLine();
        ImGui.TextColored(Gris, "-N : mnemes qui manquent pour la recette la plus proche");
    }

    // ---------- fiche de l'action ----------

    private void DessinerFiche(EtatPersonnage etat, List<EtatAction> etats)
    {
        var e = etats.FirstOrDefault(x => x.Action.Numero == choisie);
        if (e == null)
        {
            ImGui.TextWrapped("Choisis une action pour voir ses recettes.");
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
        lettres.Length == 5 ? "tous les rôles" : string.Join(", ", Roles.Where(r => r.Lettre.Length > 0 && lettres.Contains(r.Lettre, StringComparison.Ordinal)).Select(r => r.Nom.ToLowerInvariant()));

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

    /// <summary>Dessine une icône du jeu ; avec un identifiant, c'est un bouton (renvoie true au clic).</summary>
    private bool Icone(uint icone, Vector2 taille, string? bouton = null)
    {
        var image = TextureIcone(icone);
        if (bouton != null)
            return image is { } t ? ImGui.ImageButton(t, taille) : ImGui.Button(bouton, taille);
        if (image is { } texture) ImGui.Image(texture, taille);
        else ImGui.Dummy(taille);
        return false;
    }

    private static ref int Unsafe(ref Affichage valeur) => ref System.Runtime.CompilerServices.Unsafe.As<Affichage, int>(ref valeur);
}
