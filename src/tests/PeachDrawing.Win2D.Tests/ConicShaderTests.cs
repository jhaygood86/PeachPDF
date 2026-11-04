using PeachDrawing.Core;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// The conic gradient painted by the pixel shader against the same gradient painted as wedges (which a gradient of more than
/// <see cref="Win2dConicShader.MaxStops"/> stops falls back to): the two must agree wherever the wedges' one-degree steps allow.
/// </summary>
public class ConicShaderTests
{
    private static readonly (PaintColor Color, double Angle)[] Base =
    [
        (PaintColor.FromArgb(255, 255, 0, 0), 0.5),
        (PaintColor.FromArgb(255, 0, 200, 0), 2.5),
        (PaintColor.FromArgb(255, 0, 0, 255), 4.5),
    ];

    // The same stops, with the last one repeated until there are too many for the shader: the gradient is unchanged, the path is the wedges.
    private static (PaintColor[] Colors, double[] Angles) Stops(bool forWedges)
    {
        var colors = Base.Select(s => s.Color).ToList();
        var angles = Base.Select(s => s.Angle).ToList();
        if (forWedges)
        {
            while (colors.Count <= Win2dConicShader.MaxStops)
            {
                colors.Add(colors[^1]);
                angles.Add(angles[^1]);
            }
        }

        return (colors.ToArray(), angles.ToArray());
    }

    private static Scene Draw(bool forWedges, Action<Win2dCanvas>? transform = null, float dpi = 96)
    {
        var scene = Scene.Create(80, 80, options: new Win2dComposerOptions { Dpi = dpi });
        var (colors, angles) = Stops(forWedges);
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(40, 40), 36, colors, angles);
        transform?.Invoke(scene.Canvas);
        scene.Canvas.DrawRectangle(conic, 0, 0, 80, 80);
        return scene;
    }

    private static void AssertClose(Scene shader, Scene wedges, int tolerance, double cx = 40, double cy = 40, double radius = 36)
    {
        var worst = 0;
        var at = (0, 0);
        for (var y = 2; y < 78; y += 3)
        {
            for (var x = 2; x < 78; x += 3)
            {
                // The wedges are drawn aliased and the shader's disc is smoothed, so the rim of the disc is not compared.
                if (Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2)) > radius - 2)
                    continue;

                var a = shader.Px(x, y);
                var b = wedges.Px(x, y);

                // Skip the immediate neighbourhood of a stop's angle (the wedges are a degree wide there) and of the centre.
                var d = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
                if (d > worst)
                {
                    worst = d;
                    at = (x, y);
                }
            }
        }

        Assert.True(worst <= tolerance, $"largest difference {worst} at {at}: shader {shader.Px(at.Item1, at.Item2)}, wedges {wedges.Px(at.Item1, at.Item2)}");
    }

    [Fact]
    public void The_shader_paints_the_same_gradient_as_the_wedges()
    {
        using var shader = Draw(false);
        using var wedges = Draw(true);

        // The gradient really is there: reds, greens and blues all appear.
        Assert.True(shader.OpaquePixels() > 3000);
        AssertClose(shader, wedges, 12);
    }

    [Fact]
    public void The_shader_keeps_the_alpha_of_a_translucent_stop_exactly()
    {
        // The wedges overlap their neighbours slightly, which doubles up a translucent colour along every seam; the shader has no seams.
        using var scene = Scene.Create(80, 80);
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(40, 40), 36,
            [PaintColor.FromArgb(128, 0, 0, 255), PaintColor.FromArgb(128, 0, 0, 255)], [0, 2 * Math.PI]);
        scene.Canvas.DrawRectangle(conic, 0, 0, 80, 80);

        foreach (var (x, y) in new[] { (60, 40), (40, 60), (20, 40), (40, 20), (55, 55), (25, 25) })
        {
            var (r, g, b, a) = scene.Px(x, y);
            Assert.InRange(a, 126, 130);
            Assert.InRange(b, 126, 130);
            Assert.Equal(0, r + g);
        }
    }

    [Fact]
    public void The_shader_follows_the_current_transform()
    {
        void Move(Win2dCanvas canvas) => canvas.PushTransform(System.Numerics.Matrix3x2.CreateScale(0.5f) * System.Numerics.Matrix3x2.CreateTranslation(20, 10));

        using var shader = Draw(false, Move);
        using var wedges = Draw(true, Move);

        Assert.True(shader.OpaquePixels() > 500);
        AssertClose(shader, wedges, 12, 40, 30, 18);
    }

    [Fact]
    public void The_shader_agrees_with_the_wedges_on_a_high_resolution_target()
    {
        using var shader = Draw(false, dpi: 192);
        using var wedges = Draw(true, dpi: 192);

        // Twice the pixels across, so a point at (x, y) points is at (2x, 2y) pixels.
        Assert.True(shader.OpaquePixels() > 3000);
        for (var y = 6; y < 154; y += 7)
        {
            for (var x = 6; x < 154; x += 7)
            {
                if (Math.Sqrt(Math.Pow(x - 80, 2) + Math.Pow(y - 80, 2)) > 68)
                    continue;

                var a = shader.Px(x, y);
                var b = wedges.Px(x, y);
                Assert.True(Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B)) <= 14, $"({x}, {y}): {a} vs {b}");
            }
        }
    }
}
