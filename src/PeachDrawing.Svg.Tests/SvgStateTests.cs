using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System.Numerics;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgStateTests
{
    [Fact]
    public void ATransformIsAGroupWithItsMatrixAndTheDrawingIsInsideIt()
    {
        using var scene = Create();
        scene.Canvas.PushTransform(new Matrix3x2(2, 0, 0, 3, 10, 20));
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        scene.Canvas.PopTransform();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);

        var drawn = scene.Drawn().ToList();
        Assert.Equal(2, drawn.Count);
        Assert.Equal(Ns + "g", drawn[0].Name);
        Assert.Equal("matrix(2 0 0 3 10 20)", Attr(drawn[0], "transform"));
        Assert.Equal(Ns + "rect", Assert.Single(drawn[0].Elements()).Name);
        Assert.Equal(Ns + "rect", drawn[1].Name);
    }

    [Fact]
    public void AnIdentityTransformWritesNoGroupButStillNestsProperly()
    {
        using var scene = Create();
        scene.Canvas.PushTransform(Matrix3x2.Identity);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        scene.Canvas.PopTransform();

        Assert.Equal(Ns + "rect", Assert.Single(scene.Drawn()).Name);
    }

    [Fact]
    public void NestedTransformsComposeInCurrentTransformAndAreRestoredOnPop()
    {
        using var scene = Create();
        var g = scene.Canvas;
        g.PushTransform(Matrix3x2.CreateTranslation(10, 0));
        g.PushTransform(Matrix3x2.CreateScale(2, 2));

        var p = Vector2.Transform(new Vector2(1, 1), g.CurrentTransform);
        Assert.Equal(12, p.X, 4);
        Assert.Equal(2, p.Y, 4);
        Assert.Equal(2, g.TransformScale.X, 4);

        g.PopTransform();
        Assert.Equal(Matrix3x2.CreateTranslation(10, 0), g.CurrentTransform);
        g.PopTransform();
        Assert.Equal(Matrix3x2.Identity, g.CurrentTransform);
        Assert.Equal((1.0, 1.0), g.TransformScale);
    }

    [Fact]
    public void ARectangleClipIsAClipPathAndAGroupThatUsesIt()
    {
        using var scene = Create();
        scene.Canvas.PushClip(new Rect(5, 6, 70, 80));
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 100, 100);
        scene.Canvas.PopClip();

        var clipPath = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "clipPath", clipPath.Name);
        var rect = Assert.Single(clipPath.Elements());
        Assert.Equal(("5", "6", "70", "80"), (Attr(rect, "x"), Attr(rect, "y"), Attr(rect, "width"), Attr(rect, "height")));

        var group = Assert.Single(scene.Drawn());
        Assert.Equal($"url(#{Attr(clipPath, "id")})", Attr(group, "clip-path"));
        Assert.Single(group.Elements());
    }

    [Fact]
    public void TheSameClipPushedTwiceSharesOneClipPath()
    {
        using var scene = Create();
        for (var i = 0; i < 2; i++)
        {
            scene.Canvas.PushClip(new Rect(0, 0, 10, 10));
            scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
            scene.Canvas.PopClip();
        }

        Assert.Single(scene.Defs());
        Assert.Equal(2, scene.Drawn().Count());
    }

    [Fact]
    public void APathClipKeepsItsCurvesAndItsFillRule()
    {
        using var scene = Create();
        var path = scene.Canvas.GetGraphicsPath();
        path.AddCircle(20, 20, 10);
        path.FillMode = FillMode.EvenOdd;
        scene.Canvas.PushClip(path);
        scene.Canvas.PopClip();

        var clipPath = Assert.Single(scene.Defs());
        var shape = Assert.Single(clipPath.Elements());
        Assert.Equal(Ns + "path", shape.Name);
        Assert.Contains('C', Attr(shape, "d"));
        Assert.Equal("evenodd", Attr(shape, "clip-rule"));
    }

    [Fact]
    public void AnExcludeClipIsAnEvenOddPathOfEverythingButTheRectangle()
    {
        using var scene = Create();
        scene.Canvas.PushClipExclude(new Rect(10, 10, 20, 20));
        scene.Canvas.PopClip();

        var shape = Assert.Single(Assert.Single(scene.Defs()).Elements());
        Assert.Equal("evenodd", Attr(shape, "clip-rule"));
        Assert.EndsWith("M10 10h20v20h-20Z", Attr(shape, "d"));
    }

    [Fact]
    public void TheTrackedClipBoundFollowsPushesAndPops()
    {
        using var scene = Create(300, 200);
        var g = scene.Canvas;
        Assert.Equal(new Rect(0, 0, 300, 200), g.GetClip());
        g.PushClip(new Rect(1, 2, 3, 4));
        Assert.Equal(new Rect(1, 2, 3, 4), g.GetClip());
        g.PopClip();
        Assert.Equal(new Rect(0, 0, 300, 200), g.GetClip());
    }

    [Fact]
    public void PopsCountPushesLikeThePdfCanvasAndOnlyAnEmptyStackIsRejected()
    {
        using var scene = Create();
        scene.Canvas.PushClip(new Rect(0, 0, 1, 1));
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(1, 1));

        scene.Canvas.PopClip();       // pops the innermost push, the transform
        Assert.Equal(Matrix3x2.Identity, scene.Canvas.CurrentTransform);
        scene.Canvas.PopTransform();  // and this the clip
        Assert.Throws<InvalidOperationException>(() => scene.Canvas.PopBlendMode());
        Assert.Throws<InvalidOperationException>(() => scene.Canvas.PopClip());
        Assert.Equal(new Rect(0, 0, 200, 100), scene.Canvas.GetClip());
    }

    [Fact]
    public void SuspendingTheClipsInsideALayerAndATransformPopsAsManyPushesAndKeepsTheOutputBalanced()
    {
        // A fixed-position box lifts the clips and puts them back around its painting. The canvas counts pushes, as the PDF canvas does
        // (one saved state each), so the transform between the clips goes with them for the box - and nothing is left open or unbalanced.
        using var scene = Create();
        var g = scene.Canvas;
        g.PushClip(new Rect(0, 0, 50, 50));
        using var layer = g.BeginLayer(new LayerOptions(Opacity: 0.5))!;
        g.PushTransform(Matrix3x2.CreateTranslation(5, 5));
        g.PushClip(new Rect(1, 1, 10, 10));
        g.SuspendClipping();
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 3, 3);
        g.ResumeClipping();
        g.PopClip();
        g.PopTransform();
        g.PopTransform();
        layer.Dispose();
        g.PopClip();

        var drawn = scene.Document.Root!.Descendants(Ns + "rect").Single(r => Attr(r, "width") == "3");
        var groups = drawn.Ancestors().Where(a => a.Name == Ns + "g").ToList();
        Assert.Equal("0.5", Attr(Assert.Single(groups, a => Attr(a, "opacity").Length > 0), "opacity"));
        Assert.DoesNotContain(groups, group => Attr(group, "clip-path").Length > 0 && group.Elements().Contains(drawn));
        Assert.Equal(new Rect(0, 0, 200, 100), g.GetClip());
        _ = scene.Page.ToSvg(); // well-formed
    }

    [Fact]
    public void ABlendModePushedInsideAClipLeavesNoClipGroupAroundTheBlendedShape()
    {
        // A clip-path group is an isolated group: a blend inside it would not reach the page behind the clip. The clip rides on the element.
        using var scene = Create();
        var g = scene.Canvas;
        g.PushClip(new Rect(5, 6, 70, 80));
        g.PushBlendMode(PaintBlendMode.Multiply);
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 100, 100);
        g.PopBlendMode();
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 10, 10);
        g.PopClip();
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);

        var drawn = scene.Drawn().ToList();
        Assert.Equal(3, drawn.Count);
        Assert.All(drawn, e => Assert.Equal(Ns + "rect", e.Name)); // no groups at all
        var clip = scene.Defs().Last(e => e.Name == Ns + "clipPath");
        Assert.Equal($"url(#{Attr(clip, "id")})", Attr(drawn[0], "clip-path"));
        Assert.Equal("mix-blend-mode:multiply", Attr(drawn[0], "style"));
        Assert.Equal(Attr(drawn[0], "clip-path"), Attr(drawn[1], "clip-path")); // still clipped after the blend ends
        Assert.Equal(string.Empty, Attr(drawn[1], "style"));
        Assert.Equal(string.Empty, Attr(drawn[2], "clip-path")); // and not after the clip does
        Assert.Equal(("5", "6", "70", "80"), (Attr(clip.Elements().Single(), "x"), Attr(clip.Elements().Single(), "y"), Attr(clip.Elements().Single(), "width"), Attr(clip.Elements().Single(), "height")));
    }

    [Fact]
    public void AClipPushedWhileBlendingIsFlatAndTheClipsAreIntersectedInTheCoordinatesOfTheElement()
    {
        using var scene = Create();
        var g = scene.Canvas;
        g.PushClip(new Rect(0, 0, 50, 50));
        g.PushTransform(Matrix3x2.CreateTranslation(10, 0));
        g.PushBlendMode(PaintBlendMode.Screen);
        g.PushClip(new Rect(2, 2, 20, 20)); // pushed while blending, in the translated coordinates
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);

        var group = Assert.Single(scene.Drawn());
        Assert.Equal("matrix(1 0 0 1 10 0)", Attr(group, "transform"));
        var rect = Assert.Single(group.Elements());
        var outer = scene.Defs().Single(e => Attr(e, "id") == Attr(rect, "clip-path")[5..^1]);
        var inner = scene.Defs().Single(e => Attr(e, "id") == Attr(outer, "clip-path")[5..^1]);
        // The first clip was pushed before the translation, so it is moved back by it; the second needs no move.
        Assert.Equal("matrix(1 0 0 1 -10 0)", Attr(inner.Elements().Single(), "transform"));
        Assert.Equal(string.Empty, Attr(outer.Elements().Single(), "transform"));
        Assert.Equal(string.Empty, Attr(inner, "clip-path"));
    }

    [Fact]
    public void ABlendedLayerInsideAClipCarriesTheClipItselfAndIsNotInsideItsGroup()
    {
        using var scene = Create();
        var g = scene.Canvas;
        g.PushClip(new Rect(0, 0, 50, 50));
        using (var layer = g.BeginLayer(new LayerOptions(Opacity: 0.5, BlendMode: PaintBlendMode.Multiply))!)
        {
            layer.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        g.PopClip();

        var layerGroup = Assert.Single(scene.Drawn());
        Assert.Equal("0.5", Attr(layerGroup, "opacity"));
        Assert.StartsWith("url(#", Attr(layerGroup, "clip-path"));
        Assert.Equal("isolation:isolate;mix-blend-mode:multiply", Attr(layerGroup, "style"));
        Assert.Equal(string.Empty, Attr(Assert.Single(layerGroup.Elements()), "clip-path")); // the layer is clipped as a whole, once
    }

    [Fact]
    public void AnImageDrawnUnderAFlatClipIsWrappedSoTheClipDoesNotShareItsTransform()
    {
        using var scene = Create();
        var g = scene.Canvas;
        g.PushClip(new Rect(0, 0, 50, 50));
        g.PushBlendMode(PaintBlendMode.Multiply);
        g.DrawImage(scene.Png(), new Rect(1, 2, 16, 16));

        var wrapper = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "g", wrapper.Name);
        Assert.StartsWith("url(#", Attr(wrapper, "clip-path"));
        Assert.Equal("mix-blend-mode:multiply", Attr(wrapper, "style"));
        var use = Assert.Single(wrapper.Elements());
        Assert.Equal("matrix(2 0 0 2 1 2)", Attr(use, "transform"));
        Assert.Equal(string.Empty, Attr(use, "clip-path"));
    }

    [Fact]
    public void GroupsLeftOpenAreClosedWhenThePageIsWritten()
    {
        using var scene = Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(1, 1));
        scene.Canvas.PushClip(new Rect(0, 0, 5, 5));
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);

        var outer = Assert.Single(scene.Drawn());
        var inner = Assert.Single(outer.Elements());
        Assert.Equal(Ns + "g", inner.Name);
        Assert.Equal(Ns + "rect", Assert.Single(inner.Elements()).Name);
    }

    [Theory]
    [InlineData(PaintBlendMode.Multiply, "multiply")]
    [InlineData(PaintBlendMode.ColorDodge, "color-dodge")]
    [InlineData(PaintBlendMode.SoftLight, "soft-light")]
    [InlineData(PaintBlendMode.Luminosity, "luminosity")]
    [InlineData(PaintBlendMode.Plus, "plus-lighter")]
    public void ABlendModeBlendsEachShapeDrawnWhileItIsPushed(PaintBlendMode mode, string css)
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);
        scene.Canvas.PushBlendMode(mode);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);
        scene.Canvas.PopBlendMode();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);

        var styles = scene.Drawn().Select(e => Attr(e, "style")).ToList();
        Assert.Equal(["", $"mix-blend-mode:{css}", ""], styles);
    }

    [Fact]
    public void ALayerIsAnIsolatedGroupWithItsOpacityAndBlendMode()
    {
        using var scene = Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.25, BlendMode: PaintBlendMode.Screen)))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        var group = Assert.Single(scene.Drawn());
        Assert.Equal("0.25", Attr(group, "opacity"));
        Assert.Equal("isolation:isolate;mix-blend-mode:screen", Attr(group, "style"));
        Assert.Equal(string.Empty, Attr(Assert.Single(group.Elements()), "style"));
    }

    [Fact]
    public void ABlendModePushedInsideALayerIsConfinedToItAndTheOutsideStateComesBack()
    {
        using var scene = Create();
        scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        scene.Canvas.PopBlendMode();

        var drawn = scene.Drawn().ToList();
        Assert.Equal("isolation:isolate;mix-blend-mode:multiply", Attr(drawn[0], "style"));
        Assert.Equal(string.Empty, Attr(drawn[0].Elements().Single(), "style"));
        Assert.Equal("mix-blend-mode:multiply", Attr(drawn[1], "style"));
    }

    [Fact]
    public void ALayerWithABlurIsAFilteredGroupWhoseRegionIsWideEnoughForTheBlur()
    {
        using var scene = Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(10, 10, 50, 40), Effects: [new BlurEffect(2, 4)])))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 10, 10, 50, 40);
        }

        var filter = scene.Defs().Single(e => e.Name == Ns + "filter");
        Assert.Equal(Ns + "filter", filter.Name);
        Assert.Equal("sRGB", Attr(filter, "color-interpolation-filters"));
        Assert.Equal("userSpaceOnUse", Attr(filter, "filterUnits"));
        Assert.Equal(("-2", "-2", "74", "64"), (Attr(filter, "x"), Attr(filter, "y"), Attr(filter, "width"), Attr(filter, "height")));
        var blur = Assert.Single(filter.Elements());
        Assert.Equal(Ns + "feGaussianBlur", blur.Name);
        Assert.Equal("2 4", Attr(blur, "stdDeviation"));
        Assert.Equal($"url(#{Attr(filter, "id")})", Attr(Assert.Single(scene.Drawn()), "filter"));
    }

    [Fact]
    public void ADropShadowEffectIsAFeDropShadowWithTheShadowsColourAndOffset()
    {
        using var scene = Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 50, 50),
                   Effects: [new DropShadowEffect(3, 4, 1.5, 1.5, PaintColor.FromArgb(128, 10, 20, 30))])))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 5, 5, 20, 20);
        }

        var shadow = Assert.Single(scene.Defs().Single(e => e.Name == Ns + "filter").Elements());
        Assert.Equal(Ns + "feDropShadow", shadow.Name);
        Assert.Equal(("3", "4", "1.5 1.5"), (Attr(shadow, "dx"), Attr(shadow, "dy"), Attr(shadow, "stdDeviation")));
        Assert.Equal("#0a141e", Attr(shadow, "flood-color"));
        Assert.Equal("0.502", Attr(shadow, "flood-opacity"));
    }

    [Fact]
    public void ALayerColourMatrixIsAFeColorMatrixReadingColumnsOfTheCanvasMatrix()
    {
        using var scene = Create();
        // Output red = 0.5 * input green + 0.25 offset; everything else passes through.
        var linear = Matrix4x4.Identity;
        linear.M11 = 0;
        linear.M21 = 0.5f;
        var matrix = new ColorMatrix(linear, new Vector4(0.25f, 0, 0, 0));
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: matrix, Bounds: new Rect(0, 0, 10, 10))))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        var primitive = Assert.Single(scene.Defs().Single(e => e.Name == Ns + "filter").Elements());
        Assert.Equal(Ns + "feColorMatrix", primitive.Name);
        Assert.Equal("0 0.5 0 0 0.25 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0", Attr(primitive, "values"));
    }
}
