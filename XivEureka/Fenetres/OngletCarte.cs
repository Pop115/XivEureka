using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Zones;

namespace XivEureka.Fenetres;

/// <summary>
/// Onglet « Carte » : la carte du jeu de la zone d'Eurêka, redessinée par le plugin pour qu'on
/// voie d'un coup d'œil ce qui vient de lui. On y retrouve ce que montre la carte du jeu (repères
/// fixes, noms de zone, soi, le groupe, le drapeau, les FATE en cours) et ce que sait le plugin
/// (pops du chat). Molette : zoom autour de la souris ; glisser : déplacer.
/// </summary>
public sealed class OngletCarte
{
    private const float LargeurPanneau = 340;
    private const float ZoomMax = 4f;
    private const uint IconeDrapeau = 60561;

    private static readonly Vector4 Jaune = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Gris = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly uint Blanc = 0xFFFFFFFF;
    private static readonly uint Noir = 0xFF000000;
    private static readonly uint BleuGroupe = Couleur(0.29f, 0.64f, 1f);
    private static readonly uint RougeFate = Couleur(0.85f, 0.25f, 0.3f);
    private static readonly uint RougePop = Couleur(1f, 0.3f, 0.3f);
    private static readonly uint CremeMoi = Couleur(1f, 0.96f, 0.76f);
    private static readonly uint EncreZone = Couleur(0.33f, 0.25f, 0.13f);

    private readonly Configuration configuration;
    private readonly LecteurCartes cartes;
    private readonly LecteurEtatCarte lecteurEtat;
    private readonly NomsMeteo nomsMeteo;
    private readonly ITextureProvider textures;
    private readonly Action sauver;
    private readonly Action<uint, uint, Vector3> poserDrapeau;

    private uint mapAffichee;
    private float zoom;
    private Vector2 centre;
    private bool suivreMoi = true;

    public OngletCarte(Configuration configuration, LecteurCartes cartes, LecteurEtatCarte lecteurEtat, NomsMeteo nomsMeteo,
        ITextureProvider textures, Action sauver, Action<uint, uint, Vector3> poserDrapeau)
    {
        this.configuration = configuration;
        this.cartes = cartes;
        this.lecteurEtat = lecteurEtat;
        this.nomsMeteo = nomsMeteo;
        this.textures = textures;
        this.sauver = sauver;
        this.poserDrapeau = poserDrapeau;
    }

    public void Dessiner()
    {
        var etat = lecteurEtat.Lire();
        var aEureka = Meteo.Zone(etat.Territoire) != null;
        if (aEureka && configuration.DerniereZone != etat.Territoire)
        {
            configuration.DerniereZone = etat.Territoire;
            sauver();
        }

        var zone = Meteo.Zone(aEureka ? etat.Territoire : configuration.DerniereZone) ?? Meteo.Zones[2];
        var carte = aEureka ? cartes.Carte(etat.Map) : cartes.CartePrincipale(zone.Territoire);
        if (!aEureka) etat = EtatCarte.Vide;

        DessinerBandeau(zone, aEureka);
        if (carte == null)
        {
            ImGui.TextColored(Gris, "Carte introuvable dans les données du jeu.");
            return;
        }

        var hauteur = ImGui.GetContentRegionAvail().Y;
        using (var gauche = ImRaii.Child("##carte", new Vector2(ImGui.GetContentRegionAvail().X - LargeurPanneau - 8, hauteur), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            if (gauche) DessinerCarte(carte, etat);
        ImGui.SameLine(0, 8);
        using (var droite = ImRaii.Child("##panneau", new Vector2(0, hauteur), true))
            if (droite) DessinerPanneau(carte, etat);
    }

    // ---------- bandeau : zone, heure, météo, tracker ----------

    private void DessinerBandeau(ZoneEureka zone, bool aEureka)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (!aEureka)
        {
            ImGui.SetNextItemWidth(160);
            using (var choix = ImRaii.Combo("##zone", zone.Nom))
            {
                if (choix)
                    foreach (var z in Meteo.Zones)
                        if (ImGui.Selectable(z.Nom, z == zone))
                        {
                            configuration.DerniereZone = z.Territoire;
                            mapAffichee = 0;
                            sauver();
                        }
            }
            ImGui.SameLine();
            ImGui.TextColored(Gris, "(tu n'es pas à Eurêka : carte seule)");
        }
        else
        {
            ImGui.TextColored(Jaune, zone.Nom);
        }

        var (h, m) = Meteo.HeureEorzea(maintenant);
        var (nuit, bascule) = Meteo.ProchaineBascule(maintenant);
        ImGui.SameLine();
        ImGui.TextUnformatted($"· ET {h:00}:{m:00} · {(nuit ? "nuit" : "jour")} dans {Duree(bascule - maintenant)}");

        if (configuration.DernierTracker is { } tracker)
        {
            ImGui.SameLine();
            ImGui.TextColored(Gris, $"· tracker {tracker}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Dernier tracker crié dans le chat : https://ffxiv-eureka.com/{tracker}");
        }

        // Météo : l'actuelle puis les six suivantes, avec dans combien de temps.
        var premiere = true;
        foreach (var p in Meteo.Periodes(zone, maintenant).Take(7))
        {
            if (!premiere) ImGui.SameLine(0, 14);
            var nom = nomsMeteo[p.Meteo];
            if (premiere) ImGui.TextColored(Jaune, $"{nom} (encore {Duree(p.Fin - maintenant)})");
            else ImGui.TextUnformatted($"{nom} {Duree(p.Debut - maintenant)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{p.Debut.ToLocalTime():HH:mm} → {p.Fin.ToLocalTime():HH:mm} (heure locale)");
            premiere = false;
        }
        ImGui.Spacing();
    }

    // ---------- la carte ----------

    private void DessinerCarte(CarteDuJeu carte, EtatCarte etat)
    {
        var origine = ImGui.GetCursorScreenPos();
        var taille = ImGui.GetContentRegionAvail();
        var ajuste = Math.Min(taille.X, taille.Y) / GeometrieCarte.TailleTexture;
        if (mapAffichee != carte.Map)
        {
            mapAffichee = carte.Map;
            zoom = ajuste;
            centre = new Vector2(GeometrieCarte.TailleTexture / 2);
        }

        ImGui.InvisibleButton("##zone-carte", taille);
        var survol = ImGui.IsItemHovered();
        var souris = ImGui.GetIO().MousePos;
        Vector2 Ecran(Vector2 pixel) => origine + (taille / 2) + ((pixel - centre) * zoom);
        Vector2 PixelSous(Vector2 ecran) => centre + ((ecran - origine - (taille / 2)) / zoom);

        if (survol && ImGui.GetIO().MouseWheel != 0)
        {
            var avant = PixelSous(souris);
            zoom = Math.Clamp(zoom * (ImGui.GetIO().MouseWheel > 0 ? 1.2f : 1 / 1.2f), ajuste * 0.8f, ZoomMax);
            centre += avant - PixelSous(souris);
            suivreMoi = false;
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            centre -= ImGui.GetIO().MouseDelta / zoom;
            suivreMoi = false;
        }
        if (suivreMoi && etat.Moi is { } ici) centre = carte.Geometrie.Pixel(ici);

        var dessin = ImGui.GetWindowDrawList();
        var fond = textures.GetFromGame(carte.Texture).GetWrapOrEmpty();
        dessin.AddImage(fond.Handle, Ecran(Vector2.Zero), Ecran(new Vector2(GeometrieCarte.TailleTexture)));

        var calques = configuration.Calques;
        var g = carte.Geometrie;
        var echelleIcone = Math.Clamp(zoom / ajuste, 1f, 1.6f);

        if (calques.NomsDeZone)
            foreach (var r in carte.Reperes.Where(r => r.EstNomDeZone))
                TexteCentre(dessin, Ecran(r.Pixel), r.Nom!, EncreZone, contour: Couleur(0.94f, 0.89f, 0.73f));

        if (calques.Reperes)
            foreach (var r in carte.Reperes.Where(r => !r.EstNomDeZone))
            {
                var p = Ecran(r.Pixel);
                Icone(dessin, r.Icone, p, 22 * echelleIcone);
                if (r.Nom != null && Proche(souris, p, 12 * echelleIcone)) ImGui.SetTooltip(r.Nom);
            }

        if (calques.Fates)
            foreach (var f in etat.Fates)
            {
                var p = Ecran(g.Pixel(f.Position));
                dessin.AddCircleFilled(p, Math.Max(6, f.Rayon * zoom * g.Echelle / 100f), (RougeFate & 0x00FFFFFF) | 0x40000000);
                Icone(dessin, f.Icone, p, 26 * echelleIcone);
                var etiquette = f.EnPreparation ? f.Nom : $"{f.Nom} · {f.Progres} % · {Duree(f.Restant)}";
                TexteCentre(dessin, p + new Vector2(0, 16 * echelleIcone), etiquette, Blanc, Noir);
            }

        if (calques.Pops)
            foreach (var pop in etat.Pops)
            {
                var p = Ecran(g.Pixel(pop.Position));
                var pulsation = 10 + (4 * MathF.Sin((float)ImGui.GetTime() * 4));
                dessin.AddCircle(p, pulsation, RougePop, 0, 3);
                TexteCentre(dessin, p - new Vector2(0, 26), $"{pop.Titre} · il y a {Duree(DateTime.UtcNow - pop.Quand)}", Blanc, Noir);
            }

        if (etat.Drapeau is { } drapeau) Icone(dessin, IconeDrapeau, Ecran(g.Pixel(drapeau)), 24 * echelleIcone);

        if (calques.Groupe)
            foreach (var membre in etat.Groupe)
            {
                var p = Ecran(g.Pixel(membre));
                dessin.AddCircleFilled(p, 5, BleuGroupe);
                dessin.AddCircle(p, 5, Noir, 0, 1.5f);
            }

        if (etat.Moi is { } moi) FlecheMoi(dessin, Ecran(g.Pixel(moi)), etat.Orientation);

        if (survol)
        {
            var coord = g.Coordonnees(PixelSous(souris));
            var texte = $"X {coord.X:0.0} · Y {coord.Y:0.0}";
            dessin.AddRectFilled(origine + new Vector2(6, taille.Y - 26), origine + new Vector2(16 + ImGui.CalcTextSize(texte).X, taille.Y - 6), 0xD0202028, 4);
            dessin.AddText(origine + new Vector2(11, taille.Y - 24), Blanc, texte);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##menu-carte");
        }

        using (var menu = ImRaii.Popup("##menu-carte"))
        {
            if (menu)
            {
                if (ImGui.MenuItem("Poser le drapeau du jeu ici"))
                    poserDrapeau(carte.Territoire, carte.Map, g.Monde(PixelSous(ImGui.GetMousePosOnOpeningCurrentPopup())));
                if (ImGui.MenuItem("Recentrer sur moi")) suivreMoi = true;
            }
        }

        // Boutons en haut à droite de la carte.
        ImGui.SetCursorScreenPos(origine + new Vector2(taille.X - 120, 6));
        if (ImGui.SmallButton(suivreMoi ? "Suit ma position" : "Centrer sur moi")) suivreMoi = true;
    }

    private static void FlecheMoi(ImDrawListPtr dessin, Vector2 p, float orientation)
    {
        // Rotation 0 = face au sud du monde (+Z), donc vers le bas de la carte.
        var avant = new Vector2(MathF.Sin(orientation), MathF.Cos(orientation));
        var cote = new Vector2(-avant.Y, avant.X);
        var pointe = p + (avant * 13);
        var gauche = p - (avant * 8) + (cote * 8);
        var droite = p - (avant * 8) - (cote * 8);
        dessin.AddTriangleFilled(pointe, gauche, p - (avant * 3), CremeMoi);
        dessin.AddTriangleFilled(pointe, p - (avant * 3), droite, CremeMoi);
        dessin.AddTriangle(pointe, gauche, droite, Noir, 2);
    }

    // ---------- panneau de droite ----------

    private void DessinerPanneau(CarteDuJeu carte, EtatCarte etat)
    {
        ImGui.TextColored(Jaune, "FATE en cours");
        if (etat.Fates.Count == 0) ImGui.TextColored(Gris, "Aucune (les FATE lapin ne sont pas montrées).");
        foreach (var f in etat.Fates.OrderByDescending(f => f.Progres))
        {
            if (ImGui.Selectable($"{f.Nom}##fate{f.Id}")) Centrer(carte, f.Position);
            ImGui.SameLine();
            ImGui.TextColored(Gris, f.EnPreparation ? "en préparation" : $"{f.Progres} % · {Duree(f.Restant)}");
        }

        ImGui.Spacing();
        ImGui.TextColored(Jaune, "Pops du chat");
        if (etat.Pops.Count == 0) ImGui.TextColored(Gris, "Aucun depuis 10 min.");
        foreach (var (pop, i) in etat.Pops.Select((p, i) => (p, i)).Reverse())
        {
            if (ImGui.Selectable($"{pop.Titre}##pop{i}")) Centrer(carte, pop.Position);
            ImGui.SameLine();
            ImGui.TextColored(Gris, $"il y a {Duree(DateTime.UtcNow - pop.Quand)}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Gris, "Calques");
        var c = configuration.Calques;
        Case("Repères du jeu", () => c.Reperes, v => c.Reperes = v);
        Case("Noms de zone", () => c.NomsDeZone, v => c.NomsDeZone = v);
        Case("FATE en cours", () => c.Fates, v => c.Fates = v);
        Case("Pops du chat", () => c.Pops, v => c.Pops = v);
        Case("Groupe", () => c.Groupe, v => c.Groupe = v);
        ImGui.Spacing();
        ImGui.TextColored(Gris, "Molette : zoom · glisser : déplacer · clic droit : drapeau.");
    }

    private void Centrer(CarteDuJeu carte, Vector3 monde)
    {
        centre = carte.Geometrie.Pixel(monde);
        suivreMoi = false;
    }

    private void Case(string libelle, Func<bool> lire, Action<bool> ecrire)
    {
        var valeur = lire();
        if (!ImGui.Checkbox(libelle, ref valeur)) return;
        ecrire(valeur);
        sauver();
    }

    // ---------- outils de dessin ----------

    private void Icone(ImDrawListPtr dessin, uint icone, Vector2 centreIcone, float taille)
    {
        if (icone == 0 || !textures.TryGetFromGameIcon(new GameIconLookup(icone), out var partagee)
                       || !partagee.TryGetWrap(out var image, out _)) return;
        var demi = new Vector2(taille / 2);
        dessin.AddImage(image.Handle, centreIcone - demi, centreIcone + demi);
    }

    private static void TexteCentre(ImDrawListPtr dessin, Vector2 centreTexte, string texte, uint couleur, uint contour)
    {
        var p = centreTexte - (ImGui.CalcTextSize(texte) / 2);
        foreach (var d in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
            dessin.AddText(p + d, contour, texte);
        dessin.AddText(p, couleur, texte);
    }

    private static bool Proche(Vector2 a, Vector2 b, float rayon) => Vector2.DistanceSquared(a, b) <= rayon * rayon;

    /// <summary>Couleur ImGui (ABGR) ; calculée sans ImGui, utilisable hors d'une image.</summary>
    private static uint Couleur(float r, float v, float b, float a = 1f) =>
        ((uint)(a * 255) << 24) | ((uint)(b * 255) << 16) | ((uint)(v * 255) << 8) | (uint)(r * 255);

    /// <summary>« 12 min », « 1 h 05 », « 40 s ».</summary>
    public static string Duree(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00}"
        : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min"
        : $"{Math.Max(0, (int)d.TotalSeconds)} s";
}
