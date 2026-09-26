using PeachDrawing.Text.Internal.Fonts.OpenType;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Direct coverage for <see cref="Type2CharstringInterpreter"/> against hand-crafted charstrings
    /// (built via <see cref="SyntheticCff"/>, shared with <c>CffTableTests</c>) - isolates operators a
    /// real bundled font's own glyph repertoire doesn't happen to exercise (e.g. Source Code Pro's
    /// "l"/"o" never emit a bare <c>rrcurveto</c>, an out-of-range 16-bit/32-bit operand encoding, or
    /// an explicit <c>return</c>), on top of the end-to-end proof <c>GetTextOutlineTests</c> already
    /// gives for the interpreter as a whole against that real font.
    /// </summary>
    public class Type2CharstringInterpreterTests
    {
        private static byte B(int value) => (byte)(value + 139); // single-byte operand, -107..107

        private static CffTable Cff(byte[][] charstrings, byte[][]? globalSubrs = null) =>
            new(SyntheticCff.OrdinaryFont(charstrings, globalSubrs), tableStart: 0);

        [Fact]
        public void TryGetGlyphOutline_UnsupportedTable_ReturnsFalse()
        {
            // Missing FDArray/FDSelect entirely (no CharStrings either) - still unsupported.
            var cff = new CffTable(SyntheticCff.CidKeyedFont(), tableStart: 0);

            Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void TryGetGlyphOutline_CidKeyedFontWithFdArrayAndFdSelect_ResolvesEachGidsOwnFdLocalSubrs(int fdSelectFormat)
        {
            // Both GIDs' own charstrings are byte-identical (push -107; callsubr 0) - see
            // SyntheticCff.CidKeyedFontWithFdArrayAndFdSelect's remarks. FDSelect maps GID 0 to FD 0
            // (whose local subr 0 does rmoveto(10,10)) and GID 1 to FD 1 (rmoveto(20,20)), so the two
            // outlines can only differ if CffTable/Type2CharstringInterpreter actually resolve a
            // different local Subrs INDEX per GID rather than always the (nonexistent) top-level one.
            var cff = new CffTable(SyntheticCff.CidKeyedFontWithFdArrayAndFdSelect(fdSelectFormat), tableStart: 0);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline0));
            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 1, out var outline1));

            var contour0 = Assert.Single(outline0.Contours);
            var contour1 = Assert.Single(outline1.Contours);
            Assert.Equal(10, contour0.Start.X);
            Assert.Equal(10, contour0.Start.Y);
            Assert.Equal(20, contour1.Start.X);
            Assert.Equal(20, contour1.Start.Y);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void TryGetGlyphOutline_GlyphIndexOutOfRange_ReturnsFalse(int glyphIndex)
        {
            var cff = Cff([[14]]); // one glyph: a bare endchar

            Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, glyphIndex, out _));
        }

        [Fact]
        public void RMoveTo_SixteenBitOperand_PlacesContourAtTheEncodedCoordinate()
        {
            // b0 == 28: a 16-bit signed int operand - dx = 300 needs this form (single-byte and the
            // 247-254 short forms only reach +/-1131).
            byte[] cs = [28, 0x01, 0x2C, B(10), 21, 14]; // dx=300 (0x012C), dy=10, rmoveto, endchar
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(300, contour.Start.X);
            Assert.Equal(10, contour.Start.Y);
        }

        [Fact]
        public void RMoveTo_ThirtyTwoBitFixedOperand_PlacesContourAtTheEncodedCoordinate()
        {
            // b0 == 255: a 16.16 fixed operand - dx = 5.5 (0x00058000 / 65536.0).
            byte[] cs = [255, 0x00, 0x05, 0x80, 0x00, B(5), 21, 14];
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(5.5, contour.Start.X);
            Assert.Equal(5, contour.Start.Y);
        }

        [Fact]
        public void RRCurveTo_SixOperands_EndsAtTheComposedCurvePoint()
        {
            byte[] cs =
            [
                B(0), B(0), 21, // rmoveto 0,0
                B(10), B(0), B(0), B(10), B(-10), B(0), 8, // rrcurveto: (10,0)(0,10)(-10,0)
                14
            ];
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            var segment = Assert.Single(contour.Segments);
            Assert.True(segment.IsCubic);
            Assert.Equal(0, segment.End.X, 6);
            Assert.Equal(10, segment.End.Y, 6);
        }

        [Fact]
        public void RCurveLine_SixPlusTwoOperands_EndsWithALineAfterTheCurve()
        {
            byte[] cs =
            [
                B(0), B(0), 21, // rmoveto 0,0
                B(5), B(0), B(0), B(5), B(5), B(0), // curve group: (5,0)(0,5)(5,0)
                B(3), B(3), // trailing line: (3,3)
                24, // rcurveline
                14
            ];
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(2, contour.Segments.Count);
            Assert.True(contour.Segments[0].IsCubic);
            Assert.Equal(10, contour.Segments[0].End.X, 6);
            Assert.Equal(5, contour.Segments[0].End.Y, 6);
            Assert.False(contour.Segments[1].IsCubic);
            Assert.Equal(13, contour.Segments[1].End.X, 6);
            Assert.Equal(8, contour.Segments[1].End.Y, 6);
        }

        [Fact]
        public void RLineCurve_TwoPlusSixOperands_EndsWithACurveAfterTheLine()
        {
            byte[] cs =
            [
                B(0), B(0), 21, // rmoveto 0,0
                B(3), B(0), // leading line: (3,0)
                B(5), B(0), B(0), B(5), B(-5), B(0), // curve group: (5,0)(0,5)(-5,0)
                25, // rlinecurve
                14
            ];
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(2, contour.Segments.Count);
            Assert.False(contour.Segments[0].IsCubic);
            Assert.Equal(3, contour.Segments[0].End.X, 6);
            Assert.Equal(0, contour.Segments[0].End.Y, 6);
            Assert.True(contour.Segments[1].IsCubic);
            Assert.Equal(3, contour.Segments[1].End.X, 6);
            Assert.Equal(5, contour.Segments[1].End.Y, 6);
        }

        [Fact]
        public void CallGsubr_RunsTheSubroutineAndHonorsItsOwnExplicitReturn()
        {
            // bias for 1 global subr is 107, so index 0 is pushed as (0 - 107) = -107.
            byte[] subr0 = [B(5), 7, 11]; // push 5, vlineto, explicit return
            byte[] cs =
            [
                B(0), B(0), 21, // rmoveto 0,0
                B(-107), 29, // callgsubr(0)
                14
            ];
            var cff = Cff([cs], globalSubrs: [subr0]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            var segment = Assert.Single(contour.Segments);
            Assert.False(segment.IsCubic);
            Assert.Equal(0, segment.End.X, 6);
            Assert.Equal(5, segment.End.Y, 6);
        }

        [Fact]
        public void EndChar_WithLeftoverOperands_FailsSoftRatherThanMisreadingAsSeac()
        {
            // Two operands survive ConsumeMoveWidth's own width removal (which only ever drops one) -
            // the deprecated 4-argument seac form is deliberately unsupported (see the file header).
            byte[] cs = [B(0), B(0), 14];
            var cff = Cff([cs]);

            Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out _));
        }

        [Fact]
        public void UnknownOperator_FailsSoftRatherThanThrowing()
        {
            // 12 is the escape prefix for the arithmetic/storage/flex operators - deliberately
            // unimplemented (see the file header), so it must fail soft like any other operator this
            // interpreter doesn't know.
            byte[] cs = [12];
            var cff = Cff([cs]);

            Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out _));
        }

        [Fact]
        public void HstemHm_OddOperandCount_ConsumesTheLeadingWidthAndKeepsTheStemPair()
        {
            byte[] cs =
            [
                B(0), B(0), B(0), 18, // three operands (odd - the first is the width) then hstemhm
                B(0), B(0), 21, // a real moveto afterward proves the stem-width consumption above
                14              // didn't also eat into this or otherwise desync the operand stack
            ];
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(0, contour.Start.X);
            Assert.Equal(0, contour.Start.Y);
        }

        [Fact]
        public void RMoveTo_ThreeOperands_ConsumesTheLeadingWidthNotTheCoordinates()
        {
            byte[] cs = [B(20), B(5), B(3), 21, 14]; // width=20, dx=5, dy=3, rmoveto
            var cff = Cff([cs]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out var outline));
            var contour = Assert.Single(outline.Contours);
            Assert.Equal(5, contour.Start.X);
            Assert.Equal(3, contour.Start.Y);
        }
    }
}
