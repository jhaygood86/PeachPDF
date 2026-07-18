using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Utils;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Unit tests for <see cref="FontResolvingOptions"/> - specifically that every constructor
    /// defaults <see cref="FontResolvingOptions.Stretch"/> to normal (<see cref="TtfFontDescription.DefaultStretch"/>)
    /// when the caller doesn't specify one, matching the weight-only constructors' own long-standing
    /// 700/400 Weight-from-<see cref="XFontStyle"/>.Bold defaulting.
    /// </summary>
    public class FontResolvingOptionsTests
    {
        [Fact]
        public void StyleOnlyConstructor_DefaultsStretchToNormal()
        {
            var options = new FontResolvingOptions(XFontStyle.Regular);

            Assert.Equal(TtfFontDescription.DefaultStretch, options.Stretch);
        }

        [Fact]
        public void StyleSimulationsConstructor_DefaultsStretchToNormal_AndSetsOverrideFlag()
        {
            var options = new FontResolvingOptions(XFontStyle.Bold, XStyleSimulations.BoldSimulation);

            Assert.Equal(TtfFontDescription.DefaultStretch, options.Stretch);
            Assert.True(options.OverrideStyleSimulations);
            Assert.True(options.MustSimulateBold);
            Assert.Equal(700, options.Weight);
        }

        [Fact]
        public void WeightAndStretchConstructor_UsesSpecifiedStretch()
        {
            var options = new FontResolvingOptions(XFontStyle.Regular, weight: 600, stretch: 3);

            Assert.Equal(600, options.Weight);
            Assert.Equal(3, options.Stretch);
        }
    }
}
