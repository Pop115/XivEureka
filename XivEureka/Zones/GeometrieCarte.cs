using System.Numerics;

namespace XivEureka.Zones;

/// <summary>
/// Passage entre les trois repères d'une carte du jeu : la position monde (X, Z), le pixel de la
/// texture de la carte (2048 × 2048, origine en haut à gauche) et les coordonnées affichées
/// (« X: 24.0 Y: 17.5 »). Échelle et décalage viennent de la ligne Map (SizeFactor, OffsetX/Y).
/// Formule du jeu, la même que <c>Dalamud.Utility.MapUtil</c> ; les repères de la feuille
/// MapMarker sont déjà en pixels de texture.
/// </summary>
public sealed record GeometrieCarte(ushort Echelle, short DecalageX, short DecalageY)
{
    public const float TailleTexture = 2048f;

    private float C => Echelle / 100f;

    /// <summary>Pixel de la texture sous une position monde (Y, la hauteur, est ignoré).</summary>
    public Vector2 Pixel(Vector3 monde) =>
        new((monde.X + DecalageX) * C + TailleTexture / 2, (monde.Z + DecalageY) * C + TailleTexture / 2);

    /// <summary>Position monde (Y = 0) sous un pixel de la texture.</summary>
    public Vector3 Monde(Vector2 pixel) =>
        new((pixel.X - TailleTexture / 2) / C - DecalageX, 0, (pixel.Y - TailleTexture / 2) / C - DecalageY);

    /// <summary>Coordonnées affichées par le jeu pour un pixel de la texture.</summary>
    public Vector2 Coordonnees(Vector2 pixel) => pixel / (50 * C) + Vector2.One;

    /// <summary>Pixel de la texture pour des coordonnées de carte (celles du wiki ou du chat).</summary>
    public Vector2 PixelDeCoordonnees(Vector2 coordonnees) => (coordonnees - Vector2.One) * (50 * C);
}
