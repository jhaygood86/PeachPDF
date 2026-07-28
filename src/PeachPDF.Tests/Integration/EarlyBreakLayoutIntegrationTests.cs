using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragments;
using System.Collections.Generic;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A box relocated by one of <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">CSS
    /// Fragmentation Level 3 §4.3</see>'s corrections is laid out again at its new position rather than
    /// translated to it.
    /// </summary>
    /// <remarks>
    /// The distinction is invisible for a box that had not yet reached the page boundary, and decisive
    /// for one that had: its later lines were laid out against the next band's top, so translating the
    /// box carries that fragmentainer gap along inside it — as blank space between two lines, and as
    /// height the box does not use.
    /// <para>
    /// Fixtures use a 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>. <c>orphans</c>/<c>widows</c> are pinned to 1 wherever the box
    /// under test is not the one being tested for them, since the default of 2 relocates a straddling
    /// two-line box on its own and would mask what the other arms do.
    /// </para>
    /// </remarks>
    public class EarlyBreakLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;
        private const double LineHeight = 20;

        // The headline: a box that had already flowed lines onto the next page before the decision was
        // taken. 130 and 150 straddle; 140 is the alignment where the carried gap happened to be zero,
        // which is exactly why it must not be the only case tested.
        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedBox_HasNoInteriorGap(double fillerHeight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedMonolithicBox_HasNoInteriorGap(double fillerHeight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "overflow:hidden"), pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        // A translated box keeps the gap as height it does not use, so its height depends on where it
        // happened to straddle. Laid out again, it is the height of its own content wherever it lands.
        [Fact]
        public async Task RelocatedBox_IsNoTallerThanTheSameBoxThatNeverMoved()
        {
            var (undisturbed, _) = await LayoutHarness.LayoutAsync(
                GapDocument(0, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);
            var settled = HeightOfCard(undisturbed);

            foreach (var filler in new double[] { 130, 140, 150 })
            {
                var (root, _) = await LayoutHarness.LayoutAsync(
                    GapDocument(filler, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

                Assert.Equal(settled, HeightOfCard(root), 6);
            }
        }

        // Relocation must not cost a fragmentainer pass: the decision is taken and acted on inside the
        // pass that discovered it, which is what §4.3 sanctions.
        [Fact]
        public async Task RelocatingABox_TakesNoExtraFragmentainerPass()
        {
            var (_, moved) = await LayoutHarness.LayoutAsync(
                GapDocument(130, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);
            var (_, unmoved) = await LayoutHarness.LayoutAsync(
                GapDocument(130, ""), pageHeight: PageHeight, margin: Margin);

            Assert.Equal(unmoved.FragmentainerPasses, moved.FragmentainerPasses);
        }

        // The latch. Without it the relocated box's own epilogue asks the same question of the same
        // geometry, and - since an unsatisfiable avoid is relaxed rather than skipped (§5.3) - answers
        // "still does not fit", walking the box down the document one page per pass.
        [Fact]
        public async Task BoxTallerThanTheBand_MovesAtMostOnce()
        {
            var lines = string.Join("", Enumerable.Range(0, 14).Select(i => $"Line {i}<br>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:130pt'>filler</div>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt'>{lines}</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            // Taller than a band, so it cannot be made to fit; it must still land within one page of
            // where it started rather than being pushed indefinitely.
            Assert.True(card.ActualBottom - card.Location.Y > container.PageBandHeightOf(0),
                "fixture must be taller than one band for this to test relaxation");
            Assert.True(container.PageIndexOf(card.Location.Y) <= 1,
                $"a box that fits nowhere must not walk down the document, but landed at y={card.Location.Y:F1}");
        }

        // orphans/widows reaches the same mechanism, so it gets the same guarantee.
        [Fact]
        public async Task OrphansPushedParagraph_HasNoInteriorGap()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:145pt'>filler</div>"
                + $"<div id='card' style='orphans:3;widows:3;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            AssertLinesAreEvenlySpaced(LayoutHarness.FindById(root, "card")!);
        }

        // The keep-with-next pull is the one correction a box cannot carry out for itself: the break
        // falls before a sibling placed before it, so only the parent's child loop can re-run it.
        // Whichever way it is carried out, the heading comes along and lands at the destination band's
        // top with the box below it.
        [Theory]
        [InlineData(105)]
        [InlineData(115)]
        [InlineData(125)]
        public async Task PulledRun_MovesTogetherToTheDestinationBandTop(double fillerHeight)
        {
            var (heading, card, container) = await PulledRunAsync(fillerHeight);

            // The UA print stylesheet's h1-h6 { break-after: avoid } is what chains the two.
            var headingPage = container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(headingPage, container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
            Assert.True(heading.ActualBottom <= card.Location.Y + 1.0,
                "the heading must still sit above the box it is chained to");
            Assert.Equal(container.PageTopOf(headingPage), heading.Location.Y, 6);
        }

        // Where the run is still part of the pass being laid out, it is re-run rather than moved, so
        // the box that pulled it re-flows at its new position like any other relocated box.
        [Theory]
        [InlineData(105)]
        [InlineData(115)]
        [InlineData(125)]
        public async Task PulledRun_IsLaidOutAgain_SoTheBoxHasNoInteriorGap(double fillerHeight)
        {
            var (_, card, _) = await PulledRunAsync(fillerHeight);

            AssertLinesAreEvenlySpaced(card);
        }

        /// <summary>
        /// The run head belongs to a fragmentainer the driver has already filled — a structural condition,
        /// not a geometric one: its index lies below the index this pass resumed at. At 115pt of filler the
        /// box's own first line fits on the page and the rest does not, so its content breaks and it
        /// completes on a later pass, by which time the heading above it is settled.
        /// </summary>
        /// <remarks>
        /// This was characterized as a boundary — the group was moved, carrying the fragmentainer gap into
        /// the box, exactly as every one of these corrections used to. The driver can now re-enter the pass
        /// that placed the head, so it is the invariant it was drafted as. The theory above covers the
        /// no-interior-gap half at this filler height; what this adds is that the page the head left keeps
        /// no fragment of it, which is the un-emission half.
        /// </remarks>
        [Fact]
        public async Task PulledRun_FromAnEarlierPass_LeavesNoFragmentOnThePageItLeft()
        {
            var (heading, card, container) = await PulledRunAsync(115);

            var headingPage = container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(
                headingPage,
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));

            var elsewhere = container.FragmentTree!.Fragmentainers
                .Where(f => f.SlotIndex != headingPage)
                .SelectMany(f => Flatten(f.Root))
                .ToList();

            Assert.DoesNotContain(elsewhere, f => ReferenceEquals(f.Box, heading));
        }

        /// <summary>
        /// The workhorse invariant for a correction that reaches back across a pass: every word the
        /// document authored is claimed by exactly one fragment. It fails one way if the re-entered pass
        /// leaves a ghost from the layout it replaced, and the other way if un-emitting the head's
        /// fragmentainer discards content that legitimately belonged to it.
        /// </summary>
        [Theory]
        [InlineData(105)]
        [InlineData(115)]
        [InlineData(125)]
        public async Task PulledRun_ClaimsEveryWordExactlyOnce(double fillerHeight)
        {
            var (_, _, container) = await PulledRunAsync(fillerHeight);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        /// <summary>
        /// One restart per run head per child loop, so a run whose members keep reaching the same
        /// conclusion cannot cycle. Where that guard declines, the head is not offered to the driver
        /// either — the loop has already tried it — and the correction falls back to moving the group,
        /// which is what it always did.
        /// </summary>
        [Fact]
        public async Task PulledRun_AlreadyRestartedOnThisPass_IsMovedRatherThanRestartedAgain()
        {
            // The spacer alone spans three bands, so everything below it is placed well past the
            // fragmentainer the pass is filling and the head is reconsidered more than once.
            var html = LayoutHarness.Wrap(
                "<div style='height:400pt'>filler</div>"
                + "<h2 id='heading' style='margin:0;font-size:10pt;line-height:20pt'>Heading</h2>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var heading = LayoutHarness.FindById(root, "heading")!;
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(
                container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
            Assert.True(heading.ActualBottom <= card.Location.Y + 1.0,
                "the heading must still sit above the box it is chained to");
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// A box whose subtree contains a table that repeats a header keeps the old translation, because
        /// laying such a table out a second time does not reproduce the first result: the repeating
        /// group is detached from the tree and replaced by one proxy per page, and a second run finds
        /// neither. The box must still be relocated — only the way it gets there changes.
        /// </summary>
        [Fact]
        public async Task BoxContainingARepeatingTable_IsStillRelocated()
        {
            var rows = string.Concat(Enumerable.Range(0, 4).Select(i => $"<tr><td>Row {i}</td></tr>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:120pt'>filler</div>"
                + "<div id='card' style='break-inside:avoid;orphans:1;widows:1;font-size:10pt;line-height:20pt'>"
                + $"<table><thead><tr><th>Heading</th></tr></thead><tbody>{rows}</tbody></table></div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(1, container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
            Assert.Equal(container.PageTopOf(1), card.Location.Y, 6);
        }

        /// <summary>
        /// The repeating-table exclusion has to be asked of every box a restart would replay, not only
        /// of the one that raised it: the run is found by walking siblings, so the index range re-run is
        /// wider than the run, and a table in it that repeats a header does not survive a second layout.
        /// </summary>
        /// <remarks>
        /// Asserted as an invariant of the table rather than of the relocation, because whether this
        /// particular fixture reaches the restart at all depends on where the page grid falls: what must
        /// hold either way is that the table still repeats exactly one header group, once per page it
        /// spans, with no stale proxy left behind.
        /// </remarks>
        [Theory]
        [InlineData(60)]
        [InlineData(80)]
        [InlineData(100)]
        public async Task RunHeadContainingARepeatingTable_KeepsItsHeaderIntact(double fillerHeight)
        {
            var rows = string.Concat(Enumerable.Range(0, 6).Select(i => $"<tr><td>Row {i}</td></tr>"));
            var html = LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<div id='head' style='break-after:avoid;font-size:10pt;line-height:20pt'>"
                + $"<table><thead><tr><th>H</th></tr></thead><tbody>{rows}</tbody></table></div>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var table = LayoutHarness.Descendants(LayoutHarness.FindById(root, "head")!)
                .First(b => b.Display == CssConstants.Table);
            var proxies = table.Boxes.OfType<CssProxyBox>().ToList();

            Assert.NotEmpty(proxies);
            Assert.Single(proxies.Select(p => p.SourceBox).Distinct());
            Assert.All(proxies, p => Assert.Equal(CssConstants.TableHeaderGroup, p.SourceBox.Display));

            // Every proxy must sit on a page of its own — a stale one left by a discarded layout would
            // duplicate a position.
            Assert.Equal(proxies.Count, proxies.Select(p => Math.Round(p.Location.Y, 3)).Distinct().Count());
        }

        private static async Task<(CssBox Heading, CssBox Card, HtmlContainerInt Container)> PulledRunAsync(double fillerHeight)
        {
            var html = LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<h2 id='heading' style='margin:0;font-size:10pt;line-height:20pt'>Heading</h2>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            return (LayoutHarness.FindById(root, "heading")!, LayoutHarness.FindById(root, "card")!, container);
        }

        /// <summary>
        /// The gap a translation carries shows up as one line sitting further below its predecessor than
        /// the line height accounts for.
        /// </summary>
        private static void AssertLinesAreEvenlySpaced(CssBox card)
        {
            var tops = card.LineBoxes
                .SelectMany(l => l.Words)
                .Select(w => Math.Round(w.Top, 3))
                .Distinct()
                .Order()
                .ToList();

            Assert.True(tops.Count > 1, "fixture must produce more than one line for spacing to mean anything");

            for (var i = 1; i < tops.Count; i++)
            {
                Assert.True(tops[i] - tops[i - 1] <= LineHeight + 0.5,
                    $"line {i} sits {tops[i] - tops[i - 1]:F1}pt below its predecessor, "
                    + $"more than the {LineHeight}pt line height - a fragmentainer gap carried inside the box");
            }
        }

        private static double HeightOfCard(CssBox root)
        {
            var card = LayoutHarness.FindById(root, "card")!;
            return Math.Round(card.ActualBottom - card.Location.Y, 6);
        }

        // ── §3.1 propagation (the container travels too) ──────────────────────────────────────────────

        /// <summary>
        /// §3.1's break point before a container's first in-flow child <i>is</i> the break point before the
        /// container, so a §4.3 mover relocating that child has to move the container with it. Left behind,
        /// the container spans the boundary and paints an empty copy of its own background, border and
        /// padding on the page its content just left.
        /// </summary>
        [Theory]
        [InlineData("break-inside:avoid")]
        [InlineData("overflow:hidden")]
        public async Task RelocatedFirstChild_TakesItsContainerWithIt(string cardCss)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrappedCardDocument(fillerHeight: 130, cardCss), pageHeight: PageHeight, margin: Margin);

            var wrapper = LayoutHarness.FindById(root, "wrapper")!;
            var card = LayoutHarness.FindById(root, "card")!;

            var nextBandTop = container.PageTopOf(1);

            Assert.Equal(nextBandTop, wrapper.Location.Y, 1);
            Assert.True(card.Location.Y >= wrapper.Location.Y - 0.001,
                $"the card must sit inside its wrapper, but is at {card.Location.Y:F1} against {wrapper.Location.Y:F1}");

            // And the wrapper is no longer on the page it left, which is the whole visible defect.
            Assert.Equal(1, container.PageIndexOf(wrapper.Location.Y));
        }

        /// <summary>
        /// The redirect is a relaxation ladder rung, not an unconditional rewrite: a container that does not
        /// fit the destination is left where it is and the box moves alone. Moving it anyway would put it
        /// somewhere it also does not fit, so it would break again from its new top and be asked again —
        /// the runaway <c>CanBeLaidOutAgain</c>'s own fit test exists to prevent.
        /// </summary>
        [Fact]
        public async Task RelocatedFirstChild_LeavesAContainerThatDoesNotFitTheDestination()
        {
            // The card straddles the boundary and fits a band on its own, so the mover fires; the wrapper's
            // own extent (its top down to the card's bottom) is 180pt against a 160pt band, so it cannot go.
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrappedCardDocument(fillerHeight: 50, "break-inside:avoid", wrapperCss: "padding-top:100pt"),
                pageHeight: PageHeight, margin: Margin);

            var wrapper = LayoutHarness.FindById(root, "wrapper")!;
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(0, container.PageIndexOf(wrapper.Location.Y));
            Assert.Equal(1, container.PageIndexOf(card.Location.Y));
        }

        // ── the re-entered pass is one that resumed into a paragraph ─────────────────────────────────

        /// <summary>
        /// Re-entering a pass undoes the layout the passes from it on produced, which is what lets the box
        /// that pass resumes <i>into</i> be continued a second time. Its line boxes are the case: a resumed
        /// flow deliberately does not clear them and the box's prologue is once per layout, so lines the
        /// discarded passes appended are still there — and <c>CssLayoutEngine.FinalizeLineBoxes</c> runs
        /// from the record's own <c>CompletedLineCount</c>, handing every one of them to
        /// <see cref="CssLineBox.AssignRectanglesToBoxes"/> a second time.
        /// </summary>
        /// <remarks>
        /// The fixture is #415's: a paragraph long enough to break at the first page boundary, then two
        /// headings each chained to a <c>break-inside: avoid</c> block. The second block is what raises the
        /// decision, on a pass later than the one that placed its heading, so the driver goes back to a pass
        /// that was entered mid-paragraph. Each combination below was found by sweep and each throws without
        /// the rollback.
        /// </remarks>
        [Theory]
        [InlineData(50, 30, 120)]
        [InlineData(60, 50, 140)]
        [InlineData(80, 30, 140)]
        [InlineData(100, 40, 160)]
        [InlineData(100, 50, 120)]
        public async Task PulledRun_ReEnteringAPassThatResumedIntoAParagraph_LaysItOutAgain(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10);

            // The paragraph the re-entered pass continued holds each of its own words on exactly one of its
            // own line boxes. A line the rollback failed to discard shows up here as a word on two of them.
            var lead = LayoutHarness.FindById(root, "lead")!;
            var hosted = lead.LineBoxes.SelectMany(l => l.Words).ToList();

            Assert.NotEmpty(hosted);
            Assert.Equal(hosted.Count, hosted.Distinct().Count());
            Assert.Equal(WordsIn(lead).Count, hosted.Count);
        }

        /// <summary>
        /// And the correction itself still happens: each heading lands on the page its own block starts on,
        /// with the block below it.
        /// </summary>
        [Theory]
        [InlineData(50, 30, 120)]
        [InlineData(80, 30, 140)]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_KeepsEachHeadingWithItsBlock(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10);

            foreach (var n in new[] { 1, 2 })
            {
                var heading = LayoutHarness.FindById(root, $"h{n}")!;
                var card = LayoutHarness.FindById(root, $"card{n}")!;

                Assert.Equal(
                    container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                    container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
                Assert.True(heading.ActualBottom <= card.Location.Y + 1.0,
                    $"heading {n} must still sit above the block it is chained to");
            }
        }

        /// <summary>
        /// #374's workhorse invariant, over the content the pull moves: every word of each relocated block is
        /// claimed by exactly one fragment. It fails one way if the re-entered pass leaves a ghost from the
        /// layout it replaced, and the other way if the rollback discards content that legitimately belonged
        /// to a fragmentainer already filled.
        /// </summary>
        /// <remarks>
        /// Asked of the whole document since #433: a word the pass that froze the first slot never reached
        /// used to carry document Y 0, which lies inside that slot's own band, so the first page's fragment
        /// claimed it — a pre-existing defect this fixture tripped over, which is why the assertion was
        /// originally scoped to the two blocks the pull moves.
        /// </remarks>
        [Theory]
        [InlineData(50, 30, 120)]
        [InlineData(80, 30, 140)]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_ClaimsEachBlockWordExactlyOnce(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10);

            var authored = WordsIn(root);

            Assert.NotEmpty(authored);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => (object)w.Word)
                .ToList();

            Assert.Equal(authored.Count, claimed.Count);
            Assert.Equal(claimed.Count, claimed.Distinct(ReferenceEqualityComparer.Instance).Count());
        }

        /// <summary>
        /// The fixture family really does send the driver back to a pass it had already finished — which is
        /// what makes the theory above a test of this correction rather than of layout in general, and what
        /// a future guard silently declining the rewind would break.
        /// </summary>
        /// <remarks>
        /// Asked of the family rather than of each row: which row reaches the re-entry depends on where the
        /// page boundary falls in the paragraph, and that is a function of the platform's font metrics — the
        /// same sensitivity that has caught §5.4 regressions on <c>windows-latest</c> only. That the family
        /// reaches it is stable; which member does is not.
        /// </remarks>
        [Fact]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_ReEntersThatPass()
        {
            var rewound = 0;

            foreach (var (leadWords, cardWords, pageHeight) in new[]
                     {
                         (50, 30, 120.0), (60, 50, 140.0), (80, 30, 140.0), (100, 40, 160.0), (100, 50, 120.0)
                     })
            {
                var (_, container) = await LayoutHarness.LayoutAsync(
                    ResumedParagraphDocument(leadWords, cardWords),
                    pageWidth: 300, pageHeight: pageHeight, margin: 10);

                rewound += container.PassRewinds;
            }

            Assert.True(rewound > 0, "no member of the fixture family sent the driver back to a finished pass");
        }

        private static List<CssRect> WordsIn(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

        /// <summary>
        /// A paragraph that breaks at the first page boundary, followed by two headings each chained to a
        /// block that asks not to be broken.
        /// </summary>
        private static string ResumedParagraphDocument(int leadWords, int cardWords) =>
            LayoutHarness.Wrap(
                $"<p id='lead'>{Filler(leadWords, "lead")}</p>"
                + "<h2 id='h1'>Head</h2>"
                + $"<div id='card1' style='break-inside:avoid'>{Filler(cardWords, "a")}</div>"
                + "<h2 id='h2'>Head</h2>"
                + $"<div id='card2' style='break-inside:avoid'>{Filler(cardWords, "b")}</div>");

        private static string Filler(int count, string prefix) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"{prefix}{i}"));

        private static string WrappedCardDocument(double fillerHeight, string cardCss, string wrapperCss = "") =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + $"<div id='wrapper' style='{wrapperCss};background:#eee;border:1pt solid #000'>"
                + $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div></div>");

        private static string GapDocument(double fillerHeight, string cardCss) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");
    }
}
