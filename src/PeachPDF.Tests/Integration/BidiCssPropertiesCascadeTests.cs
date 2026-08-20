using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end cascade tests for <c>direction</c>, <c>unicode-bidi</c>, and <c>writing-mode</c> - all
    /// three are now <see cref="CssProperty{T}"/>-typed (see <c>ComputedStyleAreas.TextArea</c>), so this
    /// exercises the real HTML/CSS pipeline rather than constructing bare <c>CssBox</c> instances, proving
    /// the typed migration didn't just compile but actually parses/cascades/inherits correctly.
    /// </summary>
    public class BidiCssPropertiesCascadeTests
    {
        [Fact]
        public async Task Direction_Rtl_InheritsFromParentToChild()
        {
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="direction: rtl">
                  <span id="child">hello</span>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(DirectionMode.Rtl, child!.Direction.Value);
        }

        [Fact]
        public async Task Direction_DefaultsToLtr()
        {
            var html = LayoutHarness.Wrap("""<span id="el">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Ltr, el!.Direction.Value);
        }

        [Theory]
        [InlineData("embed", (int)UnicodeMode.Embed)]
        [InlineData("isolate", (int)UnicodeMode.Isolate)]
        [InlineData("bidi-override", (int)UnicodeMode.BidirectionalOverride)]
        [InlineData("isolate-override", (int)UnicodeMode.IsolateOverride)]
        [InlineData("plaintext", (int)UnicodeMode.Plaintext)]
        [InlineData("normal", (int)UnicodeMode.Normal)]
        public async Task UnicodeBidi_AllValues_ParseAndCascade(string value, int expected)
        {
            var html = LayoutHarness.Wrap($"""<span id="el" style="unicode-bidi: {value}">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal((UnicodeMode)expected, el!.UnicodeBidi.Value);
        }

        [Fact]
        public async Task UnicodeBidi_DoesNotInherit()
        {
            // unicode-bidi is CSS-spec Inherited: no - a child with no unicode-bidi of its own must see
            // the property's initial value (normal), not the parent's isolate.
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="unicode-bidi: isolate">
                  <span id="child">hello</span>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(UnicodeMode.Normal, child!.UnicodeBidi.Value);
        }

        [Theory]
        [InlineData("horizontal-tb", (int)WritingMode.HorizontalTb)]
        [InlineData("vertical-rl", (int)WritingMode.VerticalRl)]
        [InlineData("vertical-lr", (int)WritingMode.VerticalLr)]
        [InlineData("sideways-rl", (int)WritingMode.SidewaysRl)]
        [InlineData("sideways-lr", (int)WritingMode.SidewaysLr)]
        public async Task WritingMode_AllValues_ParseAndCascade(string value, int expected)
        {
            var html = LayoutHarness.Wrap($"""<span id="el" style="writing-mode: {value}">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal((WritingMode)expected, el!.WritingMode.Value);
        }

        [Fact]
        public async Task WritingMode_InheritsFromParentToChild()
        {
            // writing-mode is CSS-spec Inherited: yes, unlike unicode-bidi.
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="writing-mode: vertical-rl">
                  <span id="child">hello</span>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(WritingMode.VerticalRl, child!.WritingMode.Value);
        }

        [Fact]
        public async Task WritingMode_DefaultsToHorizontalTb()
        {
            var html = LayoutHarness.Wrap("""<span id="el">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(WritingMode.HorizontalTb, el!.WritingMode.Value);
        }

        [Theory]
        [InlineData("mixed", (int)TextOrientation.Mixed)]
        [InlineData("upright", (int)TextOrientation.Upright)]
        [InlineData("sideways", (int)TextOrientation.Sideways)]
        public async Task TextOrientation_AllValues_ParseAndCascade(string value, int expected)
        {
            var html = LayoutHarness.Wrap($"""<span id="el" style="text-orientation: {value}">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal((TextOrientation)expected, el!.TextOrientation.Value);
        }

        [Fact]
        public async Task TextOrientation_InheritsFromParentToChild()
        {
            // text-orientation is CSS-spec Inherited: yes.
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="text-orientation: upright">
                  <span id="child">hello</span>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(TextOrientation.Upright, child!.TextOrientation.Value);
        }

        [Fact]
        public async Task TextOrientation_DefaultsToMixed()
        {
            var html = LayoutHarness.Wrap("""<span id="el">hello</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(TextOrientation.Mixed, el!.TextOrientation.Value);
        }
    }
}
