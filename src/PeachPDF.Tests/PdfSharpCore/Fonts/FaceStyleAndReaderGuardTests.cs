using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// The engine's own view of a requested style (<see cref="FaceStyle"/>, <see cref="SyntheticStyle"/>, the typeface
    /// key built from them) and the font reader's refusal of inputs it cannot use. The refusals are exercised with
    /// bytes crafted from a bundled font, since no real font in the repo is a collection, a bitmap-only font, or one
    /// with no usable <c>cmap</c>.
    /// </summary>
    public class FaceStyleAndReaderGuardTests
    {
        [Theory]
        [InlineData(0, false, false, false)]
        [InlineData(1, true, false, false)]
        [InlineData(2, false, true, false)]
        [InlineData(3, true, true, true)]
        public void RequestedStyle_IsReadAsBoldItalicAndBoth(int style, bool bold, bool italic, bool both)
        {
            var options = new FontResolvingOptions((FaceStyle)style);

            Assert.Equal(bold, options.IsBold);
            Assert.Equal(italic, options.IsItalic);
            Assert.Equal(both, options.IsBoldItalic);
            Assert.Equal(bold ? 700 : 400, options.Weight);
        }

        [Theory]
        [InlineData(0, false, false, "")]
        [InlineData(1, true, false, "|b+/i-")]
        [InlineData(2, false, true, "|b-/i+")]
        [InlineData(3, true, true, "|b+/i+")]
        public void ForcedSynthesis_IsReportedAndKeyedIntoTheTypefaceKey(int synthesis, bool bold, bool italic, string suffix)
        {
            var options = new FontResolvingOptions(FaceStyle.Regular, (SyntheticStyle)synthesis);

            Assert.Equal(bold, options.MustSimulateBold);
            Assert.Equal(italic, options.MustSimulateItalic);
            Assert.Equal("tk:some family/n/400/5" + suffix, options.ComputeTypefaceKey("Some Family"));
        }

        [Fact]
        public void TypefaceKey_RejectsAnUndefinedSynthesisValue()
        {
            var options = new FontResolvingOptions(FaceStyle.Regular, (SyntheticStyle)8);

            Assert.Throws<ArgumentOutOfRangeException>(() => options.ComputeTypefaceKey("Fam"));
        }

        [Fact]
        public void TypefaceKey_DistinguishesItalicWeightAndStretch()
        {
            var normal = new FontResolvingOptions(FaceStyle.Regular).ComputeTypefaceKey("F");
            var italic = new FontResolvingOptions(FaceStyle.Italic).ComputeTypefaceKey("F");
            var semiBold = new FontResolvingOptions(FaceStyle.Regular, 600, 3).ComputeTypefaceKey("F");

            Assert.Equal("tk:f/n/400/5", normal);
            Assert.Equal("tk:f/i/400/5", italic);
            Assert.Equal("tk:f/n/600/3", semiBold);
            Assert.Equal(normal, LoadedTypeface.ComputeKey("F", false, false));
            Assert.Equal(new FontResolvingOptions(FaceStyle.BoldItalic).ComputeTypefaceKey("F"), LoadedTypeface.ComputeKey("F", true, true));
        }

        [Theory]
        [InlineData(1, true, false)]
        [InlineData(2, false, true)]
        [InlineData(3, true, true)]
        public void ResolverInfo_BuiltFromSynthesisFlags_KnowsWhatMustBeFaked(int synthesis, bool bold, bool italic)
        {
            var info = new FontResolverInfo("face", (SyntheticStyle)synthesis);

            Assert.Equal(bold, info.MustSimulateBold);
            Assert.Equal(italic, info.MustSimulateItalic);
        }

        [Fact]
        public void AddFont_WithBoldItalicOverrides_FilesTheFaceUnderThoseAxes()
        {
            string family = "OverrideFamily-" + Guid.NewGuid().ToString("N");
            var resolver = new FontResolver();
            using (var stream = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(stream, family, weightOverride: 700, isItalicOverride: true);

            var info = resolver.ResolveTypeface(family, 700, true, 5);

            Assert.NotNull(info);
            Assert.False(info.MustSimulateBold);
            Assert.False(info.MustSimulateItalic);
        }

        [Fact]
        public void FontFactory_ComputesTheTypefaceKeyItself_WhenTheCallerSuppliesNone()
        {
            string family = "KeylessFamily-" + Guid.NewGuid().ToString("N");
            var resolver = new FontResolver();
            using (var stream = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(stream, family);

            var info = FontFactory.ResolveTypeface(family, new FontResolvingOptions(FaceStyle.Regular), "", resolver);

            Assert.NotNull(info);
        }

        [Fact]
        public void FontDescription_FallsBackToAStyleDerivedWeight_WhenOs2SaysZero()
        {
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            var (offset, _) = FindTable(bytes, "OS/2");
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 4), 0); // usWeightClass

            var description = TtfFontDescription.LoadDescription(new MemoryStream(bytes));

            Assert.Equal(TtfFontDescription.DefaultWeight, description.Weight);
        }

        [Fact]
        public void Reader_RejectsATrueTypeCollectionThatWasNotExtractedFirst()
        {
            var bytes = new byte[64];
            Encoding.ASCII.GetBytes("ttcf").CopyTo(bytes, 0);

            Assert.Throws<InvalidOperationException>(() => new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes)));
        }

        [Fact]
        public void Reader_RejectsABitmapFont()
        {
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            Encoding.ASCII.GetBytes("bhed").CopyTo(bytes, 12 + 16 * (tables - 1)); // rename the last table's tag

            Assert.Throws<NotSupportedException>(() => new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes)));
        }

        [Fact]
        public void Reader_RejectsAFontWhoseCmapHasNoUsablePlatform()
        {
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            var (offset, _) = FindTable(bytes, "cmap");
            int subtables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2));
            for (int i = 0; i < subtables; i++)
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 4 + 8 * i), 9); // an unassigned platform id

            Assert.Throws<InvalidOperationException>(() => new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes)));
        }

        private static (int Offset, int Length) FindTable(byte[] font, string tag)
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (int i = 0; i < tables; i++)
            {
                int record = 12 + 16 * i;
                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                    return ((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
            }

            throw new InvalidOperationException("No " + tag + " table in the fixture font.");
        }
    }
}
