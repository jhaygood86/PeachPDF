using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for CSS custom properties (--foo) and the var() function at the
    /// cascade-resolution level: inheritance, fallback, cycle detection, revert/revert-layer,
    /// and interaction with the existing 3-phase cascade and !important machinery.
    /// </summary>
    public class CustomPropertiesIntegrationTests
    {
        [Fact]
        public async Task Var_UsedOnDescendant_AppliesInheritedCustomPropertyValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="--main-color: red">
                  <span id="child" style="color: var(--main-color)">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            // Custom properties bypass the typed ColorProperty converter (var() can't be resolved until
            // cascade time), so the value round-trips as literal text rather than being normalized to rgb().
            Assert.Equal("red", child!.Color);
        }

        [Fact]
        public async Task CustomProperty_InheritsThroughMultipleLevelsWithoutRedeclaration()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  .theme { --brand-color: green; }
                </style></head><body>
                <div id="parent" class="theme">
                  <div id="mid">
                    <span id="child" style="color: var(--brand-color)">text</span>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("green", child!.Color);
        }

        [Fact]
        public async Task CustomProperty_ChildOverride_DoesNotMutateParentOrSibling()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="--x: red">
                  <div id="childA" style="--x: blue; color: var(--x)">a</div>
                  <span id="childB" style="color: var(--x)">b</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var childA = FindById(root, "childA");
            var childB = FindById(root, "childB");

            Assert.NotNull(childA);
            Assert.NotNull(childB);
            Assert.Equal("blue", childA!.Color);
            Assert.Equal("red", childB!.Color);
        }

        [Fact]
        public async Task Var_WithFallback_UsedWhenCustomPropertyUndefined()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="margin-top: var(--undefined-spacing, 10px)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("10px", el!.MarginTop);
        }

        [Fact]
        public async Task Var_WithoutFallback_UndefinedInheritedProperty_FallsBackLikeInherit()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: purple">
                  <span id="child" style="color: var(--undefined)">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(128, 0, 128)", child!.Color);
        }

        [Fact]
        public async Task Var_WithoutFallback_UndefinedNonInheritedProperty_FallsBackLikeInitial()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="margin-top: 40px">
                  <div id="child" style="margin-top: var(--undefined)">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("0", child!.MarginTop);
        }

        [Fact]
        public async Task Var_MultipleOccurrencesInOneShorthandValue_ShorthandExpansionStillWorks()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--a: 10px; --b: 20px; margin: var(--a) var(--b)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("10px", el!.MarginTop);
            Assert.Equal("10px", el.MarginBottom);
            Assert.Equal("20px", el.MarginLeft);
            Assert.Equal("20px", el.MarginRight);
        }

        [Fact]
        public async Task Var_NestedFallback_ResolvesInnerReferenceFirst()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--b: green; --c: var(--a, var(--b, red)); color: var(--c)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("green", el!.Color);
        }

        [Fact]
        public async Task Var_FallbackContainingFunctionWithCommas_SplitsOnlyAtTopLevel()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--x: var(--undefined, rgb(1, 2, 3)); background-color: var(--x)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Contains("rgb(1, 2, 3)", el!.BackgroundColor);
        }

        [Fact]
        public async Task Content_WithLiteralVarTextInQuotes_IsNotMisinterpretedAsAReference()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style='content: "var(--not-a-reference)"'>text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("\"var(--not-a-reference)\"", el!.Content);
        }

        [Fact]
        public async Task MultiHopCyclicCustomProperties_ResolveAsGuaranteedInvalid_WithoutHanging()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: purple">
                  <div id="el" style="--a: var(--b); --b: var(--c); --c: var(--a); color: var(--a)">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            // color is inherited; a cyclic (guaranteed-invalid) var() reference falls back like "unset" would.
            Assert.Equal("rgb(128, 0, 128)", el!.Color);
        }

        [Fact]
        public async Task SelfReferencingCustomProperty_IsGuaranteedInvalid_EvenWithOwnFallback()
        {
            // Per the CSS Custom Properties spec, writing var(--self, ...) inside --self's own
            // definition is a self-reference regardless of the fallback: --self must become
            // permanently guaranteed-invalid, matching real browsers (Chrome, Firefox) — the fallback
            // does NOT rescue it. A consumer with no fallback of its own therefore also gets nothing.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: purple">
                  <div id="el" style="--self: var(--self, orange); color: var(--self)">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(128, 0, 128)", el!.Color);
        }

        [Fact]
        public async Task OneDirectionalChain_IsNotTreatedAsCyclic_ResolvesCorrectly()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--a: var(--b); --b: blue; color: var(--a)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("blue", el!.Color);
        }

        [Fact]
        public async Task Revert_CustomPropertyInInlineStyle_RestoresAuthorPhaseValue()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { --theme-color: blue; }
                </style></head><body>
                <div id="el" style="--theme-color: revert; color: var(--theme-color)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("blue", el!.Color);
        }

        [Fact]
        public async Task RevertLayer_CustomPropertyInInlineStyle_BehavesLikeRevert()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { --theme-color: blue; }
                </style></head><body>
                <div id="el" style="--theme-color: revert-layer; color: var(--theme-color)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("blue", el!.Color);
        }

        [Fact]
        public async Task Inherit_CustomPropertyDeclaration_PullsParentValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="--x: red">
                  <span id="child" style="--x: inherit; color: var(--x)">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("red", child!.Color);
        }

        [Fact]
        public async Task Initial_CustomPropertyDeclaration_ClearsInheritedValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="--x: red">
                  <span id="child" style="--x: initial; color: var(--x, blue)">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("blue", child!.Color);
        }

        [Fact]
        public async Task CustomProperty_NamesAreCaseSensitive()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--Foo: blue; --foo: red; color: var(--Foo); background-color: var(--foo)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("blue", el!.Color);
            Assert.Equal("red", el.BackgroundColor);
        }

        [Fact]
        public async Task DeferredVarProperty_OverriddenByLaterPlainValue_UsesPlainValue()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { --x: green; color: var(--x); }
                  #el { color: blue; }
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
        public async Task DeferredVarProperty_OverriddenByLaterVarValue_UsesLatestValue()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { --a: green; color: var(--a); }
                  #el { --b: blue; color: var(--b); }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("blue", el!.Color);
        }

        [Fact]
        public async Task CurrentColor_SourcedThroughCustomProperty_ResolvesCorrectly()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="color: red; --x: currentcolor; background-color: var(--x)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(255, 0, 0)", el!.BackgroundColor);
        }

        [Fact]
        public async Task Var_UsedInBackgroundShorthand_AppliesResolvedColor()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--bg: #8e44ad; background: var(--bg)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(142, 68, 173)", el!.BackgroundColor);
        }

        [Fact]
        public async Task Var_WithFallbackInBackgroundShorthand_AppliesFallbackColor()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="background: var(--undefined-bg, #8e44ad)">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(142, 68, 173)", el!.BackgroundColor);
        }

        [Fact]
        public async Task Var_WithNestedFallbackChainInBackgroundShorthand_AppliesFinalFallbackColor()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="background: var(--undefined-a, var(--undefined-b, #d35400))">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(211, 84, 0)", el!.BackgroundColor);
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
