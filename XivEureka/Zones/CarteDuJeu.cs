using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Zones;

/// <summary>
/// Un repère fixe de la carte du jeu (feuille MapMarker) : soit une icône (éthérite, PNJ, lieu
/// notable, avec son nom éventuel), soit un nom de zone écrit sur la carte (<see cref="Icone"/> = 0).
/// </summary>
public sealed record RepereCarte(uint Icone, Vector2 Pixel, string? Nom)
{
    public bool EstNomDeZone => Icone == 0;
}

/// <summary>Une carte du jeu prête à dessiner : sa texture, sa géométrie et ses repères fixes.</summary>
public sealed record CarteDuJeu(uint Map, uint Territoire, string Nom, string Texture, GeometrieCarte Geometrie,
    IReadOnlyList<RepereCarte> Reperes);

/// <summary>Lit les cartes dans les feuilles du jeu (Map, MapMarker), une fois par carte.</summary>
public sealed class LecteurCartes(IDataManager donnees)
{
    private readonly Dictionary<uint, CarteDuJeu?> cache = [];

    /// <summary>La carte d'une ligne Map ; null si la ligne n'existe pas ou n'a pas de texture.</summary>
    public CarteDuJeu? Carte(uint map)
    {
        if (cache.TryGetValue(map, out var deja)) return deja;
        return cache[map] = Lire(map);
    }

    /// <summary>La carte principale d'un territoire (la première ligne Map qui le désigne).</summary>
    public CarteDuJeu? CartePrincipale(uint territoire)
    {
        var ligne = donnees.GetExcelSheet<Map>().FirstOrDefault(m => m.TerritoryType.RowId == territoire && !m.Id.IsEmpty);
        return ligne.RowId == 0 ? null : Carte(ligne.RowId);
    }

    private CarteDuJeu? Lire(uint numero)
    {
        if (donnees.GetExcelSheet<Map>().GetRowOrDefault(numero) is not { } map || map.Id.IsEmpty) return null;

        // « z3fc/00 » → ui/map/z3fc/00/z3fc00_m.tex
        var id = map.Id.ExtractText();
        var texture = $"ui/map/{id}/{id.Replace("/", string.Empty)}_m.tex";
        if (!donnees.FileExists(texture)) return null;

        var reperes = new List<RepereCarte>();
        if (donnees.GetSubrowExcelSheet<MapMarker>().GetRowOrDefault(map.MapMarkerRange) is { } marqueurs)
        {
            foreach (var m in marqueurs)
            {
                if (m.X == 0 && m.Y == 0) continue;
                var nom = m.PlaceNameSubtext.ValueNullable?.Name.ExtractText();
                if (m.Icon == 0 && string.IsNullOrEmpty(nom)) continue;
                reperes.Add(new RepereCarte(m.Icon, new Vector2(m.X, m.Y), string.IsNullOrEmpty(nom) ? null : nom));
            }
        }

        var nomCarte = map.PlaceName.ValueNullable?.Name.ExtractText() ?? id;
        return new CarteDuJeu(numero, map.TerritoryType.RowId, nomCarte, texture,
            new GeometrieCarte(map.SizeFactor, map.OffsetX, map.OffsetY), reperes);
    }
}
