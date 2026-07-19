using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Assert.True(document.PageCount <= 6,
                $"expected a small handful of pages (the fixture's own huge, intentionally-off-screen " +
                $"100em margins account for some unavoidable blank space), got {document.PageCount}");
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

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
