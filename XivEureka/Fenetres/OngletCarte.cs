using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Tracker;
using XivEureka.Zones;

namespace XivEureka.Fenetres;

/// <summary>Ce que l'onglet Carte reçoit du reste du plugin, en plus des données du jeu.</summary>
public sealed record SourcesCarte(
    Func<IReadOnlyDictionary<ushort, DateTime>> FatesTerminees,
    Func<IReadOnlySet<ushort>> Prepares,
    Func<int?> NiveauElementaire,
    Func<IReadOnlyDictionary<uint, int>> EclatsUtiles,
    SuiviTracker Tracker,
    Action<uint, uint, Vector3> PoserDrapeau);

/// <summary>
/// Onglet « Carte » : la carte du jeu de la zone d'Eurêka, redessinée par le plugin pour qu'on
/// voie d'un coup d'œil ce qui vient de lui. On y retrouve ce que montre la carte du jeu (repères
/// fixes, noms de zone, soi, le groupe, le drapeau, les FATE en cours) et ce que sait le plugin :
/// les NM avec leur état, la zone de prépa du NM choisi, les pops du chat, et à droite « Où aller
/// maintenant ». Molette : zoom autour de la souris ; glisser : déplacer ; clic droit : drapeau.
/// </summary>
public sealed class OngletCarte
{
    private const float LargeurPanneau = 360;
    private const float ZoomMax = 4f;
    private const uint IconeDrapeau = 60561;
    private const float RayonPrepa = 1.3f; // en unités de carte, autour de chaque lieu de prépa
    private static readonly TimeSpan IntervalleCalcul = TimeSpan.FromSeconds(1);

    private static readonly Vector4 Jaune = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Gris = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly uint Blanc = Couleur(1, 1, 1);
    private static readonly uint Noir = Couleur(0, 0, 0);
    private static readonly uint BleuGroupe = Couleur(0.29f, 0.64f, 1f);
    private static readonly uint RougeFate = Couleur(0.77f, 0.23f, 0.29f);
    private static readonly uint RougePop = Couleur(1f, 0.3f, 0.3f);
    private static readonly uint CremeMoi = Couleur(1f, 0.96f, 0.76f);
    private static readonly uint EncreZone = Couleur(0.33f, 0.25f, 0.13f);
    private static readonly uint Parchemin = Couleur(0.94f, 0.89f, 0.73f);
    private static readonly uint FondNm = Couleur(0.11f, 0.11f, 0.13f);
    private static readonly uint FondRespawn = Couleur(0.36f, 0.35f, 0.39f);
    private static readonly uint VioletPrepa = Couleur(0.42f, 0.17f, 0.55f);

    private static readonly Dictionary<string, (uint Couleur, string Nom)> Elements = new()
    {
        ["fire"] = (Couleur(0.91f, 0.36f, 0.24f), "Feu"),
        ["ice"] = (Couleur(0.44f, 0.82f, 0.93f), "Glace"),
        ["wind"] = (Couleur(0.37f, 0.81f, 0.48f), "Vent"),
        ["earth"] = (Couleur(0.85f, 0.71f, 0.29f), "Terre"),
        ["lightning"] = (Couleur(0.72f, 0.48f, 0.9f), "Foudre"),
        ["water"] = (Couleur(0.29f, 0.56f, 0.91f), "Eau"),
    };

    private readonly Configuration configuration;
    private readonly LecteurCartes cartes;
    private readonly LecteurEtatCarte lecteurEtat;
    private readonly CatalogueNm catalogueNm;
    private readonly NomsJeu noms;
    private readonly ITextureProvider textures;
    private readonly SourcesCarte sources;
    private readonly Action sauver;

    private uint mapAffichee;
    private float zoom;
    private Vector2 centre;
    private bool suivreMoi = true;
    private uint? nmChoisi;

    private DateTime prochainCalcul = DateTime.MinValue;
    private IReadOnlyList<SituationNm> situations = [];
    private IReadOnlyList<Conseil> conseils = [];

    public OngletCarte(Configuration configuration, LecteurCartes cartes, LecteurEtatCarte lecteurEtat, CatalogueNm catalogueNm,
        NomsJeu noms, ITextureProvider textures, SourcesCarte sources, Action sauver)
    {
        this.configuration = configuration;
        this.cartes = cartes;
        this.lecteurEtat = lecteurEtat;
        this.catalogueNm = catalogueNm;
        this.noms = noms;
        this.textures = textures;
        this.sources = sources;
        this.sauver = sauver;
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
        Calculer(zone, etat);

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

    /// <summary>Situations des NM et conseils, recalculés une fois par seconde (pas à chaque image).</summary>
    private void Calculer(ZoneEureka zone, EtatCarte etat)
    {
        if (DateTime.UtcNow < prochainCalcul) return;
        prochainCalcul = DateTime.UtcNow + IntervalleCalcul;
        var maintenant = DateTimeOffset.UtcNow;
        if (catalogueNm.Zone(zone.Territoire) is not { } nms)
        {
            situations = [];
            conseils = [];
            return;
        }
        situations = Situations.Calculer(zone, nms, etat.Fates, sources.FatesTerminees(), sources.Prepares(), maintenant);
        conseils = Conseils.Calculer(situations, maintenant, sources.NiveauElementaire(), sources.EclatsUtiles(), noms);
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
                            prochainCalcul = DateTime.MinValue;
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
        ImGui.TextUnformatted($"· ET {h:00}:{m:00} · {(nuit ? "nuit" : "jour")} dans {Formats.Duree(bascule - maintenant)}");
        if (sources.NiveauElementaire() is { } niveau)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"· niveau élémentaire {niveau}");
        }

        // Météo : l'actuelle puis les six suivantes, avec dans combien de temps.
        var premiere = true;
        foreach (var p in Meteo.Periodes(zone, maintenant).Take(7))
        {
            if (!premiere) ImGui.SameLine(0, 14);
            var nom = noms.Meteo(p.Meteo);
            if (premiere) ImGui.TextColored(Jaune, $"{nom} (encore {Formats.Duree(p.Fin - maintenant)})");
            else ImGui.TextUnformatted($"{nom} {Formats.Duree(p.Debut - maintenant)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{p.Debut.ToLocalTime():HH:mm} → {p.Fin.ToLocalTime():HH:mm} (heure locale)");
            premiere = false;
        }
        if (aEureka) DessinerTracker();
        ImGui.Spacing();
    }

    /// <summary>Ligne du tracker : lequel, son état, et les gestes utiles (copier, ouvrir, créer, changer).</summary>
    private void DessinerTracker()
    {
        var t = sources.Tracker;
        ImGui.TextColored(t.Suivi != null ? Jaune : Gris, "Tracker :");
        ImGui.SameLine();
        ImGui.TextUnformatted(t.Statut);
        if (t.IdSuivi is { } id)
        {
            var lien = $"https://ffxiv-eureka.com/{id}";
            ImGui.SameLine();
            if (ImGui.SmallButton("Copier le lien")) ImGui.SetClipboardText(lien);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("À crier dans le chat pour que les autres le suivent.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Ouvrir")) Dalamud.Utility.Util.OpenLink(lien);
        }
        else if (t.CreationPrevue is { } quand)
        {
            ImGui.SameLine();
            ImGui.TextColored(Gris, $"· création dans {Formats.Duree(quand - DateTime.UtcNow)} si aucun n'est crié");
            ImGui.SameLine();
            if (ImGui.SmallButton("Créer maintenant")) t.CreerMaintenant();
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Créer un tracker")) t.CreerMaintenant();
        }

        var autres = t.AutresTrackers;
        if (autres.Count == 0) return;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        using var choix = ImRaii.Combo("##autres-trackers", $"{autres.Count} autre(s) tracker(s) de la zone");
        if (!choix) return;
        foreach (var autre in autres)
        {
            var maj = autre.MajLe is { } m ? $"mis à jour il y a {Formats.Duree(DateTimeOffset.UtcNow - m)}" : "jamais mis à jour";
            if (ImGui.Selectable($"{autre.Id} · {autre.Morts.Count} NM tué(s) · {maj}")) t.Choisir(autre.Id);
        }
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
        var echelle = Math.Clamp(zoom / ajuste, 1f, 1.6f);

        if (calques.NomsDeZone)
            foreach (var r in carte.Reperes.Where(r => r.EstNomDeZone))
                TexteCentre(dessin, Ecran(r.Pixel), r.Nom!, EncreZone, Parchemin);

        if (calques.Reperes)
            foreach (var r in carte.Reperes.Where(r => !r.EstNomDeZone))
            {
                var p = Ecran(r.Pixel);
                Icone(dessin, r.Icone, p, 22 * echelle);
                if (r.Nom != null && Proche(souris, p, 12 * echelle)) ImGui.SetTooltip(r.Nom);
            }

        var choisie = situations.FirstOrDefault(s => s.Nm.Pnj == nmChoisi);
        if (calques.Prepa && choisie != null)
            foreach (var lieu in choisie.Nm.Prepa.Lieux)
            {
                var p = Ecran(g.PixelDeCoordonnees(lieu));
                var rayon = RayonPrepa * 50 * g.Echelle / 100f * zoom;
                dessin.AddCircleFilled(p, rayon, (VioletPrepa & 0x00FFFFFF) | 0x38000000);
                dessin.AddCircle(p, rayon, VioletPrepa, 0, 2);
                TexteCentre(dessin, p + new Vector2(0, rayon + 8), $"prépa : {noms.Pnj(choisie.Nm.Prepa.Pnj)}", VioletPrepa, Parchemin);
            }

        if (calques.Fates)
            foreach (var f in etat.Fates.Where(f => situations.All(s => s.Nm.Fate != f.Id)))
            {
                // FATE sans fiche de NM (zone sans données) : comme sur la carte du jeu.
                var p = Ecran(g.Pixel(f.Position));
                Icone(dessin, f.Icone, p, 26 * echelle);
                TexteCentre(dessin, p + new Vector2(0, 16 * echelle), EtiquetteFate(f), Blanc, Noir);
            }

        var maintenant = DateTimeOffset.UtcNow;
        if (calques.Nm)
            foreach (var s in situations)
                DessinerNm(dessin, s, Ecran(g.PixelDeCoordonnees(s.Nm.Position)), souris, maintenant, echelle);

        if (calques.Pops)
            foreach (var pop in etat.Pops)
            {
                var p = Ecran(g.Pixel(pop.Position));
                var pulsation = 10 + (4 * MathF.Sin((float)ImGui.GetTime() * 4));
                dessin.AddCircle(p, pulsation, RougePop, 0, 3);
                TexteCentre(dessin, p - new Vector2(0, 26), $"{pop.Titre} · il y a {Formats.Duree(DateTime.UtcNow - pop.Quand)}", Blanc, Noir);
            }

        if (etat.Drapeau is { } drapeau) Icone(dessin, IconeDrapeau, Ecran(g.Pixel(drapeau)), 24 * echelle);

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
            dessin.AddRectFilled(origine + new Vector2(6, taille.Y - 26), origine + new Vector2(16 + ImGui.CalcTextSize(texte).X, taille.Y - 6), 0xD0282020, 4);
            dessin.AddText(origine + new Vector2(11, taille.Y - 24), Blanc, texte);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##menu-carte");
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.GetIO().MouseDragMaxDistanceSqr[0] < 16)
                nmChoisi = situations.FirstOrDefault(s => Proche(souris, Ecran(g.PixelDeCoordonnees(s.Nm.Position)), 16 * echelle))?.Nm.Pnj ?? nmChoisi;
        }

        using (var menu = ImRaii.Popup("##menu-carte"))
        {
            if (menu)
            {
                if (ImGui.MenuItem("Poser le drapeau du jeu ici"))
                    sources.PoserDrapeau(carte.Territoire, carte.Map, g.Monde(PixelSous(ImGui.GetMousePosOnOpeningCurrentPopup())));
                if (ImGui.MenuItem("Recentrer sur moi")) suivreMoi = true;
            }
        }

        ImGui.SetCursorScreenPos(origine + new Vector2(taille.X - 120, 6));
        if (ImGui.SmallButton(suivreMoi ? "Suit ma position" : "Centrer sur moi")) suivreMoi = true;
    }

    private void DessinerNm(ImDrawListPtr dessin, SituationNm s, Vector2 p, Vector2 souris, DateTimeOffset maintenant, float echelle)
    {
        var rayon = 13 * echelle;
        var element = Elements.GetValueOrDefault(s.Nm.Element, (Couleur: Blanc, Nom: s.Nm.Element));
        var fond = s.Fate != null ? RougeFate : s.EnRespawn(maintenant) ? FondRespawn : FondNm;
        if (s.Fate != null) dessin.AddCircleFilled(p, rayon + 6, (RougeFate & 0x00FFFFFF) | 0x60000000);
        if (s.Nm.Pnj == nmChoisi) dessin.AddCircle(p, rayon + 4, CremeMoi, 0, 3);
        dessin.AddCircleFilled(p, rayon, fond);
        dessin.AddCircle(p, rayon, element.Couleur, 0, 3);
        var niveau = s.Nm.Niveau.ToString();
        dessin.AddText(p - (ImGui.CalcTextSize(niveau) / 2), Blanc, niveau);

        TexteCentre(dessin, p + new Vector2(0, rayon + 8), noms.Pnj(s.Nm.Pnj), EncreZone, Parchemin);
        if (Badge(s, maintenant) is { } badge)
            TexteCentre(dessin, p + new Vector2(0, rayon + 22), badge, Blanc, Noir);

        if (Proche(souris, p, rayon + 2) && ImGui.IsWindowHovered())
        {
            using var bulle = ImRaii.Tooltip();
            DessinerFiche(s, maintenant, boutons: false);
        }
    }

    /// <summary>Le petit texte sous le nom du NM : FATE, respawn ou condition à venir.</summary>
    private string? Badge(SituationNm s, DateTimeOffset maintenant)
    {
        if (s.Fate is { } f) return EtiquetteFate(f);
        if (s.EnRespawn(maintenant)) return $"respawn ≥ {Formats.Duree(s.RespawnAuPlusTot!.Value - maintenant)}";
        if (s.Prepare) return "préparé";
        if (s.Attente is not { } fenetre) return null;
        var condition = s.FenetreNm != null ? s.Nm.Condition : s.Nm.Prepa.Condition;
        var nom = condition.Meteo is { } m ? noms.Meteo(m) : "nuit";
        return fenetre.EnCours(maintenant) ? $"{nom} en cours" : $"{nom} dans {Formats.Duree(fenetre.Debut - maintenant)}";
    }

    private static string EtiquetteFate(FateCarte f) =>
        f.EnPreparation ? "FATE en préparation" : $"FATE {f.Progres} % · {Formats.Duree(f.Restant)}";

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
        var maintenant = DateTimeOffset.UtcNow;
        ImGui.TextColored(Jaune, "Où aller maintenant");
        if (situations.Count == 0) ImGui.TextColored(Gris, "Pas encore de données de NM pour cette zone.");
        else if (conseils.Count == 0) ImGui.TextColored(Gris, "Rien de pressé : aucune FATE, aucune condition dans la demi-heure.");
        foreach (var (c, i) in conseils.Select((c, i) => (c, i)))
        {
            using var id = ImRaii.PushId(i);
            ImGui.TextColored(Jaune, $"{i + 1}.");
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(c.Titre);
            ImGui.TextColored(Gris, c.Texte);
            ImGui.PopTextWrapPos();
            if (ImGui.SmallButton("Voir"))
            {
                CentrerCoordonnees(carte, c.Coordonnees);
                nmChoisi = c.Pnj ?? nmChoisi;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Drapeau"))
                sources.PoserDrapeau(carte.Territoire, carte.Map, carte.Geometrie.Monde(carte.Geometrie.PixelDeCoordonnees(c.Coordonnees)));
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        if (situations.FirstOrDefault(s => s.Nm.Pnj == nmChoisi) is { } choisie)
        {
            ImGui.Separator();
            DessinerFiche(choisie, maintenant, boutons: true, carte);
        }

        if (etat.Pops.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Jaune, "Pops du chat");
            foreach (var (pop, i) in etat.Pops.Select((p, i) => (p, i)).Reverse())
            {
                if (ImGui.Selectable($"{pop.Titre}##pop{i}")) Centrer(carte, pop.Position);
                ImGui.SameLine();
                ImGui.TextColored(Gris, $"il y a {Formats.Duree(DateTime.UtcNow - pop.Quand)}");
            }
        }

        ImGui.Separator();
        ImGui.TextColored(Gris, "Calques");
        var k = configuration.Calques;
        using (var table = ImRaii.Table("##calques", 2))
        {
            if (table)
            {
                ImGui.TableNextColumn(); Case("NM", () => k.Nm, v => k.Nm = v);
                ImGui.TableNextColumn(); Case("Zone de prépa", () => k.Prepa, v => k.Prepa = v);
                ImGui.TableNextColumn(); Case("Pops du chat", () => k.Pops, v => k.Pops = v);
                ImGui.TableNextColumn(); Case("Groupe", () => k.Groupe, v => k.Groupe = v);
                ImGui.TableNextColumn(); Case("Repères du jeu", () => k.Reperes, v => k.Reperes = v);
                ImGui.TableNextColumn(); Case("Noms de zone", () => k.NomsDeZone, v => k.NomsDeZone = v);
                ImGui.TableNextColumn(); Case("Autres FATE", () => k.Fates, v => k.Fates = v);
            }
        }
        ImGui.TextColored(Gris, "Molette : zoom · glisser : déplacer · clic : fiche du NM · clic droit : drapeau.");
    }

    private void DessinerFiche(SituationNm s, DateTimeOffset maintenant, bool boutons, CarteDuJeu? carte = null)
    {
        var nm = s.Nm;
        var element = Elements.GetValueOrDefault(nm.Element, (Couleur: Blanc, Nom: nm.Element)).Nom;
        ImGui.TextColored(Jaune, noms.Pnj(nm.Pnj));
        ImGui.SameLine();
        ImGui.TextColored(Gris, $"Nv {nm.Niveau} · {element} · {Formats.Coordonnees(nm.Position)}");
        ImGui.PushTextWrapPos(boutons ? 0 : 360);
        ImGui.TextUnformatted(Conseils.TextePrepa(nm, noms));
        ImGui.TextUnformatted("Condition : " + TexteCondition(s, maintenant));
        var lache = new[] { nm.Eclat }.Concat(nm.Objets).Select(o => Logos.TextesJeu.SansNonIdentifie(noms.Objet(o)));
        ImGui.TextUnformatted("Lâche : " + string.Join(", ", lache));
        ImGui.TextUnformatted("État : " + (Badge(s, maintenant) is { } badge ? badge
            : s.RespawnAuPlusTot != null ? "réapparu (2 h passées)" : "inconnu (pas vu tué dans cette instance)"));
        ImGui.PopTextWrapPos();

        if (!boutons || carte == null) return;
        if (ImGui.SmallButton("Centrer")) CentrerCoordonnees(carte, nm.Position);
        ImGui.SameLine();
        if (ImGui.SmallButton("Drapeau sur la FATE"))
            sources.PoserDrapeau(carte.Territoire, carte.Map, carte.Geometrie.Monde(carte.Geometrie.PixelDeCoordonnees(nm.Position)));
        if (nm.Prepa.Lieux.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Drapeau sur la prépa"))
                sources.PoserDrapeau(carte.Territoire, carte.Map, carte.Geometrie.Monde(carte.Geometrie.PixelDeCoordonnees(nm.Prepa.Lieux[0])));
        }
    }

    private string TexteCondition(SituationNm s, DateTimeOffset maintenant)
    {
        string Fenetre(Condition c, Fenetre? f, string qui)
        {
            var nom = c.Meteo is { } m ? noms.Meteo(m) : "la nuit";
            return f == null ? $"{qui} : {nom}"
                : f.EnCours(maintenant) ? $"{qui} : {nom}, en cours (encore {Formats.Duree(f.Fin - maintenant)})"
                : $"{qui} : {nom}, dans {Formats.Duree(f.Debut - maintenant)}";
        }
        if (!s.Nm.Condition.Aucune) return Fenetre(s.Nm.Condition, s.FenetreNm, "le NM n'apparaît que par");
        if (!s.Nm.Prepa.Condition.Aucune) return Fenetre(s.Nm.Prepa.Condition, s.FenetrePrepa, "le mob de prépa n'apparaît que par");
        return "aucune";
    }

    private void Centrer(CarteDuJeu carte, Vector3 monde)
    {
        centre = carte.Geometrie.Pixel(monde);
        suivreMoi = false;
    }

    private void CentrerCoordonnees(CarteDuJeu carte, Vector2 coordonnees)
    {
        centre = carte.Geometrie.PixelDeCoordonnees(coordonnees);
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
}
