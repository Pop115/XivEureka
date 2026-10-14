using System.Numerics;

namespace XivEureka.Zones;

/// <summary>
/// Converts between the three frames of a game map: the world position (X, Z), the pixel of the
/// map texture (2048 × 2048, origin top left) and the displayed coordinates ("X: 24.0 Y: 17.5").
/// Scale and offset come from the Map row (SizeFactor, OffsetX/Y). The game's formula, the same as
/// <c>Dalamud.Utility.MapUtil</c>; MapMarker sheet points are already in texture pixels.
/// </summary>
public sealed record MapGeometry(ushort Scale, short OffsetX, short OffsetY)
{
    public const float TextureSize = 2048f;

    private float C => Scale / 100f;

    /// <summary>Texture pixel under a world position (Y, the height, is ignored).</summary>
    public Vector2 Pixel(Vector3 world) =>
        new((world.X + OffsetX) * C + TextureSize / 2, (world.Z + OffsetY) * C + TextureSize / 2);

    /// <summary>World position (Y = 0) under a texture pixel.</summary>
    public Vector3 World(Vector2 pixel) =>
        new((pixel.X - TextureSize / 2) / C - OffsetX, 0, (pixel.Y - TextureSize / 2) / C - OffsetY);

    /// <summary>Coordinates the game displays for a texture pixel.</summary>
    public Vector2 Coordinates(Vector2 pixel) => pixel / (50 * C) + Vector2.One;

    /// <summary>Texture pixel for map coordinates (from the wiki or the chat).</summary>
    public Vector2 PixelFromCoordinates(Vector2 coordinates) => (coordinates - Vector2.One) * (50 * C);
}
