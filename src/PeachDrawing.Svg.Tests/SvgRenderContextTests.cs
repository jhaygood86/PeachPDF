using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Svg.Tests;

public class SvgRenderContextTests
{
    [Fact]
    public async Task AFontAddedToTheContextIsFoundByItsFamilyName()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Test Family");

        Assert.True(context.IsFontExists("Test Family"));
        var font = context.GetFont("Test Family", 16, PaintFontStyle.Regular);
        Assert.NotNull(font);
        Assert.Equal(16, font!.Size);
    }

    [Fact]
    public async Task FontsAreBuiltInPointsWhateverTheLayoutUnitIs()
    {
        var context = new SvgRenderContext { PixelsPerPoint = 2 };
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Test Family");

        Assert.Equal(2, context.LayoutUnitsPerPoint);
        Assert.Equal(10, context.GetFont("Test Family", 20, PaintFontStyle.Regular)!.Size);
    }

    [Fact]
    public async Task ASubclassHearsAboutFontsAddedToIt()
    {
        var context = new Spy();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Spied");

        Assert.Equal(["Spied"], context.Added);
    }

    [Fact]
    public void KnownColourNamesResolveAndUnknownOnesAreEmpty()
    {
        var context = new SvgRenderContext();

        Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), context.GetColor("red"));
        Assert.Equal(PaintColor.Empty, context.GetColor("not-a-colour"));
    }

    [Fact]
    public void TheMediaTypeIsScreenWhenOfferedAndAllOtherwise()
    {
        var context = new SvgRenderContext();

        Assert.Equal("screen", context.GetCssMediaType(["print", "screen"]));
        Assert.Equal("all", context.GetCssMediaType(["print"]));
    }

    [Fact]
    public async Task AFontFaceStreamIsAddedAndAnUnsupportedFormatIsRefused()
    {
        var context = new Spy();
        await using var stream = File.OpenRead(BundledFonts.Ttf);

        Assert.True(await context.AddFace("Via Face", stream, "truetype"));
        Assert.True(context.IsFontExists("Via Face"));
        Assert.False(await context.AddFace("Never", new MemoryStream(), "embedded-opentype"));
        Assert.False(await context.AddLocalFontFamily("Missing Local", "No Such Face"));
    }

    [Fact]
    public async Task AFontForACodepointComesOnlyFromAFamilyThatHasIt()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Latin Only");

        Assert.NotNull(context.GetFontForCodepoint("Latin Only", 16, PaintFontStyle.Regular, new System.Text.Rune('a')));
        Assert.Null(context.GetFontForCodepoint("Latin Only", 16, PaintFontStyle.Regular, new System.Text.Rune(0x4E2D)));
        Assert.Null(context.GetFontForCodepoint("No Such Family", 16, PaintFontStyle.Regular, new System.Text.Rune('a')));
    }

    private sealed class Spy : SvgRenderContext
    {
        public List<string> Added { get; } = [];

        public Task<bool> AddFace(string family, Stream stream, string? format) => AddFontFromStream(family, stream, format);

        protected override void OnFontFamilyAdded(string familyName) => Added.Add(familyName);
    }
}
