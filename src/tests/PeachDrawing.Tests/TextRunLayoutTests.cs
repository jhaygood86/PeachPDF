using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests;

/// <summary>Measuring a run of text the way a canvas does: its width with spacing, its boxes and its characters, from the font's own shaping.</summary>
public class TextRunLayoutTests
{
    private static readonly Font Font = Load(BundledFonts.Ttf);

    private static Font Load(string path)
    {
        var set = new FontSet();
        var family = set.AddFile(path, new AddOptions { FamilyName = "TextRun-" + Guid.NewGuid().ToString("N") });
        Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
        return new TypefaceFont(match.Typeface, 40);
    }

    [Fact]
    public void LetterSpacing_IsAddedAfterEveryCharacter_AndWordSpacingAfterEverySpace()
    {
        var plain = TextRunMeasure.Measure(Font, "ab cd").Width;
        var letters = TextRunMeasure.Measure(Font, "ab cd", letterSpacing: 3).Width;
        var words = TextRunMeasure.Measure(Font, "ab cd", wordSpacing: 7).Width;

        Assert.Equal(plain + 5 * 3, letters, 3);
        Assert.Equal(plain + 7, words, 3);
    }

    [Fact]
    public void TheEmSquareIsSharedBetweenAscentAndDescentInTheFontsProportion()
    {
        var metrics = TextRunMeasure.Measure(Font, "x");

        Assert.Equal(40, metrics.EmAscent + metrics.EmDescent, 6);
        Assert.Equal(metrics.FontAscent / (metrics.FontAscent + metrics.FontDescent), metrics.EmAscent / 40, 6);
        Assert.True(metrics.InkRight > metrics.InkLeft);
        Assert.True(metrics.InkAscent > 0);
    }

    [Fact]
    public void ACharacterWithAMarkIsOneCluster_AndItsBoxIsWhereItsAdvanceSays()
    {
        var layout = TextRunLayout.Create(Font, "aéb");

        Assert.Equal([0, 1, 3, 4], layout.Boundaries);
        var boxes = layout.SelectionBoxes(0, 1);
        Assert.Single(boxes);
        Assert.Equal(0, boxes[0].X, 3);
        Assert.Equal(layout.CaretX(1), boxes[0].Width, 3);
        Assert.Equal(layout.Width, layout.CaretX(4), 3);
    }

    [Fact]
    public void APointHalfWayBetweenTwoCharactersGoesToTheOneBeforeIt()
    {
        var layout = TextRunLayout.Create(Font, "abc");
        var a = layout.CaretX(1);

        Assert.Equal(0, layout.IndexFromOffset(a / 2));
        Assert.Equal(1, layout.IndexFromOffset(a / 2 + 1));
        Assert.Equal(0, layout.IndexFromOffset(-100));
        Assert.Equal(3, layout.IndexFromOffset(layout.Width + 100));
    }

    [Fact]
    public void TheInkOfARangeIsOnlyThatOfItsCharacters()
    {
        var layout = TextRunLayout.Create(Font, "i  I");

        Assert.True(layout.TryGetInk(0, 1, out var left, out var right, out _, out _));
        Assert.True(right < layout.CaretX(3));
        Assert.False(layout.TryGetInk(1, 3, out _, out _, out _, out _));
        Assert.True(layout.TryGetInk(3, 4, out var capitalLeft, out _, out _, out _));
        Assert.True(capitalLeft >= layout.CaretX(3) - 1e-6);
    }

    [Fact]
    public void ARightToLeftLineStartsAtItsRightEdge()
    {
        var layout = TextRunLayout.Create(Font, "ab", rightToLeft: true);

        // The first character of a right-to-left line is its right-most.
        Assert.True(layout.CaretX(0) > layout.CaretX(2) || layout.Runs.Any(r => r.IsRightToLeft) == false);
    }

    [Fact]
    public void ForAFontWithNoSmallCapitals_TheLowerCaseLettersAreCapitalsAtASmallerSize()
    {
        var small = new PeachDrawing.Text.Shaping.ShapeSettings { Caps = PeachDrawing.Text.Shaping.CapsMode.SmallCaps };
        var noSmallCaps = Load(BundledFonts.ColorV0);
        Assert.True(SyntheticCaps.Applies(noSmallCaps, small));

        var normal = TextRunMeasure.Measure(noSmallCaps, "Hello").Width;
        var allCapitals = TextRunMeasure.Measure(noSmallCaps, "HELLO").Width;
        var withSmall = TextRunMeasure.Measure(noSmallCaps, "Hello", features: small).Width;
        var smallOnly = TextRunMeasure.Measure(noSmallCaps, "hello", features: small).Width;

        Assert.NotEqual(normal, withSmall, 3);
        Assert.True(withSmall < allCapitals);
        Assert.Equal(TextRunMeasure.Measure(noSmallCaps, "HELLO").Width * SyntheticCaps.Scale, smallOnly, 2);
        Assert.False(SyntheticCaps.Applies(noSmallCaps, new PeachDrawing.Text.Shaping.ShapeSettings()));
    }

    [Fact]
    public void TheAllSmallCapsModeShrinksTheCapitalsToo_AndASplitKeepsTheSizesApart()
    {
        var text = SyntheticCaps.Convert("aBc", PeachDrawing.Text.Shaping.CapsMode.AllSmallCaps, out var small);
        Assert.Equal("ABC", text);
        Assert.Equal([true, true, true], small);

        text = SyntheticCaps.Convert("aBc", PeachDrawing.Text.Shaping.CapsMode.SmallCaps, out small);
        Assert.Equal([true, false, true], small);
        Assert.Equal([("A", 0, true), ("B", 1, false), ("C", 2, true)], SyntheticCaps.Split(text, small));
    }

    [Fact]
    public void ARightToLeftLineStartsAtTheLeftEdgeWithExactPositions()
    {
        var layout = TextRunLayout.Create(Font, "(.)", rightToLeft: true);

        // Laid out in a width of 1e9 it sat at 1e9 minus its width, where single precision is 64 apart.
        Assert.Equal(layout.Width, layout.CaretX(0), 3);
        Assert.Equal(0, layout.CaretX(3), 3);
        var box = Assert.Single(layout.SelectionBoxes(1, 2));
        Assert.True(box.X > 0 && box.X + box.Width < layout.Width);
        Assert.True(box.Width > 0 && box.Width < layout.Width / 2);
    }

    [Fact]
    public void APointInARunIsForThatRunsCharacters_WhereTwoRunsMeetAtOnePlace()
    {
        var rtl = TextRunLayout.Create(Font, "))a((", rightToLeft: true);
        var run = rtl.Runs.Single(r => !r.IsRightToLeft);
        var inside = run.X + run.Width * 0.25;

        // A point in the left-to-right run's left half is before its character, whichever way the neighbours' boundaries are drawn.
        Assert.Equal(run.Start, rtl.IndexFromOffset(inside));
        Assert.Equal(run.End, rtl.IndexFromOffset(run.X + run.Width * 0.75));
    }
}
