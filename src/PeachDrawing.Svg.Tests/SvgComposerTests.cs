using PeachDrawing.Core;
using System.Text;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgComposerTests
{
    [Fact]
    public void AnEmptyPageIsAWellFormedSvgDocumentSizedInPoints()
    {
        using var scene = Create(300, 150);

        var root = scene.Document.Root!;
        Assert.Equal(Ns + "svg", root.Name);
        Assert.Equal(("300pt", "150pt", "0 0 300 150"), (Attr(root, "width"), Attr(root, "height"), Attr(root, "viewBox")));
        Assert.Empty(root.Elements());
    }

    [Fact]
    public void ThePageIsInLayoutUnitsSoTheViewBoxScalesByPixelsPerPoint()
    {
        using var scene = Create(100, 50, pixelsPerPoint: 2);

        var root = scene.Document.Root!;
        Assert.Equal(("100pt", "50pt", "0 0 200 100"), (Attr(root, "width"), Attr(root, "height"), Attr(root, "viewBox")));
        Assert.Equal(2, scene.Canvas.PixelsPerPoint);
        Assert.Equal(new Rect(0, 0, 200, 100), scene.Canvas.GetClip());
    }

    [Fact]
    public void TitleDescriptionAndBackgroundAreWrittenBeforeTheDrawing()
    {
        using var scene = Create(options: new SvgComposerOptions { Title = "A & B", Description = "desc", Background = PaintColor.FromArgb(128, 255, 255, 255) });
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);

        var elements = scene.Document.Root!.Elements().ToList();
        Assert.Equal(["title", "desc", "rect", "rect"], elements.Select(e => e.Name.LocalName));
        Assert.Equal("A & B", elements[0].Value);
        Assert.Equal(("#ffffff", "0.502"), (Attr(elements[2], "fill"), Attr(elements[2], "fill-opacity")));
    }

    [Fact]
    public void ATransparentBackgroundWritesNoRectangle()
    {
        using var scene = Create(options: new SvgComposerOptions { Background = PaintColor.Transparent });

        Assert.Empty(scene.Document.Root!.Elements());
    }

    [Fact]
    public void SavingToAStreamWritesUtf8WithoutAByteOrderMarkAndLeavesTheStreamOpen()
    {
        using var scene = Create(options: new SvgComposerOptions { Title = "é" });
        using var stream = new MemoryStream();
        scene.Page.Save(stream);

        Assert.True(stream.CanWrite);
        var bytes = stream.ToArray();
        Assert.Equal((byte)'<', bytes[0]);
        Assert.Contains("<title>é</title>", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void SavingToAWriterWritesTheSameDocumentAsToSvg()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(1, 2, 3), 0, 0, 5, 5);
        using var writer = new StringWriter();
        scene.Page.Save(writer);

        Assert.Equal(scene.Page.ToSvg(), writer.ToString());
    }

    [Fact]
    public void SavingTwiceGivesTheSameDocument()
    {
        using var scene = Create();
        scene.Canvas.PushClip(new Rect(0, 0, 5, 5));
        scene.Canvas.DrawRectangle(scene.Solid(1, 2, 3), 0, 0, 5, 5);

        Assert.Equal(scene.Page.ToSvg(), scene.Page.ToSvg());
    }

    [Fact]
    public void EachPageIsItsOwnDocumentWithItsOwnDefinitions()
    {
        var context = new SvgRenderContext();
        using var composer = new SvgComposer(context);
        var first = composer.AddPage(10, 10);
        var second = composer.AddPage(20, 20);
        first.Canvas.PushClip(new Rect(0, 0, 5, 5));
        first.Canvas.PopClip();

        Assert.Equal(2, composer.PageCount);
        Assert.Equal([first, second], composer.Pages);
        Assert.Contains("<clipPath", first.ToSvg());
        Assert.DoesNotContain("<clipPath", second.ToSvg());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, -1)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void APageNeedsAPositiveFiniteSize(double width, double height)
    {
        using var composer = new SvgComposer(new SvgRenderContext());

        Assert.Throws<ArgumentOutOfRangeException>(() => composer.AddPage(width, height));
    }

    [Fact]
    public void TheComposerRequiresAContext()
    {
        Assert.Throws<ArgumentNullException>(() => new SvgComposer(null!));
    }

    [Fact]
    public void DrawingAfterThePageWasWrittenDoesNotChangeTheWrittenPage()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);
        var written = scene.Page.ToSvg();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 5, 5, 1, 1);

        Assert.Equal(written, scene.Page.ToSvg());
    }
}
