namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class LengthTests
    {
        [Fact]
        public void Constructor_SetsValueAndType()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal(10f, length.Value);
            Assert.Equal(Length.Unit.Px, length.Type);
        }

        [Theory]
        [InlineData((int)Length.Unit.Px, true)]
        [InlineData((int)Length.Unit.Pt, true)]
        [InlineData((int)Length.Unit.In, true)]
        [InlineData((int)Length.Unit.Cm, true)]
        [InlineData((int)Length.Unit.Mm, true)]
        [InlineData((int)Length.Unit.Pc, true)]
        [InlineData((int)Length.Unit.Em, false)]
        [InlineData((int)Length.Unit.Ex, false)]
        [InlineData((int)Length.Unit.Rem, false)]
        [InlineData((int)Length.Unit.Percent, false)]
        [InlineData((int)Length.Unit.Ch, false)]
        [InlineData((int)Length.Unit.Vw, false)]
        [InlineData((int)Length.Unit.Vh, false)]
        [InlineData((int)Length.Unit.Vmin, false)]
        [InlineData((int)Length.Unit.Vmax, false)]
        [InlineData((int)Length.Unit.Vi, false)]
        [InlineData((int)Length.Unit.Vb, false)]
        [InlineData((int)Length.Unit.Svw, false)]
        [InlineData((int)Length.Unit.Svh, false)]
        [InlineData((int)Length.Unit.Svi, false)]
        [InlineData((int)Length.Unit.Svb, false)]
        [InlineData((int)Length.Unit.Svmin, false)]
        [InlineData((int)Length.Unit.Svmax, false)]
        [InlineData((int)Length.Unit.Lvw, false)]
        [InlineData((int)Length.Unit.Lvh, false)]
        [InlineData((int)Length.Unit.Lvi, false)]
        [InlineData((int)Length.Unit.Lvb, false)]
        [InlineData((int)Length.Unit.Lvmin, false)]
        [InlineData((int)Length.Unit.Lvmax, false)]
        [InlineData((int)Length.Unit.Dvw, false)]
        [InlineData((int)Length.Unit.Dvh, false)]
        [InlineData((int)Length.Unit.Dvi, false)]
        [InlineData((int)Length.Unit.Dvb, false)]
        [InlineData((int)Length.Unit.Dvmin, false)]
        [InlineData((int)Length.Unit.Dvmax, false)]
        [InlineData((int)Length.Unit.Cqw, false)]
        [InlineData((int)Length.Unit.Cqh, false)]
        [InlineData((int)Length.Unit.Cqi, false)]
        [InlineData((int)Length.Unit.Cqb, false)]
        [InlineData((int)Length.Unit.Cqmin, false)]
        [InlineData((int)Length.Unit.Cqmax, false)]
        public void IsAbsolute_And_IsRelative_MatchUnitCategory(int unitValue, bool expectedAbsolute)
        {
            var length = new Length(1f, (Length.Unit)unitValue);

            Assert.Equal(expectedAbsolute, length.IsAbsolute);
            Assert.Equal(!expectedAbsolute, length.IsRelative);
        }

        [Theory]
        [InlineData((int)Length.Unit.Px, "px")]
        [InlineData((int)Length.Unit.Em, "em")]
        [InlineData((int)Length.Unit.Ex, "ex")]
        [InlineData((int)Length.Unit.Cm, "cm")]
        [InlineData((int)Length.Unit.Mm, "mm")]
        [InlineData((int)Length.Unit.In, "in")]
        [InlineData((int)Length.Unit.Pt, "pt")]
        [InlineData((int)Length.Unit.Pc, "pc")]
        [InlineData((int)Length.Unit.Ch, "ch")]
        [InlineData((int)Length.Unit.Rem, "rem")]
        [InlineData((int)Length.Unit.Vw, "vw")]
        [InlineData((int)Length.Unit.Vh, "vh")]
        [InlineData((int)Length.Unit.Vmin, "vmin")]
        [InlineData((int)Length.Unit.Vmax, "vmax")]
        [InlineData((int)Length.Unit.Vi, "vi")]
        [InlineData((int)Length.Unit.Vb, "vb")]
        [InlineData((int)Length.Unit.Svw, "svw")]
        [InlineData((int)Length.Unit.Svh, "svh")]
        [InlineData((int)Length.Unit.Svi, "svi")]
        [InlineData((int)Length.Unit.Svb, "svb")]
        [InlineData((int)Length.Unit.Svmin, "svmin")]
        [InlineData((int)Length.Unit.Svmax, "svmax")]
        [InlineData((int)Length.Unit.Lvw, "lvw")]
        [InlineData((int)Length.Unit.Lvh, "lvh")]
        [InlineData((int)Length.Unit.Lvi, "lvi")]
        [InlineData((int)Length.Unit.Lvb, "lvb")]
        [InlineData((int)Length.Unit.Lvmin, "lvmin")]
        [InlineData((int)Length.Unit.Lvmax, "lvmax")]
        [InlineData((int)Length.Unit.Dvw, "dvw")]
        [InlineData((int)Length.Unit.Dvh, "dvh")]
        [InlineData((int)Length.Unit.Dvi, "dvi")]
        [InlineData((int)Length.Unit.Dvb, "dvb")]
        [InlineData((int)Length.Unit.Dvmin, "dvmin")]
        [InlineData((int)Length.Unit.Dvmax, "dvmax")]
        [InlineData((int)Length.Unit.Cqw, "cqw")]
        [InlineData((int)Length.Unit.Cqh, "cqh")]
        [InlineData((int)Length.Unit.Cqi, "cqi")]
        [InlineData((int)Length.Unit.Cqb, "cqb")]
        [InlineData((int)Length.Unit.Cqmin, "cqmin")]
        [InlineData((int)Length.Unit.Cqmax, "cqmax")]
        [InlineData((int)Length.Unit.Percent, "%")]
        [InlineData((int)Length.Unit.None, "")]
        public void UnitString_MatchesUnitName(int unitValue, string expected)
        {
            var length = new Length(1f, (Length.Unit)unitValue);

            Assert.Equal(expected, length.UnitString);
        }

        [Theory]
        [InlineData("ch", (int)Length.Unit.Ch)]
        [InlineData("cm", (int)Length.Unit.Cm)]
        [InlineData("em", (int)Length.Unit.Em)]
        [InlineData("ex", (int)Length.Unit.Ex)]
        [InlineData("in", (int)Length.Unit.In)]
        [InlineData("mm", (int)Length.Unit.Mm)]
        [InlineData("pc", (int)Length.Unit.Pc)]
        [InlineData("pt", (int)Length.Unit.Pt)]
        [InlineData("px", (int)Length.Unit.Px)]
        [InlineData("rem", (int)Length.Unit.Rem)]
        [InlineData("vh", (int)Length.Unit.Vh)]
        [InlineData("vmax", (int)Length.Unit.Vmax)]
        [InlineData("vmin", (int)Length.Unit.Vmin)]
        [InlineData("vw", (int)Length.Unit.Vw)]
        [InlineData("vi", (int)Length.Unit.Vi)]
        [InlineData("vb", (int)Length.Unit.Vb)]
        [InlineData("svw", (int)Length.Unit.Svw)]
        [InlineData("svh", (int)Length.Unit.Svh)]
        [InlineData("svi", (int)Length.Unit.Svi)]
        [InlineData("svb", (int)Length.Unit.Svb)]
        [InlineData("svmin", (int)Length.Unit.Svmin)]
        [InlineData("svmax", (int)Length.Unit.Svmax)]
        [InlineData("lvw", (int)Length.Unit.Lvw)]
        [InlineData("lvh", (int)Length.Unit.Lvh)]
        [InlineData("lvi", (int)Length.Unit.Lvi)]
        [InlineData("lvb", (int)Length.Unit.Lvb)]
        [InlineData("lvmin", (int)Length.Unit.Lvmin)]
        [InlineData("lvmax", (int)Length.Unit.Lvmax)]
        [InlineData("dvw", (int)Length.Unit.Dvw)]
        [InlineData("dvh", (int)Length.Unit.Dvh)]
        [InlineData("dvi", (int)Length.Unit.Dvi)]
        [InlineData("dvb", (int)Length.Unit.Dvb)]
        [InlineData("dvmin", (int)Length.Unit.Dvmin)]
        [InlineData("dvmax", (int)Length.Unit.Dvmax)]
        [InlineData("cqw", (int)Length.Unit.Cqw)]
        [InlineData("cqh", (int)Length.Unit.Cqh)]
        [InlineData("cqi", (int)Length.Unit.Cqi)]
        [InlineData("cqb", (int)Length.Unit.Cqb)]
        [InlineData("cqmin", (int)Length.Unit.Cqmin)]
        [InlineData("cqmax", (int)Length.Unit.Cqmax)]
        [InlineData("%", (int)Length.Unit.Percent)]
        [InlineData("bogus", (int)Length.Unit.None)]
        public void GetUnit_ParsesKnownSuffixes(string suffix, int expectedValue)
        {
            Assert.Equal((Length.Unit)expectedValue, Length.GetUnit(suffix));
        }

        [Fact]
        public void TryParse_ValidLength_ReturnsTrue()
        {
            var success = Length.TryParse("10px", out var result);

            Assert.True(success);
            Assert.Equal(10f, result.Value);
            Assert.Equal(Length.Unit.Px, result.Type);
        }

        [Fact]
        public void TryParse_ZeroWithoutUnit_ReturnsZeroLength()
        {
            var success = Length.TryParse("0", out var result);

            Assert.True(success);
            Assert.Equal(Length.Zero, result);
        }

        [Fact]
        public void TryParse_NonZeroValueWithUnrecognizedUnit_ReturnsFalse()
        {
            var success = Length.TryParse("10bogus", out _);

            Assert.False(success);
        }

        [Theory]
        [InlineData("not-a-length")]
        [InlineData("abc")]
        public void TryParse_NonNumericInput_ReturnsFalse(string input)
        {
            // StylesheetUnit reports a tokenize failure as a null unit string with value 0 — the
            // unitless-zero acceptance must not mistake that for a genuinely parsed bare "0".
            var success = Length.TryParse(input, out _);

            Assert.False(success);
        }

        [Fact]
        public void TryParse_UnitlessNonZero_ReturnsFalse()
        {
            // CSS Values & Units §5.1: only zero may omit its unit.
            var success = Length.TryParse("5", out _);

            Assert.False(success);
        }

        [Fact]
        public void ToPixel_ConvertsAbsoluteUnit()
        {
            var length = new Length(1f, Length.Unit.In);

            // ToPixel resolves to the engine's internal layout unit, points: 1in = 72pt.
            Assert.Equal(72f, length.ToPixel());
        }

        [Fact]
        public void ToPixel_RelativeUnit_Throws()
        {
            var length = new Length(1f, Length.Unit.Em);

            Assert.Throws<InvalidOperationException>(() => length.ToPixel());
        }

        [Fact]
        public void ToPixels_Em_UsesEmFactor()
        {
            var length = new Length(2f, Length.Unit.Em);

            Assert.Equal(24d, length.ToPixels(12, 0, 0));
        }

        [Fact]
        public void ToPixels_Rem_UsesRemFactor()
        {
            var length = new Length(2f, Length.Unit.Rem);

            Assert.Equal(32d, length.ToPixels(0, 16, 0));
        }

        [Fact]
        public void ToPixels_Percent_UsesHundredPercentFactor()
        {
            var length = new Length(50f, Length.Unit.Percent);

            Assert.Equal(100d, length.ToPixels(0, 0, 200));
        }

        [Theory]
        [InlineData((int)Length.Unit.Cqw, 200d, 100d, 100d)]  // 50% of the 200pt container inline/width size
        [InlineData((int)Length.Unit.Cqi, 200d, 100d, 100d)]  // same value here (horizontal writing mode: inline == width)
        [InlineData((int)Length.Unit.Cqh, 100d, 200d, 100d)]  // 50% of the 200pt container block/height size
        [InlineData((int)Length.Unit.Cqb, 100d, 200d, 100d)]  // same value here (horizontal writing mode: block == height)
        [InlineData((int)Length.Unit.Cqmin, 300d, 100d, 50d)] // min(300,100) = 100 -> 50% = 50
        [InlineData((int)Length.Unit.Cqmax, 300d, 100d, 150d)] // max(300,100) = 300 -> 50% = 150
        public void ToPixels_ContainerRelativeUnit_UsesContainerSize(int unitValue, double inlinePt, double blockPt, double expected)
        {
            var length = new Length(50f, (Length.Unit)unitValue);

            // A horizontal-tb container has no divergence between its physical width/height and its own
            // inline/block axis, so the same numbers feed both pairs of parameters here - see
            // ToPixels_Cqw_UsesPhysicalWidth_NotContainerInlineAxis below for a vertical container, where
            // they genuinely differ and only the correct pair may be read.
            Assert.Equal(expected, length.ToPixels(0, 0, 0, inlinePt, blockPt, null, null, inlinePt, blockPt));
        }

        [Theory]
        [InlineData((int)Length.Unit.Cqw)]
        [InlineData((int)Length.Unit.Cqh)]
        [InlineData((int)Length.Unit.Cqi)]
        [InlineData((int)Length.Unit.Cqb)]
        [InlineData((int)Length.Unit.Cqmin)]
        [InlineData((int)Length.Unit.Cqmax)]
        public void ToPixels_ContainerRelativeUnit_WithNoAncestorContainerOrViewport_ResolvesToZero(int unitValue)
        {
            // No container or viewport context at all supplied - the ultimate fallback with genuinely no
            // context at all (e.g. a media-query/@page call site).
            var length = new Length(50f, (Length.Unit)unitValue);

            Assert.Equal(0d, length.ToPixels(0, 0, 0));
        }

        [Theory]
        [InlineData((int)Length.Unit.Cqw, 400d)]   // 50% of the 800pt viewport width
        [InlineData((int)Length.Unit.Cqi, 400d)]   // same value here (horizontal writing mode)
        [InlineData((int)Length.Unit.Cqh, 300d)]   // 50% of the 600pt viewport height
        [InlineData((int)Length.Unit.Cqb, 300d)]   // same value here (horizontal writing mode)
        [InlineData((int)Length.Unit.Cqmin, 300d)] // min(800,600) = 600 -> 50% = 300
        [InlineData((int)Length.Unit.Cqmax, 400d)] // max(800,600) = 800 -> 50% = 400
        public void ToPixels_ContainerRelativeUnit_WithNoAncestorContainer_FallsBackToViewportSize(int unitValue, double expected)
        {
            // No ancestor query container (both container-pair args null) but a real viewport size
            // supplied - CSS Containment 3 §6.2's cq* -> sv* fallback (issue #615), not the old hardcoded 0.
            var length = new Length(50f, (Length.Unit)unitValue);

            Assert.Equal(expected, length.ToPixels(0, 0, 0, null, null, 800d, 600d, null, null, 800d, 600d));
        }

        [Theory]
        [InlineData((int)Length.Unit.Cqmin, 300d)]
        [InlineData((int)Length.Unit.Cqmax, 300d)]
        public void ToPixels_ContainerRelativeUnit_InlineSizeOnlyContainer_FallsBackToViewportForBlockAxisOnly(int unitValue, double containerInlinePt)
        {
            // An inline-size-only container supplies a real inline size but no block size
            // (CssBox.GetContainerRelativeUnitBasis returns null for the block axis there) - the
            // fallback must apply per-axis, substituting the viewport only for the missing block axis,
            // not falling back to the viewport for both axes just because one is missing.
            var length = new Length(50f, (Length.Unit)unitValue);
            const double viewportWidthPt = 1000d;  // deliberately different from containerInlinePt, so a
            const double viewportHeightPt = 200d;  // wrong "fall back to viewport entirely" bug would show.

            var result = length.ToPixels(0, 0, 0, containerInlinePt, null, viewportWidthPt, viewportHeightPt,
                null, null, viewportWidthPt, viewportHeightPt);

            // cqmin/cqmax combine the REAL container inline size (300) with the VIEWPORT-fallback block
            // size (200), i.e. min/max(300, 200) * 50% - not min/max(1000, 200) (all-viewport) or
            // min/max(300, 0) (old hardcoded-zero fallback).
            var expected = (Length.Unit)unitValue == Length.Unit.Cqmin
                ? Math.Min(containerInlinePt, viewportHeightPt) / 100d * 50d
                : Math.Max(containerInlinePt, viewportHeightPt) / 100d * 50d;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToPixels_Cqw_UsesContainerPhysicalWidth_NotContainerInlineAxis()
        {
            // A vertical container's own inline axis (physical height) and physical width genuinely
            // diverge - cqw must always read the physical width (containerWidthPt), never the axis-aware
            // inline size (containerInlineSizePt), even when both are supplied and different.
            var length = new Length(50f, Length.Unit.Cqw);

            var result = length.ToPixels(0, 0, 0,
                containerInlineSizePt: 999d, containerBlockSizePt: null,
                viewportWidthPt: null, viewportHeightPt: null,
                containerWidthPt: 200d, containerHeightPt: null);

            Assert.Equal(100d, result); // 50% of containerWidthPt (200), not containerInlineSizePt (999)
        }

        [Fact]
        public void ToPixels_Cqi_UsesContainerInlineAxis_NotContainerPhysicalWidth()
        {
            // The mirror of the test above: cqi must always read the axis-aware inline size
            // (containerInlineSizePt), never the physical width (containerWidthPt) - proving the two are
            // genuinely separate switch arms, not just documentation.
            var length = new Length(50f, Length.Unit.Cqi);

            var result = length.ToPixels(0, 0, 0,
                containerInlineSizePt: 300d, containerBlockSizePt: null,
                viewportWidthPt: null, viewportHeightPt: null,
                containerWidthPt: 999d, containerHeightPt: null);

            Assert.Equal(150d, result); // 50% of containerInlineSizePt (300), not containerWidthPt (999)
        }

        [Theory]
        [InlineData((int)Length.Unit.Vw, 400d)]
        [InlineData((int)Length.Unit.Svw, 400d)]
        [InlineData((int)Length.Unit.Lvw, 400d)]
        [InlineData((int)Length.Unit.Dvw, 400d)]
        [InlineData((int)Length.Unit.Vh, 300d)]
        [InlineData((int)Length.Unit.Svh, 300d)]
        [InlineData((int)Length.Unit.Lvh, 300d)]
        [InlineData((int)Length.Unit.Dvh, 300d)]
        [InlineData((int)Length.Unit.Vmin, 300d)]
        [InlineData((int)Length.Unit.Svmin, 300d)]
        [InlineData((int)Length.Unit.Lvmin, 300d)]
        [InlineData((int)Length.Unit.Dvmin, 300d)]
        [InlineData((int)Length.Unit.Vmax, 400d)]
        [InlineData((int)Length.Unit.Svmax, 400d)]
        [InlineData((int)Length.Unit.Lvmax, 400d)]
        [InlineData((int)Length.Unit.Dvmax, 400d)]
        public void ToPixels_ViewportUnit_ResolvesAgainstPageSize(int unitValue, double expected)
        {
            // 800x600 page box; 50% of whichever axis the unit names. Small/large/dynamic variants
            // resolve identically to the plain unit - a PDF page has no scrollbar/dynamic chrome to
            // distinguish them by.
            var length = new Length(50f, (Length.Unit)unitValue);

            Assert.Equal(expected, length.ToPixels(0, 0, 0, null, null, 800d, 600d));
        }

        [Theory]
        [InlineData((int)Length.Unit.Vi, 400d)]
        [InlineData((int)Length.Unit.Svi, 400d)]
        [InlineData((int)Length.Unit.Lvi, 400d)]
        [InlineData((int)Length.Unit.Dvi, 400d)]
        [InlineData((int)Length.Unit.Vb, 300d)]
        [InlineData((int)Length.Unit.Svb, 300d)]
        [InlineData((int)Length.Unit.Lvb, 300d)]
        [InlineData((int)Length.Unit.Dvb, 300d)]
        public void ToPixels_ViewportUnit_LogicalForm_ResolvesAgainstRootAxisSize(int unitValue, double expected)
        {
            // 800x600 root-inline/root-block basis (a horizontal-tb root, so numerically identical to the
            // physical-pair test above) - proving vi/vb read viewportInlineSizePt/viewportBlockSizePt, not
            // viewportWidthPt/viewportHeightPt (which are deliberately left null here).
            var length = new Length(50f, (Length.Unit)unitValue);

            Assert.Equal(expected, length.ToPixels(0, 0, 0, null, null, null, null,
                null, null, viewportInlineSizePt: 800d, viewportBlockSizePt: 600d));
        }

        [Fact]
        public void ToPixels_Vw_UsesPhysicalWidth_NotRootInlineAxis()
        {
            // A vertical root's own inline axis (physical height) and physical width genuinely diverge -
            // vw must always read the physical width (viewportWidthPt), never the axis-aware inline size
            // (viewportInlineSizePt), even when both are supplied and different.
            var length = new Length(50f, Length.Unit.Vw);

            var result = length.ToPixels(0, 0, 0, null, null,
                viewportWidthPt: 800d, viewportHeightPt: null,
                containerWidthPt: null, containerHeightPt: null,
                viewportInlineSizePt: 999d, viewportBlockSizePt: null);

            Assert.Equal(400d, result); // 50% of viewportWidthPt (800), not viewportInlineSizePt (999)
        }

        [Fact]
        public void ToPixels_Vi_UsesRootInlineAxis_NotPhysicalWidth()
        {
            // The mirror of the test above: vi must always read the axis-aware inline size
            // (viewportInlineSizePt), never the physical width (viewportWidthPt) - the exact case that
            // was previously broken (Vi aliased directly to Vw with no writing-mode consultation at all).
            var length = new Length(50f, Length.Unit.Vi);

            var result = length.ToPixels(0, 0, 0, null, null,
                viewportWidthPt: 999d, viewportHeightPt: null,
                containerWidthPt: null, containerHeightPt: null,
                viewportInlineSizePt: 600d, viewportBlockSizePt: null);

            Assert.Equal(300d, result); // 50% of viewportInlineSizePt (600), not viewportWidthPt (999)
        }

        [Theory]
        [InlineData((int)Length.Unit.Vw)]
        [InlineData((int)Length.Unit.Vh)]
        [InlineData((int)Length.Unit.Vmin)]
        [InlineData((int)Length.Unit.Vmax)]
        [InlineData((int)Length.Unit.Vi)]
        [InlineData((int)Length.Unit.Vb)]
        public void ToPixels_ViewportUnit_WithNoPageContext_ResolvesToZero(int unitValue)
        {
            var length = new Length(50f, (Length.Unit)unitValue);

            Assert.Equal(0d, length.ToPixels(0, 0, 0));
        }

        [Fact]
        public void ToPixels_Ch_ApproximatesHalfEm()
        {
            // CSS Values & Units §6.2's own sanctioned fallback when measuring the real "0" glyph is
            // impractical - the same 0.5em formula this engine already uses for ex's x-height.
            var length = new Length(2f, Length.Unit.Ch);

            Assert.Equal(12d, length.ToPixels(12, 0, 0));
        }

        [Fact]
        public void ToPixels_Pc_TwelvePointsPerPica()
        {
            var length = new Length(1f, Length.Unit.Pc);

            Assert.Equal(12d, length.ToPixels(0, 0, 0));
        }

        [Fact]
        public void To_ConvertsBetweenAbsoluteUnits()
        {
            var length = new Length(1f, Length.Unit.In);

            // px is spec-correct (CSS Values & Units §6.2): 1px = 1/96in, so 1in = 72pt = 96 CSS px.
            Assert.Equal(96f, length.To(Length.Unit.Px));
        }

        [Fact]
        public void To_In_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(1f, length.To(Length.Unit.In));
        }

        [Fact]
        public void To_Mm_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(25.4f, length.To(Length.Unit.Mm), 3);
        }

        [Fact]
        public void To_Pc_ConvertsFromPoints()
        {
            var length = new Length(12f, Length.Unit.Pt);

            Assert.Equal(1f, length.To(Length.Unit.Pc));
        }

        [Fact]
        public void To_Pt_ReturnsSameValue()
        {
            var length = new Length(42f, Length.Unit.Pt);

            Assert.Equal(42f, length.To(Length.Unit.Pt));
        }

        [Fact]
        public void To_Cm_ConvertsFromPoints()
        {
            var length = new Length(72f, Length.Unit.Pt);

            Assert.Equal(2.54f, length.To(Length.Unit.Cm), 3);
        }

        [Fact]
        public void To_RelativeTargetUnit_Throws()
        {
            var length = new Length(1f, Length.Unit.Px);

            Assert.Throws<InvalidOperationException>(() => length.To(Length.Unit.Em));
        }

        [Fact]
        public void Equality_ComparesValueAndType()
        {
            var a = new Length(10f, Length.Unit.Px);
            var b = new Length(10f, Length.Unit.Px);
            var c = new Length(10f, Length.Unit.Em);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a length"));
        }

        [Fact]
        public void GetHashCode_SameForEqualLengths()
        {
            var a = new Length(10f, Length.Unit.Px);
            var b = new Length(10f, Length.Unit.Px);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void CompareTo_SameUnit_ComparesValue()
        {
            var small = new Length(1f, Length.Unit.Px);
            var large = new Length(2f, Length.Unit.Px);

            Assert.True(small < large);
            Assert.True(large > small);
            Assert.True(small <= large);
            Assert.True(large >= small);
        }

        [Fact]
        public void CompareTo_DifferentAbsoluteUnits_ComparesInPixels()
        {
            var oneInch = new Length(1f, Length.Unit.In);
            var oneCm = new Length(1f, Length.Unit.Cm);

            Assert.True(oneInch > oneCm);
        }

        [Fact]
        public void ToString_Zero_OmitsUnit()
        {
            var length = new Length(0f, Length.Unit.Px);

            Assert.Equal("0", length.ToString());
        }

        [Fact]
        public void ToString_NonZero_IncludesUnit()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal("10px", length.ToString());
        }

        [Fact]
        public void ToString_WithFormatProvider()
        {
            var length = new Length(10f, Length.Unit.Px);

            Assert.Equal("10px", length.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void PredefinedConstants_HaveExpectedValues()
        {
            Assert.Equal(new Length(0f, Length.Unit.Px), Length.Zero);
            Assert.Equal(new Length(50f, Length.Unit.Percent), Length.Half);
            Assert.Equal(new Length(100f, Length.Unit.Percent), Length.Full);
            Assert.Equal(new Length(1f, Length.Unit.Px), Length.Thin);
            Assert.Equal(new Length(3f, Length.Unit.Px), Length.Medium);
            Assert.Equal(new Length(5f, Length.Unit.Px), Length.Thick);
        }
    }
}
