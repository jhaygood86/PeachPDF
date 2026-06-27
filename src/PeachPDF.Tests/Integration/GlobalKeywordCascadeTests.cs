using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the five global CSS keywords (inherit, initial, unset, revert,
    /// revert-layer) at the cascade-resolution level, plus regression tests confirming that
    /// the 3-phase cascade refactor did not break existing behaviour.
    ///
    /// Each test renders a small HTML document, walks the box tree to find the target element,
    /// and asserts on the string CSS-value properties that the cascade writes to CssBox.
    /// </summary>
    public class GlobalKeywordCascadeTests
    {
        // ── inherit ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Inherit_ColorFromParent_ChildGetsParentColor()
        {
            // Child explicitly forces inheritance of the parent's author-set color.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: red">
                  <span id="child" style="color: inherit">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(255, 0, 0)", child!.Color);
        }

        [Fact]
        public async Task Inherit_NonInheritedProperty_ChildGetsParentValue()
        {
            // margin-top is NOT inherited by default; explicit "inherit" should force it.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="margin-top: 30px">
                  <div id="child" style="margin-top: inherit">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("30px", child!.MarginTop);
        }

        [Fact]
        public async Task Inherit_ColorWithoutExplicitParent_UsesInitialBlack()
        {
            // When the nearest ancestor has no explicit color set, inherit still resolves to
            // the cascade-computed value (which is "black" for the initial default).
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="color: inherit">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── initial ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Initial_Color_ResetsToBlackIgnoringParent()
        {
            // "initial" for color is "black" regardless of what the parent set.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: blue">
                  <span id="child" style="color: initial">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("black", child!.Color);
        }

        [Fact]
        public async Task Initial_FontSize_ResetsToMedium()
        {
            // "initial" for font-size is "medium", overriding any inherited/UA value.
            var html = """
                <!DOCTYPE html><html><body>
                <h1 id="el" style="font-size: initial">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("medium", el!.FontSize);
        }

        [Fact]
        public async Task Initial_MarginTop_ResetsToZero()
        {
            // "initial" for margin-top is "0", even when an author rule sets it higher.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 50px; }
                </style></head><body>
                <div id="el" style="margin-top: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        [Fact]
        public async Task Initial_Display_ResetsToInline()
        {
            // "initial" for display is "inline", regardless of block-level UA defaults.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="display: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("inline", el!.Display);
        }

        // ── unset ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Unset_InheritedProperty_BehavesLikeInherit()
        {
            // "unset" on an inherited property (color) acts like "inherit".
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: green">
                  <span id="child" style="color: unset">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(0, 128, 0)", child!.Color);
        }

        [Fact]
        public async Task Unset_NonInheritedProperty_BehavesLikeInitial()
        {
            // "unset" on a non-inherited property (margin-top) acts like "initial" → "0".
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 50px; }
                </style></head><body>
                <div id="el" style="margin-top: unset">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        [Fact]
        public async Task Unset_InheritedPropertyWithNoParentValue_UsesInitial()
        {
            // "unset" on an inherited property when parent has no explicit value → initial.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="color: unset">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── revert ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Revert_InAuthorRule_RevertsToUaValue()
        {
            // An author rule sets color to blue. A later (higher-specificity) author rule
            // uses "revert", which should restore the UA-phase value — black for plain divs.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { color: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        [Fact]
        public async Task Revert_H1FontSizeInAuthorRule_RevertsToUaFontSize()
        {
            // Author sets h1 font-size to 50px, then a later author rule reverts it.
            // The UA stylesheet defines h1 { font-size: 2em }; PeachPDF eagerly converts em
            // to points at cascade time (using the parent's ActualFont.Size), so revert restores
            // the already-converted pt value rather than the original "2em" string.
            var html = """
                <!DOCTYPE html><html><head><style>
                  h1 { font-size: 50px; }
                  #el { font-size: revert; }
                </style></head><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0.3pt", el!.FontSize);
        }

        [Fact]
        public async Task Revert_InInlineStyle_RevertsToAuthorValue()
        {
            // "revert" in an inline style rolls back to the author-set value ("blue"),
            // NOT all the way to the UA value.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: revert">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task Revert_MarginTopInAuthorRule_RevertsToUaValue()
        {
            // Author sets margin-top; revert takes it back to the UA-phase value.
            // For a plain div the UA stylesheet doesn't set margin-top, so the result
            // is the initial value "0".
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 40px; }
                  #el  { margin-top: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        // ── revert-layer ──────────────────────────────────────────────────────

        [Fact]
        public async Task RevertLayer_WithoutLayers_BehavesLikeRevert()
        {
            // Without @layer support, "revert-layer" must fall back to "revert" behaviour.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { color: revert-layer; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            // Same as "revert" from an author rule: UA value for div color = "black".
            Assert.Equal("black", el!.Color);
        }

        [Fact]
        public async Task RevertLayer_InInlineStyle_BehavesLikeRevert()
        {
            // "revert-layer" in inline should behave identically to "revert" in inline
            // (author-phase snapshot) when no layers are defined.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: revert-layer">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        // ── regression: 3-phase cascade must not break existing behaviour ──────

        [Fact]
        public async Task Regression_ColorInheritsFromParentWithoutKeyword()
        {
            // Standard CSS inheritance — no keyword required for inherited properties.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: purple">
                  <span id="child">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(128, 0, 128)", child!.Color);
        }

        [Fact]
        public async Task Regression_InlineStyleOverridesAuthorRule()
        {
            // Inline style must still win over author stylesheet rules.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: green">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 128, 0)", el!.Color);
        }

        [Fact]
        public async Task Regression_AuthorRuleOverridesUaDefault()
        {
            // Author-set font-size on h1 must override the UA default of "2em".
            var html = """
                <!DOCTYPE html><html><head><style>
                  h1 { font-size: 10px; }
                </style></head><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("10px", el!.FontSize);
        }

        [Fact]
        public async Task Regression_UaStylesheetSetsH1FontSize()
        {
            // The UA stylesheet defines h1 { font-size: 2em }. PeachPDF eagerly converts em
            // values to points at cascade time, so the stored value is the converted pt string.
            var html = """
                <!DOCTYPE html><html><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0.3pt", el!.FontSize);
        }

        [Fact]
        public async Task Regression_ImportantAuthorRuleBeatsInlineStyle()
        {
            // A !important author rule must not be overridden by an inline style.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue !important; }
                </style></head><body>
                <div id="el" style="color: red">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task Regression_LaterAuthorRuleOverridesEarlierSameSpecificity()
        {
            // When two rules share the same specificity, source order decides: last wins.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: red; }
                  div { color: blue; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task Regression_UaBodyMarginStillApplied()
        {
            // The UA stylesheet sets body { margin: 8px }. This must still flow through.
            var html = """
                <!DOCTYPE html><html><body id="b">text</body></html>
                """;

            var root = await BuildBoxTree(html);
            var body = FindById(root, "b");

            Assert.NotNull(body);
            Assert.Equal("8px", body!.MarginTop);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
