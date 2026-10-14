using System.Collections.Generic;
using System.Numerics;

namespace XivEureka.Windows;

/// <summary>An axis-aligned screen rectangle.</summary>
public readonly record struct ScreenRect(Vector2 Min, Vector2 Max)
{
    public static ScreenRect Centered(Vector2 center, Vector2 size) => new(center - (size / 2), center + (size / 2));

    public bool Overlaps(ScreenRect other) =>
        Min.X < other.Max.X && other.Min.X < Max.X && Min.Y < other.Max.Y && other.Min.Y < Max.Y;
}

/// <summary>
/// Greedy label placement for one frame of the map: markers are registered as obstacles first, then
/// each label tries a few spots around its marker (below, above, right, left) and takes the first one
/// that overlaps nothing placed so far. Labels that must not be dropped fall back to "below";
/// optional ones (area names) are skipped when they would overlap.
/// </summary>
public sealed class LabelPlacer
{
    private const float Gap = 4;
    private readonly List<ScreenRect> taken = [];

    public void AddObstacle(ScreenRect rect) => taken.Add(rect);

    /// <summary>Places a label of <paramref name="size"/> next to a round marker; returns its rectangle.</summary>
    public ScreenRect PlaceAround(Vector2 marker, float markerRadius, Vector2 size)
    {
        var offset = markerRadius + Gap;
        ScreenRect[] candidates =
        [
            new(new Vector2(marker.X - (size.X / 2), marker.Y + offset), new Vector2(marker.X + (size.X / 2), marker.Y + offset + size.Y)),
            new(new Vector2(marker.X - (size.X / 2), marker.Y - offset - size.Y), new Vector2(marker.X + (size.X / 2), marker.Y - offset)),
            new(new Vector2(marker.X + offset, marker.Y - (size.Y / 2)), new Vector2(marker.X + offset + size.X, marker.Y + (size.Y / 2))),
            new(new Vector2(marker.X - offset - size.X, marker.Y - (size.Y / 2)), new Vector2(marker.X - offset, marker.Y + (size.Y / 2))),
        ];
        foreach (var candidate in candidates)
        {
            if (IsFree(candidate))
            {
                taken.Add(candidate);
                return candidate;
            }
        }
        taken.Add(candidates[0]);
        return candidates[0];
    }

    /// <summary>Takes <paramref name="rect"/> if it is free; false (and nothing taken) otherwise.</summary>
    public bool TryPlace(ScreenRect rect)
    {
        if (!IsFree(rect)) return false;
        taken.Add(rect);
        return true;
    }

    private bool IsFree(ScreenRect rect)
    {
        foreach (var t in taken)
            if (t.Overlaps(rect)) return false;
        return true;
    }
}
