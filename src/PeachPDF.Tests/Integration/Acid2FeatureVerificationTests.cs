using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Targeted regression coverage for mechanisms the real Acid2 test (src/PeachPDF.TestHarness/acid2.html)
    /// exercises that were already implemented before the Acid2 compliance work, but had no test coverage
    /// shaped around how Acid2 actually combines them - per this repo's convention of testing every feature
    /// a compliance fixture exercises, not just the gaps that needed fixing. See Acid2RegressionTests for
    /// the full-fixture regression test and TestHarness showcase.
    /// </summary>
    public class Acid2FeatureVerificationTests
    {
        // ─── min-height / max-height precedence (CSS2.1 §10.7) ─────────────────────
        // Acid2's ".picture p" rule: "max-height: 2mm; /* min-height overrides max-height, see 10.7 */"

        [Fact]
        public async Task MinHeightWinsOverConflictingMaxHeight()
        {
            // Absolute units throughout (no em/mm) so the expected value doesn't depend on font-size
            // resolution/unit conversion - only the min-vs-max precedence itself is under test.
            var html = Wrap("<div id='b' style='height:8px; min-height:20px; max-height:5px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "b")!;

            // min-height (20px) and max-height (5px) conflict; min-height must win per §10.7, so the
            // box's actual height must be 20px, not clamped down to 5px.
            Assert.InRange(box.ActualBottom - box.Location.Y, 19.5, 20.5);
        }

        [Fact]
        public async Task MaxHeightAppliesWhenNotConflictingWithMinHeight()
        {
            // Sanity check for the same mechanism without a conflict: max-height alone must still clamp.
            var html = Wrap("<div id='b' style='height:100px; max-height:20px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "b")!;

            Assert.InRange(box.ActualBottom - box.Location.Y, 19.5, 20.5);
        }

        // ─── percentage height/min-height against an indefinite containing block resolves to
        // "auto"/0, not against the containing block's own content-driven height (CSS2.1 §10.5/§10.7) ──
        // Acid2's ".nose": "height: 60%; max-height: 3em;" inside ".picture" (an auto-height container),
        // with the fixture's own comment: "percentages become auto (see 10.5 and 10.7) and intrinsic
        // height is more than 3em, so 3em wins". A real bug found while chasing this down: a box whose
        // OWN height is content-driven ("auto") was still being flagged internally as having a "definite"
        // height once layout resolved a number for it, so percentage heights on ITS children resolved
        // against that incidental content height instead of being treated as auto - inflating a would-be
        // 3em-tall box to hundreds of pixels.

        [Fact]
        public async Task PercentageHeight_AgainstAutoHeightContainingBlock_ResolvesToAuto_LettingMaxHeightWin()
        {
            var html = Wrap(
                "<div id='cb'>" +
                "  <div id='content' style='height:50px;'></div>" +
                "  <div id='b' style='height:60%; max-height:20px;'></div>" +
                "</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "b")!;

            // "height: 60%" must resolve to auto (zero intrinsic content) here, not against #cb's own
            // content-driven height - so max-height:20px is the only thing constraining #b's height.
            Assert.InRange(box.ActualBottom - box.Location.Y, 0, 20.5);
        }

        [Fact]
        public async Task PercentageMinHeight_AgainstAutoHeightContainingBlock_DoesNotInflateBox()
        {
            var html = Wrap(
                "<div id='cb'>" +
                "  <div id='content' style='height:50px;'></div>" +
                "  <div id='b' style='min-height:80%;'></div>" +
                "</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "b")!;

            // "min-height: 80%" against an indefinite containing block must be treated as 0 (CSS2.1
            // §10.7), not resolved against #cb's own ~50px content height - #b has no content of its
            // own, so it must collapse to (near) zero height, not 80% of anything.
            Assert.InRange(box.ActualBottom - box.Location.Y, 0, 1);
        }

        // ─── a box's own bottom margin must not fold into its own border-box height (CSS2.1 §8.3.1) ──
        // A related general bug found via the same investigation: when a box is its parent's last
        // in-flow child, code meant to collapse an EMPTY last child's margin through a borderless/
        // paddingless parent was instead folding THIS box's own bottom margin into its own ActualBottom
        // whenever it happened to be its parent's last child - even when the box had its own non-zero
        // bottom border (which CSS2.1 explicitly says blocks any such collapsing). Acid2's
        // ".picture { border: 1em solid transparent; margin: 0 0 100em 3em; }" is exactly this shape:
        // its 100em bottom margin was being added directly into its own height, inflating a ~230px box
        // to over 1100px tall.

        [Fact]
        public async Task BottomMargin_OnLastChildWithOwnBottomBorder_DoesNotInflateOwnHeight()
        {
            var html = Wrap(
                "<div id='outer'>" +
                "  <div id='b' style='border-bottom:10px solid black; margin-bottom:200px;'>" +
                "    <div id='content' style='height:20px;'></div>" +
                "  </div>" +
                "</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "b")!;

            // #b's border-box height must be its own content (20px) plus its own bottom border (10px)
            // only - the 200px margin-bottom is external spacing after #b, not part of #b's own height.
            Assert.InRange(box.ActualBottom - box.Location.Y, 29.5, 30.5);
        }

        // ─── float + negative margins + clearance sign (CSS2.1 §8.3.1/§9.5.1) ──────
        // Acid2's ".nose": float:left with a negative top/bottom margin; ".smile": clear:both with
        // (per the fixture's own comment) negative clearance.

        [Fact]
        public async Task FloatWithNegativeMargin_PullsBoxAboveNormalFlowPosition()
        {
            var html = Wrap(@"
                <div id='before' style='height:20px;'></div>
                <div id='floated' style='float:left; margin-top:-10px; width:30px; height:30px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var before = FindById(root, "before")!;
            var floated = FindById(root, "floated")!;

            // Without the negative margin the float would start at before.ActualBottom (20). The
            // negative 10px margin must pull it above that.
            Assert.True(floated.Location.Y < before.ActualBottom - 0.5,
                $"Expected floated.Y < {before.ActualBottom} (pulled up by negative margin), got {floated.Location.Y}");
        }

        [Fact]
        public async Task ClearBoth_MovesBelowFloatBottom()
        {
            var html = Wrap(@"
                <div id='floated' style='float:left; width:30px; height:50px;'></div>
                <div id='cleared' style='clear:both; height:10px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var floated = FindById(root, "floated")!;
            var cleared = FindById(root, "cleared")!;

            Assert.True(cleared.Location.Y >= floated.ActualBottom - 0.5,
                $"Expected cleared.Y >= {floated.ActualBottom} (cleared below float), got {cleared.Location.Y}");
        }

        // ─── attribute selectors exactly as Acid2 combines them ────────────────────
        // "[class~=one].first.one" and "[class~=one][class~=first] [class=second\ two][class=\"second two\"]"
        // and the intentionally-invalid "[class=second two]" (unquoted space) which must NOT match.

        [Fact]
        public async Task AttrTildeCombinedWithClassSelectors_Matches()
        {
            var html = Wrap("<div id='t' class='first one'></div>"
                + "<style>[class~=one].first.one { color: rgb(1, 2, 3); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(1, 2, 3)", box.Color);
        }

        [Fact]
        public async Task DescendantAttrTildeCombo_MatchesEscapedSpaceClassValue()
        {
            var html = Wrap("<div class='first one'><span id='t' class='second two'></span></div>"
                + "<style>[class~=one][class~=first] [class=second\\ two][class=\"second two\"] { color: rgb(4, 5, 6); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(4, 5, 6)", box.Color);
        }

        [Fact]
        public async Task UnquotedSpaceInAttrSelector_IsInvalidAndNeverMatches()
        {
            // Per CSS2.1 grammar, an attribute selector value must be an IDENT or STRING - a bare,
            // unquoted "second two" (with a space) is not a valid IDENT, so this whole selector/rule
            // must be dropped, never matched, even though the element's actual class is exactly that.
            var html = Wrap("<div id='t' class='second two' style='color: rgb(255,0,0)'></div>"
                + "<style>[class=second two] { color: rgb(4, 5, 6); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(255, 0, 0)", box.Color);
        }

        // ─── stacking context: z-index covers position:fixed regardless of source order ──
        // Acid2's ".intro { position:relative; z-index:2; }" comment: "should cover the black and red
        // bars that are fixed-positioned" - i.e. paints on top of position:fixed content even though
        // the fixed content is declared/appears later in the box tree / has its own stacking context.

        [Fact]
        public async Task PositionedZIndex_PaintsOverFixedPositionedContent()
        {
            // Painting-order coverage per CLAUDE.md: asserts the actual sequence of RGraphics calls,
            // not just final layout/geometry - a black position:fixed bar declared AFTER (later in the
            // box tree than) a white position:relative;z-index:2 box must still be painted BEFORE it
            // (i.e. underneath), matching Acid2's ".intro { z-index: 2 }" requirement.
            var html = Wrap(@"
                <div id='fixedbar' style='position:fixed; top:0; left:0; width:50px; height:50px; background:rgb(0,0,0);'></div>
                <div id='intro' style='position:relative; z-index:2; width:50px; height:50px; background:rgb(255,255,255);'></div>");

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using (var measureGraphics = new GraphicsAdapter(adapter, measure, 1.0))
                await container.PerformLayout(measureGraphics);

            var recorder = new TestRecordingGraphics();
            await container.PerformPaint(recorder);

            var drawRectCalls = recorder.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var blackIndex = drawRectCalls.FindIndex(c => c.Color == RColor.FromArgb(255, 0, 0, 0));
            var whiteIndex = drawRectCalls.FindIndex(c => c.Color == RColor.FromArgb(255, 255, 255, 255));

            Assert.True(blackIndex >= 0, "Expected the fixed-positioned black box to be drawn.");
            Assert.True(whiteIndex >= 0, "Expected the z-index:2 white box to be drawn.");
            Assert.True(whiteIndex > blackIndex,
                "Expected the z-index:2 positioned box to paint after (on top of) the fixed-positioned box.");
        }

        // ─── :link / :hover non-interactive behavior against a real anchor ─────────
        // Acid2's ".intro :link { color: blue }" / ".intro :visited { color: purple }".

        [Fact]
        public async Task LinkPseudoClass_MatchesRealAnchorWithHref()
        {
            var html = Wrap("<div class='intro'><a id='t' href='#top'>text</a></div>"
                + "<style>.intro :link { color: rgb(0, 0, 255); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var anchor = FindById(root, "t")!;
            Assert.Equal("rgb(0, 0, 255)", anchor.Color);
        }

        [Fact]
        public async Task HoverPseudoClass_NeverMatches_NonInteractiveRenderer()
        {
            var html = Wrap("<div id='t'><span>text</span></div>"
                + "<style>#t:hover { color: rgb(0, 0, 255); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.NotEqual("rgb(0, 0, 255)", box.Color);
        }

        // ─── multi-property background shorthand with a data: URI, exactly as Acid2's .forehead uses ──

        [Fact]
        public async Task BackgroundShorthand_ColorAndDataUriImage_BothApply()
        {
            const string png1x1 =
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";
            var html = Wrap("<div id='t' style='width:50px; height:50px; "
                + "background: red url(" + png1x1 + ");'></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;

            Assert.Equal("rgb(255, 0, 0)", box.BackgroundColor);
            Assert.NotNull(box.BackgroundImages);
            Assert.Single(box.BackgroundImages!);
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
