using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Column-scoped footnote areas: <c>float: footnote</c> with CSS Page Floats'
    /// <c>float-reference: column</c> routes a note to the bottom of the column its call landed in,
    /// rather than the bottom of the page.
    /// </summary>
    public class FootnoteColumnScopeIntegrationTests
    {
        [Fact]
        public async Task FloatReferenceColumn_ReachesTheBox()
        {
            // Asserts the non-initial value deliberately: a keyword-only property tested with its own
            // initial value passes by coincidence even when PropertyFactory drops the whole declaration
            // before it ever reaches CssBox.
            var (root, _) = await LayoutAsync(Wrap("<p id='p' style='float-reference: column'>Text</p>"));

            var box = FindById(root, "p");
            Assert.NotNull(box);
            Assert.Equal(FloatReference.Column, box!.FloatReference.Value);
        }

        [Theory]
        [InlineData("inline", nameof(FloatReference.Inline))]
        [InlineData("column", nameof(FloatReference.Column))]
        [InlineData("region", nameof(FloatReference.Region))]
        [InlineData("page", nameof(FloatReference.Page))]
        public async Task FloatReference_ParsesEveryKeyword(string declared, string expected)
        {
            // The enum is internal, so the theory data names the member rather than passing it - xUnit
            // needs a public parameter type.
            var (root, _) = await LayoutAsync(Wrap($"<p id='p' style='float-reference: {declared}'>Text</p>"));

            Assert.Equal(expected, FindById(root, "p")!.FloatReference.Value.ToString());
        }

        [Fact]
        public async Task FloatReference_DefaultsToInline()
        {
            var (root, _) = await LayoutAsync(Wrap("<p id='p'>Text</p>"));

            Assert.Equal(FloatReference.Inline, FindById(root, "p")!.FloatReference.Value);
        }

        [Fact]
        public async Task FloatReference_AnUnknownValue_FallsBackToTheInitialValue()
        {
            var (root, _) = await LayoutAsync(Wrap("<p id='p' style='float-reference: nonsense'>Text</p>"));

            Assert.Equal(FloatReference.Inline, FindById(root, "p")!.FloatReference.Value);
        }

        [Fact]
        public async Task Supports_FloatReferenceColumn_IsHonoredAsARealCondition()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@supports (float-reference: column) { #p { color: rgb(1, 2, 3); } }"
                + "</style></head><body style='margin:0'><p id='p'>Text</p></body></html>";

            var (root, _) = await LayoutAsync(html);

            Assert.Equal("rgb(1, 2, 3)", FindById(root, "p")!.Color);
        }
    }
}
