using System.Numerics;
using XivEureka.Windows;
using Xunit;

namespace XivEureka.Tests;

public class LabelPlacerTests
{
    private static readonly Vector2 Label = new(60, 14);

    [Fact]
    public void Goes_below_the_marker_when_free()
    {
        var placer = new LabelPlacer();
        var rect = placer.PlaceAround(new Vector2(100, 100), 10, Label);
        Assert.Equal(new Vector2(70, 114), rect.Min);
    }

    [Fact]
    public void Moves_above_then_aside_when_the_spot_is_taken()
    {
        var placer = new LabelPlacer();
        placer.AddObstacle(new ScreenRect(new Vector2(0, 112), new Vector2(200, 130)));   // below is taken
        var above = placer.PlaceAround(new Vector2(100, 100), 10, Label);
        Assert.True(above.Max.Y <= 90);

        placer.AddObstacle(new ScreenRect(new Vector2(0, 60), new Vector2(200, 90)));     // above is taken too
        var aside = placer.PlaceAround(new Vector2(100, 100), 10, Label);
        Assert.True(aside.Min.X >= 110);
    }

    [Fact]
    public void Two_close_markers_get_labels_that_do_not_overlap()
    {
        var placer = new LabelPlacer();
        var a = placer.PlaceAround(new Vector2(100, 100), 10, Label);
        var b = placer.PlaceAround(new Vector2(120, 105), 10, Label);
        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void Optional_labels_are_dropped_when_they_would_overlap()
    {
        var placer = new LabelPlacer();
        placer.AddObstacle(ScreenRect.Centered(new Vector2(50, 50), new Vector2(20)));
        Assert.False(placer.TryPlace(ScreenRect.Centered(new Vector2(55, 50), Label)));
        Assert.True(placer.TryPlace(ScreenRect.Centered(new Vector2(200, 200), Label)));
    }
}
