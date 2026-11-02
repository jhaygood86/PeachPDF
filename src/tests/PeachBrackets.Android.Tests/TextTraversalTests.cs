using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class TextTraversalTests
{
    [Fact]
    public void ACharacterStepMovesOverOneTextElement_NotOneUtf16Unit()
    {
        const string text = "a\U0001F600éb"; // a, an emoji (a surrogate pair), e + combining accent, b

        Assert.Equal((0, 1), TextTraversal.Next(text, 0, TextGranularity.Character));
        Assert.Equal((1, 3), TextTraversal.Next(text, 1, TextGranularity.Character));
        Assert.Equal((3, 5), TextTraversal.Next(text, 3, TextGranularity.Character));
        Assert.Equal((5, 6), TextTraversal.Next(text, 5, TextGranularity.Character));
        Assert.Null(TextTraversal.Next(text, 6, TextGranularity.Character));

        Assert.Equal((5, 6), TextTraversal.Previous(text, 6, TextGranularity.Character));
        Assert.Equal((3, 5), TextTraversal.Previous(text, 5, TextGranularity.Character));
        Assert.Equal((1, 3), TextTraversal.Previous(text, 3, TextGranularity.Character));
        Assert.Equal((0, 1), TextTraversal.Previous(text, 1, TextGranularity.Character));
        Assert.Null(TextTraversal.Previous(text, 0, TextGranularity.Character));
    }

    [Fact]
    public void AWordStepSkipsPunctuationAndSpacesAndKeepsContractionsTogether()
    {
        const string text = "  Don't stop, believin'!";

        var first = TextTraversal.Next(text, 0, TextGranularity.Word)!.Value;
        Assert.Equal("Don't", text[first.Start..first.End]);
        var second = TextTraversal.Next(text, first.End, TextGranularity.Word)!.Value;
        Assert.Equal("stop", text[second.Start..second.End]);
        var third = TextTraversal.Next(text, second.End, TextGranularity.Word)!.Value;
        Assert.Equal("believin'", text[third.Start..third.End]);
        Assert.Null(TextTraversal.Next(text, third.End, TextGranularity.Word));

        var back = TextTraversal.Previous(text, text.Length, TextGranularity.Word)!.Value;
        Assert.Equal("believin'", text[back.Start..back.End]);
        back = TextTraversal.Previous(text, back.Start, TextGranularity.Word)!.Value;
        Assert.Equal("stop", text[back.Start..back.End]);
        back = TextTraversal.Previous(text, back.Start, TextGranularity.Word)!.Value;
        Assert.Equal("Don't", text[back.Start..back.End]);
        Assert.Null(TextTraversal.Previous(text, back.Start, TextGranularity.Word));
    }

    [Fact]
    public void AParagraphStepMovesBetweenLineBreaks()
    {
        const string text = "one\n\ntwo words\nthree";

        var a = TextTraversal.Next(text, 0, TextGranularity.Paragraph)!.Value;
        Assert.Equal("one", text[a.Start..a.End]);
        var b = TextTraversal.Next(text, a.End, TextGranularity.Paragraph)!.Value;
        Assert.Equal("two words", text[b.Start..b.End]);
        var c = TextTraversal.Next(text, b.End, TextGranularity.Paragraph)!.Value;
        Assert.Equal("three", text[c.Start..c.End]);
        Assert.Null(TextTraversal.Next(text, c.End, TextGranularity.Paragraph));

        var back = TextTraversal.Previous(text, text.Length, TextGranularity.Paragraph)!.Value;
        Assert.Equal("three", text[back.Start..back.End]);
        back = TextTraversal.Previous(text, back.Start, TextGranularity.Paragraph)!.Value;
        Assert.Equal("two words", text[back.Start..back.End]);
        back = TextTraversal.Previous(text, back.Start, TextGranularity.Paragraph)!.Value;
        Assert.Equal("one", text[back.Start..back.End]);
        Assert.Null(TextTraversal.Previous(text, back.Start, TextGranularity.Paragraph));
    }

    [Fact]
    public void LinesAndPagesAreNotSupported_AndAPositionOutsideTheTextIsClamped()
    {
        Assert.Null(TextTraversal.Next("abc", 0, TextGranularity.Line));
        Assert.Null(TextTraversal.Previous("abc", 3, TextGranularity.Page));
        Assert.Equal((0, 1), TextTraversal.Next("abc", -5, TextGranularity.Character));
        Assert.Equal((2, 3), TextTraversal.Previous("abc", 99, TextGranularity.Character));
        Assert.Equal(TextGranularity.Character | TextGranularity.Word | TextGranularity.Paragraph, TextTraversal.Supported);
        Assert.Throws<ArgumentNullException>(() => TextTraversal.Next(null!, 0, TextGranularity.Word));
        Assert.Throws<ArgumentNullException>(() => TextTraversal.Previous(null!, 0, TextGranularity.Word));
    }
}
