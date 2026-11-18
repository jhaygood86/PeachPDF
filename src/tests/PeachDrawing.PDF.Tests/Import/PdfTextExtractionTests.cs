using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// <see cref="PdfPageSource.GetText"/> and <see cref="PdfPageText"/>. Every glyph of the test font is 500 units wide (the file's own /Widths), so a
    /// character at 10 pt is 5 pt wide wherever the substitute font draws it, and positions can be asserted exactly.
    /// </summary>
    public class PdfTextExtractionTests
    {
        private const string Font =
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths [{W}] >>";

        private static readonly string FontObject = Font.Replace("{W}", string.Join(" ", Enumerable.Repeat("500", 224)));

        private static PdfPageSource Open(string content, string pageExtras = "", int width = 200, int height = 100, params string[] more) =>
            PdfPageSource.Open(TestPdf.Build(content, resources: "/Font << /F1 5 0 R >> /XObject << /Fm0 6 0 R >>", pageExtras: pageExtras,
                width: width, height: height, extraObjects: [FontObject, .. more]));

        private static PdfPageText Text(string content, string pageExtras = "", int width = 200, int height = 100, params string[] more) =>
            Open(content, pageExtras, width, height, more).GetText(0);

        [Fact]
        public void OneLine_IsReadWithTheBoxOfEachCharacter()
        {
            var text = Text("BT /F1 10 Tf 10 40 Td (Hello World) Tj ET");

            Assert.Equal("Hello World", text.Text);
            Assert.Equal(11, text.Length);
            Assert.Equal(1, text.LineCount);
            // H is 5 pt wide starting at x = 10; the baseline is 60 pt from the top of a 100 pt page, and the box runs from 0.8 em above it to 0.2 em below.
            var h = Assert.Single(text.GetBounds(0, 1));
            Assert.Equal(10, h.X, 3);
            Assert.Equal(5, h.Width, 3);
            Assert.Equal(52, h.Y, 3);
            Assert.Equal(10, h.Height, 3);
            // The whole line is one rectangle, from the start of H to the end of d.
            var line = Assert.Single(text.GetBounds(0, 11));
            Assert.Equal(10, line.X, 3);
            Assert.Equal(55, line.Width, 3);
        }

        [Fact]
        public void LinesAreSeparatedByANewline_AndAnEmptyLineHasNoRectangle()
        {
            var text = Text("BT /F1 10 Tf 10 60 Td 14 TL (First line) Tj T* (Second line) Tj ET");

            Assert.Equal("First line\nSecond line", text.Text);
            Assert.Equal(2, text.LineCount);
            Assert.Equal(new PdfTextRange(0, 10), text.LineAt(3));
            Assert.Equal(new PdfTextRange(11, 11), text.LineAt(15));
            var both = text.GetBounds(0, text.Length);
            Assert.Equal(2, both.Count);
            Assert.True(both[1].Y > both[0].Y, "the second line is lower on the page");
            Assert.Empty(text.GetBounds(10, 1));              // just the line break
        }

        [Fact]
        public void AGapInATJArrayBecomesASpace_AndKerningDoesNot()
        {
            var text = Text("BT /F1 10 Tf 10 40 Td [(Hel) 30 (lo) -600 (World)] TJ ET");

            Assert.Equal("Hello World", text.Text);
        }

        [Fact]
        public void TextInAFormXObject_IsReadAtTheFormsPosition()
        {
            var form = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 200 100] /Matrix [1 0 0 1 20 0] /Resources << /Font << /F1 5 0 R >> >>",
                "BT /F1 10 Tf 0 40 Td (Inside) Tj ET");
            var text = Text("/Fm0 Do", more: form);

            Assert.Equal("Inside", text.Text);
            Assert.Equal(20, text.GetBounds(0, 1)[0].X, 3);
        }

        [Fact]
        public void InvisibleText_ThatIsTheTextLayerOfAScan_IsStillRead()
        {
            var text = Text("BT /F1 10 Tf 3 Tr 10 40 Td (Scanned words) Tj ET");

            Assert.Equal("Scanned words", text.Text);
        }

        [Fact]
        public void APageWithNoText_HasEmptyText()
        {
            var text = Text("1 0 0 rg 0 0 50 50 re f");

            Assert.Equal("", text.Text);
            Assert.Equal(0, text.LineCount);
            Assert.Equal(-1, text.HitTest(new PaintPoint(10, 10), nearest: true));
            Assert.Empty(text.Find("a"));
            Assert.Empty(text.GetBounds(0, 5));
        }

        [Fact]
        public void ARotatedPage_ReportsPositionsAsTheViewerShowsThem()
        {
            // A 200 x 100 page turned 90 degrees is shown 100 wide and 200 tall: the line now runs down the page.
            var text = Text("BT /F1 10 Tf 10 40 Td (Hello) Tj ET", pageExtras: "/Rotate 90");

            Assert.Equal("Hello", text.Text);
            var first = text.GetBounds(0, 1)[0];
            var last = text.GetBounds(4, 1)[0];
            Assert.True(last.Y > first.Y + 15, "later characters are further down: " + first.Y + " then " + last.Y);
            Assert.Equal(first.X, last.X, 3);
        }

        [Fact]
        public void HitTest_AnswersWithTheCaretPositionNearestAPoint()
        {
            var text = Text("BT /F1 10 Tf 10 40 Td (Hello World) Tj ET");

            Assert.Equal(0, text.HitTest(new PaintPoint(11, 57)));             // left half of H
            Assert.Equal(1, text.HitTest(new PaintPoint(14, 57)));             // right half of H
            Assert.Equal(11, text.HitTest(new PaintPoint(64, 57)));            // right half of d
            Assert.Equal(-1, text.HitTest(new PaintPoint(150, 57)));           // past the text
            Assert.Equal(11, text.HitTest(new PaintPoint(150, 57), nearest: true));
            Assert.Equal(0, text.HitTest(new PaintPoint(0, 90), nearest: true));
        }

        [Fact]
        public void WordAt_And_GetText_AreClampedToTheText()
        {
            var text = Text("BT /F1 10 Tf 10 40 Td (Hello, big_world 42) Tj ET");

            Assert.Equal(new PdfTextRange(0, 5), text.WordAt(2));
            Assert.Equal(new PdfTextRange(5, 1), text.WordAt(5));              // the comma is a word of its own
            Assert.Equal(new PdfTextRange(7, 9), text.WordAt(10));
            Assert.Equal(new PdfTextRange(17, 2), text.WordAt(18));
            Assert.Equal(new PdfTextRange(text.Length, 0), text.WordAt(99));
            Assert.Equal("Hello", text.GetText(0, 5));
            Assert.Equal("42", text.GetText(-3, 999).Substring(text.Length - 2));
            Assert.Equal("", text.GetText(99, 5));
            Assert.Equal(new PdfTextRange(0, 0), text.LineAt(-1));
        }

        [Fact]
        public void Find_MatchesCaseInsensitivelyByDefault_AndHonorsTheOptions()
        {
            var text = Text("BT /F1 10 Tf 10 60 Td 12 TL (The cat saw a Cat and a concatenation) Tj T* (caf\\351 and cafe) Tj ET");

            Assert.Equal([new PdfTextRange(4, 3), new PdfTextRange(14, 3), new PdfTextRange(27, 3)], text.Find("cat"));
            Assert.Equal([new PdfTextRange(14, 3)], text.Find("Cat", PdfTextSearchOptions.MatchCase));
            Assert.Equal([new PdfTextRange(4, 3), new PdfTextRange(14, 3)], text.Find("cat", PdfTextSearchOptions.WholeWord));
            Assert.Single(text.Find("cafe"));
            Assert.Equal(2, text.Find("cafe", PdfTextSearchOptions.IgnoreDiacritics).Count);
            Assert.Empty(text.Find(""));
            Assert.Empty(text.Find("zebra"));
            Assert.Throws<ArgumentNullException>(() => text.Find(null!));
        }

        [Fact]
        public void Find_MatchesAPhraseThatWrapsOntoTheNextLine()
        {
            var text = Text("BT /F1 10 Tf 10 60 Td 12 TL (The quick brown) Tj T* (fox jumps) Tj ET");

            var match = Assert.Single(text.Find("brown fox"));
            Assert.Equal(2, text.GetBounds(match.Start, match.Length).Count);
        }

        [Fact]
        public void ALigatureCode_ExpandsToItsCharactersWhichShareTheGlyphsBox()
        {
            var cmap = TestPdf.Stream("", "1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <66> <00660069> endbfchar");
            var font = "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths ["
                + string.Join(" ", Enumerable.Repeat("500", 224)) + "] /ToUnicode 6 0 R >>";
            var source = PdfPageSource.Open(TestPdf.Build("BT /F1 10 Tf 10 40 Td (fx) Tj ET", resources: "/Font << /F1 5 0 R >>",
                extraObjects: [font, cmap]));

            var ligature = source.GetText(0);

            Assert.Equal("fix", ligature.Text);
            var f = ligature.GetBounds(0, 1)[0];
            var i = ligature.GetBounds(1, 1)[0];
            Assert.Equal(2.5, f.Width, 3);
            Assert.Equal(f.X + 2.5, i.X, 3);
        }

        [Fact]
        public void BadPageNumbers_Throw()
        {
            var source = Open("BT /F1 10 Tf 10 40 Td (x) Tj ET");

            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetText(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetText(-1));
        }
    }
}
