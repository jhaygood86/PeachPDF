using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// HTML matches element and attribute names <b>ASCII</b> case-insensitively, and a class name is
    /// matched ASCII case-insensitively at most. <c>InvariantCultureIgnoreCase</c> applies full
    /// Unicode case folding instead, under which U+212A KELVIN SIGN equals ASCII <c>k</c> — so a
    /// class of <c>K</c> matched the selector <c>.k</c>, and a <c>K</c> element matched <c>k</c>.
    /// <para>
    /// Ordinal is both the rule the surrounding comments already state and the cheaper comparison:
    /// the selector matcher runs it for one name against every candidate rule for every box, and a
    /// culture-aware comparison routes each through ICU.
    /// </para>
    /// </summary>
    public class AsciiNameMatchingTests
    {
        private const string Kelvin = "K";   // KELVIN SIGN — folds to ASCII 'k' under Unicode rules

        [Fact]
        public async Task AKelvinSignClassDoesNotMatchAnAsciiKSelector()
        {
            var (root, _) = await LayoutAsync(
                "<!DOCTYPE html><html><head><style>.k { color: rgb(255,0,0) }</style></head>"
                + $"<body style='margin:0'><div id='t' class='{Kelvin}'>x</div></body></html>");

            Assert.NotEqual("rgb(255, 0, 0)", FindById(root, "t")!.Color);
        }

        [Fact]
        public async Task AnAsciiClassStillMatchesCaseInsensitively()
        {
            // The contrast case: ASCII case-insensitivity is the rule and must survive. Both an exact
            // and a differently-cased ASCII class still match.
            var (root, _) = await LayoutAsync(
                "<!DOCTYPE html><html><head><style>.k { color: rgb(255,0,0) }</style></head>"
                + "<body style='margin:0'><div id='lower' class='k'>x</div>"
                + "<div id='upper' class='K'>x</div></body></html>");

            Assert.Equal("rgb(255, 0, 0)", FindById(root, "lower")!.Color);
            Assert.Equal("rgb(255, 0, 0)", FindById(root, "upper")!.Color);
        }

        [Fact]
        public async Task ImpliedTagClosingMatchesTagNamesAsciiCaseInsensitively()
        {
            // DomUtils.FindParent drives the parser's implied-tag-closing rules (a <table> closes an
            // open <p>). It compared with CurrentCultureIgnoreCase, which is worse than the invariant
            // comparison this PR replaces elsewhere: full Unicode folding AND locale-dependent, so the
            // same document could parse differently on two machines.
            var (root, _) = await LayoutAsync(
                "<!DOCTYPE html><html><head><style>p { color: rgb(0,0,255) }</style></head>"
                + "<body style='margin:0'><P id='t'>x</P></body></html>");

            Assert.Equal("rgb(0, 0, 255)", FindById(root, "t")!.Color);
        }

        [Fact]
        public async Task AStylesheetLinkRelationMatchesAsciiCaseInsensitively()
        {
            // CascadeParseStyles matched the `style` tag name and the `stylesheet` link relation the
            // same locale-dependent way. An uppercase STYLE element must still be honoured.
            var (root, _) = await LayoutAsync(
                "<!DOCTYPE html><html><head><STYLE>#t { color: rgb(0,128,0) }</STYLE></head>"
                + "<body style='margin:0'><div id='t'>x</div></body></html>");

            Assert.Equal("rgb(0, 128, 0)", FindById(root, "t")!.Color);
        }

        [Fact]
        public async Task AnAsciiTagStillMatchesCaseInsensitively()
        {
            var (root, _) = await LayoutAsync(
                "<!DOCTYPE html><html><head><style>div { color: rgb(0,128,0) }</style></head>"
                + "<body style='margin:0'><DIV id='t'>x</DIV></body></html>");

            Assert.Equal("rgb(0, 128, 0)", FindById(root, "t")!.Color);
        }
    }
}
