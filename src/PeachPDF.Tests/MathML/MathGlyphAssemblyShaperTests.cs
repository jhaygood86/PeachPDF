using System.Linq;
using PeachPDF.Fonts.OpenType;
using PeachPDF.MathML;
using Xunit;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// Coverage for <see cref="MathGlyphAssemblyShaper"/> against STIX Two Math's real parenleft
    /// GlyphAssembly data (<c>MathTableTests.Variants_VerticalConstruction_ForParenLeft...</c>):
    /// 3 parts - bottom cap (glyph 4862, start=0, end=250, fullAdvance=1273), an extender (glyph 4861,
    /// start=1000, end=1000, fullAdvance=1252), and a top cap (glyph 4860, start=250, end=0,
    /// fullAdvance=1273) - with MinConnectorOverlap=100.
    /// </summary>
    public class MathGlyphAssemblyShaperTests
    {
        static MathGlyphAssembly ParenLeftAssembly() => new()
        {
            ItalicsCorrection = 0,
            Parts =
            [
                new MathGlyphPart(4862, StartConnectorLength: 0, EndConnectorLength: 250, FullAdvance: 1273, IsExtender: false),
                new MathGlyphPart(4861, StartConnectorLength: 1000, EndConnectorLength: 1000, FullAdvance: 1252, IsExtender: true),
                new MathGlyphPart(4860, StartConnectorLength: 250, EndConnectorLength: 0, FullAdvance: 1273, IsExtender: false),
            ],
        };

        [Fact]
        public void Shape_TargetBeyondLargestVariant_ProducesMultiplePartsCoveringTarget()
        {
            const double target = 10000;
            var result = MathGlyphAssemblyShaper.Shape(ParenLeftAssembly(), minConnectorOverlap: 100, target);

            Assert.NotNull(result);
            Assert.True(result!.Value.SizeDesignUnits >= target);
            // At least the two end caps plus one or more repeats of the extender.
            Assert.True(result.Value.Parts.Count >= 3);
            // Bottom-to-top order: offsets strictly increase from 0.
            Assert.Equal(0, result.Value.Parts[0].OffsetDesignUnits);
            for (int i = 1; i < result.Value.Parts.Count; i++)
                Assert.True(result.Value.Parts[i].OffsetDesignUnits > result.Value.Parts[i - 1].OffsetDesignUnits);
        }

        [Fact]
        public void Shape_ZeroConnectorOnOutwardFacingEdge_DoesNotInvalidateAssembly()
        {
            // The real font's own end caps have a 0 connector on their OUTWARD-facing edge (start of the
            // bottom cap, end of the top cap) despite MinConnectorOverlap=100 - a legitimate font design
            // (that edge never joins to anything), which must not be rejected as "invalid".
            var result = MathGlyphAssemblyShaper.Shape(ParenLeftAssembly(), minConnectorOverlap: 100, targetDesignUnits: 5000);
            Assert.NotNull(result);
        }

        [Fact]
        public void Shape_NoExtenderPart_ReturnsNull()
        {
            var assembly = new MathGlyphAssembly
            {
                ItalicsCorrection = 0,
                Parts = [new MathGlyphPart(1, 0, 0, 1000, IsExtender: false)],
            };
            Assert.Null(MathGlyphAssemblyShaper.Shape(assembly, minConnectorOverlap: 100, targetDesignUnits: 5000));
        }

        [Fact]
        public void Shape_TargetWithinNonExtendedSize_UsesZeroRepetitions()
        {
            // A target already covered by the two end caps alone (2546 units, minus overlap) shouldn't
            // need any extender repeats.
            var result = MathGlyphAssemblyShaper.Shape(ParenLeftAssembly(), minConnectorOverlap: 100, targetDesignUnits: 100);
            Assert.NotNull(result);
            Assert.Equal(2, result!.Value.Parts.Count);
        }

        [Fact]
        public void Shape_EmptyPartsList_ReturnsNull()
        {
            var assembly = new MathGlyphAssembly { ItalicsCorrection = 0, Parts = [] };
            Assert.Null(MathGlyphAssemblyShaper.Shape(assembly, minConnectorOverlap: 100, targetDesignUnits: 5000));
        }

        [Fact]
        public void Shape_ExtenderTooShortToGrowPastMinConnectorOverlap_ReturnsNull()
        {
            // An extender whose own FullAdvance doesn't exceed MinConnectorOverlap can never actually
            // add any net size once its required overlap is subtracted (S_Ext,NonOverlapping <= 0).
            var assembly = new MathGlyphAssembly
            {
                ItalicsCorrection = 0,
                Parts =
                [
                    new MathGlyphPart(1, 0, 50, 500, IsExtender: false),
                    new MathGlyphPart(2, 50, 50, 50, IsExtender: true),
                ],
            };
            Assert.Null(MathGlyphAssemblyShaper.Shape(assembly, minConnectorOverlap: 100, targetDesignUnits: 5000));
        }

        [Fact]
        public void Shape_TargetCoveredByLoneNonExtenderPart_UsesSingleGlyphWithZeroOverlap()
        {
            // A single non-extender part (plus one extender that never needs to repeat, since the
            // target is already covered) exercises the AssemblyGlyphCount(rMin) <= 1 branch, where the
            // actual overlap value is irrelevant per spec and this implementation uses 0.
            var assembly = new MathGlyphAssembly
            {
                ItalicsCorrection = 0,
                Parts =
                [
                    new MathGlyphPart(1, 0, 100, 1000, IsExtender: false),
                    new MathGlyphPart(2, 100, 100, 500, IsExtender: true),
                ],
            };
            var result = MathGlyphAssemblyShaper.Shape(assembly, minConnectorOverlap: 50, targetDesignUnits: 100);
            Assert.NotNull(result);
            Assert.Single(result!.Value.Parts);
            Assert.Equal(1000, result.Value.SizeDesignUnits);
        }
    }
}
