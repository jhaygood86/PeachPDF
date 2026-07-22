using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the <c>@property</c> at-rule (CSS Properties &amp; Values API Level 1) at the
    /// cascade-resolution level: a registered property's <c>initial-value</c> resolving through <c>var()</c>
    /// when unset, the <c>inherits</c> descriptor governing propagation to descendants, and a
    /// syntax-mismatched value falling back to the initial-value.
    /// </summary>
    public class AtPropertyCascadeIntegrationTests
    {
        [Fact]
        public async Task RegisteredInitialValue_ResolvesViaVar_WhenNeverSet()
        {
            // --c is defined only by @property (no author declaration sets it), so var(--c) must resolve to
            // its registered initial-value. This is exactly how charts.css supplies its default palette.
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: false; initial-value: blue; }
                  #el { color: var(--c); }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el.Color);
        }

        [Fact]
        public async Task InheritsFalse_ChildResolvesToInitial_NotParentValue()
        {
            // With inherits:false, a child that doesn't set --c does NOT see the parent's set value — it
            // resolves --c to the registered initial-value instead. The parent still sees its own set value.
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: false; initial-value: blue; }
                  #parent { --c: red; color: var(--c); }
                  #child { color: var(--c); }
                </style></head><body>
                <div id="parent"><span id="child">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var parent = FindById(root, "parent")!;
            var child = FindById(root, "child")!;

            Assert.Equal("rgb(255, 0, 0)", parent.Color);   // parent: its own set value
            Assert.Equal("rgb(0, 0, 255)", child.Color);    // child: initial-value (did NOT inherit red)
        }

        [Fact]
        public async Task InheritsTrue_ChildInheritsParentValue()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: true; initial-value: blue; }
                  #parent { --c: red; }
                  #child { color: var(--c); }
                </style></head><body>
                <div id="parent"><span id="child">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child")!;

            Assert.Equal("rgb(255, 0, 0)", child.Color);    // inherited the parent's set value
        }

        [Fact]
        public async Task SyntaxMismatchedValue_FallsBackToInitial()
        {
            // --c is set to a value that is not a <color>; at computed-value time that is invalid, so var(--c)
            // falls back to the registered initial-value rather than producing a broken color.
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: false; initial-value: green; }
                  #el { --c: 123notacolor; color: var(--c); }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal("rgb(0, 128, 0)", el.Color);       // green initial-value
        }

        [Fact]
        public async Task UnsetKeyword_OnInheritsFalseProperty_ResolvesToInitial_NotParent()
        {
            // The `unset` keyword acts as `inherit` for an inheriting property, else `initial`. For a registered
            // inherits:false property, `--c: unset` on the child must therefore give the initial-value, not the
            // parent's set value.
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: false; initial-value: blue; }
                  #parent { --c: red; }
                  #child { --c: unset; color: var(--c); }
                </style></head><body>
                <div id="parent"><span id="child">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child")!;

            Assert.Equal("rgb(0, 0, 255)", child.Color);    // initial-value (unset != inherit here)
        }

        [Fact]
        public async Task UnsetKeyword_OnInheritingProperty_TakesParentValue()
        {
            // For an inheriting registered property, `unset` behaves as `inherit` — the child takes the parent's
            // value. (Exercises the inherits==true branch of the unset resolution.)
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: true; initial-value: blue; }
                  #parent { --c: red; }
                  #child { --c: unset; color: var(--c); }
                </style></head><body>
                <div id="parent"><span id="child">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child")!;

            Assert.Equal("rgb(255, 0, 0)", child.Color);    // parent's value (unset == inherit here)
        }

        [Fact]
        public async Task InvalidRule_MissingInitialValueForTypedSyntax_IsIgnored()
        {
            // A typed syntax with no initial-value is an invalid @property rule (ignored). --c is therefore
            // unregistered and unset, so var(--c) is guaranteed-invalid and the fallback (orange) is used.
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "<color>"; inherits: false; }
                  #el { color: var(--c, orange); }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal("rgb(255, 165, 0)", el.Color);     // the var() fallback, not a registered initial
        }

        [Fact]
        public async Task SelfReferentialInitialValue_DoesNotRecurse_RuleIsInvalid()
        {
            // A universal-syntax initial-value that references its own property via var() is not
            // computationally independent, so the @property rule is invalid and dropped. --c is therefore
            // unregistered and unset; var(--c) is guaranteed-invalid and falls back to the literal fallback.
            // (Regression: without rejecting var() in initial-value this recursed to a StackOverflow.)
            var html = """
                <!DOCTYPE html><html><head><style>
                  @property --c { syntax: "*"; inherits: false; initial-value: var(--c); }
                  #el { color: var(--c, purple); }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal("rgb(128, 0, 128)", el.Color);     // the fallback; no crash
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
