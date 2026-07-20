using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for a customer-reported bug: an <c>&lt;img&gt;</c> (or any inline-level child - a
    /// <c>&lt;span&gt;</c> with text behaves identically) shared a <c>display: flex</c> container with a
    /// block-level sibling, and was silently dropped - never laid out, never painted, no error. A lone
    /// image (or all-inline flex children) worked fine; wrapping the image in its own real element also
    /// worked, which was the customer's actual production workaround.
    ///
    /// Root cause, confirmed empirically (see the two fixes below) and independently against a second,
    /// fully-diagnosed report of the same bug:
    /// <list type="number">
    /// <item><c>DomParser.CorrectInlineBoxesParent</c> wraps a run of inline content in an anonymous
    /// block (<c>CssBox.CreateBlock</c>) whenever a flex container mixes inline-level and block-level
    /// children (<c>ContainsVariantBoxes</c>) - standard CSS anonymous-box generation, and also correct
    /// per CSS Flexbox §4 (contiguous inline content becomes its own anonymous flex item). The
    /// <c>&lt;img&gt;</c>/<c>&lt;span&gt;</c> becomes the sole child of this wrapper.</item>
    /// <item><see cref="CssLayoutEngineFlex"/>'s own item-collection filter (and
    /// <c>CssLayoutEngineColumns</c>'s identical one, for multicol) discards anonymous
    /// (<c>HtmlTag == null</c>) boxes considered "space or empty", via <see cref="CssBox.IsSpaceOrEmpty"/>
    /// - correct in intent (drop genuinely whitespace-only anonymous runs between real items), wrong in
    /// implementation: that property only ever inspected the box's own <see cref="CssBox.Words"/>, never
    /// recursing into <see cref="CssBox.Boxes"/>. The wrapper's own <c>Words</c> is empty (its content
    /// lives on its child), so it always read as "empty" - silently dropped from <c>rawItems</c>, never
    /// measured, positioned, or painted.</item>
    /// <item>Separately, even after fixing the recursion, an image word never overrides the base
    /// <see cref="CssRect.IsSpaces"/> default (<c>true</c>) - meant for a genuinely-unmeasured word, not
    /// a replaced element with real visible content - so an image-only wrapper would still have read as
    /// empty without also overriding it on <see cref="CssRectImage"/>.</item>
    /// </list>
    ///
    /// Per CLAUDE.md's testing conventions, the paint-level tests assert an actual recorded
    /// <c>DrawImage</c>/<c>DrawString</c> call (via <see cref="TestRecordingGraphics"/>), not just that
    /// layout completes - a layout-only check would have missed this: for content nested inside a
    /// dropped anonymous wrapper (not itself the direct flex item), the *wrapper's* geometry is what
    /// flex assigns, and properties like <c>ActualBoxSizingWidth</c> read directly off the nested
    /// replaced element are not the property flex layout actually resolves - only the paint call proves
    /// the content really reached the page.
    /// </summary>
    public class FlexReplacedElementIntegrationTests
    {
        // A real 1x1 yellow-pixel PNG data URI (also used by BackgroundAttachmentFixedIntegrationTests/
        // the Acid2 fixture). Tests that care about a specific rendered size set explicit width/height
        // HTML attributes, which win over the (here, trivial 1x1) decoded intrinsic size.
        private const string PngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        [Fact]
        public async Task Image_WithBlockSibling_InFlexRow_ActuallyPaints()
        {
            // The customer's exact repro shape: a sized logo image alongside a block-level title/nav -
            // exactly the "mixed inline + block flex children" shape that triggered the anonymous-wrapper
            // drop.
            var html = Wrap($"""
                <div style='display:flex; align-items:center;'>
                    <img id='i' src='{PngDataUri}' width='80' height='57'>
                    <div class='title'>Account Statement</div>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);

            var g = new TestRecordingGraphics();
            await root.Paint(g);

            var call = Assert.Single(g.DrawImageCalls);
            // <img width>/<height> attributes are px (unitless), now laid out at spec-correct
            // 1px = 0.75pt: 80px -> 60pt, 57px -> 42.75pt.
            Assert.Equal(60, call.DestRect.Width, 1);
            Assert.Equal(42.75, call.DestRect.Height, 1);
        }

        [Fact]
        public async Task Span_WithBlockSibling_InFlexRow_ActuallyPaints()
        {
            // Not img-specific: a <span> with real text mixed with a block sibling is dropped the same
            // way, since the anonymous wrapper's own Words is empty regardless of what kind of inline
            // content it wraps.
            var html = Wrap("""
                <div style='display:flex'>
                    <span id='s'>inline span text</span>
                    <div class='title'>Account Statement</div>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);

            var g = new TestRecordingGraphics();
            await root.Paint(g);

            Assert.Contains(g.DrawStringCalls, c => c.Text.Contains("inline"));
        }

        [Fact]
        public async Task WhitespaceOnlyRun_BetweenFlexItems_IsStillDiscarded()
        {
            // Guards the filter's own original purpose: the whitespace-only text run the HTML source's
            // own indentation/newlines produce between the two <div> flex items must not become a
            // phantom third flex item taking up its own space in the main-axis layout - if it did, 'b'
            // would land further right than flush against 'a's own width.
            var html = Wrap("""
                <div style='display:flex'>
                    <div id='a' style='width:50px;height:20px;'></div>
                    <div id='b' style='width:50px;height:20px;'></div>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.Location.X + a.ActualBoxSizingWidth, b.Location.X, 1);
        }

        [Fact]
        public async Task AllInlineFlexChildren_UnchangedByTheFix()
        {
            // All-inline flex children (no block sibling) never go through CorrectInlineBoxesParent's
            // wrapping in the first place - must keep working exactly as before.
            var html = Wrap($"""
                <div style='display:flex'>
                    <img id='i' src='{PngDataUri}' width='80' height='57'>
                    <span id='s'>label</span>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);

            var g = new TestRecordingGraphics();
            await root.Paint(g);

            Assert.Single(g.DrawImageCalls);
            Assert.Contains(g.DrawStringCalls, c => c.Text.Contains("label"));
        }

        [Fact]
        public async Task Image_InFlexRow_WithFlexGrow_ResizesAndStillPaints()
        {
            var html = Wrap($"""
                <div style='display:flex; width:300px;'>
                    <img id='i' src='{PngDataUri}' width='80' height='57' style='flex-grow:1'>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);

            var g = new TestRecordingGraphics();
            await root.Paint(g);

            var call = Assert.Single(g.DrawImageCalls);
            Assert.True(call.DestRect.Width > 0 && call.DestRect.Height > 0,
                $"Grown image dest rect should be non-degenerate, but was {call.DestRect}");
        }

        [Fact]
        public async Task SvgInline_WithBlockSibling_InFlexRow_GetsIntrinsicSize()
        {
            // With a block sibling present, the svg itself is nested inside the anonymous wrapper flex
            // assigns geometry to (see this class's own doc comment) - ActualBoxSizingWidth read
            // directly off the nested svg isn't the property flex layout actually resolves, so this
            // asserts the same way the img/span tests above do: an actual recorded paint call.
            var html = Wrap("""
                <div style='display:flex'>
                    <svg id='s' width='40' height='30'><rect width='40' height='30' fill='red'/></svg>
                    <div class='title'>Title</div>
                </div>
                """);
            var (root, _) = await BuildAndLayout(html);

            var g = new TestRecordingGraphics();
            await root.Paint(g);

            var red = PeachPDF.Html.Adapters.Entities.RColor.FromArgb(255, 0, 0);
            Assert.Contains(g.Log, entry => entry switch
            {
                TestRecordingGraphics.DrawRectCall r => r.Color == red,
                TestRecordingGraphics.DrawPathCall p => p.Color == red,
                TestRecordingGraphics.DrawPolygonCall poly => poly.Color == red,
                _ => false
            });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
