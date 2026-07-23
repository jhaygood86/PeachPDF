using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #156: <c>@page { size: ... }</c> must honor font-relative lengths (<c>em</c>/<c>ex</c>/
    /// <c>rem</c>) per css-page-3 §7.1, resolving them against the root element's font — the same
    /// <c>PageLengthContext</c> the base <c>@page</c> margins use. Percentages are not a
    /// <c>&lt;length&gt;</c> for <c>size</c>, and viewport/<c>ch</c> units have no page-sheet basis;
    /// both leave the configured page size in place. Asserts against the parse-time-computed
    /// <c>HtmlContainerInt.CssPageSize</c> (true PDF points), read from the same em basis the fixture
    /// resolves, so the expectation is exact regardless of the default page font size.
    /// </summary>
    public class PageSizeUnitIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        [Fact]
        public async Task Size_EmDimensions_ResolveAgainstRootFont()
        {
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 10em 15em; }
                </style></head><body><p>content</p></body></html>
                """);

            var emPt = container.Root!.GetEmHeight(); // ppp = 1: layout units == pt
            Assert.True(emPt > 0);

            Assert.NotNull(container.CssPageSize);
            Assert.Equal(10 * emPt, container.CssPageSize!.Value.Width, 3);
            Assert.Equal(15 * emPt, container.CssPageSize!.Value.Height, 3);
        }

        [Fact]
        public async Task Size_AbsoluteDimensions_StillResolve()
        {
            // Regression guard: absolute lengths keep resolving through the new size-dimension path.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 20mm 40mm; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.NotNull(container.CssPageSize);
            Assert.Equal(20 * 72.0 / 25.4, container.CssPageSize!.Value.Width, 3);
            Assert.Equal(40 * 72.0 / 25.4, container.CssPageSize!.Value.Height, 3);
        }

        [Fact]
        public async Task Size_Percentage_IsIgnored()
        {
            // `%` is not a <length> for `size` (sheet geometry is document-global) — the declaration
            // is dropped, so no CssPageSize override is produced.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 50%; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Null(container.CssPageSize);
        }

        [Fact]
        public async Task Size_ViewportUnit_IsIgnored()
        {
            // Viewport units have no page-sheet basis; the declaration is dropped.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 40vw 60vh; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Null(container.CssPageSize);
        }

        [Theory]
        // Issue #163: a `size` value with any token that isn't a valid component is invalid *as a
        // whole* (CSS Syntax §9) and must be dropped, keeping the configured page size — not partially
        // honored by squaring the one dimension that happened to parse.
        [InlineData("nonsense 40mm")]  // an unparseable token beside a valid length
        [InlineData("40em 50%")]       // the issue's example: `%` is not a <length> for `size`
        [InlineData("40em 0")]         // a degenerate zero dimension
        [InlineData("-5em 40mm")]      // a negative dimension
        [InlineData("10em 20em 30em")] // more than two lengths
        [InlineData("a4 40em")]        // a named size can't combine with a length
        public async Task Size_InvalidToken_DropsWholeDeclaration(string size)
        {
            var container = await BuildAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { size: {{size}}; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.Null(container.CssPageSize);
        }

        [Fact]
        public async Task Size_NamedWithOrientation_StillResolves()
        {
            // Regression guard: the valid [ <page-size> || orientation ] branch is unaffected.
            var container = await BuildAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: a4 landscape; }
                </style></head><body><p>content</p></body></html>
                """);

            Assert.NotNull(container.CssPageSize);
            // A4 is 595.28 × 841.89pt portrait; `landscape` swaps to width > height.
            Assert.True(container.CssPageSize!.Value.Width > container.CssPageSize!.Value.Height);
            Assert.Equal(841.89, container.CssPageSize!.Value.Width, 2);
            Assert.Equal(595.28, container.CssPageSize!.Value.Height, 2);
        }

        private static async Task<HtmlContainerInt> BuildAsync(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(SheetW, SheetH)
            };
            await container.SetHtml(html, null);
            return container;
        }
    }
}
