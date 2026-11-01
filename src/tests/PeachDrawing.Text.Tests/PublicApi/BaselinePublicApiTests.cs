using PeachDrawing.Text;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// The <c>BASE</c> table as a consumer outside the assembly reads it, through <see cref="Typeface.BaselineData"/> and
    /// <see cref="Typeface.TryGetBaselineHeight"/>. The expected values were read from the bundled fonts with fontTools, so
    /// they do not come from the reader under test.
    /// </summary>
    public class BaselinePublicApiTests
    {
        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Base-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void BaselineData_IsPresentOnlyOnAFontWithABaseTable()
        {
            Assert.True(Face(BundledFonts.Cjk).HasBaselineData);
            Assert.NotNull(Face(BundledFonts.Cjk).BaselineData);

            var plain = Face(BundledFonts.Math);
            Assert.False(plain.HasBaselineData);
            Assert.Null(plain.BaselineData);
            Assert.False(plain.TryGetBaselineHeight(false, null, "ideo", out _));
        }

        [Fact]
        public void HorizontalAxis_IsTheFontsOwnCoordinates()
        {
            var table = Face(BundledFonts.Cjk).BaselineData!;

            Assert.True(table.HasAxis(vertical: false));
            Assert.True(table.TryGetBaseline(false, "latn", "romn", out var alphabetic));
            Assert.True(table.TryGetBaseline(false, "latn", "ideo", out var ideographic));
            Assert.True(table.TryGetBaseline(false, "latn", "icfb", out var faceBottom));
            Assert.True(table.TryGetBaseline(false, "latn", "icft", out var faceTop));
            Assert.Equal(0, alphabetic);
            Assert.Equal(-120, ideographic);
            Assert.Equal(-67, faceBottom);
            Assert.Equal(827, faceTop);
        }

        [Fact]
        public void VerticalAxis_IsASeparateSetOfCoordinates()
        {
            var table = Face(BundledFonts.Cjk).BaselineData!;

            Assert.True(table.HasAxis(vertical: true));
            Assert.True(table.TryGetBaseline(true, "hani", "romn", out var alphabetic));
            Assert.True(table.TryGetBaseline(true, "hani", "ideo", out var ideographic));
            Assert.Equal(120, alphabetic);
            Assert.Equal(0, ideographic);
        }

        [Fact]
        public void AnUnlistedScript_FallsBackToTheDefaultScript_AndAnUnlistedBaselineIsAbsent()
        {
            var table = Face(BundledFonts.Cjk).BaselineData!;

            Assert.True(table.TryGetBaseline(false, "zzzz", "ideo", out var viaUnknown));
            Assert.True(table.TryGetBaseline(false, null, "ideo", out var viaNull));
            Assert.Equal(-120, viaUnknown);
            Assert.Equal(-120, viaNull);
            Assert.False(table.TryGetBaseline(false, null, "hang", out _));
            Assert.Throws<ArgumentNullException>(() => table.TryGetBaseline(false, null, null!, out _));
        }

        [Fact]
        public void BaselineHeight_IsRelativeToTheAlphabeticBaseline_InEms()
        {
            var sourceSans = Face(BundledFonts.Ttf);

            Assert.True(sourceSans.TryGetBaselineHeight(false, null, "ideo", out var ideographic));
            Assert.Equal(-0.17, ideographic, 6);

            var cjk = Face(BundledFonts.Cjk);
            Assert.True(cjk.TryGetBaselineHeight(false, null, "icft", out var top));
            Assert.Equal(0.827, top, 6);

            // The vertical axis puts romn at 120, so heights are measured from there.
            Assert.True(cjk.TryGetBaselineHeight(true, null, "ideo", out var verticalIdeographic));
            Assert.Equal(-0.12, verticalIdeographic, 6);
        }
    }
}
