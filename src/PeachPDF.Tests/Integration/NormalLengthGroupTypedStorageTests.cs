using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for the "normal | length" group (<c>word-spacing</c>,
    /// <c>letter-spacing</c>, <c>row-gap</c>, <c>column-gap</c>) - each now backed by
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;NormalKeyword, LengthOrCalc&gt;&gt;</c>, the second grammar
    /// shape through the keyword-or-value mechanism after <c>z-index</c>/<c>column-count</c>'s
    /// integer-or-auto.
    /// Complements the existing layout-geometry tests (<c>LetterWordSpacingLayoutIntegrationTests</c>,
    /// <c>FlexboxIntegrationTests</c>, <c>GridLayoutIntegrationTests</c>, <c>MulticolLayoutIntegrationTests</c>),
    /// which exercise these properties indirectly through their effect on measured/laid-out positions
    /// rather than asserting the stored <c>.Value</c> directly.
    /// </summary>
    public class NormalLengthGroupTypedStorageTests
    {
        [Fact]
        public async Task WordSpacing_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='word-spacing:NORMAL'></div>");
            Assert.True(box.WordSpacing.Value.IsKeyword);
        }

        [Fact]
        public async Task WordSpacing_LengthValue_StoresParsedLength()
        {
            var box = await GetBoxAsync("<div id='el' style='word-spacing:4px'></div>");
            Assert.True(box.WordSpacing.Value is { IsValue: true, Value: { Length: { Value: 4f } } });
        }

        [Fact]
        public async Task LetterSpacing_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='letter-spacing:Normal'></div>");
            Assert.True(box.LetterSpacing.Value.IsKeyword);
        }

        [Fact]
        public async Task LetterSpacing_EmValue_IsEagerlyResolvedToPoints()
        {
            // The no-ems valueComputation must still run - a stored "1em" (unresolved) would silently
            // compound if a descendant with a different font-size ever read it, since TextArea inherits
            // by reference (see CssBox.NoEms's own doc comment).
            var box = await GetBoxAsync("<div id='el' style='font-size:20px; letter-spacing:1em'></div>");
            Assert.True(box.LetterSpacing.Value is { IsValue: true, Value: { IsCalc: false } });
            Assert.Equal(Length.Unit.Pt, box.LetterSpacing.Value.Value!.Value.Length!.Value.Type);
        }

        [Fact]
        public async Task RowGap_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:grid; row-gap:NORMAL'></div>");
            Assert.True(box.FlexRowGap.Value.IsKeyword);
        }

        [Fact]
        public async Task RowGap_LengthValue_StoresParsedLength()
        {
            var box = await GetBoxAsync("<div id='el' style='display:grid; row-gap:8px'></div>");
            Assert.True(box.FlexRowGap.Value is { IsValue: true, Value: { Length: { Value: 8f } } });
        }

        [Fact]
        public async Task ColumnGap_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:grid; column-gap:Normal'></div>");
            Assert.True(box.FlexColumnGap.Value.IsKeyword);
        }

        [Fact]
        public async Task ColumnGap_LengthValue_StoresParsedLength()
        {
            var box = await GetBoxAsync("<div id='el' style='display:grid; column-gap:6px'></div>");
            Assert.True(box.FlexColumnGap.Value is { IsValue: true, Value: { Length: { Value: 6f } } });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<PeachPDF.Html.Core.Dom.CssBox> GetBoxAsync(string bodyHtml)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(bodyHtml));
            var box = LayoutHarness.FindById(root, "el");
            Assert.NotNull(box);
            return box!;
        }
    }
}
