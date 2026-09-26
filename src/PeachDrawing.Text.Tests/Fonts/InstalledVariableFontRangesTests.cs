using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.IO;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// An installed variable font is discovered with the ranges of its own axes (read from <c>fvar</c> while its description is read), so it
    /// competes in face matching for every weight, width and slant it can draw instead of at its default alone.
    /// </summary>
    public class InstalledVariableFontRangesTests
    {
        private static VariationAxis AxisOf(string path, string tag)
        {
            var family = new FontSet().AddFile(path, new AddOptions { FamilyName = "Axes-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface.Axes.Single(a => a.Tag == tag);
        }

        [Fact]
        public void ADescription_CarriesTheRangesOfTheWeightAndWidthAxes()
        {
            var description = TtfFontDescription.LoadDescription(BundledFonts.VariableTest);

            var weight = AxisOf(BundledFonts.VariableTest, AxisTags.Weight);
            var width = AxisOf(BundledFonts.VariableTest, AxisTags.Width);
            Assert.Equal(new AxisRange(weight.Minimum, weight.Maximum), description.WeightRange);
            Assert.Equal(new AxisRange(width.Minimum, width.Maximum), description.WidthRange);
            Assert.Null(description.ObliqueRange);
        }

        [Fact]
        public void ASlantAxis_IsAnObliqueRangeLeaningToTheRight()
        {
            var description = TtfFontDescription.LoadDescription(BundledFonts.VariableSlantTest);

            var slant = AxisOf(BundledFonts.VariableSlantTest, AxisTags.Slant);
            Assert.Equal(new AxisRange(-slant.Maximum, -slant.Minimum), description.ObliqueRange);
            Assert.True(description.ObliqueRange!.Value.Contains(0));
        }

        [Fact]
        public void AFontThatIsNotVariable_HasNoRanges()
        {
            var description = TtfFontDescription.LoadDescription(BundledFonts.Ttf);

            Assert.Null(description.WeightRange);
            Assert.Null(description.WidthRange);
            Assert.Null(description.ObliqueRange);
        }

        [Fact]
        public void TheDiscoveredFace_CoversTheAxisRanges_AndAStaticFaceCoversItsOwnValue()
        {
            var (_, families) = FontResolver.ParseSystemFonts([BundledFonts.VariableTest, BundledFonts.Ttf]);

            var variableName = TtfFontDescription.LoadDescription(BundledFonts.VariableTest).FontFamilyInvariantCulture.ToLowerInvariant();
            var staticName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture.ToLowerInvariant();
            var variable = families[variableName].Faces.Single();
            var ordinary = families[staticName].Faces.Single();

            var weight = AxisOf(BundledFonts.VariableTest, AxisTags.Weight);
            Assert.Equal(new AxisRange(weight.Minimum, weight.Maximum), variable.Ranges.Weight);
            Assert.NotNull(variable.Declared);
            Assert.Null(ordinary.Declared);
            Assert.Equal(new AxisRange(ordinary.Weight), ordinary.Ranges.Weight);
        }

        [Fact]
        public void AnAxisTheFontLacks_IsCoveredAtTheFacesOwnValue()
        {
            var (_, families) = FontResolver.ParseSystemFonts([BundledFonts.VariableSlantTest]);
            var description = TtfFontDescription.LoadDescription(BundledFonts.VariableSlantTest);

            var face = families.Values.Single().Faces.Single();

            Assert.Equal(description.WidthRange ?? new AxisRange(WidthClasses.ToPercent(face.Stretch)), face.Ranges.Width);
            Assert.Equal(description.WeightRange ?? new AxisRange(face.Weight), face.Ranges.Weight);
            Assert.NotNull(face.Ranges.Oblique);
        }

        private static int FvarRecord(byte[] font)
        {
            var tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (var i = 0; i < tables; i++)
            {
                var record = 12 + 16 * i;
                if (System.Text.Encoding.ASCII.GetString(font, record, 4) == "fvar")
                    return record;
            }

            throw new InvalidOperationException("The fixture has no fvar table.");
        }

        private static TtfFontDescription Load(byte[] font)
        {
            using var stream = new MemoryStream(font);
            return TtfFontDescription.LoadDescription(stream);
        }

        [Fact]
        public void AMalformedFvar_LeavesTheFontUsableAtItsDefault()
        {
            var original = File.ReadAllBytes(BundledFonts.VariableTest);
            var record = FvarRecord(original);
            var tableOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(record + 8));

            // A count of axes that runs past the table.
            var tooManyAxes = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(tooManyAxes.AsSpan(tableOffset + 8), 40);
            BinaryPrimitives.WriteUInt16BigEndian(tooManyAxes.AsSpan(tableOffset + 10), 4000);
            // No axes at all.
            var noAxes = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(noAxes.AsSpan(tableOffset + 8), 0);
            // A table too short to hold its own header.
            var tooShort = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(tooShort.AsSpan(record + 12), 8);
            // A table that starts beyond the end of the file.
            var pastTheEnd = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(pastTheEnd.AsSpan(record + 8), (uint)original.Length + 100);
            // Axis records smaller than the format's.
            var thinRecords = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(thinRecords.AsSpan(tableOffset + 10), 8);

            // An offset to the axes that leaves no room for them in the table.
            var farAxes = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(farAxes.AsSpan(tableOffset + 4), ushort.MaxValue);

            foreach (var broken in new[] { tooManyAxes, noAxes, tooShort, pastTheEnd, thinRecords, farAxes })
            {
                var description = Load(broken);

                Assert.NotEmpty(description.FontFamilyInvariantCulture);
                Assert.Null(description.WeightRange);
                Assert.Null(description.WidthRange);
                Assert.Null(description.ObliqueRange);
            }

            Assert.NotNull(Load(original).WeightRange);
        }
    }
}
