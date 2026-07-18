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

        [Fact]
        public async Task ClearBoth_NegativeClearance_DoesNotPushBelowNaturalFlowPosition()
        {
            // Acid2's ".smile { clear: both }" own comment: "clearance is negative (see 8.3.1 and
            // 9.5.1)". When the box's natural (already-flowed) position is already below the float's
            // bottom edge, clearance is effectively negative/zero - clear:both must NOT additionally
            // push the box further down past where normal flow already placed it.
            var html = Wrap(@"
                <div id='floated' style='float:left; width:30px; height:10px;'></div>
                <div id='before' style='height:200px;'></div>
                <div id='cleared' style='clear:both; height:10px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var before = FindById(root, "before")!;
            var cleared = FindById(root, "cleared")!;

            // "cleared" naturally flows to right after "before" (Y=200-ish, well past the 10px-tall
            // float) - clear:both must leave it there, not add extra clearance on top.
            Assert.InRange(cleared.Location.Y - before.ActualBottom, -0.5, 0.5);
        }

        // ─── negative margins outside the float case ────────────────────────────────
        // Acid2's ".empty div { margin: 0 2em -6em 4em }" (negative bottom margin bleeding into
        // next-sibling spacing) and ".smile div div span em strong { margin-bottom: -1em }" (should
        // have NO effect, per the fixture's own comment, since the parent has non-collapsing top+bottom
        // borders).

        [Fact]
        public async Task NegativeBottomMargin_NonFloatBlock_BleedsIntoNextSiblingSpacing()
        {
            var html = Wrap(@"
                <div id='a' style='height:20px; margin-bottom:-6px;'></div>
                <div id='b' style='height:10px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            // Without the negative margin, "b" would start right at a's own bottom edge. The -6px
            // bottom margin must pull it 6px above that.
            Assert.InRange(b.Location.Y, a.ActualBottom - 6.5, a.ActualBottom - 5.5);
        }

        [Fact]
        public async Task NegativeBottomMargin_OnLastChildOfBorderedParent_DoesNotCollapseThrough()
        {
            // A negative margin-bottom on the LAST child of a parent that has its own non-zero
            // top-and-bottom border must have no effect on the parent's height - margin collapsing
            // through a parent boundary is blocked by any border/padding on that side (CSS2.1 §8.3.1),
            // the same rule already covered for MarginBottomCollapse in the Round 1 work, but not
            // previously tested with a negative margin value specifically.
            var html = Wrap(
                "<div id='parent' style='border-top:1px solid black; border-bottom:1px solid black;'>"
                + "<div id='child' style='height:20px; margin-bottom:-10px;'></div></div>");
            var (root, _) = await BuildAndLayout(html);
            var parent = FindById(root, "parent")!;

            // Parent's content height must be exactly the child's 20px (plus its own 1px+1px borders =
            // 22px total) - the child's negative margin must not shrink it.
            Assert.InRange(parent.ActualBottom - parent.Location.Y, 21.5, 22.5);
        }

        // ─── `bottom`/`right` offset properties for position:relative and position:absolute ──
        // Regression coverage for the fix making `bottom` (previously entirely unimplemented) actually
        // move a positioned box. Acid2's ".smile div { position:relative; top:0; bottom:-1em; }".

        [Fact]
        public async Task PositionRelative_BottomOffset_MovesBoxOppositeDirection()
        {
            // top is auto, bottom is set - per CSS2.1 §9.4.3, bottom applies with its sign flipped
            // (a positive "bottom" pulls the box UP, i.e. subtracts from Y).
            var html = Wrap("<div id='t' style='position:relative; bottom:10px; width:10px; height:10px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;

            // Static-flow position is Y=8 (default body margin); "bottom:10px" must move it to Y=-2.
            Assert.InRange(box.Location.Y, -2.5, -1.5);
        }

        [Fact]
        public async Task PositionAbsolute_BottomOffset_PositionsRelativeToContainingBlockBottomEdge()
        {
            var html = Wrap(
                "<div id='cb' style='position:relative; width:100px; height:100px;'>"
                + "<div id='t' style='position:absolute; bottom:10px; width:10px; height:10px;'></div></div>");
            var (root, _) = await BuildAndLayout(html);
            var cb = FindById(root, "t")!.ParentBox!;
            var box = FindById(root, "t")!;

            // Box's bottom edge must sit 10px above the containing block's own bottom (padding) edge.
            var expectedBottom = cb.ActualBottom - 10;
            Assert.InRange(box.ActualBottom, expectedBottom - 0.5, expectedBottom + 0.5);
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

        // ─── universal selector `*` combined with a combinator, incl. the "star html" chain ──
        // Acid2's ".intro *", ".forehead *", "div.parser-container *", "* div.parser", and
        // "* html .parser" - the last of which, unlike a browser quirks-mode hack, genuinely matches
        // here (`html` is an ordinary, real, matchable type selector in this renderer's DOM).

        [Fact]
        public async Task UniversalSelector_CombinedWithDescendant_MatchesAnyDescendant()
        {
            var html = Wrap("<div class='intro'><span id='t'></span></div>"
                + "<style>.intro * { color: rgb(7, 8, 9); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(7, 8, 9)", box.Color);
        }

        [Fact]
        public async Task StarHtmlChain_MatchesRealHtmlElement_UnlikeQuirksModeHack()
        {
            // "* html .parser": universal selector, descendant-combined with a real "html" type
            // selector, descendant-combined with ".parser" - since there is a genuine "*" ancestor of
            // the real <html> root's descendant chain to satisfy (any element between the matched
            // ".parser" box and the document, ultimately including <body> as an ancestor of "html" is
            // not required - "*" just needs SOME ancestor, and <html> itself has no such ancestor, so
            // this specifically verifies matching still succeeds via <body>/<div> ancestors of the
            // inner element, not via <html> itself lacking a parent).
            var html = Wrap("<div id='wrap'><div class='parser' id='t'></div></div>"
                + "<style>* html .parser { color: rgb(10, 11, 12); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(10, 11, 12)", box.Color);
        }

        // ─── repeated class within one compound selector (".two.error.two") ────────
        // Per CSS2.1 §6.4.3, specificity counts occurrences of simple selectors in the selector text,
        // not unique classes - a literal triplication like this must still match (redundant AND) and
        // must count 3 class-selectors' worth of specificity, not 2 (a naive de-dup would break both).

        [Fact]
        public async Task RepeatedClassInCompoundSelector_StillMatches()
        {
            var html = Wrap("<div id='t' class='two error'></div>"
                + "<style>.two.error.two { color: rgb(13, 14, 15); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(13, 14, 15)", box.Color);
        }

        [Fact]
        public async Task RepeatedClassInCompoundSelector_SpecificityCountsEachOccurrence()
        {
            // ".two.error.two" (3 class-selectors' worth of specificity) must beat ".two.error" (2)
            // regardless of declaration order, proving duplicates aren't collapsed for specificity.
            var html = Wrap("<div id='t' class='two error'></div>"
                + "<style>.two.error.two { color: rgb(16, 17, 18); } .two.error { color: rgb(19, 20, 21); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(16, 17, 18)", box.Color);
        }

        // ─── chained adjacent-sibling combinator ("p + table + p") ─────────────────

        [Fact]
        public async Task ChainedAdjacentSiblingCombinator_MatchesTwoHopsBack()
        {
            var html = Wrap("<p></p><table></table><p id='t'></p>"
                + "<style>p + table + p { color: rgb(22, 23, 24); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(22, 23, 24)", box.Color);
        }

        [Fact]
        public async Task ChainedAdjacentSiblingCombinator_DoesNotMatchWithoutBothHops()
        {
            // Same shape but the second <p> isn't immediately preceded by <table> (an extra <div> sits
            // between them) - must not match.
            var html = Wrap("<p></p><table></table><div></div><p id='t' style='color: rgb(255,0,0)'></p>"
                + "<style>p + table + p { color: rgb(22, 23, 24); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(255, 0, 0)", box.Color);
        }

        // ─── deep (5-level) descendant combinator chain ─────────────────────────────
        // Acid2's ".smile div div span em strong" shape.

        [Fact]
        public async Task DeepDescendantChain_MatchesFiveLevelsDown()
        {
            var html = Wrap("<div class='smile'><div><div><span><em><strong id='t'></strong></em></span></div></div></div>"
                + "<style>.smile div div span em strong { color: rgb(25, 26, 27); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(25, 26, 27)", box.Color);
        }

        // ─── escaped selector token ("\.parser") never matches any real element ────
        // The leading "." is CSS-escaped, so the whole token is a literal type-selector name
        // (".parser"), not a class selector - no real HTML element is ever named that.

        [Fact]
        public async Task EscapedSelectorToken_NeverMatchesAnyElement()
        {
            var html = Wrap("<div id='t' class='parser' style='color: rgb(255,0,0)'></div>"
                + @"<style>\.parser { color: rgb(28, 29, 30); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(255, 0, 0)", box.Color);
        }

        // ─── same-declaration-block duplicate property: the later declaration wins ──
        // Acid2's ".parser-container div { color: maroon; border: solid; color: orange; }".

        [Fact]
        public async Task DuplicatePropertyWithinOneDeclarationBlock_LaterValueWins()
        {
            var html = Wrap("<div id='t'></div>"
                + "<style>#t { color: rgb(31, 32, 33); border: solid; color: rgb(34, 35, 36); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;
            Assert.Equal("rgb(34, 35, 36)", box.Color);
        }

        // ─── content: '' generated pseudo-element boxes survive to paint time ──────
        // Acid2's ".nose div div:before"/".nose div :after" both use "content: ''" purely to create a
        // real border/background box (no text). A prior bug in DomParser.CorrectTextBoxes treated an
        // empty-string Text exactly like meaningless inter-tag whitespace and deleted the box before
        // layout/paint ever ran.

        [Fact]
        public async Task EmptyStringContent_DirectlyAttachedPseudoElement_SurvivesAndPaints()
        {
            var html = Wrap("<div id='b'></div>"
                + "<style>#b:before { content: ''; display:block; width:20px; height:20px; "
                + "background: rgb(37,38,39); border: 2px solid rgb(40,41,42); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var div = FindById(root, "b")!;

            var beforeBox = div.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);
            Assert.NotNull(beforeBox);

            var g = new TestRecordingGraphics();
            await div.Paint(g);

            Assert.Contains(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                r => r.Color == RColor.FromArgb(37, 38, 39));
            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task EmptyStringContent_CombinatorAttachedPseudoElement_SurvivesAndPaints()
        {
            // The exact shape ".nose div :after" uses: a descendant combinator (whitespace) immediately
            // before the pseudo-element, rather than direct attachment - this is also the shape that
            // needed the SelectorConstructor fix so the box gets synthesized at all in the first place.
            var html = Wrap("<div id='outer'><div id='b'></div></div>"
                + "<style>#outer :after { content: ''; display:block; width:20px; height:20px; "
                + "background: rgb(43,44,45); border: 2px solid rgb(46,47,48); }</style>");
            var (root, _) = await BuildAndLayout(html);
            var div = FindById(root, "b")!;

            var afterBox = div.Boxes.FirstOrDefault(b => b.IsAfterPseudoElement);
            Assert.NotNull(afterBox);

            var g = new TestRecordingGraphics();
            await div.Paint(g);

            Assert.Contains(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                r => r.Color == RColor.FromArgb(43, 44, 45));
            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        // ─── font shorthand's slash-separated <font-size>/<line-height> syntax ─────
        // Acid2's "font: 2em/24px sans-serif" (#top) / "font: 2em/24px sans-serif" (.intro).

        [Fact]
        public async Task FontShorthand_SlashSeparatedLineHeight_ResolvesFontSizeAndLineHeight()
        {
            var html = Wrap("<div id='parent'><div id='t' style='font: 2em/24px sans-serif'>x</div></div>");
            var (root, _) = await BuildAndLayout(html);
            var parent = FindById(root, "parent")!;
            var box = FindById(root, "t")!;

            // "2em" font-size is eagerly converted to points against the parent's actual font size (see
            // CssBoxProperties.FontSize); "24px" line-height is a plain length and stays as declared.
            var expectedPt = 2 * parent.ActualFont.Size;
            Assert.Equal($"{expectedPt:0.0}pt", box.FontSize);
            Assert.Equal("24px", box.LineHeight);
        }

        // ─── vertical-align: bottom on an inline REPLACED element (not text) ───────
        // Acid2's "#eyes-a object { display: inline; vertical-align: bottom }" combined with
        // "#eyes-a { height: 0; line-height: 2em; text-align: right }" on the parent.

        [Fact]
        public async Task VerticalAlignBottom_OnInlineReplacedImage_AlignsToLineBottom()
        {
            // A tall inline text sibling (large font-size) establishes real line-box height that's
            // taller than the image, so there's genuine room for "bottom" to move the image into - a
            // lone small image on an otherwise-empty line has nothing taller to align against (this
            // engine's line box tracks participants' own content bounds rather than treating a bare
            // "line-height" declaration as an enforced minimum for alignment purposes - a separate,
            // narrower gap than the one under test here, not addressed by this fix).
            const string png1x1 =
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";
            var html = Wrap(
                "<div id='line'>"
                + "<span id='tall' style='font-size:40px;'>Tall</span>"
                + $"<img id='img' src='{png1x1}' style='display:inline; vertical-align:bottom; width:10px; height:10px;' />"
                + "</div>");
            var (root, _) = await BuildAndLayout(html);
            var img = FindById(root, "img")!;
            var tall = FindById(root, "tall")!;

            // The image's actual paint position is its own word's Rectangle (see
            // CssBoxImage.PaintImpCore: "var r = _imageWord.Rectangle; ...; g.DrawImage(...)"), not the
            // box's own Location - so that's what must reflect the alignment. Its bottom edge must sit
            // at the (taller) line's bottom edge, not vertically centered or aligned to its own top.
            var tallTextBox = tall.Words.Count > 0 ? tall : tall.Boxes[0];
            var tallWordBottom = tallTextBox.Words[0].Top + tallTextBox.Words[0].Height;
            var imgBottom = img.FirstWord.Top + img.FirstWord.Height;
            Assert.InRange(imgBottom, tallWordBottom - 1, tallWordBottom + 1);
        }

        // ─── margin: auto centering a fixed-width block via ordinary (non-flex) layout ──
        // Acid2's ".nose div div { width: 2em; height: 2em; margin: auto }" nested inside a float.

        [Fact]
        public async Task MarginAuto_CentersFixedWidthBlock_OrdinaryBlockLayout()
        {
            var html = Wrap("<div id='container' style='width:100px;'>"
                + "<div id='t' style='width:20px; height:20px; margin: 0 auto;'></div></div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var box = FindById(root, "t")!;

            // (100 - 20) / 2 = 40px on each side - genuine centering, not "auto resolves to 0".
            Assert.InRange(box.Location.X - container.Location.X, 39.5, 40.5);
        }

        // ─── list-style: none combined with anonymous-table-cell-wrapping ──────────
        // Acid2's "<li class='fourth-part'>" is display:table-cell-adjacent inside a display:table
        // <ul>, and separately has "list-style: none" - the marker-suppression mechanism must not get
        // confused by the anonymous table-cell wrapper the CSS2.1 §17.2.1 pass adds around it.

        [Fact]
        public async Task ListStyleNone_OnLiInsideAnonymousTableWrapping_SuppressesMarker()
        {
            var html = Wrap("<ul style='display:table'>"
                + "<li id='t' style='list-style:none;'></li>"
                + "<li style='display:table-cell'></li>"
                + "</ul>");
            var (root, _) = await BuildAndLayout(html);
            var li = FindById(root, "t")!;

            // A marker placeholder box is still synthesized (CssBoxMarker.ResolveDefaultContent just
            // leaves it contentless when list-style-type:none), so its bare presence in li.Boxes isn't
            // the right thing to assert - its actual rendered content must be empty.
            var markerBox = li.Boxes.FirstOrDefault(b => b.IsMarkerPseudoElement);
            Assert.NotNull(markerBox);
            Assert.True(string.IsNullOrEmpty(markerBox!.Text) && markerBox.Words.Count == 0,
                "expected list-style:none to leave the marker box with no rendered content");
        }

        // ─── border-spacing: 0 removes real inter-cell geometry gaps ───────────────

        [Fact]
        public async Task BorderSpacingZero_AdjacentCells_HaveNoGapBetweenThem()
        {
            var html = Wrap("<table style='border-spacing:0;'><tr>"
                + "<td id='c1' style='width:20px;'>a</td>"
                + "<td id='c2' style='width:20px;'>b</td>"
                + "</tr></table>");
            var (root, _) = await BuildAndLayout(html);
            var c1 = FindById(root, "c1")!;
            var c2 = FindById(root, "c2")!;

            Assert.InRange(c2.Location.X - c1.ActualRight, -0.5, 0.5);
        }

        // ─── float: inherit ──────────────────────────────────────────────────────
        // Acid2's ".smile div div span em { float: inherit }" (the span itself is "float: right").

        [Fact]
        public async Task FloatInherit_InheritsParentsComputedFloatValue()
        {
            var html = Wrap("<div id='parent' style='float:left;'>"
                + "<div id='t' style='float:inherit;'></div></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "t")!;

            Assert.Equal("left", box.Float);
        }

        // ─── overflow: hidden on the root <html> in a multi-page document ──────────
        // The fixture's own comment: "hides scrollbars on viewport, see 11.1.1:3" - in a paginated PDF
        // there's no scrollbar, so this must not have the unintended side effect of clipping away
        // content on any page beyond the first.

        [Fact]
        public async Task OverflowHiddenOnRootHtml_DoesNotClipLaterPages()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>html{overflow:hidden} "
                + ".section{page-break-after:always;height:900px;background:rgb(9,9,9);}</style></head><body>");
            for (var i = 0; i < 4; i++)
                sb.Append($"<div class='section' id='s{i}'>section {i}</div>");
            sb.Append("</body></html>");

            var generator = new PeachPDF.PdfGenerator();
            var document = await generator.GeneratePdf(sb.ToString(), PeachPDF.PageSize.A4, margin: 0);

            Assert.True(document.PageCount >= 4, $"expected at least 4 pages, got {document.PageCount}");
            for (var i = 0; i < document.PageCount; i++)
            {
                var content = document.Pages[i].Contents;
                Assert.True(content is { Elements.Count: > 0 }, $"page {i + 1} should have content");
            }
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
