using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Parser coverage for the optional vertical-writing-mode tables (<c>vhea</c>/<c>vmtx</c>/<c>VORG</c>)
    /// and <see cref="OpenTypeDescriptor"/>'s vertical-metrics query methods. None of the bundled fonts
    /// (all Latin/Hebrew, see <see cref="BundledFonts"/>) ship real vertical metrics, so - following the
    /// GSUB synthetic-table precedent in <c>GsubTableSyntheticTests</c> - this hand-crafts each table's
    /// bytes directly and appends them past the end of a real, already-valid font file, then manually
    /// registers a <see cref="TableDirectoryEntry"/> pointing at the appended bytes. Unlike CPAL/COLR
    /// (which self-position from their own <c>DirectoryEntry.Offset</c>), vhea/vmtx/VORG read
    /// sequentially from the font reader's current position - like hhea/hmtx - so each still needs an
    /// explicit <see cref="OpenTypeFontface.Seek"/> before construction, exactly as
    /// <see cref="OpenTypeFontface.Read"/> itself does.
    /// </summary>
    public class VerticalMetricsTablesTests
    {
        private static byte[] Concat(byte[] a, byte[] b)
        {
            var combined = new byte[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        /// <summary>Loads the bundled TrueType font and registers <paramref name="tag"/> against the
        /// appended synthetic bytes.</summary>
        private static (OpenTypeFontface Face, int TableStart) BuildFaceWithSyntheticTable(string tag, byte[] tableBytes)
            => BuildFaceWithSyntheticTable(BundledFonts.Ttf, tag, tableBytes);

        /// <summary>Loads <paramref name="basePath"/> and registers <paramref name="tag"/> against the
        /// appended synthetic bytes - the base-font-path parameter lets a caller choose a CFF-flavored
        /// base (<see cref="BundledFonts.Otf"/>) instead of the default TrueType one, needed to exercise
        /// <c>VORG</c>'s CFF-only restriction (<see cref="OpenTypeDescriptor.HasVerticalOrigin"/>).</summary>
        private static (OpenTypeFontface Face, int TableStart) BuildFaceWithSyntheticTable(string basePath, string tag, byte[] tableBytes)
        {
            byte[] fontBytes = File.ReadAllBytes(basePath);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, tableBytes);
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            face.TableDictionary[tag] = new TableDirectoryEntry(tag) { Offset = tableStart };
            return (face, tableStart);
        }

        /// <summary>Loads a real font and registers both vhea and vmtx against their appended synthetic bytes.</summary>
        private static OpenTypeFontface BuildFaceWithVheaAndVmtxTables(byte[] vheaBytes, byte[] vmtxBytes)
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int vheaStart = fontBytes.Length;
            byte[] withVhea = Concat(fontBytes, vheaBytes);
            int vmtxStart = withVhea.Length;
            byte[] combined = Concat(withVhea, vmtxBytes);

            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            face.TableDictionary[TableTagNames.VHea] = new TableDirectoryEntry(TableTagNames.VHea) { Offset = vheaStart };
            face.TableDictionary[TableTagNames.VMtx] = new TableDirectoryEntry(TableTagNames.VMtx) { Offset = vmtxStart };
            return face;
        }

        /// <summary>
        /// Splices one new table directory entry into a real, already-valid font file's own SFNT header -
        /// unlike <see cref="BuildFaceWithSyntheticTable(string,byte[])"/>, which registers the appended
        /// bytes only in the in-memory <see cref="OpenTypeFontface.TableDictionary"/> AFTER the font has
        /// already been parsed. This is needed to exercise <see cref="OpenTypeFontface.Read"/>'s own
        /// table-detection branches (the <c>Seek(...) != -1</c> checks), which only see tables present in
        /// the file's real directory at parse time. See <see cref="SyntheticFontTables"/> for the shared
        /// implementation (also used by integration tests that embed the resulting bytes for real).
        /// </summary>
        private static byte[] InsertTableDirectoryEntry(byte[] fontBytes, string tag, byte[] tableBytes)
            => SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, tag, tableBytes);

        private static byte[] BuildVhea(short ascent, short descent, ushort numOfLongVerMetrics)
            => SyntheticFontTables.BuildVhea(ascent, descent, numOfLongVerMetrics);

        [Fact]
        public void VerticalHeaderTable_ParsesFieldsCorrectly()
        {
            var (face, _) = BuildFaceWithSyntheticTable(TableTagNames.VHea, BuildVhea(ascent: 880, descent: -120, numOfLongVerMetrics: 3));

            Assert.NotEqual(-1, face.Seek(VerticalHeaderTable.Tag));
            var vhea = new VerticalHeaderTable(face);

            Assert.Equal(0x00010000, vhea.version);
            Assert.Equal(880, vhea.ascent);
            Assert.Equal(-120, vhea.descent);
            Assert.Equal(3, vhea.numOfLongVerMetrics);
        }

        [Fact]
        public void VerticalMetricsTable_ParsesLongMetricsAndTrailingTopSideBearings()
        {
            byte[] vheaBytes = BuildVhea(ascent: 880, descent: -120, numOfLongVerMetrics: 2);

            var vmtxBuilder = new SfntByteBuilder();
            vmtxBuilder.U16(1000); vmtxBuilder.S16(50);  // glyph 0: advanceHeight=1000, topSideBearing=50
            vmtxBuilder.U16(1200); vmtxBuilder.S16(-30); // glyph 1: advanceHeight=1200, topSideBearing=-30
            vmtxBuilder.S16(77);                          // trailing topSideBearing-only entry (glyph 2)
            byte[] vmtxBytes = vmtxBuilder.ToArray();

            var face = BuildFaceWithVheaAndVmtxTables(vheaBytes, vmtxBytes);

            Assert.NotEqual(-1, face.Seek(VerticalHeaderTable.Tag));
            face.vhea = new VerticalHeaderTable(face);

            // Constrain maxp's glyph count so the trailing-topSideBearing-array branch (numGlyphs >
            // numOfLongVerMetrics) is exercised without needing a synthetic maxp table too.
            ushort realNumGlyphs = face.maxp.numGlyphs;
            face.maxp.numGlyphs = 3;
            try
            {
                Assert.NotEqual(-1, face.Seek(VerticalMetricsTable.Tag));
                var vmtx = new VerticalMetricsTable(face);

                Assert.Equal(2, vmtx.Metrics.Length);
                Assert.Equal(1000, vmtx.Metrics[0].advanceHeight);
                Assert.Equal(50, vmtx.Metrics[0].topSideBearing);
                Assert.Equal(1200, vmtx.Metrics[1].advanceHeight);
                Assert.Equal(-30, vmtx.Metrics[1].topSideBearing);
                Assert.Single(vmtx.TopSideBearing);
                Assert.Equal(77, vmtx.TopSideBearing[0]);
            }
            finally
            {
                face.maxp.numGlyphs = realNumGlyphs;
            }
        }

        [Fact]
        public void VerticalOriginTable_ResolvesPerGlyphOverrideAndDefault()
        {
            var b = new SfntByteBuilder();
            b.U16(1); // majorVersion
            b.U16(0); // minorVersion
            b.S16(880); // defaultVertOriginY
            b.U16(2); // numVertOriginYMetrics
            b.U16(5); b.S16(900); // glyph 5 -> 900
            b.U16(9); b.S16(700); // glyph 9 -> 700

            var (face, _) = BuildFaceWithSyntheticTable(TableTagNames.VOrg, b.ToArray());
            Assert.NotEqual(-1, face.Seek(VerticalOriginTable.Tag));
            var vorg = new VerticalOriginTable(face);

            Assert.Equal(880, vorg.defaultVertOriginY);
            Assert.Equal(900, vorg.VertOriginYFor(5));
            Assert.Equal(700, vorg.VertOriginYFor(9));
            Assert.Equal(880, vorg.VertOriginYFor(123)); // no explicit override -> default
        }

        [Fact]
        public void VerticalOriginTable_TruncatedData_ThrowsInvalidOperationException()
        {
            // Only the 6-byte header, no room for the one promised VertOriginYMetric record - Read()
            // runs past the end of the font's own byte buffer and must surface as InvalidOperationException,
            // not the raw IndexOutOfRangeException.
            var b = new SfntByteBuilder();
            b.U16(1);
            b.U16(0);
            b.S16(880);
            b.U16(1); // numVertOriginYMetrics=1, but no record bytes follow

            var (face, _) = BuildFaceWithSyntheticTable(TableTagNames.VOrg, b.ToArray());
            face.Seek(VerticalOriginTable.Tag);

            Assert.Throws<System.InvalidOperationException>(() => new VerticalOriginTable(face));
        }

        [Fact]
        public void OpenTypeFontfaceRead_DiscoversVorgFromTheFontsOwnTableDirectory()
        {
            var b = new SfntByteBuilder();
            b.U16(1); // majorVersion
            b.U16(0); // minorVersion
            b.S16(500); // defaultVertOriginY
            b.U16(0); // numVertOriginYMetrics

            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] spliced = InsertTableDirectoryEntry(fontBytes, TableTagNames.VOrg, b.ToArray());

            var face = XFontSource.GetOrCreateFrom(spliced).Fontface;

            Assert.NotNull(face.vorg);
            Assert.Equal(500, face.vorg.defaultVertOriginY);
            // The real font's other tables must still parse correctly after the directory splice.
            Assert.NotNull(face.cmap);
            Assert.NotNull(face.hmtx);
        }

        private static OpenTypeDescriptor Descriptor(OpenTypeFontface face) =>
            new("vertical-metrics-test", "vertical-metrics-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));

        [Fact]
        public void GlyphIndexToVerticalAdvance_FallsBackToUnitsPerEm_WhenFontHasNoVerticalTables()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            Assert.Null(face.vhea);
            Assert.Null(face.vmtx);

            var descriptor = Descriptor(face);

            Assert.Equal(face.head.unitsPerEm, descriptor.GlyphIndexToVerticalAdvance(0));
        }

        [Fact]
        public void GlyphIndexToVerticalAdvance_UsesVmtx_AndClampsToTheLastMetricPastItsRange()
        {
            byte[] vheaBytes = BuildVhea(ascent: 880, descent: -120, numOfLongVerMetrics: 2);
            var vmtxBuilder = new SfntByteBuilder();
            vmtxBuilder.U16(1000); vmtxBuilder.S16(0);
            vmtxBuilder.U16(1500); vmtxBuilder.S16(0);
            byte[] vmtxBytes = vmtxBuilder.ToArray();

            var face = BuildFaceWithVheaAndVmtxTables(vheaBytes, vmtxBytes);

            face.Seek(VerticalHeaderTable.Tag);
            face.vhea = new VerticalHeaderTable(face);
            ushort realNumGlyphs = face.maxp.numGlyphs;
            face.maxp.numGlyphs = 2;
            try
            {
                face.Seek(VerticalMetricsTable.Tag);
                face.vmtx = new VerticalMetricsTable(face);

                var descriptor = Descriptor(face);

                Assert.Equal(1000, descriptor.GlyphIndexToVerticalAdvance(0));
                Assert.Equal(1500, descriptor.GlyphIndexToVerticalAdvance(1));
                // glyphIndex beyond numOfLongVerMetrics shares the last metric's advance height.
                Assert.Equal(1500, descriptor.GlyphIndexToVerticalAdvance(50));
            }
            finally
            {
                face.maxp.numGlyphs = realNumGlyphs;
            }
        }

        [Fact]
        public void GlyphIndexToVerticalAdvance_FallsBackToUnitsPerEm_WhenVmtxHasNoMaxpToCountAgainst()
        {
            // VerticalMetricsTable.Read only populates Metrics when both vhea and maxp are non-null - a
            // font with vhea+vmtx table entries but no readable maxp leaves vmtx.Metrics null. The
            // fallback check must treat that the same as "no vmtx" rather than dereferencing it.
            byte[] vheaBytes = BuildVhea(ascent: 880, descent: -120, numOfLongVerMetrics: 1);
            var vmtxBuilder = new SfntByteBuilder();
            vmtxBuilder.U16(1000); vmtxBuilder.S16(0);

            var face = BuildFaceWithVheaAndVmtxTables(vheaBytes, vmtxBuilder.ToArray());

            face.Seek(VerticalHeaderTable.Tag);
            face.vhea = new VerticalHeaderTable(face);

            var realMaxp = face.maxp;
            face.maxp = null!;
            try
            {
                face.Seek(VerticalMetricsTable.Tag);
                face.vmtx = new VerticalMetricsTable(face);
                Assert.Null(face.vmtx.Metrics);

                var descriptor = Descriptor(face);

                Assert.Equal(face.head.unitsPerEm, descriptor.GlyphIndexToVerticalAdvance(0));
            }
            finally
            {
                face.maxp = realMaxp;
            }
        }

        /// <summary>Builds the <c>VORG</c> synthetic bytes shared by the honored/ignored pair below:
        /// <c>defaultVertOriginY = 880</c>, one per-glyph override (<c>glyph 0 -&gt; 950</c>).</summary>
        private static byte[] BuildVorg()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0);
            b.S16(880);
            b.U16(1);
            b.U16(0); b.S16(950); // glyph 0 -> 950
            return b.ToArray();
        }

        [Fact]
        public void GlyphIndexToVerticalOrigin_UsesVorgWhenPresent_OnCffFont()
        {
            // VORG "may only be used in CFF or CFF2 OpenType fonts" (OpenType spec) - BundledFonts.Otf
            // (Source Code Pro) is confirmed CFF-flavored (a real CFF table, no glyf), so this is the
            // case where a real VORG table is actually trusted.
            var (face, _) = BuildFaceWithSyntheticTable(BundledFonts.Otf, TableTagNames.VOrg, BuildVorg());
            face.Seek(VerticalOriginTable.Tag);
            face.vorg = new VerticalOriginTable(face);

            var descriptor = Descriptor(face);
            Assert.True(descriptor.HasVerticalOrigin);

            var (_, y) = descriptor.GlyphIndexToVerticalOrigin(0);
            Assert.Equal(950, y);
        }

        [Fact]
        public void GlyphIndexToVerticalOrigin_IgnoresVorgOnTrueTypeFonts()
        {
            // "If present in OpenType fonts containing TrueType outline data, it must be ignored" - a
            // VORG table spliced onto a real glyf font (BundledFonts.Ttf) must not be consulted;
            // GlyphIndexToVerticalOrigin falls through to its vhea/os2 chain exactly as it would for a
            // font with no VORG table at all.
            var (face, _) = BuildFaceWithSyntheticTable(TableTagNames.VOrg, BuildVorg());
            face.Seek(VerticalOriginTable.Tag);
            face.vorg = new VerticalOriginTable(face);
            Assert.NotNull(face.vorg); // the table did parse - it's the *consulting* of it that must stop

            var descriptor = Descriptor(face);
            Assert.False(descriptor.HasVerticalOrigin);

            var (_, y) = descriptor.GlyphIndexToVerticalOrigin(0);
            Assert.NotEqual(950, y);
            Assert.NotEqual(880, y); // not even VORG's own default - the table is ignored altogether
        }

        [Fact]
        public void HasVerticalOrigin_TrueOnlyForCffFontWithRealVorg()
        {
            var (cffWithVorg, _) = BuildFaceWithSyntheticTable(BundledFonts.Otf, TableTagNames.VOrg, BuildVorg());
            cffWithVorg.Seek(VerticalOriginTable.Tag);
            cffWithVorg.vorg = new VerticalOriginTable(cffWithVorg);
            Assert.True(Descriptor(cffWithVorg).HasVerticalOrigin);

            var (ttfWithVorg, _) = BuildFaceWithSyntheticTable(TableTagNames.VOrg, BuildVorg());
            ttfWithVorg.Seek(VerticalOriginTable.Tag);
            ttfWithVorg.vorg = new VerticalOriginTable(ttfWithVorg);
            Assert.False(Descriptor(ttfWithVorg).HasVerticalOrigin); // real VORG, but TrueType-flavored

            var cffWithoutVorg = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Otf)).Fontface;
            Assert.False(Descriptor(cffWithoutVorg).HasVerticalOrigin); // CFF-flavored, but no VORG

            var ttfWithoutVorg = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            Assert.False(Descriptor(ttfWithoutVorg).HasVerticalOrigin);
        }

        [Fact]
        public void GlyphIndexToVerticalOrigin_PrefersVheaAscentOverTypoAscender_WhenNoVorg()
        {
            var (face, _) = BuildFaceWithSyntheticTable(TableTagNames.VHea, BuildVhea(ascent: 750, descent: -150, numOfLongVerMetrics: 1));
            face.Seek(VerticalHeaderTable.Tag);
            face.vhea = new VerticalHeaderTable(face);
            Assert.Null(face.vorg);
            Assert.NotEqual(750, face.os2.sTypoAscender);

            var descriptor = Descriptor(face);
            var (_, y) = descriptor.GlyphIndexToVerticalOrigin(0);

            Assert.Equal(750, y);
        }

        [Fact]
        public void GlyphIndexToVerticalOrigin_FallsBackToTypoAscender_WhenNoVorg()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            Assert.Null(face.vorg);
            Assert.NotEqual(0, face.os2.sTypoAscender);

            var descriptor = Descriptor(face);
            var (_, y) = descriptor.GlyphIndexToVerticalOrigin(0);

            Assert.Equal(face.os2.sTypoAscender, y);
        }

        [Fact]
        public void HasVerticalMetrics_TrueOnlyWhenVheaAndVmtxBothPresent()
        {
            byte[] vheaBytes = BuildVhea(ascent: 880, descent: -120, numOfLongVerMetrics: 1);
            var vmtxBuilder = new SfntByteBuilder();
            vmtxBuilder.U16(1000); vmtxBuilder.S16(0);

            var withBoth = BuildFaceWithVheaAndVmtxTables(vheaBytes, vmtxBuilder.ToArray());
            withBoth.Seek(VerticalHeaderTable.Tag);
            withBoth.vhea = new VerticalHeaderTable(withBoth);
            ushort realNumGlyphs = withBoth.maxp.numGlyphs;
            withBoth.maxp.numGlyphs = 1;
            try
            {
                withBoth.Seek(VerticalMetricsTable.Tag);
                withBoth.vmtx = new VerticalMetricsTable(withBoth);

                Assert.True(Descriptor(withBoth).HasVerticalMetrics);
            }
            finally
            {
                withBoth.maxp.numGlyphs = realNumGlyphs;
            }

            var withNeither = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            Assert.False(Descriptor(withNeither).HasVerticalMetrics);
        }

        /// <summary>
        /// Confirms the repo's own bundled production CJK font (used by
        /// <c>TextOrientationIntegrationTests</c>'s upright-run coverage) genuinely carries real
        /// <c>vhea</c>/<c>vmtx</c> data - not a synthetic fixture - so issue #770's real-metrics path is
        /// exercised by an actual font PeachPDF ships. Checks provenance (the returned value traces back
        /// to a real parsed <c>vmtx.Metrics</c> entry, via the same monospaced-tail clamp
        /// <see cref="OpenTypeDescriptor.GlyphIndexToVerticalAdvance"/> itself documents) rather than a
        /// specific numeric value, since a CJK ideograph's real vertical advance height commonly
        /// coincides numerically with the no-vmtx one-em fallback (both being close to one em) - a raw
        /// value-vs-fallback comparison would be a fragile, misleading proof.
        /// </summary>
        [Fact]
        public void RealBundledCjkFont_HasVerticalMetrics_AdvanceComesFromARealVmtxEntry()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Cjk)).Fontface;
            Assert.NotNull(face.vhea);
            Assert.NotNull(face.vmtx);

            var descriptor = Descriptor(face);
            Assert.True(descriptor.HasVerticalMetrics);

            // U+30C6 "テ" (katakana TE) - the same upright character TextOrientationIntegrationTests uses.
            var glyphIndex = descriptor.CharCodeToGlyphIndex(new System.Text.Rune(0x30C6));
            Assert.NotEqual(0, glyphIndex); // the font actually has a glyph for this character

            var advance = descriptor.GlyphIndexToVerticalAdvance(glyphIndex);
            var clampedIndex = System.Math.Min(glyphIndex, face.vhea.numOfLongVerMetrics - 1);
            Assert.Equal(face.vmtx.Metrics[clampedIndex].advanceHeight, advance);
            Assert.True(advance > 0);
        }

        [Fact]
        public void GlyphIndexToVerticalOrigin_FallsBackToUnitsPerEm_WhenNoVorgAndNoTypoAscender()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            short realAscender = face.os2.sTypoAscender;
            face.os2.sTypoAscender = 0;
            try
            {
                var descriptor = Descriptor(face);
                var (_, y) = descriptor.GlyphIndexToVerticalOrigin(0);

                Assert.Equal(face.head.unitsPerEm, y);
            }
            finally
            {
                face.os2.sTypoAscender = realAscender;
            }
        }
    }
}
