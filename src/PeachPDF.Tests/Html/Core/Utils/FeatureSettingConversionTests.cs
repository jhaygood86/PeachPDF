using PeachDrawing.Text.Shaping;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// The CSS layer keeps its resolved font features as (tag, value) pairs and hands the shaper the engine's own
    /// <see cref="FeatureSetting"/> list. The shaper's lookup cache compares that list by reference, so the conversion has to give
    /// the same list back for the same source, or every box and every SVG run would miss the cache.
    /// </summary>
    public class FeatureSettingConversionTests
    {
        [Fact]
        public void ToFeatureSettings_KeepsTheTagsValuesAndOrder()
        {
            IReadOnlyList<(string Tag, int Value)> source = [("ss01", 1), ("cv02", 3)];

            var converted = TextShapingFeatureResolver.ToFeatureSettings(source);

            Assert.Equal([new FeatureSetting("ss01", 1), new FeatureSetting("cv02", 3)], converted);
        }

        [Fact]
        public void ToFeatureSettings_GivesTheSameListForTheSameSource()
        {
            IReadOnlyList<(string Tag, int Value)> source = [("ss01", 1)];

            Assert.Same(TextShapingFeatureResolver.ToFeatureSettings(source), TextShapingFeatureResolver.ToFeatureSettings(source));
        }

        [Fact]
        public void ToFeatureSettings_GivesTheOneSharedEmptyListForAnyEmptySource()
        {
            IReadOnlyList<(string Tag, int Value)> first = [];
            IReadOnlyList<(string Tag, int Value)> second = new List<(string, int)>();

            Assert.Same(TextShapingFeatureResolver.ToFeatureSettings(first), TextShapingFeatureResolver.ToFeatureSettings(second));
            Assert.Empty(TextShapingFeatureResolver.ToFeatureSettings(first));
        }
    }
}
