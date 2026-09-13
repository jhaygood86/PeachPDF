using PeachPDF.CSS;
using PeachPDF.Html.Core.Paint;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// Tests for <see cref="FilterEffectResolver"/> - the paint-time resolution of a parsed
    /// <c>filter</c> function list into an opacity multiplier and a composed <see cref="ColorMatrix"/>,
    /// per the exact Filter Effects Level 1 §3 equivalences (<c>brightness()</c>/<c>contrast()</c> as
    /// <c>feComponentTransfer type="linear"</c>, <c>invert()</c> as <c>feComponentTransfer type="table"</c>).
    /// Asserted via <see cref="ColorMatrix.Apply"/> against hand-computed expected outputs rather than by
    /// re-deriving the formula in the test, so a wrong coefficient actually fails.
    /// </summary>
    public class FilterEffectResolverTests
    {
        private static FilterGrammar.FilterFunction Fn(string name, params string[] arguments) =>
            new() { Name = name, Arguments = arguments };

        private static readonly Vector4 SampleColor = new(0.2f, 0.4f, 0.6f, 1f);

        [Fact]
        public void EmptyList_NoOpacityChange_NoColorMatrix()
        {
            var resolved = FilterEffectResolver.Resolve([]);

            Assert.Equal(1.0, resolved.OpacityMultiplier);
            Assert.False(resolved.HasColorMatrix);
        }

        [Theory]
        [InlineData("grayscale")]
        [InlineData("hue-rotate")]
        [InlineData("saturate")]
        [InlineData("sepia")]
        [InlineData("blur")]
        public void DocumentedNoOpFunctions_ContributeNothing(string name)
        {
            var resolved = FilterEffectResolver.Resolve([Fn(name, "50%")]);

            Assert.Equal(1.0, resolved.OpacityMultiplier);
            Assert.False(resolved.HasColorMatrix);
        }

        [Fact]
        public void DropShadow_IsIgnoredByTheColorMatrixResolver()
        {
            // drop-shadow() adds shadow geometry, not a recoloring - handled separately by
            // FragmentPainter.PaintFilterDropShadows, never folded into this resolver's matrix/opacity.
            var resolved = FilterEffectResolver.Resolve([Fn("drop-shadow", "2px", "2px", "0", "")]);

            Assert.Equal(1.0, resolved.OpacityMultiplier);
            Assert.False(resolved.HasColorMatrix);
        }

        [Fact]
        public void Opacity_MultipliesTheOpacityMultiplier()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("opacity", "0.5")]);

            Assert.Equal(0.5, resolved.OpacityMultiplier, 5);
            Assert.False(resolved.HasColorMatrix);
        }

        [Fact]
        public void Opacity_AcceptsAPercentageArgument()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("opacity", "80%")]);

            Assert.Equal(0.8, resolved.OpacityMultiplier, 5);
        }

        [Fact]
        public void Opacity_ClampsAboveOneToOne()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("opacity", "1.5")]);

            Assert.Equal(1.0, resolved.OpacityMultiplier, 5);
        }

        [Fact]
        public void Opacity_NoArgument_DefaultsToOne()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("opacity")]);

            Assert.Equal(1.0, resolved.OpacityMultiplier, 5);
        }

        [Fact]
        public void MultipleOpacityFunctions_ComposeMultiplicatively()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("opacity", "0.5"), Fn("opacity", "0.5")]);

            Assert.Equal(0.25, resolved.OpacityMultiplier, 5);
        }

        [Fact]
        public void Brightness_IsChannelIndependent_AndScalesRgbOnly()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("brightness", "1.5")]);

            Assert.True(resolved.HasColorMatrix);
            Assert.True(resolved.ColorMatrix.IsChannelIndependent);

            var result = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(SampleColor.X * 1.5f, result.X, 4);
            Assert.Equal(SampleColor.Y * 1.5f, result.Y, 4);
            Assert.Equal(SampleColor.Z * 1.5f, result.Z, 4);
        }

        [Fact]
        public void Brightness_NoArgument_DefaultsToOne_Identity()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("brightness")]);

            var result = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(SampleColor.X, result.X, 4);
        }

        [Fact]
        public void Contrast_AppliesSlopeAndIntercept()
        {
            // contrast(0.5): slope = 0.5, intercept = 0.5 - 0.5*0.5 = 0.25.
            var resolved = FilterEffectResolver.Resolve([Fn("contrast", "0.5")]);

            var result = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(SampleColor.X * 0.5f + 0.25f, result.X, 4);
            Assert.Equal(SampleColor.Y * 0.5f + 0.25f, result.Y, 4);
            Assert.Equal(SampleColor.Z * 0.5f + 0.25f, result.Z, 4);
        }

        [Fact]
        public void Invert_FullAmount_FlipsEachChannel()
        {
            // invert(1): slope = 1 - 2*1 = -1, intercept = 1 -> output = 1 - input.
            var resolved = FilterEffectResolver.Resolve([Fn("invert", "1")]);

            var result = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(1f - SampleColor.X, result.X, 4);
            Assert.Equal(1f - SampleColor.Y, result.Y, 4);
            Assert.Equal(1f - SampleColor.Z, result.Z, 4);
        }

        [Fact]
        public void Invert_ZeroAmount_IsIdentity()
        {
            var resolved = FilterEffectResolver.Resolve([Fn("invert", "0")]);

            var result = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(SampleColor.X, result.X, 4);
            Assert.Equal(SampleColor.Y, result.Y, 4);
            Assert.Equal(SampleColor.Z, result.Z, 4);
        }

        [Fact]
        public void Invert_ClampsAboveOneToOne()
        {
            var fullyInverted = FilterEffectResolver.Resolve([Fn("invert", "1")]).ColorMatrix.Apply(SampleColor);
            var overOne = FilterEffectResolver.Resolve([Fn("invert", "2")]).ColorMatrix.Apply(SampleColor);

            Assert.Equal(fullyInverted.X, overOne.X, 4);
        }

        [Fact]
        public void BrightnessAndContrast_ComposeInAuthoredOrder()
        {
            // brightness(2) applied first, then contrast(0.5): matches manually chaining the two
            // Apply() calls, proving Compose's ordering (this-then-appliedAfterThis) is used correctly.
            var resolved = FilterEffectResolver.Resolve([Fn("brightness", "2"), Fn("contrast", "0.5")]);

            var brightened = FilterEffectResolver.Resolve([Fn("brightness", "2")]).ColorMatrix.Apply(SampleColor);
            var expected = FilterEffectResolver.Resolve([Fn("contrast", "0.5")]).ColorMatrix.Apply(brightened);

            var actual = resolved.ColorMatrix.Apply(SampleColor);
            Assert.Equal(expected.X, actual.X, 4);
            Assert.Equal(expected.Y, actual.Y, 4);
            Assert.Equal(expected.Z, actual.Z, 4);
        }

        [Fact]
        public void ColorMatrixFunctionsMixedWithNoOps_OnlyTheColorMatrixFunctionsContribute()
        {
            var withNoOps = FilterEffectResolver.Resolve([Fn("grayscale", "50%"), Fn("brightness", "1.5"), Fn("hue-rotate", "45deg")]);
            var brightnessOnly = FilterEffectResolver.Resolve([Fn("brightness", "1.5")]);

            var a = withNoOps.ColorMatrix.Apply(SampleColor);
            var b = brightnessOnly.ColorMatrix.Apply(SampleColor);
            Assert.Equal(b.X, a.X, 4);
            Assert.Equal(b.Y, a.Y, 4);
            Assert.Equal(b.Z, a.Z, 4);
        }
    }
}
