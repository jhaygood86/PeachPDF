using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// End-to-end tests for the <c>dir="auto"</c>/&lt;bdi&gt;-default directionality pre-pass
    /// (<c>DomParser.ResolveAutoDirectionality</c>), which resolves the HTML Standard's first-strong-
    /// character algorithm before the cascade runs, so the result rides the normal UA-stylesheet
    /// <c>[dir]</c> attribute-selector rules.
    /// </summary>
    public class CascadeAutoDirectionalityTests
    {
        [Fact]
        public async Task DirAuto_WithHebrewText_ResolvesToRtl()
        {
            var html = LayoutHarness.Wrap("""<p id="el" dir="auto">שלום עולם</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Rtl, el!.Direction.Value);
        }

        [Fact]
        public async Task DirAuto_WithLatinText_ResolvesToLtr()
        {
            var html = LayoutHarness.Wrap("""<p id="el" dir="auto">hello world</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Ltr, el!.Direction.Value);
        }

        [Fact]
        public async Task DirAuto_WithNoStrongCharacters_DefaultsToLtr()
        {
            var html = LayoutHarness.Wrap("""<p id="el" dir="auto">123 !@#</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Ltr, el!.Direction.Value);
        }

        [Fact]
        public async Task DirAuto_SkipsNestedElementWithOwnDirAttribute()
        {
            // The outer paragraph's own auto-detection must not see the nested RTL text, since the
            // inner span is its own directionality scope (it carries a dir attribute of its own).
            var html = LayoutHarness.Wrap("""<p id="el" dir="auto"><span dir="rtl">שלום</span> hello</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Ltr, el!.Direction.Value);
        }

        [Fact]
        public async Task Bdi_WithNoDirAttribute_DefaultsToAutoAndIsolates()
        {
            var html = LayoutHarness.Wrap("""<bdi id="el">שלום עולם</bdi>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Rtl, el!.Direction.Value);
            Assert.Equal(UnicodeMode.Isolate, el!.UnicodeBidi.Value);
        }

        [Fact]
        public async Task DirAuto_SkipsNestedBdi_EvenWithoutItsOwnDirAttribute()
        {
            // A nested <bdi> is always its own isolated directionality scope, dir attribute or not - the
            // outer paragraph's auto-detection must not see its RTL content.
            var html = LayoutHarness.Wrap("""<p id="el" dir="auto"><bdi>שלום</bdi> hello</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Ltr, el!.Direction.Value);
        }

        [Fact]
        public async Task Bdo_WithDirRtl_OverridesAndSetsBidiOverride()
        {
            var html = LayoutHarness.Wrap("""<bdo id="el" dir="rtl">hello</bdo>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Rtl, el!.Direction.Value);
            Assert.Equal(UnicodeMode.BidirectionalOverride, el!.UnicodeBidi.Value);
        }

        [Fact]
        public async Task DirRtl_Explicit_SetsEmbedNotOverride()
        {
            // A plain dir="rtl" (not <bdo>) maps to unicode-bidi: embed per the UA stylesheet, distinct
            // from <bdo>'s bidi-override.
            var html = LayoutHarness.Wrap("""<div id="el" dir="rtl">hello</div>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DirectionMode.Rtl, el!.Direction.Value);
            Assert.Equal(UnicodeMode.Embed, el!.UnicodeBidi.Value);
        }
    }
}
