using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Full-fixture regression coverage for the real Acid2 test (http://acid2.acidtests.org/), loaded
    /// byte-identical from <c>TestSupport/acid2.html</c> (mirrored from
    /// <c>src/PeachPDF.TestHarness/acid2.html</c>, which is also the TestHarness showcase source) rather
    /// than re-typed as a string, so this test tracks the genuine fixture. See
    /// <see cref="Acid2FeatureVerificationTests"/> for coverage of the individual mechanisms Acid2
    /// exercises in isolation.
    ///
    /// This is deliberately a smoke test plus a handful of landmark box-geometry sanity checks, not a
    /// pixel-exact "matches the Acid2 reference rendering" assertion - a paginated PDF renderer has no
    /// equivalent of the fixed-size, non-scrolling browser viewport Acid2 assumes (its huge `100em`
    /// margins on `#top`/`.picture` are meant to be scrolled/clipped out of view via `overflow: hidden`
    /// on `html`, not paginated), so some structural difference from an on-screen browser rendering is
    /// inherent to the format, not a bug. The geometry bounds below exist specifically to catch the
    /// class of regression that visual inspection during this test's development actually found: a
    /// content-driven-height box being treated as having a "definite" height for its descendants'
    /// percentage-height resolution (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngine.ApplyHeight"/>),
    /// and a box's own bottom margin folding into its own border-box height when it doubles as its
    /// parent's last child (<see cref="CssBox.MarginBottomCollapse"/>) - both of which inflated
    /// `.picture` from roughly 230px tall to over 1100px tall, and are exactly the kind of thing a
    /// PDF-content-stream-substring test would never catch (see this repo's testing conventions).
    /// </summary>
    public class Acid2RegressionTests
    {
        private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "TestSupport", "acid2.html");

        [Fact]
        public async Task FullFixture_GeneratesPdf_WithContentOnEveryPage()
        {
            var html = File.ReadAllText(FixturePath);
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 0);

            Assert.True(document.PageCount > 0, "the Acid2 fixture should generate at least one page");

            for (var i = 0; i < document.PageCount; i++)
            {
                Assert.True(PageHasContent(document.Pages[i]), $"page {i + 1} of {document.PageCount} should have content");
            }
        }

        [Fact]
        public async Task FullFixture_DoesNotBalloonIntoExcessivePageCount()
        {
            // Regression guard for the MarginBottomCollapse/IsHeightCalculated bugs described in the
            // class doc comment: both inflated ".picture" (and, transitively, total document height) by
            // roughly 900px. A recurrence of either would push this well past a handful of pages again.
            var html = File.ReadAllText(FixturePath);
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 0);

            Assert.True(document.PageCount <= 3,
                $"expected a small handful of pages (the fixture's own huge, intentionally-off-screen " +
                $"100em margins account for some unavoidable blank space, though HtmlContainerInt." +
                $"GetPaginationSlots now skips wholly content-empty pages per CSS Paged Media Level 3 " +
                $"§3.2), got {document.PageCount}");
        }

        [Fact]
        public async Task FullFixture_MatchesPrinceXmlPageCount()
        {
            // Prince XML (a reference-quality commercial CSS->PDF renderer this project has compared
            // against before - see the "Dictionary Parity with Prince XML" work) renders this exact
            // fixture in exactly 2 pages: ".intro" on its own page, and "#top"/".picture"'s real face
            // content on a second page, with the huge intentional "100em" margin gaps before/after
            // (see the class doc comment) never materializing as their own content-empty pages at all
            // (HtmlContainerInt.GetPaginationSlots, per CSS Paged Media Level 3 §3.2). If this
            // regresses, either that skip mechanism broke or the real face/intro content grew enough
            // to spill onto an extra page.
            var html = File.ReadAllText(FixturePath);
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 0);

            Assert.Equal(2, document.PageCount);
        }

        [Fact]
        public async Task FixedBars_RepeatOntoPage2_KnownResidualNotCoveredByIntro()
        {
            // Documents a known, accepted residual rather than a silent gap: the fixture's own
            // ".intro { z-index: 2 }" is only meant to cover ".picture p"/".picture p + p" (two
            // position:fixed bars) on the page it itself lands on (page 1) - it's normal-flow, so it
            // can't repeat onto page 2 the way position:fixed content correctly does (matching real CSS
            // Paged Media running-header semantics; see FixedPositionPaginationIntegrationTests). Since
            // the real fixture's ".intro" and "#top"/".picture" land on two DIFFERENT pages, the fixed
            // bars still bleed onto page 2, technically uncovered - this part is inherent to a
            // non-scrolling paginated renderer (there's no single viewport for ".intro" to visually sit
            // in front of both landing spots at once) and isn't something a pagination fix can close.
            // What margin truncation at unforced breaks (CssBox.PerformLayoutImp's Static/Relative
            // branch) DID fix is a separate, larger problem this test doesn't cover: before that fix,
            // "#top"/".picture" landed ~350-450pt into page 2 (nowhere near these bars) because their
            // preceding 100em margins paginated through as literal blank space instead of being
            // discarded at the page break - see FixedBars_AndFaceContent_LandOnSamePageNearItsTop below
            // for that. Detected generically here (no hardcoded pixel geometry): a solid-fill rect
            // command that is byte-identical between page 1 and page 2 can only be explained by
            // position:fixed content, which alone ignores scroll offset and repeats at the exact same
            // coordinates on every page (CssBox.cs's "IsFixed" branches) - regular in-flow content's
            // coordinates always differ page to page.
            var html = File.ReadAllText(FixturePath);
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(0);
            var document = await generator.GeneratePdf(html, config);

            Assert.Equal(2, document.PageCount);

            var page1Rects = GetSolidFillRects(document.Pages[0]);
            var page2Rects = GetSolidFillRects(document.Pages[1]);

            Assert.NotEmpty(page1Rects);
            Assert.NotEmpty(page2Rects);
            Assert.True(page1Rects.Intersect(page2Rects).Any(),
                "expected at least one solid-fill rect (the fixed bars) to be byte-identical between " +
                "page 1 and page 2, confirming the documented residual is still present");
        }

        [Fact]
        public async Task FixedBars_AndFaceContent_LandOnSamePageNearItsTop()
        {
            // CSS Fragmentation Level 3 §5.2: "When an unforced break occurs before or after a
            // block-level box, any margins adjoining the break are truncated to zero." Prince XML (which
            // this was verified against directly - see its own published Acid2 PDF sample) applies this:
            // "#top"'s 100em margin-top doesn't paginate through as blank pages, it's discarded at the
            // break, so "#top"/".picture" land flush near the top of whichever page they end up on -
            // right where the two position:fixed bars (".picture p"/".picture p.bad", always ~9-12em
            // from the top of every page, see the test above) already are, instead of ~350-450pt further
            // down the same page as before this fix.
            var (root, container) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var top = FindById(root, "top")!;
            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            var offsetWithinPage = top.Location.Y % pageHeight;

            Assert.True(offsetWithinPage <= marginTop + 20,
                $"#top should land within ~20pt of the top of its page (offset={offsetWithinPage}, " +
                $"marginTop={marginTop}), not deep into a mostly-blank page");
        }

        [Fact]
        public async Task Picture_HasCompactHeight_NotInflatedByItsOwnBottomMargin()
        {
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var picture = FindByClass(root, "picture")!;

            var height = picture.ActualBottom - picture.Location.Y;

            // The real face content (forehead/eyes/nose/smile/chin/parser/table/image-height-test lines)
            // is on the order of 200-300px tall. ".picture"'s own 100em (1200px) bottom margin must NOT
            // be folded into this - if it is, height balloons past 1000px.
            Assert.InRange(height, 50, 400);
        }

        [Fact]
        public async Task Nose_PercentageHeightResolvesToAuto_CappedByMaxHeight()
        {
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;

            var height = nose.ActualBottom - nose.Location.Y;

            // ".nose { min-height: 80%; height: 60%; max-height: 3em; }" inside the auto-height
            // ".picture" - per the fixture's own comment, both percentages resolve to auto/0 (CSS2.1
            // §10.5/§10.7), so max-height:3em (36px at the fixture's 12px base font) is what actually
            // constrains it, not hundreds of pixels resolved against ".picture"'s content height.
            Assert.InRange(height, 0, 40);
        }

        [Fact]
        public async Task LandmarkElements_AreFoundAndPositionedInDocumentOrder()
        {
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));

            var intro = FindByClass(root, "intro");
            var top = FindById(root, "top");
            var picture = FindByClass(root, "picture");
            var forehead = FindByClass(root, "forehead");
            var eyes = FindByClass(root, "eyes");

            Assert.NotNull(intro);
            Assert.NotNull(top);
            Assert.NotNull(picture);
            Assert.NotNull(forehead);
            Assert.NotNull(eyes);

            // Document order: intro, then #top, then .picture (and its descendants).
            Assert.True(intro!.Location.Y <= top!.Location.Y);
            Assert.True(top.Location.Y <= picture!.Location.Y);
            Assert.True(picture.Location.Y <= forehead!.Location.Y);
            Assert.True(picture.Location.Y <= eyes!.Location.Y);
        }

        [Fact]
        public async Task Nose_GeneratedPseudoElementBoxes_SurviveWithRealGeometry()
        {
            // Regression for two Round 2 bugs found via visual inspection of this exact fixture:
            // 1) ".nose div :after" (a pseudo-element preceded by a descendant combinator - note the
            //    space) previously never got synthesized as a real box at all (SelectorConstructor only
            //    wrapped a directly-attached trailing pseudo-element into a matchable CompoundSelector).
            // 2) both this rule and ".nose div div:before" use "content: ''", which
            //    DomParser.CorrectTextBoxes previously deleted before layout/paint ever ran (it treated
            //    an empty-string Text exactly like meaningless inter-tag whitespace).
            // Together these silently dropped the nose's border/background geometry entirely.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;

            var pseudoBoxes = new List<CssBox>();
            void CollectPseudoElements(CssBox box)
            {
                if (box.IsBeforePseudoElement || box.IsAfterPseudoElement) pseudoBoxes.Add(box);
                foreach (var child in box.Boxes) CollectPseudoElements(child);
            }
            CollectPseudoElements(nose);

            Assert.True(pseudoBoxes.Count >= 2,
                $"expected at least 2 generated pseudo-element boxes under .nose (:before and :after), found {pseudoBoxes.Count}");
            Assert.All(pseudoBoxes, b => Assert.True(b.ActualBottom - b.Location.Y >= 0,
                "a generated pseudo-element box should have valid (non-negative) resolved geometry"));
        }

        [Fact]
        public async Task Nose_DoesNotOverlapEyesRow_FloatRespectsForeheadsTrailingMargin()
        {
            // Regression for CssBox.MarginTopCollapse's IsFloated branch: it previously returned ONLY
            // the float's own top margin, silently discarding the preceding in-flow sibling's own
            // trailing margin entirely (conflating "floats never COLLAPSE/merge margins" with "floats
            // ignore the preceding margin"). ".forehead" (margin-bottom: 4em) immediately precedes
            // ".nose" (float:left, margin: -2em ...) - the correct gap is forehead's 4em margin-bottom
            // PLUS nose's own -2em margin-top (summed, net +2em), not just the raw -2em with forehead's
            // margin dropped, which pulled ".nose" a full margin-bottom too far up into ".eyes" (whose
            // own opaque background then hid the nose diamond entirely instead of the two elements
            // merely touching at ".eyes"'s bottom edge, as the fixture intends).
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var forehead = FindByClass(root, "forehead")!;
            var eyes = FindByClass(root, "eyes")!;
            var nose = FindByClass(root, "nose")!;

            Assert.True(nose.Location.Y > forehead.ActualBottom,
                $"nose (Location.Y={nose.Location.Y}) should sit below forehead's own border-box bottom (ActualBottom={forehead.ActualBottom}), not pulled up past it");

            // Nose should sit at or just below eyes' bottom edge - not deeply overlapping it.
            Assert.True(nose.Location.Y >= eyes.ActualBottom - 0.5,
                $"nose (Location.Y={nose.Location.Y}) should not overlap eyes' row (ActualBottom={eyes.ActualBottom}) by more than a rounding epsilon");
        }

        [Fact]
        public async Task NoseDivDiv_BeforeAndAfterPseudoElements_MeetFlushWithNoGap()
        {
            // Regression for CssBox.PerformLayoutImp's static/relative positioning formula
            // double-counting a preceding sibling's own border-bottom-width (see
            // Acid2FeatureVerificationTests.BorderedBox_FollowingSibling_StartsAtBorderBoxBottom_
            // NotDoubleCountingBorderBottom for the isolated mechanism). ".nose div div:before" (a
            // black-bottom-border triangle) and ".nose div :after" (a black-top-border triangle) are
            // meant to meet flush, together forming the nose's black diamond outline with no gap - a
            // gap the exact size of ":before"'s own 1em border-bottom left ".nose div div"'s own red
            // "trap" background (meant to always stay hidden) visible as a red band through the middle.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;
            var noseDivDiv = nose.Boxes.First(b => b.HtmlTag != null).Boxes.First(b => b.HtmlTag != null);

            var before = noseDivDiv.Boxes.Single(b => b.IsBeforePseudoElement);
            var after = noseDivDiv.Boxes.Single(b => b.IsAfterPseudoElement);

            Assert.InRange(after.Location.Y - before.ActualBottom, -0.5, 0.5);
        }

        [Fact]
        public async Task NoseDiv_MiddleLevel_DoesNotReceiveBogusPseudoElementBox()
        {
            // Regression for a Round 8 bug in CssData.DoesSelectorMatch(ComplexSelector, box): when
            // re-verifying an EXISTING pseudo-element box (created under ".nose div div", the
            // innermost div) against its own selector, the ancestor-chain walk failed to advance past
            // the pseudo box's owner before testing the next compound - letting that same owner
            // (nose>div>div) satisfy BOTH the pseudo box's own non-pseudo identity check AND the
            // selector's middle "div" ancestor requirement, effectively letting the chain "borrow" one
            // ancestor level it wasn't entitled to. This made ".nose div div:before"/".nose div :after"
            // (wrongly) also match nose>div (the MIDDLE div, one level too high), producing a bogus
            // pseudo-element sibling that pushed the real nose>div>div down by exactly its own height.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;
            var noseDiv = nose.Boxes.First(b => b.HtmlTag != null);
            var noseDivDiv = noseDiv.Boxes.First(b => b.HtmlTag != null);

            Assert.False(noseDiv.Boxes.Any(b => b.IsBeforePseudoElement || b.IsAfterPseudoElement),
                "the middle-level div (.nose > div) should not receive its own generated pseudo-element box - only the innermost div (.nose > div > div) is targeted by \".nose div div:before\"/\".nose div :after\"");

            // nose>div>div is the only real child in normal flow, so it should start exactly at its
            // containing block's content-top - not shifted down by a phantom preceding sibling.
            Assert.InRange(noseDivDiv.Location.Y, noseDiv.ClientTop - 0.5, noseDiv.ClientTop + 0.5);

            // And its bottom must stay within .nose's own (max-height:3em-capped) bottom - not overflow
            // past the visible border, as it did when the phantom sibling pushed it down.
            Assert.True(noseDivDiv.ActualBottom <= nose.ActualBottom + 0.5,
                $"nose>div>div (ActualBottom={noseDivDiv.ActualBottom}) should not overflow past .nose's own bottom (ActualBottom={nose.ActualBottom})");
        }

        [Fact]
        public async Task SmileDiv_BottomOffset_ShiftsPositionAwayFromStaticFlow()
        {
            // Regression for the "bottom" offset property being entirely unimplemented: ".smile div {
            // position:relative; bottom:-1em; }" (no "top" declared) previously never moved at all.
            var (root, container) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var smile = FindByClass(root, "smile")!;
            var smileDiv = smile.Boxes.FirstOrDefault(b => b.Position == "relative");

            Assert.NotNull(smileDiv);
            // With top:auto and bottom:-1em, the used top offset is +1em (CSS2.1 §9.4.3's sign-flip
            // rule) - i.e. the box must sit strictly below where plain static flow would have put it.
            Assert.True(smileDiv!.Location.Y > smile.Location.Y,
                "expected .smile div's bottom offset to shift it below its static flow position");
        }

        [Fact]
        public async Task Forehead_HasNonZeroHeight_NotCollapsedByNbspOrMarginCollapseBugs()
        {
            // Regression for Round 4: ".forehead"'s inner content is 30 consecutive &nbsp; characters
            // (Acid2's actual technique) - a bug in ParseToWords (nbsp treated as ordinary collapsible
            // whitespace) combined with an over-broad guard in MarginBottomCollapse previously collapsed
            // this box to (near) zero height, hiding its tiling background entirely behind solid red.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var forehead = FindByClass(root, "forehead")!;

            var height = forehead.ActualBottom - forehead.Location.Y;
            Assert.True(height > 5, $"expected .forehead to have real, non-collapsed height, got {height}");
        }

        [Fact]
        public async Task Forehead_BackgroundImage_ActuallyLoads()
        {
            // Regression for a Round 3 bug: DataUriUtils.TryDecodeDataUri never percent-decoded a
            // base64 payload before calling Convert.FromBase64String, so ".forehead"'s tiling
            // background (written with its "/" characters percent-escaped as "%2F", per the real
            // fixture) silently failed to decode - only the "background: red" color painted, with no
            // image on top of it at all.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var forehead = FindByClass(root, "forehead")!;

            Assert.NotNull(forehead.BackgroundImages);
            var layer = Assert.Single(forehead.BackgroundImages!);
            var urlImage = Assert.IsType<PeachPDF.Html.Core.Entities.CssImage.Url>(layer);
            Assert.NotNull(urlImage.Image);
        }

        [Fact]
        public async Task Eyes_ObjectFallbackChain_ResolvesToRealImage_NotErrorText()
        {
            // Same Round 3 data-URI bug as above, but for the "eyes" nested <object> fallback chain -
            // its innermost object's real PNG (with 8-bit alpha) is also written with percent-escaped
            // base64. Before the fix, it silently failed to decode, so the whole chain fell back all
            // the way to rendering the literal fallback text "ERROR" instead of the resolved image.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyesA = FindById(root, "eyes-a")!;

            var hasImageWord = false;
            void CheckForImage(CssBox box)
            {
                if (box.Words.Any(w => w.IsImage)) hasImageWord = true;
                foreach (var child in box.Boxes) CheckForImage(child);
            }
            CheckForImage(eyesA);

            Assert.True(hasImageWord, "expected #eyes-a's object fallback chain to resolve to a real image");
            Assert.DoesNotContain(eyesA.Words, w => !w.IsImage);
        }

        [Fact]
        public async Task Eyes_ResolvedObjectsOwnCssBackground_Loads()
        {
            // Regression: CssBoxObject.MeasureWordsSize (and CssBoxImage.MeasureWordsSize) override the
            // base CssBox.MeasureWordsSize and short-circuit as soon as they know they're replaced
            // content, WITHOUT ever calling the base logic that loads this box's own `background-image`
            // layers (CssBox.EnsureAuxiliaryImagesLoadedAsync) - so a replaced element's own CSS
            // background silently never loaded its image at all. "#eyes-a object object object" is
            // exactly this: a resolved, replaced <object> (the eye-icon PNG) that ALSO has its own
            // "background: url(...) fixed 1px 0" checkerboard tile - before this fix, that tile's
            // BackgroundImages[0].Image stayed permanently null, so CssImagePainter.Paint's
            // "urlImage.Image != null" guard always failed and the tile never painted at all, leaving
            // ".eyes"'s own red background fully exposed across almost the entire eye-icon region
            // instead of interlocking into solid yellow with "#eyes-b"'s matching tile.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyesA = FindById(root, "eyes-a")!;

            CssBox? resolvedObjectWithBackground = null;
            void FindResolvedObjectWithBackground(CssBox box)
            {
                if (box.HtmlTag?.Name == "object" && box.Words.Any(w => w.IsImage) && box.BackgroundImages is { Count: > 0 })
                    resolvedObjectWithBackground = box;
                foreach (var child in box.Boxes) FindResolvedObjectWithBackground(child);
            }
            FindResolvedObjectWithBackground(eyesA);

            Assert.NotNull(resolvedObjectWithBackground);
            var backgroundImage = Assert.Single(resolvedObjectWithBackground!.BackgroundImages!);
            var urlImage = Assert.IsType<PeachPDF.Html.Core.Entities.CssImage.Url>(backgroundImage);
            Assert.NotNull(urlImage.Image);
        }

        [Fact]
        public async Task SecondLineBlockquote_MarginAndOriginInsideBorderedPicture_BothApplyCorrectly()
        {
            // Regression for a Round 3 bug: position:absolute never added the box's own margin, and
            // anchored off the containing block's border-box edge instead of its padding-box edge.
            // "[class~=one].first.one" has "top:0; margin: 36px 0 0 60px;" inside ".picture" (which has
            // a 1em border) - dropping the margin alone landed it at .picture's own border-box top-left
            // corner instead of 36px/60px inside its content (padding-box) edge.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var blockquote = FindByClass(root, "first")!;
            var picture = FindByClass(root, "picture")!;

            var expectedY = picture.ClientTop + blockquote.ActualMarginTop;
            var expectedX = picture.ClientLeft + blockquote.ActualMarginLeft;
            Assert.InRange(blockquote.Location.Y, expectedY - 0.5, expectedY + 0.5);
            Assert.InRange(blockquote.Location.X, expectedX - 0.5, expectedX + 0.5);
        }

        [Fact]
        public async Task Eyes_ShrinkToFitWidth_MatchesWidestChild_NotSummedAcrossSiblings()
        {
            // Regression for a real bug found via a direct comparison against Acid2's own reference
            // rendering (http://acid2.acidtests.org/reference.png) - the first time this project's
            // verification went beyond comparing a fresh render only against a *previous round's own*
            // screenshot. ".eyes" (position:absolute, no explicit width) should shrink-to-fit around its
            // widest child - "#eyes-a" (containing the resolved eye-icon image) - but CssBox.
            // GetMinMaxSumWords's paddingSum accumulator was never reset between siblings the way its
            // maxSum counterpart already was, so "#eyes-b"'s and "#eyes-c"'s own unrelated borders (they
            // contribute no text/image content of their own) summed into the same shared total instead
            // of only the widest line's own padding counting - inflating ".eyes" well past its actual
            // content and rendering its red background as a wide, obviously-wrong band instead of a
            // tight accent around the eye icons.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyes = FindByClass(root, "eyes")!;
            var eyesA = FindById(root, "eyes-a")!;

            var eyesWidth = eyes.ActualRight - eyes.Location.X;
            var eyesAWidth = eyesA.ActualRight - eyesA.Location.X;

            Assert.InRange(eyesWidth, eyesAWidth - 1, eyesAWidth + 1);
        }

        [Fact]
        public async Task Eyes_ShrinkToFitWidth_MatchesEyeIconIntrinsicWidth_NotEyesBAndEyesCSummed()
        {
            // Regression for a real bug: "eyesWidth ≈ eyesAWidth" alone (the test above) is
            // tautologically true regardless of whether ".eyes" is correctly ~128 or wrongly inflated
            // - "#eyes-a" always fills whatever width ".eyes" resolves to, per normal block flow, so
            // that assertion alone never actually catches a shrink-to-fit regression. This pins an
            // absolute upper bound: ".eyes" must not approach "#eyes-b"'s width (10em=90pt) plus
            // "#eyes-c"'s width (10em=90pt) summed together (~180pt+), which is exactly what
            // GetMinMaxSumWords's explicit-width floor produced when it wrongly ADDED multiple
            // separate block-level siblings' explicit widths instead of taking their max (see
            // PositionAbsoluteAutoWidth_MultipleExplicitWidthSiblings_TakesWidestNotSum in
            // Acid2FeatureVerificationTests.cs for the isolated mechanism test).
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyes = FindByClass(root, "eyes")!;

            var eyesWidth = eyes.ActualRight - eyes.Location.X;

            Assert.True(eyesWidth < 170,
                $"expected .eyes's shrink-to-fit width to stay well under #eyes-b + #eyes-c's summed widths (~180pt+), got {eyesWidth}");
        }

        [Fact]
        public async Task HeightZero_WithOverflowingLineContent_ContributesZeroToFlow()
        {
            // Regression for CssValueParser.IsValidLength rejecting a bare unitless "0" - CssUtils.
            // SetPropertyValue's "height" case gated assignment behind IsValidLengthProperty, so
            // "height: 0" was silently never applied to CssBox.Height at all (it stayed "auto"),
            // letting the box's line-height-driven content height (24px here, from the tall inline
            // image plus line-height:2em) push the next sibling down instead of contributing zero
            // height to the flow as declared.
            var html = "<html><body>" +
                "<div id='target' style='height:0; line-height:2em;'>" +
                "<img src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAABnRSTlMAAAAAAABupgeRAAAABmJLR0QA/wD/AP+gvaeTAAAAEUlEQVR42mP4/58BCv7/ZwAAHfAD/abwPj4AAAAASUVORK5CYII=' style='width:24px;height:24px;vertical-align:bottom;'>" +
                "</div>" +
                "<div id='next'>NEXT</div>" +
                "</body></html>";
            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;
            var next = FindById(root, "next")!;

            Assert.Equal("0", target.Height);
            Assert.InRange(target.ActualBottom - target.Location.Y, -0.01, 0.01);
            Assert.InRange(next.Location.Y, target.Location.Y - 0.5, target.Location.Y + 0.5);
        }

        [Fact]
        public async Task EyesA_HeightZero_DoesNotPushEyesBAndEyesCDown()
        {
            // Same underlying IsValidLength bug as HeightZero_WithOverflowingLineContent_
            // ContributesZeroToFlow, exercised through the real fixture: "#eyes-a { height: 0 }" must
            // not push "#eyes-b"/"#eyes-c" 24px (its line-height-driven content height) further down
            // than "#eyes-a"'s own top - they must start at the same Y, letting the eye-icon row and
            // the checkerboard/red row occupy the same vertical space the real Acid2 rendering needs.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyesA = FindById(root, "eyes-a")!;
            var eyesB = FindById(root, "eyes-b")!;
            var eyesC = FindById(root, "eyes-c")!;

            Assert.Equal("0", eyesA.Height);
            Assert.InRange(eyesB.Location.Y, eyesA.Location.Y - 0.5, eyesA.Location.Y + 0.5);
            Assert.InRange(eyesC.Location.Y, eyesA.Location.Y - 0.5, eyesA.Location.Y + 0.5);
        }

        [Fact]
        public async Task EyesC_PlainEmptyBlock_HasNoPhantomBeforeAfterBoxes()
        {
            // Regression: PeachPDF's own default UA stylesheet has a blanket ":before, :after {
            // white-space: pre-line }" rule (CssDefaults.cs) that matches every element without ever
            // setting `content` - per CSS2.1 this must compute to content:none (no box generated at
            // all), but DomParser.CorrectTextBoxes used to leave a real, empty, Display:inline
            // ::before/::after CssBox on every element regardless, defeating CssBox.ActsAsInline's
            // "Boxes.Count > 0" guard against misclassifying a genuinely empty block box as an
            // inline-only wrapper - which broke "#eyes-c" (a plain empty block meant to paint
            // bottom-most per Appendix E) into painting in the wrong stacking pass.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyesC = FindById(root, "eyes-c")!;

            Assert.Empty(eyesC.Boxes);
        }

        [Fact]
        public async Task Eyes_FloatChild_OrdersLocally_NotHoistedPastNonStackingContextAncestor()
        {
            // Regression for DomUtils.FlattenStackingContext/SearchForHoistableDescendants: ".eyes"
            // is position:absolute with no z-index, so it does NOT establish its own CSS stacking
            // context - but it's still "positioned", so per Appendix E step 6 it must be its own
            // atomic local-ordering scope for its own float/positioned-without-z-index descendants
            // (like "#eyes-b"), not defer them all the way to the document root. Before this fix,
            // FlattenStackingContext(".eyes") returned only its plain in-flow children ("#eyes-a"/
            // "#eyes-c") - "#eyes-b" (a float) was entirely absent, hoisted instead to the root's own
            // participant list, painting relative to the root's whole subtree instead of interleaved
            // correctly within ".eyes" itself (see CssBox.PaintImpCore's block/float/inline stacking
            // loop, which relies on FlattenStackingContext to supply all three locally).
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var eyes = FindByClass(root, "eyes")!;
            var eyesB = FindById(root, "eyes-b")!;

            var participants = DomUtils.FlattenStackingContext(eyes).ToList();

            Assert.Contains(participants, p => p.Box == eyesB);
        }

        [Fact]
        public async Task Parser_BackgroundStaysYellow_StarHtmlHackNeverMatches()
        {
            // Regression for CssData.DoesSelectorMatch's AllSelector case matching PeachPDF's own
            // synthetic root wrapper box (see DomParser.GenerateCssTree) - which made the fixture's
            // "* html .parser { background: gray; ... }" (the classic quirks-mode-only hack, which must
            // NEVER match in a standards-mode engine) incorrectly match and override ".parser"'s real
            // "background: yellow" from earlier in the same stylesheet.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var parser = FindByClass(root, "parser")!;

            Assert.Equal("rgb(255, 255, 0)", parser.BackgroundColor);
        }

        [Fact]
        public async Task FixedPositionMargin_ShiftsSecondParagraphBelowFirstsBlackBar()
        {
            // Regression for CssBox.PerformLayoutImp's position:fixed branch dropping the box's own
            // margin entirely (unlike every other positioning scheme). Per the HTML4 DTD, "<p><table>
            // ...</table></p><p class='bad'>..." auto-closes the first <p> the moment <table> opens,
            // producing two SIBLING <p> elements - the second ("p.bad") is matched by both ".picture p"
            // (margin:0; top:9em - shared with the first paragraph) AND, being genuinely preceded by a
            // <table> sibling, ".picture p + table + p { margin-top: 3em; }" too. Both paragraphs share
            // the exact same "top:9em" offset, so the ONLY thing that should separate them vertically is
            // that 3em margin-top on the second one - asserting against the box's own resolved
            // ActualMarginTop (rather than a hardcoded pixel/point value) keeps this test independent of
            // this environment's em-to-point conversion specifics.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var picture = FindByClass(root, "picture")!;

            var paragraphs = new List<CssBox>();
            void Collect(CssBox b)
            {
                if (b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true) paragraphs.Add(b);
                foreach (var c in b.Boxes) Collect(c);
            }
            Collect(picture);

            Assert.Equal(2, paragraphs.Count);
            var first = paragraphs[0];
            var second = paragraphs[1];

            Assert.Equal("bad", second.HtmlTag?.TryGetAttribute("class", ""));
            Assert.True(second.ActualMarginTop > 0, "the second paragraph's own margin-top should be a real, positive value (3em)");
            Assert.InRange(second.Location.Y - first.Location.Y, second.ActualMarginTop - 0.5, second.ActualMarginTop + 0.5);
        }

        [Fact]
        public async Task AnonymousTableCells_WrappingNestedTableAndListItem_SizeToTheirChildsExplicitWidth()
        {
            // Regression for CssBox.GetMinMaxSumWords never consulting a box's own explicit CSS
            // `width` when computing intrinsic content width - only literal word/text content. This
            // is usually fine (explicit width constrains layout after content is measured) but breaks
            // down for "ul li.second-part { display: table; width: 1em; }" and "ul li.fourth-part {
            // /* display: list-item (default) */ width: 1em; }": each needs an anonymous table-cell
            // wrapper (CSS2.1 17.2.1, since neither is itself display:table-cell and their real parent
            // is an anonymous table-row), and since neither <li> has any literal text content, the
            // anonymous cell's own intrinsic-width computation found nothing to measure and sized
            // itself to 0 - overlapping/clipping its own child instead of matching "li.first-part"/
            // "li.third-part"'s own real 1em-wide cells for an evenly-spaced row.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var firstPart = FindByClass(root, "first-part")!;
            var secondPart = FindByClass(root, "second-part")!;
            var thirdPart = FindByClass(root, "third-part")!;
            var fourthPart = FindByClass(root, "fourth-part")!;

            var secondCell = secondPart.ParentBox!;
            var fourthCell = fourthPart.ParentBox!;

            Assert.NotSame(secondPart, secondCell);
            Assert.NotSame(fourthPart, fourthCell);

            var expectedWidth = firstPart.ActualRight - firstPart.Location.X;
            Assert.InRange(secondCell.ActualRight - secondCell.Location.X, expectedWidth - 0.5, expectedWidth + 0.5);
            Assert.InRange(fourthCell.ActualRight - fourthCell.Location.X, expectedWidth - 0.5, expectedWidth + 0.5);

            // No overflow: the nested table/list-item must never extend past its own anonymous cell.
            Assert.True(secondPart.ActualRight <= secondCell.ActualRight + 0.5);
            Assert.True(fourthPart.ActualRight <= fourthCell.ActualRight + 0.5);

            var thirdCellWidth = thirdPart.ActualRight - thirdPart.Location.X;
            Assert.InRange(thirdCellWidth, expectedWidth - 0.5, expectedWidth + 0.5);
        }

        [Fact]
        public async Task ThirdPartCell_HeightStretchesToRowHeight_NotClampedToOwnExplicitHeight()
        {
            // Regression for CssLayoutEngine.ApplyParentHeight: the generic post-layout height pass
            // (run once for every box in a table's subtree, via the <ul> table box's own
            // PerformLayoutImp reaching ApplyHeight/ApplyParentHeight after CssLayoutEngineTable.
            // PerformLayout returns) re-applied a table-cell's OWN explicit `height` on top of
            // CssLayoutEngineTable's already-correct row-stretch (CSS2.1 17.5.3: every cell in a row
            // stretches to the row's tallest cell), discarding the stretch. "ul li.third-part {
            // display: table-cell; height: 0.5em; /* gets stretched to fit row */ }" - per the
            // fixture's own comment - is exactly this: its declared 0.5em is shorter than
            // "li.first-part"'s 1em, so it must still end up exactly as tall as the row.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var firstPart = FindByClass(root, "first-part")!;
            var thirdPart = FindByClass(root, "third-part")!;

            Assert.InRange(thirdPart.ActualBottom, firstPart.ActualBottom - 0.5, firstPart.ActualBottom + 0.5);
        }

        [Fact]
        public async Task Smile_Clearance_LandsExactlyAtNoseFloatBottomMarginEdge()
        {
            // The fixture's own "clearance is negative (see 8.3.1 and 9.5.1)" comment on ".smile",
            // verified end-to-end. Three mechanisms have to compose for the mouth to touch the nose
            // (this test's earlier revision pinned the pre-fix behavior of all three as "correct" and
            // asserted .smile could never sit above the floats' BORDER bottoms - both premises wrong):
            //
            // 1. ".smile"'s hypothetical (clear:none) position collapses through ".empty" as ONE
            //    adjoining set per CSS2.1 §8.3.1 - {.forehead's 4em, .empty's 6.25em top AND bottom,
            //    its child's -6em, .smile's own 5em} -> 6.25em - 6em = 0.25em past ".forehead", which
            //    is well ABOVE the ".nose" float's bottom. This needs set-based max(positives) +
            //    min(negatives) accumulation (CssBox.MarginTopCollapse) including the margins of the
            //    boxes ".empty" collapses through (FoldSelfCollapsingMargins) - pairwise reduction
            //    or skipping the -6em child leaves the hypothetical position below the float, and
            //    then clearance never triggers at all.
            // 2. Because the hypothetical position is not past the float, clearance IS introduced
            //    (CSS2.1 §9.5.2) and places .smile's top border edge even with the float's bottom
            //    OUTER edge - the margin edge, ActualBottom + ActualMarginBottom, which ".nose"'s
            //    margin-bottom: -1em puts 1em ABOVE its visible border-box bottom
            //    (CssLayoutEngine.ClearBox).
            // 3. ".smile div"'s { position: relative; bottom: -1em } then shifts the black mouth bar
            //    1em back down, so the mouth's visible top lands exactly at the nose's visible
            //    bottom - see SmileAndChin_MouthBarIsContiguousWithNoseAndChin.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;
            var eyesB = FindById(root, "eyes-b")!;
            var smile = FindByClass(root, "smile")!;

            var noseBottomMarginEdge = nose.ActualBottom + nose.ActualMarginBottom;
            Assert.True(nose.ActualMarginBottom < 0, "fixture sanity: .nose has margin-bottom: -1em");
            Assert.InRange(smile.Location.Y, noseBottomMarginEdge - 0.5, noseBottomMarginEdge + 0.5);

            // ".nose" (not "#eyes-b", much higher up the face) is the lowest relevant float, so its
            // margin edge is the one that governs.
            Assert.True(noseBottomMarginEdge >= eyesB.ActualBottom + eyesB.ActualMarginBottom - 0.5);

            // Clearance is a placement, not an addition: a regression that ADDS .smile's own
            // margin-top on top of the float's bottom edge again would land it well below this.
            var wronglyPushedY = noseBottomMarginEdge + smile.ActualMarginTop;
            Assert.True(smile.Location.Y < wronglyPushedY - 1,
                $"expected .smile's real Y ({smile.Location.Y}) to be well below the wrongly-pushed value ({wronglyPushedY}) that double-counting clearance would produce");
        }

        [Fact]
        public async Task SmileAndChin_MouthBarIsContiguousWithNoseAndChin()
        {
            // The mouth (".smile div", the 12em x 2em black bar, painted 1em lower than its static
            // position by its own { position: relative; bottom: -1em }) must be flush against both
            // the nose block above it and ".chin" below it - the reference rendering has no white
            // rows anywhere inside the face. Regression coverage for the two independent bugs that
            // each opened a visible white band here:
            //
            // - Above the mouth: clearance never triggering (see
            //   Smile_Clearance_LandsExactlyAtNoseFloatBottomMarginEdge) left ".smile" ~1.5em too
            //   low, so the bar's visible top no longer touched ".nose"'s visible bottom.
            // - Below the mouth: the relative offset leaking into ".smile"'s own content-driven
            //   height (CssBox.MarginBottomCollapse reading the child's offset ActualBottom instead
            //   of its static position, CSS2.1 §9.4.3) pushed ".chin" a full 1em below the bar's
            //   visible bottom.
            var (root, _) = await BuildAndLayout(File.ReadAllText(FixturePath));
            var nose = FindByClass(root, "nose")!;
            var smile = FindByClass(root, "smile")!;
            var chin = FindByClass(root, "chin")!;
            var mouthBar = smile.Boxes[0];

            // Fixture sanity: the relative offset itself must still be applied visually.
            Assert.Equal(CssConstants.Relative, mouthBar.Position);
            Assert.True(mouthBar.RelativeOffsetY > 0, "bottom: -1em should shift the bar downward");

            // Visible top of the mouth bar == visible bottom of the nose block.
            Assert.InRange(mouthBar.Location.Y, nose.ActualBottom - 0.5, nose.ActualBottom + 0.5);

            // Visible bottom of the mouth bar == top of the chin.
            Assert.InRange(chin.Location.Y, mouthBar.ActualBottom - 0.5, mouthBar.ActualBottom + 0.5);

            // And the parent ".smile"'s own flow height must exclude the visual offset: its bottom
            // is the bar's STATIC bottom (1em above the bar's visible bottom), not the offset one.
            Assert.InRange(smile.ActualBottom, mouthBar.StaticBottom - 0.5, mouthBar.StaticBottom + 0.5);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static readonly Regex SolidFillRectPattern = new(@"[\d.]+ [\d.]+ [\d.]+ rg[\s\S]{0,60}?([\d.]+ [\d.]+ [\d.]+ [\d.]+) re\s*\nf", RegexOptions.Compiled);

        /// <summary>
        /// Extracts every "color set, then re...f" solid-fill rect command from a page's own
        /// (uncompressed) content stream(s) - the color+rect combination (not just the bare rect) so
        /// two coincidentally-same-sized-but-differently-colored rects across pages aren't conflated.
        /// </summary>
        private static HashSet<string> GetSolidFillRects(PdfPage page)
        {
            var results = new HashSet<string>();
            var content = page.Contents;
            if (content == null) return results;

            foreach (var item in content.Elements)
            {
                if (item is not PdfReference { Value: PdfDictionary dict }) continue;
                var stream = dict.Stream;
                if (stream?.Value is not { Length: > 0 } bytes) continue;

                var text = Encoding.Latin1.GetString(bytes);
                foreach (Match match in SolidFillRectPattern.Matches(text))
                {
                    results.Add(match.Value);
                }
            }

            return results;
        }

        private static bool PageHasContent(PdfPage page)
        {
            try
            {
                var content = page.Contents;
                if (content == null)
                    return false;

                if (content.Elements.Count == 0)
                    return false;

                foreach (var item in content.Elements)
                {
                    if (item is PdfReference { Value: PdfDictionary dict })
                    {
                        var stream = dict.Stream;
                        if (stream?.Value is { Length: > 0 })
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new PeachPDF.PdfSharpCore.Drawing.XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = PeachPDF.PdfSharpCore.Drawing.XGraphics.CreateMeasureContext(
                size, PeachPDF.PdfSharpCore.Drawing.XGraphicsUnit.Point, PeachPDF.PdfSharpCore.Drawing.XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
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
