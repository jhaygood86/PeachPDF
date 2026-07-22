using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tests for PeachPDF's spec-correct CSS defaulting model (CSS Cascading &amp; Inheritance 4 §2.1):
    /// a single authoritative initial-value store seeds every box uniformly before the cascade runs, so
    /// an unset property resolves to its spec initial value. Also covers the two structural-display
    /// exceptions the seed must respect (CSS Display 3 §1.1 "the box tree"): the UA <c>hr { display:block }</c>
    /// rule, and anonymous boxes whose display is set by box generation, not by cascading display's initial.
    /// </summary>
    public class InitialValueDefaultingTests
    {
        // ── The single store ──────────────────────────────────────────────────────

        [Fact]
        public void InitialValueStore_IsTheSingleComprehensiveSource()
        {
            // The public InitialValues accessor and GetInitialValue read the same store — there is no
            // second, drift-prone dictionary. Sample a spread of properties and confirm both agree, and
            // that the store is the comprehensive set (well past the ~40-entry legacy seed subset).
            Assert.True(CssDefaults.InitialValues.Count > 90);

            foreach (var (name, value) in CssDefaults.InitialValues)
            {
                Assert.Equal(value, CssDefaults.GetInitialValue(name));
            }

            // A handful of spec initials, pinned so a silent store edit fails loudly.
            Assert.Equal("inline", CssDefaults.GetInitialValue("display"));
            Assert.Equal("static", CssDefaults.GetInitialValue("position"));
            Assert.Equal("black", CssDefaults.GetInitialValue("color"));
            Assert.Equal(CssConstants.Transparent, CssDefaults.GetInitialValue("background-color"));
            Assert.Equal(CssConstants.ContentBox, CssDefaults.GetInitialValue("box-sizing"));
            Assert.Equal("baseline", CssDefaults.GetInitialValue("vertical-align"));
        }

        [Fact]
        public void InitialValueStore_FontFamily_IsPlatformDefaultFont()
        {
            // The initial font-family is UA-defined (CSS Fonts 4 §2.2). PeachPDF's is the platform-resolved
            // default font — the same value an unset family falls back to at font realization — NOT the
            // literal "serif" the legacy seed store used to carry.
            Assert.Equal(CssConstants.DefaultFont, CssDefaults.GetInitialValue("font-family"));
            Assert.NotEqual("serif", CssDefaults.GetInitialValue("font-family"));
        }

        // ── Uniform seed → initial values on a bare element ───────────────────────

        [Theory]
        [InlineData("text-align", "left")]
        [InlineData("vertical-align", "baseline")]
        [InlineData("overflow", "visible")]
        [InlineData("letter-spacing", "normal")]
        [InlineData("word-break", "normal")]
        [InlineData("white-space", "normal")]
        public async Task BareElement_UnsetProperty_ResolvesToStoreInitial(string property, string expected)
        {
            // A <div> with no author/UA declaration for these properties must expose their store initial —
            // this is only true if EVERY property in the store is seeded (not a curated subset).
            var html = """<!DOCTYPE html><html><body><div id="el">text</div></body></html>""";

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.NotNull(el);
            Assert.Equal(expected, CssUtils.GetPropertyValue(el, property));
        }

        // ── Structural display: the UA hr rule ────────────────────────────────────

        [Fact]
        public async Task Hr_ResolvesToBlockDisplay_FromUaSheet()
        {
            // The seed sets display's 'inline' initial for every element, so the UA sheet must carry
            // hr { display: block } (per the HTML rendering suggestions) for <hr> to be a block box.
            var html = """<!DOCTYPE html><html><body><hr id="rule"></body></html>""";

            var root = await BuildBoxTree(html);
            var hr = FindById(root, "rule")!;

            Assert.NotNull(hr);
            Assert.Equal("block", hr.Display);
        }

        // ── Structural display: anonymous block boxes ─────────────────────────────

        [Fact]
        public async Task AnonymousBlockBox_KeepsStructuralBlockDisplay_NotSeededInline()
        {
            // When a block box has mixed block + inline children, the inline run is wrapped in an anonymous
            // block box (CSS2 §9.2.1.1). That box has no source element, so the seed must NOT overwrite its
            // box-generation-assigned block display with display's 'inline' initial (CSS Display 3 §1.1).
            var html = """
                <!DOCTYPE html><html><body>
                <div id="wrap" style="color: red">
                  loose inline text
                  <div id="child">block child</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var wrap = FindById(root, "wrap")!;
            Assert.NotNull(wrap);

            var anon = wrap.Boxes.FirstOrDefault(b => b.HtmlTag is null && b.Display == "block");
            Assert.NotNull(anon);

            // Inherited property (color) flows into the anonymous box from its parent...
            Assert.Equal("rgb(255, 0, 0)", anon!.Color);
            // ...while a non-inherited property takes its store initial, not the parent's — here border-top
            // has no author value, so it is the initial 'none', proving the anon box was defaulted, not skipped.
            Assert.Equal(CssConstants.None, CssUtils.GetPropertyValue(anon, "border-top-style"));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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
