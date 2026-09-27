using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using static PeachDrawing.Text.Tests.Fonts.SyntheticCff2;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The <c>CFF2</c> reader and the Type 2 interpreter's CFF2 operators (<c>vsindex</c>, <c>blend</c>, no width, no <c>endchar</c>, the
    /// larger stack), against tables built by hand (<see cref="SyntheticCff2"/>) and against a damaged copy of the fixture font. What a
    /// font that is not damaged draws at a location is <see cref="Cff2OutlineTests"/>'s subject.
    /// </summary>
    public class Cff2TableTests
    {
        // One axis. Region 0 grows from the default to 1, region 1 grows from the default to -1.
        private static readonly Region[] Regions = [new(0, 1, 1), new(-1, -1, 0)];

        // Data set 0 has region 0, data set 1 has region 1, data set 2 has both.
        private static readonly int[][] DataSets = [[0], [1], [0, 1]];

        private static byte[] Store() => VariationStore(Regions, DataSets);

        private static Cff2Table Read(byte[][] charStrings, byte[][]? globalSubrs = null, FontDict[]? fonts = null, byte[]? fdSelect = null, bool variable = true)
        {
            var bytes = Table(charStrings, globalSubrs, fonts, fdSelect, variable ? Store() : null);
            return new Cff2Table(bytes, 0, bytes.Length);
        }

        private static bool TryDraw(Cff2Table table, int glyph, double coordinate, out GlyphOutline outline) =>
            Type2CharstringInterpreter.TryGetGlyphOutline(table, glyph, new VariationCoordinates([coordinate], [coordinate], ["wght"]), out outline);

        private static bool TryDraw(byte[] charString, double coordinate, out GlyphOutline outline, FontDict[]? fonts = null) =>
            TryDraw(Read([charString], fonts: fonts), 0, coordinate, out outline);

        private static (double X, double Y) Start(GlyphOutline outline) => (outline.Contours[0].Start.X, outline.Contours[0].Start.Y);

        // ---------------------------------------------------------------------------------------------------------------------------
        // blend and vsindex
        // ---------------------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(0.0, 100)]     // the default location: every scalar is 0
        [InlineData(0.5, 120)]     // halfway into region 0, whose delta is 40
        [InlineData(1.0, 140)]
        [InlineData(-0.5, 100)]    // outside region 0
        public void Blend_AddsTheDeltaScaledByTheRegion(double coordinate, double expectedX)
        {
            // 100 (delta 40) 1 blend, hmoveto, then a line so the outline is not empty.
            var cs = Cs(100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo);

            Assert.True(TryDraw(cs, coordinate, out var outline, [new FontDict()]));
            Assert.Equal(expectedX, Start(outline).X, 9);
            Assert.Equal(expectedX + 30, outline.Contours[0].Segments[0].End.X, 9);
        }

        [Fact]
        public void Blend_OfSeveralValues_ScalesEachDeltaByItsOwnRegion()
        {
            // The private vsindex 2 names both regions: (100 50) with deltas (40 -20) and (10 6).
            var cs = Cs(100, 50, 40, -20, 10, 6, 2, Op.Blend, Op.RMoveTo, 10, 0, Op.RLineTo);
            var fonts = new[] { new FontDict(VariationDataIndex: 2) };

            Assert.True(TryDraw(cs, 0.5, out var atHalf, fonts));
            Assert.Equal((120d, 55d), Start(atHalf));

            Assert.True(TryDraw(cs, -0.5, out var atMinusHalf, fonts));
            Assert.Equal((90d, 53d), Start(atMinusHalf));

            Assert.True(TryDraw(cs, 0, out var atDefault, fonts));
            Assert.Equal((100d, 50d), Start(atDefault));
        }

        [Fact]
        public void Blend_ResultsStayOnTheStack_ForTheOperatorThatFollows()
        {
            // Two blends of one value each, then rlineto takes both results as its operands.
            var cs = Cs(0, 0, Op.RMoveTo, 10, 4, 1, Op.Blend, 20, 8, 1, Op.Blend, Op.RLineTo);

            Assert.True(TryDraw(cs, 0.5, out var outline, [new FontDict()]));
            var end = outline.Contours[0].Segments[0].End;
            Assert.Equal((12d, 24d), (end.X, end.Y));
        }

        [Fact]
        public void VsIndex_ChoosesTheDataSetTheNextBlendsUse()
        {
            // Data set 1 holds region 1 only, so at -0.5 a delta of 40 is worth 20 and at +0.5 nothing.
            var cs = Cs(1, Op.VsIndex, 100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo);

            Assert.True(TryDraw(cs, -0.5, out var negative, [new FontDict()]));
            Assert.Equal(120, Start(negative).X, 9);

            Assert.True(TryDraw(cs, 0.5, out var positive, [new FontDict()]));
            Assert.Equal(100, Start(positive).X, 9);
        }

        [Fact]
        public void ThePrivateDictsVsIndex_IsWhereEveryCharstringOfThatFontDictStarts()
        {
            var cs = Cs(100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo);
            var region1 = new[] { new FontDict(VariationDataIndex: 1) };

            Assert.True(TryDraw(cs, -0.5, out var negative, region1));
            Assert.Equal(120, Start(negative).X, 9);
        }

        [Fact]
        public void VsIndex_InTheCharstring_WinsOverThePrivateDicts()
        {
            var cs = Cs(0, Op.VsIndex, 100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo);

            Assert.True(TryDraw(cs, 0.5, out var outline, [new FontDict(VariationDataIndex: 1)]));
            Assert.Equal(120, Start(outline).X, 9);
        }

        [Theory]
        [MemberData(nameof(BadBlends))]
        public void ABlendOrVsIndexThatMakesNoSense_FailsTheGlyph_AndNothingElse(string what, byte[] charString)
        {
            var table = Read([charString, Cs(10, 20, Op.RMoveTo, 5, 0, Op.RLineTo)]);

            Assert.False(TryDraw(table, 0, 0.5, out _), what);
            Assert.True(TryDraw(table, 1, 0.5, out _), "the other glyph is unaffected");
        }

        public static IEnumerable<object[]> BadBlends() =>
        [
            ["no operands", Cs(Op.Blend)],
            ["n larger than the stack", Cs(100, 40, 5, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
            ["n negative", Cs(100, 40, -1, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
            ["n huge", Cs(100, 40, 30000, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
            ["deltas missing", Cs(100, 1, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
            ["vsindex after a blend", Cs(100, 40, 1, Op.Blend, 1, Op.VsIndex, Op.HMoveTo, 1, Op.HLineTo)],
            ["vsindex with nothing to take", Cs(Op.VsIndex, 10, 20, Op.RMoveTo, 1, 1, Op.RLineTo)],
            ["vsindex with no such data set", Cs(9, Op.VsIndex, 100, 40, 1, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
            ["vsindex negative", Cs(-1, Op.VsIndex, 100, 40, 1, Op.Blend, Op.HMoveTo, 1, Op.HLineTo)],
        ];

        [Fact]
        public void ABlend_InAFontWithNoVariationStore_FailsTheGlyph()
        {
            var table = Read([Cs(100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo)], variable: false);

            Assert.True(table.IsSupported);
            Assert.False(TryDraw(table, 0, 0, out _));
        }

        [Fact]
        public void APlainCharstring_NeedsNoVariationStore()
        {
            var table = Read([Cs(100, 0, Op.RMoveTo, 30, 0, Op.RLineTo)], variable: false);

            Assert.True(TryDraw(table, 0, 0, out var outline));
            Assert.Equal(100, Start(outline).X);
        }

        [Fact]
        public void AtTheDefaultLocation_ANullVariation_BlendsNothing()
        {
            var table = Read([Cs(100, 40, 1, Op.Blend, Op.HMoveTo, 30, Op.HLineTo)]);

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(table, 0, null, out var outline));
            Assert.Equal(100, Start(outline).X);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // What CFF2 changes about a charstring
        // ---------------------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ACharstringHasNoWidth_SoAThirdOperandOfMoveToIsAnError()
        {
            // In CFF this is width, dx, dy; in CFF2 there is no width, so the extra operand is wrong.
            Assert.False(TryDraw(Cs(5, 10, 20, Op.RMoveTo, 1, 1, Op.RLineTo), 0, out _));
        }

        [Fact]
        public void ACharstringEndsWithoutEndChar_AndTheOutlineIsWhatWasDrawn()
        {
            Assert.True(TryDraw(Cs(10, 20, Op.RMoveTo, 30, 0, Op.RLineTo, 0, 30, Op.RLineTo), 0, out var outline));

            var contour = Assert.Single(outline.Contours);
            Assert.Equal(2, contour.Segments.Count);
        }

        [Fact]
        public void EndChar_IsNotAnOperatorOfCff2()
        {
            Assert.False(TryDraw(Cs(10, 20, Op.RMoveTo, 30, 0, Op.RLineTo, Op.EndChar), 0, out _));
        }

        [Fact]
        public void AnEmptyCharstring_IsAGlyphWithNoOutline()
        {
            var table = Read([[]]);

            Assert.True(table.IsSupported);
            Assert.False(TryDraw(table, 0, 0, out var outline));
            Assert.True(outline.IsEmpty);
        }

        [Fact]
        public void TheOperandStack_HoldsFiveHundredAndThirteen()
        {
            var ok = new List<object> { 0, 0, Op.RMoveTo };
            ok.AddRange(Enumerable.Repeat<object>(1, 512)); // 512 operands: 256 lines
            ok.Add(Op.RLineTo);
            Assert.True(TryDraw(Cs(ok.ToArray()), 0, out var outline));
            Assert.Equal(256, outline.Contours[0].Segments.Count);

            var tooMany = new List<object> { 0, 0, Op.RMoveTo };
            tooMany.AddRange(Enumerable.Repeat<object>(1, 514));
            tooMany.Add(Op.RLineTo);
            Assert.False(TryDraw(Cs(tooMany.ToArray()), 0, out _));
        }

        [Fact]
        public void HintMask_ReadsItsMaskBytes_FromTheStemsBlendedOntoTheStack()
        {
            // One hstem pair from a blend (10 (delta 4), 20 (delta 8)), then a hintmask with one mask byte, then the path.
            var cs = Cs(10, 20, 4, 8, 2, Op.Blend, Op.HStem, Op.HintMask, new byte[] { 0x80 }, 5, 6, Op.RMoveTo, 1, 1, Op.RLineTo);

            Assert.True(TryDraw(cs, 0, out var outline));
            Assert.Equal((5d, 6d), Start(outline));
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Subroutines
        // ---------------------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        public void EachGlyph_UsesTheLocalSubrsOfItsOwnFontDict(int fdSelectFormat)
        {
            // Both glyphs call local subr 0 (bias 107 for one subr); each Font DICT's own subr 0 moves somewhere else.
            byte[] call = Cs(-107, Op.CallSubr, 1, 0, Op.RLineTo);
            var fonts = new[]
            {
                new FontDict([Cs(10, 20, Op.RMoveTo, Op.Return)]),
                new FontDict([Cs(30, 40, Op.RMoveTo, Op.Return)]),
            };
            var table = Read([call, call], fonts: fonts, fdSelect: FdSelect(fdSelectFormat, [0, 1]));

            Assert.True(table.IsSupported);
            Assert.True(TryDraw(table, 0, 0, out var first));
            Assert.True(TryDraw(table, 1, 0, out var second));
            Assert.Equal((10d, 20d), Start(first));
            Assert.Equal((30d, 40d), Start(second));
        }

        [Fact]
        public void AGlyphWhoseFdSelectEntryNamesNoFontDict_FailsAlone()
        {
            byte[] plain = Cs(10, 20, Op.RMoveTo, 1, 1, Op.RLineTo);
            var table = Read([plain, plain, plain], fonts: [new FontDict(), new FontDict()], fdSelect: FdSelect(0, [0, 7, 1]));

            Assert.True(table.IsSupported);
            Assert.True(TryDraw(table, 0, 0, out _));
            Assert.False(TryDraw(table, 1, 0, out _));
            Assert.True(TryDraw(table, 2, 0, out _));
        }

        [Fact]
        public void AGlobalSubroutine_CanBlend_WithTheDataSetOfTheGlyphsFontDict()
        {
            // The global subr draws a line whose length blends over data set 1 (the Private DICT's), so at -1 the delta counts in full.
            var global = Cs(10, 6, 1, Op.Blend, 0, Op.RLineTo, Op.Return);
            var table = Read([Cs(0, 0, Op.RMoveTo, -107, Op.CallGSubr)], [global], [new FontDict(VariationDataIndex: 1)]);

            Assert.True(TryDraw(table, 0, -1, out var outline));
            Assert.Equal(16, outline.Contours[0].Segments[0].End.X, 9);
        }

        [Fact]
        public void SubroutinesThatCallThemselves_AreStopped()
        {
            var recursive = Cs(-107, Op.CallGSubr);
            var table = Read([Cs(0, 0, Op.RMoveTo, -107, Op.CallGSubr, 1, 1, Op.RLineTo)], [recursive]);

            Assert.False(TryDraw(table, 0, 0, out _));
        }

        [Fact]
        public void SubroutinesThatCallManyOthers_AreStoppedByTheStepBudget_NotByRunningToTheEnd()
        {
            // Nine levels of subroutine, each calling the next sixteen times: 16^9 calls if it were allowed to run.
            const int Levels = 9;
            var subrs = new byte[Levels][];
            for (int level = 0; level < Levels; level++)
            {
                var tokens = new List<object>();
                for (int call = 0; call < 16; call++)
                {
                    if (level + 1 < Levels)
                    {
                        tokens.Add(level + 1 - 107); // bias 107: the global subr count is below 1240
                        tokens.Add(Op.CallGSubr);
                    }
                }

                tokens.Add(Op.Return);
                subrs[level] = Cs(tokens.ToArray());
            }

            var table = Read([Cs(0, 0, Op.RMoveTo, -107, Op.CallGSubr, 1, 1, Op.RLineTo)], subrs);

            WorkBounds.Case(() => Assert.False(TryDraw(table, 0, 0, out _)));
        }

        [Fact]
        public void ACallToASubroutineThatIsNotThere_FailsTheGlyph()
        {
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 5, Op.CallSubr, 1, 1, Op.RLineTo), 0, out _));
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 5, Op.CallGSubr, 1, 1, Op.RLineTo), 0, out _));
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, Op.CallSubr), 0, out _));
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // The flex operators (shared with CFF)
        // ---------------------------------------------------------------------------------------------------------------------------

        private static (double X, double Y)[] Curves(GlyphOutline outline) =>
            outline.Contours[0].Segments.SelectMany(s => new[] { s.Control1, s.Control2, s.End }).Select(p => (p.X, p.Y)).ToArray();

        [Fact]
        public void HFlex_DrawsTwoCurves_ThatEndAtTheStartingHeight()
        {
            Assert.True(TryDraw(Cs(0, 0, Op.RMoveTo, 10, 20, 5, 30, 40, 50, 60, Op.HFlex), 0, out var outline));

            Assert.Equal([(10d, 0d), (30d, 5d), (60d, 5d), (100d, 5d), (150d, 0d), (210d, 0d)], Curves(outline));
        }

        [Fact]
        public void Flex_DrawsTwoCurves_AndIgnoresTheFlexDepth()
        {
            Assert.True(TryDraw(Cs(0, 0, Op.RMoveTo, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 50, Op.Flex), 0, out var outline));

            Assert.Equal([(1d, 2d), (4d, 6d), (9d, 12d), (16d, 20d), (25d, 30d), (36d, 42d)], Curves(outline));
        }

        [Fact]
        public void HFlex1_ReturnsToTheStartingHeight()
        {
            Assert.True(TryDraw(Cs(0, 0, Op.RMoveTo, 10, 5, 20, 6, 30, 40, 50, 7, 60, Op.HFlex1), 0, out var outline));

            Assert.Equal([(10d, 5d), (30d, 11d), (60d, 11d), (100d, 11d), (150d, 18d), (210d, 0d)], Curves(outline));
        }

        [Theory]
        [InlineData(new[] { 10, 0, 20, 5, 30, 0, 40, 0, 50, -5, 70 }, new[] { 10d, 0d, 30d, 5d, 60d, 5d, 100d, 5d, 150d, 0d, 220d, 0d })]  // wider than tall: d6 is x
        [InlineData(new[] { 0, 10, 0, 20, 0, 30, 0, 40, 0, 50, 7 }, new[] { 0d, 10d, 0d, 30d, 0d, 60d, 0d, 100d, 0d, 150d, 0d, 157d })]     // taller than wide: d6 is y
        public void Flex1_TakesTheLastPointOnTheAxisTheCurvesTravelAlong(int[] operands, double[] expected)
        {
            var tokens = new List<object> { 0, 0, Op.RMoveTo };
            tokens.AddRange(operands.Cast<object>());
            tokens.Add(Op.Flex1);

            Assert.True(TryDraw(Cs(tokens.ToArray()), 0, out var outline));

            Assert.Equal(expected, Curves(outline).SelectMany(p => new[] { p.X, p.Y }).ToArray());
        }

        [Fact]
        public void AFlexWithTheWrongNumberOfOperands_FailsTheGlyph()
        {
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 1, 2, 3, Op.HFlex), 0, out _));
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, Op.Flex), 0, out _));
        }

        [Fact]
        public void AnEscapedOperatorThatIsNotFlex_FailsTheGlyph()
        {
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 1, 2, new Op(12, 24), 1, 1, Op.RLineTo), 0, out _));
            Assert.False(TryDraw(Cs(0, 0, Op.RMoveTo, 1, 1, Op.RLineTo, new byte[] { 12 }), 0, out _)); // an escape with nothing after it
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // The table
        // ---------------------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ACff2Table_ExposesItsGlyphs()
        {
            var table = Read([Cs(1, 1, Op.RMoveTo), Cs(2, 2, Op.RMoveTo), []]);

            Assert.True(table.IsSupported);
            Assert.Equal(3, table.GlyphCount);
            Assert.False(table.TryGetFontDict(3, out _));
            Assert.False(table.TryGetFontDict(-1, out _));
            Assert.True(table.TryGetFontDict(2, out _));
        }

        [Fact]
        public void TheRegionScalars_OfADataSet_FollowTheLocation()
        {
            var table = Read([Cs(1, 1, Op.RMoveTo)]);

            Assert.Equal([0.5], table.GetRegionScalars(0, [0.5])!);
            Assert.Equal([0.0, 0.5], table.GetRegionScalars(2, [-0.5])!);
            Assert.Null(table.GetRegionScalars(3, [0.5]));
            Assert.Null(table.GetRegionScalars(-1, [0.5]));
            Assert.Equal([0.0], table.GetRegionScalars(0, [])!);
            Assert.Null(Read([Cs(1, 1, Op.RMoveTo)], variable: false).GetRegionScalars(0, [0.5]));
        }

        public static IEnumerable<object[]> DamagedTables()
        {
            byte[] good = Table([Cs(10, 20, Op.RMoveTo, 1, 1, Op.RLineTo)], null, null, null, Store());

            byte[] With(Action<byte[]> damage)
            {
                var copy = good.ToArray();
                damage(copy);
                return copy;
            }

            yield return ["version 1", With(b => b[0] = 1)];
            yield return ["header size too small", With(b => b[2] = 3)];
            yield return ["header size past the table", With(b => b[2] = 250)];
            yield return ["Top DICT longer than the table", With(b => { b[3] = 0xFF; b[4] = 0xFF; })];
            yield return ["cut off after the header", good[..5]];
            yield return ["cut off in the Top DICT", good[..12]];
            yield return ["cut off in the CharStrings", good[..(good.Length - 20)]];
            yield return ["too short to be a table", good[..3]];
        }

        [Theory]
        [MemberData(nameof(DamagedTables))]
        public void ADamagedTable_IsNotSupported_AndDoesNotThrow(string what, byte[] table)
        {
            var read = new Cff2Table(table, 0, table.Length);

            Assert.False(read.IsSupported, what);
            Assert.False(TryDraw(read, 0, 0, out _));
        }

        [Fact]
        public void ATableThatClaimsToBeLongerThanTheFontData_IsNotSupported()
        {
            var bytes = Table([Cs(10, 20, Op.RMoveTo)]);

            Assert.False(new Cff2Table(bytes, 0, bytes.Length + 100).IsSupported);
            Assert.False(new Cff2Table(bytes, -1, bytes.Length).IsSupported);
            Assert.False(new Cff2Table(bytes, bytes.Length, 10).IsSupported);
        }

        [Fact]
        public void AnIndexWithMoreEntriesThanTheTableCouldHold_IsRefusedBeforeAnythingIsAllocatedForIt()
        {
            var bytes = Table([Cs(10, 20, Op.RMoveTo)], [Cs(Op.Return)]);
            // The Global Subr INDEX follows the header (5) and the Top DICT; its count becomes four billion.
            int topDictLength = (bytes[3] << 8) | bytes[4];
            int global = 5 + topDictLength;
            bytes[global] = bytes[global + 1] = bytes[global + 2] = bytes[global + 3] = 0xFF;

            var table = WorkBounds.Case(() => new Cff2Table(bytes, 0, bytes.Length));

            Assert.False(table.IsSupported);
        }

        [Fact]
        public void AnIndexWhoseOffsetsAreNotInOrder_IsNotSupported()
        {
            var bytes = Table([Cs(10, 20, Op.RMoveTo), Cs(30, 40, Op.RMoveTo)], null, null, null, Store());
            int topDictLength = (bytes[3] << 8) | bytes[4];
            int storeLength = Store().Length;
            int charStrings = 5 + topDictLength + 4 + storeLength; // after the (empty) Global Subr INDEX and the VariationStore
            // count(4) offSize(1) then the offsets 1, 4, 7 (two bytes each): make the second smaller than the first.
            bytes[charStrings + 5 + 2] = 0;
            bytes[charStrings + 5 + 3] = 0;

            Assert.False(new Cff2Table(bytes, 0, bytes.Length).IsSupported);
        }

        [Fact]
        public void MoreGlyphsThanAFontCanHave_IsNotSupported()
        {
            var many = Table(Enumerable.Repeat(Array.Empty<byte>(), 65536).ToArray());

            Assert.False(new Cff2Table(many, 0, many.Length).IsSupported);

            var most = Table(Enumerable.Repeat(Array.Empty<byte>(), 65535).ToArray());
            Assert.True(new Cff2Table(most, 0, most.Length).IsSupported);
        }

        [Fact]
        public void AFontWithSeveralFontDicts_NeedsAnFdSelect()
        {
            var bytes = Table([Cs(1, 1, Op.RMoveTo)], fonts: [new FontDict(), new FontDict()]);

            Assert.False(new Cff2Table(bytes, 0, bytes.Length).IsSupported);
        }

        [Theory]
        [InlineData("a format that does not exist", new byte[] { 9, 0, 0 })]
        [InlineData("format 3 ranges cut off", new byte[] { 3, 0, 200, 0, 0, 0 })]
        [InlineData("format 3 ranges out of order", new byte[] { 3, 0, 2, 0, 5, 0, 0, 1, 0, 0, 3 })]
        [InlineData("format 4 count that cannot fit", new byte[] { 4, 0xFF, 0xFF, 0xFF, 0xFF })]
        public void ADamagedFdSelect_IsNotSupported(string what, byte[] select)
        {
            var bytes = Table([Cs(1, 1, Op.RMoveTo), Cs(1, 1, Op.RMoveTo)], fonts: [new FontDict(), new FontDict()], fdSelect: select);

            Assert.False(new Cff2Table(bytes, 0, bytes.Length).IsSupported, what);
        }

        [Fact]
        public void APrivateDictThatIsNotInsideTheTable_IsNotSupported()
        {
            var bytes = Table([Cs(1, 1, Op.RMoveTo)], fonts: [new FontDict([Cs(1, 1, Op.RMoveTo, Op.Return)])]);

            // Cut the table off inside the Local Subrs, so that the Private DICT's own INDEX does not fit.
            Assert.False(new Cff2Table(bytes, 0, bytes.Length - 3).IsSupported);
        }

        [Fact]
        public void AVariationStoreWhoseDataSetsShareTheSameBytes_IsNotSupported()
        {
            // Data sets that overlap, each claiming 256 region indexes: ten of them read 2,560 values from a table of under a thousand bytes.
            var region = new List<byte>();
            var indexes = new List<byte>();
            for (int i = 0; i < 100; i++)
                indexes.AddRange(new byte[] { 0, 0, 0, 0, 1, 0 }); // itemCount 0, wordDeltaCount 0, regionIndexCount 256, when read at a multiple of 6

            const int Sets = 10;
            var store = new List<byte>();
            void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
            void U32(List<byte> b, int v) { U16(b, v >> 16); U16(b, v & 0xFFFF); }

            int regionListAt = 8 + Sets * 4;
            int firstSetAt = regionListAt + 4 + 6; // one region of one axis
            U16(store, 1);
            U32(store, regionListAt);
            U16(store, Sets);
            for (int i = 0; i < Sets; i++)
                U32(store, firstSetAt + 6 + i * 6);
            U16(store, 1); // axisCount
            U16(store, 1); // regionCount
            U16(store, 0); U16(store, 0x4000); U16(store, 0x4000);
            // a first data set whose region index array is the pattern, so the others can start inside it
            store.AddRange(new byte[] { 0, 0, 0, 0, 1, 0 });
            store.AddRange(indexes);

            var withLength = new List<byte>();
            U16(withLength, store.Count);
            withLength.AddRange(store);

            var bytes = Table([Cs(1, 1, Op.RMoveTo)], variationStore: withLength.ToArray());

            Assert.False(new Cff2Table(bytes, 0, bytes.Length).IsSupported);
        }

        [Fact]
        public void ManyFontDictsThatNameOnePrivateDict_ReadItsLocalSubrsOnce()
        {
            // 20,000 Font DICTs, all naming one Private DICT whose Local Subrs INDEX has 5,000 entries: reading that INDEX for each would
            // allocate 20,000 arrays of 5,001 offsets (400 MB) from a table of a few hundred kilobytes.
            var subrs = Enumerable.Repeat(Cs(Op.Return), 5000).ToArray();
            var fonts = Enumerable.Repeat(new FontDict(subrs), 20000).ToArray();
            var bytes = Table([Cs(-107, Op.CallSubr, 1, 1, Op.RLineTo)], fonts: fonts, fdSelect: FdSelect(3, [19999]), sharePrivate: true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            var table = new Cff2Table(bytes, 0, bytes.Length);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(table.IsSupported);
            Assert.True(allocated < 20_000_000, $"{allocated:N0} bytes were allocated");
            Assert.True(table.TryGetFontDict(0, out var font));
            Assert.Equal(5000, font.LocalSubrs.Count);
        }

        [Fact]
        public void ADataSetThatNamesTheSameRegionManyTimes_GetsTheSameFactorEachTime()
        {
            // 40 entries, alternating region 0 and region 1: more than the sixteen from which each region's factor is worked out once.
            var table = Table([Cs(1, 1, Op.RMoveTo)], variationStore: VariationStore(Regions, [Enumerable.Range(0, 40).Select(i => i % 2).ToArray()]));
            var read = new Cff2Table(table, 0, table.Length);

            double[] scalars = read.GetRegionScalars(0, [0.5])!;

            Assert.Equal(40, scalars.Length);
            Assert.Equal(Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? 0.5 : 0.0), scalars);
        }

        [Fact]
        public void TheRegionFactors_AreKeptForTheLastLocation_AndRecomputedForAnother()
        {
            var table = Read([Cs(1, 1, Op.RMoveTo)]);

            var first = table.GetRegionScalars(0, [0.5]);
            var again = table.GetRegionScalars(0, [0.5]);
            var elsewhere = table.GetRegionScalars(0, [0.25]);
            var back = table.GetRegionScalars(0, [0.5]);

            Assert.Same(first, again);
            Assert.Equal([0.25], elsewhere!);
            Assert.Equal([0.5], back!);
        }

        [Fact]
        public void AVariationStoreThatIsNotOne_IsNotSupported()
        {
            var bytes = Table([Cs(1, 1, Op.RMoveTo)], variationStore: [0, 6, 0, 2, 0, 0, 0, 0]); // format 2

            Assert.False(new Cff2Table(bytes, 0, bytes.Length).IsSupported);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Damage to a real font
        // ---------------------------------------------------------------------------------------------------------------------------

        private static (byte[] Font, int Offset, int Length) FixtureTable()
        {
            byte[] font = File.ReadAllBytes(BundledFonts.VariableCff2Test);
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, at, 4) == "CFF2")
                {
                    int offset = (font[at + 8] << 24) | (font[at + 9] << 16) | (font[at + 10] << 8) | font[at + 11];
                    int length = (font[at + 12] << 24) | (font[at + 13] << 16) | (font[at + 14] << 8) | font[at + 15];
                    return (font, offset, length);
                }
            }

            throw new InvalidOperationException("The fixture has no CFF2 table.");
        }

        /// <summary>Reads the table and draws every glyph at three locations. Nothing may throw, whatever the bytes say.</summary>
        private static void ReadAndDrawEverything(byte[] font, int offset, int length)
        {
            var table = new Cff2Table(font, offset, length);
            if (!table.IsSupported)
                return;

            int glyphs = Math.Min(table.GlyphCount, 64);
            foreach (double coordinate in new[] { 0.0, 0.5, -1.0 })
            {
                var location = new VariationCoordinates([coordinate, coordinate], [coordinate, coordinate], ["wght", "wdth"]);
                for (int glyph = 0; glyph < glyphs; glyph++)
                {
                    Type2CharstringInterpreter.TryGetGlyphOutline(table, glyph, location, out _);
                    table.TryGetFontDict(glyph, out _);
                }
            }
        }

        [Fact]
        public void FlippingAnyByteOfTheFixturesCff2Table_NeverThrows()
        {
            var (font, offset, length) = FixtureTable();
            int supported = 0;

            // (the bound is on each case, not on the sweep: see WorkBounds)
            for (int at = 0; at < length; at++)
            {
                foreach (byte mask in new byte[] { 0xFF, 0x80, 0x01 })
                {
                    var copy = font.ToArray();
                    copy[offset + at] ^= mask;
                    if (WorkBounds.Case(() =>
                    {
                        ReadAndDrawEverything(copy, offset, length);
                        return new Cff2Table(copy, offset, length).IsSupported;
                    }, $"byte {at} ^ {mask:X2}"))
                    {
                        supported++;
                    }
                }
            }

            // Most single-byte changes still leave a table that reads (a coordinate is different, an operator is not), so the fuzz is
            // exercising the interpreter and not only the reader's refusals.
            Assert.True(supported > length, $"{supported} of {length * 3} damaged copies were still readable");
        }

        [Fact]
        public void ManyBytesChangedAtOnce_NeverThrows()
        {
            var (font, offset, length) = FixtureTable();
            var random = new Random(20260926);

            for (int round = 0; round < 3000; round++)
            {
                var copy = font.ToArray();
                int changes = 1 + random.Next(8);
                for (int i = 0; i < changes; i++)
                    copy[offset + random.Next(length)] = (byte)random.Next(256);

                ReadAndDrawEverything(copy, offset, length);
            }
        }

        [Fact]
        public void EveryTruncationOfTheFixturesCff2Table_NeverThrows()
        {
            var (font, offset, length) = FixtureTable();

            for (int cut = 0; cut < length; cut++)
                ReadAndDrawEverything(font, offset, cut);
        }

        [Fact]
        public void ACff2TableThatIsDamagedInTheFontsDirectory_LeavesAFontWithNoOutlines_NotAFontThatFailsToLoad()
        {
            var (font, _, _) = FixtureTable();
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, at, 4) == "CFF2")
                {
                    font[at + 12] = 0x7F; // a length of two billion
                    font[at + 13] = font[at + 14] = font[at + 15] = 0xFF;
                }
            }

            var set = new FontSet();
            var family = set.AddData(font, new AddOptions { FamilyName = "Damaged-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));

            Assert.False(match.Typeface.TryGetOutline(2, out _));
        }
    }
}
