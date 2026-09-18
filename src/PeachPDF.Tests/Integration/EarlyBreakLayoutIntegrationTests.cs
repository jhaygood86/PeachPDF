using PeachPDF.CSS;
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
                .First(b => b.Display.Value == DisplayMode.Table);
            var proxies = table.Boxes.OfType<CssProxyBox>().ToList();

            Assert.NotEmpty(proxies);
            Assert.Single(proxies.Select(p => p.SourceBox).Distinct());
            Assert.All(proxies, p => Assert.Equal(DisplayMode.TableHeaderGroup, p.SourceBox.Display.Value));

            // Every proxy must sit on a page of its own — a stale one left by a discarded layout would
            // duplicate a position.
            Assert.Equal(proxies.Count, proxies.Select(p => Math.Round(p.Location.Y, 3)).Distinct().Count());
        }

        private static async Task<(CssBox Heading, CssBox Card, HtmlContainerInt Container)> PulledRunAsync(
            double fillerHeight, Action<CssBox>? prepare = null)
        {
            var html = LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<h2 id='heading' style='margin:0;font-size:10pt;line-height:20pt'>Heading</h2>"
                + $"<div id='card' style='break-inside:avoid;orphans:1;widows:1;line-height:{LineHeight}pt;font-size:10pt;width:60pt'>"
                + "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(
                html, pageHeight: PageHeight, margin: Margin, prepare: prepare);

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
        [InlineData(60, 50, 140)]
        public async Task PulledRun_ReEnteringAPassThatResumedIntoAParagraph_LaysItOutAgain(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10,
                configureAdapter: RegisterResumedParagraphFont);

            Assert.True(container.PassRewinds > 0, "fixture did not re-enter a completed pass");

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
        [InlineData(60, 50, 140)]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_KeepsEachHeadingWithItsBlock(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10,
                configureAdapter: RegisterResumedParagraphFont);

            Assert.True(container.PassRewinds > 0, "fixture did not re-enter a completed pass");

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
        [InlineData(60, 50, 140)]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_ClaimsEachBlockWordExactlyOnce(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(leadWords, cardWords), pageWidth: 300, pageHeight: pageHeight, margin: 10,
                configureAdapter: RegisterResumedParagraphFont);

            Assert.True(container.PassRewinds > 0, "fixture did not re-enter a completed pass");

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
        /// The fixture pins its font so the page boundary and resulting pass re-entry are identical on every
        /// platform.
        /// </remarks>
        [Fact]
        public async Task PulledRun_FromAPassThatResumedIntoAParagraph_ReEntersThatPass()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphDocument(60, 50),
                pageWidth: 300, pageHeight: 140, margin: 10,
                configureAdapter: RegisterResumedParagraphFont);

            Assert.True(container.PassRewinds > 0, "fixture did not send the driver back to a finished pass");
        }

        /// <summary>
        /// #434: a forced break below a box the re-entered pass replays is lost, because
        /// <c>PassRewind.RollBackTo</c> only lets the resumption chain's own boxes' prologues back in - a
        /// break on one of THEIR descendants stays latched from the discarded attempt and is never
        /// retaken. <c>h2</c> here is wrapped in <c>section2</c>, after a <c>section2lead</c> sibling so
        /// css-break-3 §3.1's first-child propagation does not hoist the break onto <c>section2</c> itself
        /// - which would make it a direct-reset target instead of the grandchild #434 is about. The
        /// resumption chain names only <c>lead</c>, so <c>section2</c> is reset outright when the driver
        /// rewinds to replay <c>lead</c>/<c>h1</c>/<c>card1</c>'s pass; before the fix, that reset reached
        /// <c>section2</c> itself but not <c>h2</c>, so <c>h2</c>'s already-taken forced break stayed
        /// latched and was silently skipped on replay - landing directly on <c>section2lead</c>'s own page
        /// with room to spare, rather than opening a fresh one.
        /// </summary>
        /// <remarks>
        /// Swept, not a fixed row per combination, for the same reason
        /// <see cref="PulledRun_FromAPassThatResumedIntoAParagraph_ReEntersThatPass"/> is: which combination
        /// actually reaches the pass re-entry is a function of the platform's font metrics (word wrapping
        /// differs enough between <c>windows-latest</c> and this repo's other CI runners that a single
        /// hard-coded row is not portable), while that the family does is stable. The fix's own assertion is
        /// therefore checked for every combination that reaches the rewind, and the sweep as a whole is
        /// asked, once, whether at least one did.
        /// </remarks>
        [Fact]
        public async Task PulledRun_ReEnteringAPassThatResumedIntoAParagraph_RetakesAForcedBreakOnAGrandchild()
        {
            var rewound = 0;

            foreach (var (leadWords, pageHeight) in new[]
                     {
                         (25, 160.0), (35, 180.0), (50, 200.0), (55, 220.0), (70, 240.0), (75, 140.0),
                         (90, 280.0), (100, 160.0), (115, 180.0), (135, 200.0), (150, 220.0), (160, 160.0),
                         (170, 240.0), (190, 140.0), (190, 180.0), (190, 260.0)
                     })
            {
                var (root, container) = await LayoutHarness.LayoutAsync(
                    ResumedParagraphWithNestedForcedBreakDocument(leadWords, 30),
                    pageWidth: 300, pageHeight: pageHeight, margin: 10);

                if (container.PassRewinds == 0) continue;
                rewound += container.PassRewinds;

                var section2lead = LayoutHarness.FindById(root, "section2lead")!;
                var h2 = LayoutHarness.FindById(root, "h2")!;

                var section2leadPage =
                    container.PageIndexOf(section2lead.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);
                var h2Page = container.PageIndexOf(h2.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

                Assert.True(h2Page > section2leadPage,
                    $"break-before:page must push h2 onto a fresh page rather than leaving it on " +
                    $"section2lead's page (lead={leadWords}, pageHeight={pageHeight})");
                Assert.Equal(container.PageTopOf(h2Page), h2.Location.Y, 0.1);
            }

            Assert.True(rewound > 0, "no member of the fixture family sent the driver back to a finished pass");
        }

        /// <summary>
        /// #384: the pass being re-entered here has itself stepped over a forced break before it ever
        /// reaches <c>h1</c> - <c>marker</c>'s <c>break-after:page</c> moves the pass's own cursor forward
        /// without ending it (<c>FragmentainerContext.StepOverTo</c>), so the entry
        /// <see cref="HtmlContainerInt"/> recorded for this pass names only the slot it <i>opened</i> at,
        /// not the one it was filling by the time it placed <c>h1</c>. A lookup keyed on that opening slot
        /// (the pre-#384 shape) either finds nothing there at all and declines the rewind outright, or -
        /// worse, when some other pass happens to share the same opening slot number - finds that other
        /// pass's entry instead and rolls back to the wrong point. <c>h1</c>'s own recorded pass index
        /// (<see cref="CssBox.PlacedByPassIfStillValid"/>) answers correctly regardless, because it is
        /// stamped by the pass that actually placed it rather than derived from where that pass began.
        /// </summary>
        /// <remarks>
        /// Swept for the same reason <see cref="PulledRun_FromAPassThatResumedIntoAParagraph_ReEntersThatPass"/>
        /// is: which combination actually reaches the re-entry depends on the platform's font metrics.
        /// </remarks>
        [Fact]
        public async Task PulledRun_FromAPassThatSteppedOverAForcedBreak_ReEntersThatPass()
        {
            var rewound = 0;

            foreach (var (leadWords, cardWords, pageHeight) in new[]
                     {
                         (20, 30, 160.0), (20, 40, 160.0), (20, 50, 160.0), (20, 50, 170.0), (30, 30, 170.0)
                     })
            {
                var (_, container) = await LayoutHarness.LayoutAsync(
                    ResumedParagraphWithLeadingForcedBreakDocument(leadWords, cardWords),
                    pageWidth: 300, pageHeight: pageHeight, margin: 10);

                rewound += container.PassRewinds;
            }

            Assert.True(rewound > 0, "no member of the fixture family sent the driver back to a finished pass");
        }

        /// <summary>
        /// The companion correctness check for <see cref="PulledRun_FromAPassThatSteppedOverAForcedBreak_ReEntersThatPass"/>:
        /// wherever the rewind actually fires, each heading still lands with the block it is chained to,
        /// rather than being left behind by a declined (or misdirected) rewind.
        /// </summary>
        [Theory]
        [InlineData(20, 30, 160.0)]
        [InlineData(20, 40, 160.0)]
        [InlineData(20, 50, 160.0)]
        [InlineData(20, 50, 170.0)]
        [InlineData(30, 30, 170.0)]
        public async Task PulledRun_FromAPassThatSteppedOverAForcedBreak_KeepsEachHeadingWithItsBlock(
            int leadWords, int cardWords, double pageHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                ResumedParagraphWithLeadingForcedBreakDocument(leadWords, cardWords),
                pageWidth: 300, pageHeight: pageHeight, margin: 10);

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

        // ── a table sibling drives the self-relocation retry (#1045) ────────────────────────────────

        /// <summary>
        /// #1045: a <c>break-inside:avoid</c> box whose subtree contains a <c>&lt;table&gt;</c> drops its
        /// other children — a preceding <c>&lt;h2&gt;</c> here — when its own straddle is only discovered
        /// late (on the pass that <i>completes</i> the box, per <see cref="CssBox.PerformLayoutEpilogue"/>),
        /// because the table engine's own row-height estimate (<c>EstimateRowHeight</c>, which can
        /// undershoot) misjudges whether the table fits at discovery time. The self-relocation retry this
        /// triggers (<c>CssBox.TakeEarlyBreak</c>'s <c>_earlyBreakRetryTop</c>, driven by
        /// <c>CssBox.DriveBlockChildPass</c>) is the one <c>PassRewind</c> re-entry point that used to skip
        /// the shared rollback every other one performs — so the heading, already laid out once this
        /// generation, never got its prologue back on the retry.
        /// </summary>
        /// <remarks>
        /// Pinned rather than swept, for the same reason the boundary rows in
        /// <see cref="RelocatedBox_HasNoInteriorGap"/> are: 139.5/139.75 straddle the disagreement between
        /// the table's coarse estimate and <c>card</c>'s own precise post-layout straddle check; 130 is a
        /// control that reaches the ordinary relocation path without it. Found by sweeping this fixture's
        /// own filler height in 0.25pt steps.
        /// </remarks>
        [Theory]
        [InlineData(130)]
        [InlineData(139.5)]
        [InlineData(139.75)]
        public async Task BoxContainingATableAndAHeading_KeepsItsHeading(double fillerHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TableAndHeadingCardDocument(fillerHeight), pageHeight: PageHeight, margin: Margin);

            var heading = LayoutHarness.FindById(root, "heading");
            Assert.NotNull(heading);

            var present = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Any(f => ReferenceEquals(f.Box, heading));

            Assert.True(present, $"heading missing from the fragment tree at filler height {fillerHeight}pt");
        }

        /// <summary>
        /// The workhorse invariant, over the same fixture: every word the document authored is claimed by
        /// exactly one fragment, which fails the way #1045 did if the heading's words are left permanently
        /// excluded (see <c>CssRect.AwaitsTheNextFragmentainer</c>) after the retry.
        /// </summary>
        [Theory]
        [InlineData(130)]
        [InlineData(139.5)]
        [InlineData(139.75)]
        public async Task BoxContainingATableAndAHeading_ClaimsEveryWordExactlyOnce(double fillerHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TableAndHeadingCardDocument(fillerHeight), pageHeight: PageHeight, margin: Margin);

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
        /// The issue's own second, more severe symptom: a preceding sibling's own box straddling the
        /// boundary (here, via <c>padding-bottom</c> rather than a spacer <c>&lt;div&gt;</c> — the issue
        /// notes a spacer alone does not reproduce it) can carry the whole <c>break-inside:avoid</c> box,
        /// table and heading alike, out of the fragment tree entirely. Pinned at the offsets this fixture's
        /// own sweep found failing.
        /// </summary>
        [Theory]
        [InlineData(122)]
        [InlineData(122.25)]
        public async Task BoxContainingATableAndAHeading_SurvivesAPrecedingSiblingsPaddingBottom(double padding)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                PaddingPrecedesTableCardDocument(padding), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card");
            var heading = LayoutHarness.FindById(root, "heading");
            Assert.NotNull(card);
            Assert.NotNull(heading);

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .ToList();

            Assert.Contains(fragments, f => ReferenceEquals(f.Box, card));
            Assert.Contains(fragments, f => ReferenceEquals(f.Box, heading));
        }

        // ── a multi-line heading no longer gets claimed twice, independent of any table (#1047) ────────
        //
        // Both tests below - and the ledger they exercise (HtmlContainerInt.VerifyWordClaims,
        // FragmentEmitter's own word-claim tracker) - only exist in a DEBUG build (see those types' own
        // remarks). CI's dedicated test job runs Release, where the property this fixture sets does not
        // exist at all, so the whole region is compiled out there rather than only skipped: a local
        // `dotnet test --framework net8.0` (this repo's own prescribed daily-driver command, always
        // Debug) is what actually runs these.
#if DEBUG

        /// <summary>
        /// The mechanism issue #1047 traced through <see cref="Fragmentation.FragmentEmitter"/>'s
        /// DEBUG-only, throw-on-first-conflict word-claim ledger, now fixed on both ends. Kept as a
        /// positive assertion — via the same ledger that once threw for this fixture — rather than deleted,
        /// so a regression here is localized to the exact call path that produces it instead of only being
        /// noticed by the finished-tree walk <c>PulledRun_ClaimsEveryWordExactlyOnce</c>/
        /// <c>UnreachedWordClaimTests</c> already cover.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The confirmed mechanism</b> — and it was <i>not</i> the cross-pass <c>InvalidateFrom</c>/
        /// stale-slot race the issue itself speculated about (the ledger's exception, back when this threw,
        /// named both claims as coming from the SAME <c>EmitPass</c> call, and <c>InvalidateFrom</c>'s own
        /// release never fired for this repro at all). <c>heading</c>'s three lines do not all fit in the
        /// room <c>filler</c> leaves on the page it starts on, so its first line-breaking attempt keeps 0
        /// lines and asks for a break before itself. <c>alpha</c> (the sibling right after it) keeps too
        /// few lines to satisfy its own default <c>orphans</c> minimum too, and because the UA print
        /// stylesheet chains a heading to what follows it (<c>h1</c>–<c>h6</c> { break-after: avoid }),
        /// <c>CssBox.LayoutBlockChildren</c>'s own keep-with-next pull fires — <c>TryRestartAt</c>, a
        /// same-pass restart entirely separate from <c>CssBox.DriveBlockChildPass</c>'s own
        /// <c>break-inside:avoid</c> self-relocation retry. It repositions <c>heading</c> to the next
        /// page's own top and re-enters <c>card</c>'s child loop at it. <c>TryRestartAt</c> used to leave
        /// the pass's own active fragmentainer context pointed at the OLD, already exhausted band, so
        /// <c>heading</c> kept 0 lines a <i>second</i> time and the whole thing only resolved on the pass
        /// after next; it now steps <see cref="Fragmentation.FragmentainerContext.StepOverTo"/> to the
        /// destination slot <c>EarlyBreak.Slot</c> already names before re-entering the head, the same way
        /// every other mechanism that places content past the fragmentainer being filled already does (see
        /// that method's own remarks) — closing the extra pass this used to cost. That alone was not enough
        /// to stop this test's own ledger from throwing; see the next paragraph.
        /// </para>
        /// <para>
        /// <b>A second, independent defect compounded the first, and closing only the one above is not
        /// enough on its own.</b> <c>CssBox.CanBeLaidOutAgain</c>'s destination-fit check measures a box's
        /// own raw <c>ActualBottom - Location.Y</c> — which, for a <c>break-inside:avoid</c> box whose own
        /// first in-flow child was just relocated by the very restart above without the box's own
        /// <c>Location.Y</c> moving too, still overstates how much room a fresh, gap-free re-layout would
        /// actually need. That can still make the check answer "does not fit" for a destination band the
        /// content genuinely fits, forcing the older, gap-carrying <c>TranslateForEarlyBreak</c> path (a raw
        /// <c>OffsetTop</c> shift, not a re-layout) — which can still land a line past a fragmentainer
        /// boundary by more than a rounding tolerance. Left alone, <c>FragmentEmitter.ClaimsLine</c>'s
        /// <c>FallsPast</c> tie-break granted a live line straddling that way a second, conflicting claim on
        /// the next slot on top of its ordinary one — a case that arm's own doc comment did not expect ("no
        /// live case ... reaches the extra claim at all" [sic], written when the only known source — flex/
        /// grid measurement passes — had already been closed at the source). <c>ClaimsLine</c> no longer
        /// grants that second claim at all for ordinary (non-monolithic) content: a line belongs to the one
        /// pagination slot its own top starts in, full stop, and a defect that leaves it further from that
        /// slot's band than intended (this one, and three other independently-diagnosed shapes — a table
        /// rowspan continuation, a multi-column no-progress backstop, a flex/grid wrapping column) is a
        /// defect to fix at the defect, never a reason to paint the same line twice. See
        /// <c>FragmentEmitter.ClaimsLine</c>'s own remarks for the rest of that reasoning.
        /// </para>
        /// <para>
        /// <b>Fixing the cursor is not fixing the geometry — that took a separate, later fix.</b> When
        /// #1047 first landed, the residual translate-relaxation fallback above was left in place as a
        /// known imprecision: a <c>break-inside:avoid</c> box whose destination-fit check read a
        /// phantom-gap-inflated extent could still fall back to translating rather than being laid out
        /// again cleanly, which was confirmed (by temporary instrumentation, not kept) to overflow the
        /// destination page's own content area by as much as a full line. That translate was itself a
        /// §5.3-sanctioned relaxation (an unsatisfiable <c>avoid</c> may move the box anyway, maximizing
        /// what lands on one page) applied to an overstated "does not fit" answer — CSS already tolerated
        /// the relaxation outcome; the defect was that the check reached it more often than the content's
        /// real footprint warranted. <c>CssBox.EffectiveContentTop</c> now closes this: <c>TryRestartAt</c>
        /// records where it relocated this box's own first in-flow child
        /// (<c>CssBox._firstChildRestartedTop</c>), and <c>FitsInFragmentainer</c> measures from that
        /// instead of the box's own stale <c>Location.Y</c> when it is set — see
        /// <see cref="MultiLineHeadingRelocatedByBreakInsideAvoid_FitsWithinDestinationPage"/> below, which
        /// asserts the fit directly. Every word still lands on exactly one page (this test's own assertion)
        /// and on the correct, destination page
        /// (<see cref="PulledRun_MovesTogetherToTheDestinationBandTop"/>'s own invariant, asserted below
        /// too) — #1047 was about the former, not the latter.
        /// </para>
        /// <para>
        /// The band this fixture is confirmed to exercise ([84, 96] and, one page-height-equivalent later,
        /// [244, 256], swept in 0.25pt filler-height steps in
        /// <see cref="MultiLineHeadingRelocatedByBreakInsideAvoid_ClaimsEveryWordExactlyOnce_AcrossTheConfirmedBand"/>)
        /// is not the issue's own originally-reported [212.75, 216.5]/[335.75, 336.5] — both PR-0 (the
        /// line-box-is-the-fragmentainer-unit change) and #1184 landed in this area of the engine since the
        /// issue was filed and moved where this exact fixture's filler height needs to land, per this
        /// file's own convention of re-sweeping rather than trusting an old pinned band when a fixture stops
        /// reproducing (see this file's other pinned-offset theories). 90pt sits comfortably inside the
        /// confirmed band.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task MultiLineHeadingRelocatedByBreakInsideAvoid_ClaimsEveryWordExactlyOnce_Issue1047()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Issue1047Document(90), pageHeight: PageHeight, margin: Margin, prepare: EnableWordClaimLedger);

            AssertEveryWordClaimedExactlyOnce(container);
            AssertHeadingAndCardShareAPage(root, container);
        }

        /// <summary>
        /// Sweeps the band this fixture is confirmed to reproduce in (see the remarks on
        /// <see cref="MultiLineHeadingRelocatedByBreakInsideAvoid_ClaimsEveryWordExactlyOnce_Issue1047"/>),
        /// so a future, unrelated change to the layout engine that shifts it again is caught here — as a
        /// change in which offsets fail — rather than leaving that single-point test silently passing for
        /// the wrong reason (e.g. the band having moved off 90pt entirely).
        /// </summary>
        [Theory]
        [InlineData(84)]
        [InlineData(90)]
        [InlineData(96)]
        [InlineData(244)]
        [InlineData(250)]
        [InlineData(256)]
        public async Task MultiLineHeadingRelocatedByBreakInsideAvoid_ClaimsEveryWordExactlyOnce_AcrossTheConfirmedBand(
            double fillerHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Issue1047Document(fillerHeight), pageHeight: PageHeight, margin: Margin,
                prepare: EnableWordClaimLedger);

            AssertEveryWordClaimedExactlyOnce(container);
            AssertHeadingAndCardShareAPage(root, container);
        }

        private static void AssertEveryWordClaimedExactlyOnce(HtmlContainerInt container)
        {
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        private static void AssertHeadingAndCardShareAPage(CssBox root, HtmlContainerInt container)
        {
            var heading = LayoutHarness.FindById(root, "heading")!;
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(
                container.PageIndexOf(heading.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
        }

        /// <summary>
        /// The ledger's other half, exercised on a fixture known NOT to be one of #1047's own shapes:
        /// <see cref="PulledRunAsync"/>'s keep-with-next run pull reaches back into an already-frozen
        /// EARLIER pass (<c>HtmlContainerInt.TryRewindForRunPull</c>), which un-freezes that slot via
        /// <c>FragmentEmitter.InvalidateFrom</c> and then genuinely re-emits it later - the one case in
        /// this whole diagnostic where the SAME slot is legitimately claimed twice in sequence, first by
        /// the pass that placed <c>heading</c> on its own page and then again once the rewind moves it to
        /// <c>card</c>'s destination page. The ledger must release the first claim before accepting the
        /// second (<c>FragmentEmitter.EmitSlot</c>'s own release-before-reclaim branch) rather than reading
        /// it as a conflict - this is what proves that branch right, the same way
        /// <see cref="MultiLineHeadingRelocatedByBreakInsideAvoid_IsClaimedOnlyOnce_Reproduces1047"/> proves
        /// the OTHER branch (no release at all before the second claim) wrong.
        /// </summary>
        [Theory]
        [InlineData(105)]
        [InlineData(115)]
        [InlineData(125)]
        public async Task PulledRun_DoesNotTripTheWordClaimLedger(double fillerHeight)
        {
            var (_, _, container) = await PulledRunAsync(fillerHeight, prepare: EnableWordClaimLedger);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        /// <summary>
        /// Opts this container into <c>FragmentEmitter</c>'s per-slot word-claim ledger
        /// (<c>HtmlContainerInt.VerifyWordClaims</c>), off by default even in a DEBUG build - see that
        /// property's own remarks for why it needs an explicit opt-in rather than running for every test.
        /// </summary>
        private static void EnableWordClaimLedger(CssBox root) => root.HtmlContainer!.VerifyWordClaims = true;
#endif

        /// <summary>
        /// Issue #1047's own reduction: a <c>break-inside:avoid</c> card whose first child is a heading
        /// that wraps onto more than one line (forced by <c>width:60pt</c>) — UA-chained via the default
        /// <c>break-after:avoid</c> to the sibling right after it — followed by two more one-line siblings.
        /// A single-line heading in the same shape does not reproduce this (see the issue), which is why
        /// none of the fixtures above it in this file — all pinned to a single-line heading — ever caught
        /// it.
        /// </summary>
        /// <remarks>
        /// Deliberately declared outside the <c>#if DEBUG</c> region above, even though the fixture is
        /// #1047's own: <see cref="MultiLineHeadingRelocatedByBreakInsideAvoid_FitsWithinDestinationPage"/>
        /// needs it too, and that test does not use the DEBUG-only word-claim ledger, so it has to run
        /// (and be counted for diff coverage) in a Release build too — see that test's own remarks for why.
        /// </remarks>
        private static string Issue1047Document(double fillerHeight) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<div id='card' style='break-inside:avoid;font-size:10pt;line-height:20pt'>"
                + "<h2 id='heading' style='margin:0;width:60pt'>Aaa Bbb Ccc Ddd Eee Fff</h2>"
                + "<div>Alpha one</div>"
                + "<div>Beta two</div>"
                + "</div>");

        /// <summary>
        /// The residual imprecision left in place when #1047 was fixed:
        /// <c>CanBeLaidOutAgain</c>'s destination-fit check used to read this box's own raw
        /// <c>ActualBottom - Location.Y</c>, which a same-pass keep-with-next restart
        /// (<see cref="TryRestartAt"/>) could inflate by a "phantom gap" — the distance between this box's
        /// own, still-stale top and its first in-flow child's already-relocated one — and wrongly answer
        /// "does not fit", forcing the gap-carrying <see cref="TranslateForEarlyBreak"/> path instead of a
        /// clean re-layout. Swept across the same confirmed band the DEBUG-only ledger tests above sweep:
        /// every point used to overflow its destination page's own content area by as much as 16pt (a full
        /// line) before <c>EffectiveContentTop</c> existed, confirmed by temporary instrumentation (not
        /// kept) reading <c>card.ActualBottom</c> against <c>container.PageBottomOf</c>. Asserting the fit
        /// here, on the exact fixture the phantom gap was found in, is what proves the destination-fit
        /// check itself now answers correctly — not just that every word still lands somewhere on one
        /// page, which the sibling tests above already covered even before this fix.
        /// </summary>
        /// <remarks>
        /// Deliberately declared outside the <c>#if DEBUG</c> region above and without
        /// <c>EnableWordClaimLedger</c>: the mechanism this asserts (<c>TryRestartAt</c> relocating a box's
        /// own first in-flow child) has to be exercised in a Release build too, or the production lines it
        /// covers (<c>CssBox._firstChildRestartedTop</c>'s assignment) are only ever hit by a test region
        /// CI's own coverage job — which builds Release — compiles out entirely, silently failing the
        /// diff-coverage gate despite every framework-local run looking fully covered.
        /// </remarks>
        [Theory]
        [InlineData(84)]
        [InlineData(90)]
        [InlineData(96)]
        [InlineData(244)]
        [InlineData(250)]
        [InlineData(256)]
        public async Task MultiLineHeadingRelocatedByBreakInsideAvoid_FitsWithinDestinationPage(double fillerHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Issue1047Document(fillerHeight), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;
            var page = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var pageBottom = container.PageBottomOf(page);

            Assert.True(
                card.ActualBottom <= pageBottom + 0.01,
                $"card.ActualBottom ({card.ActualBottom}) overflowed its destination page's own content " +
                $"area (bottom {pageBottom}) by {card.ActualBottom - pageBottom}pt.");
        }

        private static string TableAndHeadingCardDocument(double fillerHeight) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<div id='card' style='break-inside:avoid;font-size:10pt;line-height:20pt'>"
                + "<h2 id='heading' style='margin:0'>Heading</h2>"
                + "<table><tr><td>Alpha</td><td>one</td></tr><tr><td>Beta</td><td>two</td></tr></table></div>");

        private static string PaddingPrecedesTableCardDocument(double precedingPadding) =>
            LayoutHarness.Wrap(
                $"<div style='padding-bottom:{precedingPadding}pt'>preceding</div>"
                + "<div id='card' style='break-inside:avoid;font-size:10pt;line-height:20pt;"
                + "border:1pt solid black;padding:4pt'>"
                + "<h2 id='heading' style='margin:0'>Heading</h2>"
                + "<table><tr><td>Alpha</td><td>one</td></tr><tr><td>Beta</td><td>two</td></tr>"
                + "<tr><td>Gamma</td><td>three</td></tr></table></div>");

        private static List<CssRect> WordsIn(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

        /// <summary>
        /// A paragraph that breaks at the first page boundary, followed by two headings each chained to a
        /// block that asks not to be broken.
        /// </summary>
        private static string ResumedParagraphDocument(int leadWords, int cardWords) =>
            LayoutHarness.Wrap(
                "<style>html { font-family: 'Early Break Fixture' }</style>"
                + $"<p id='lead'>{Filler(leadWords, "lead")}</p>"
                + "<h2 id='h1'>Head</h2>"
                + $"<div id='card1' style='break-inside:avoid'>{Filler(cardWords, "a")}</div>"
                + "<h2 id='h2'>Head</h2>"
                + $"<div id='card2' style='break-inside:avoid'>{Filler(cardWords, "b")}</div>");

        private static Task RegisterResumedParagraphFont(PeachPDF.Adapters.PdfSharpAdapter adapter) =>
            BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "Early Break Fixture");

        /// <summary>
        /// Same shape as <see cref="ResumedParagraphDocument"/>, except a <c>marker</c> div with
        /// <c>break-after:page</c> opens the document, ahead of <c>lead</c> - so the pass that places
        /// <c>lead</c>/<c>h1</c>/<c>card1</c> steps its own cursor forward before it ever reaches them
        /// (<c>FragmentainerContext.StepOverTo</c>), and its recorded pass entry names the slot it opened
        /// at (<c>marker</c>'s page), not the one it was filling by the time it placed them (see #384).
        /// </summary>
        private static string ResumedParagraphWithLeadingForcedBreakDocument(int leadWords, int cardWords) =>
            LayoutHarness.Wrap(
                "<div id='marker' style='break-after:page;margin:0'></div>"
                + $"<p id='lead'>{Filler(leadWords, "lead")}</p>"
                + "<h2 id='h1'>Head</h2>"
                + $"<div id='card1' style='break-inside:avoid'>{Filler(cardWords, "a")}</div>"
                + "<h2 id='h2'>Head</h2>"
                + $"<div id='card2' style='break-inside:avoid'>{Filler(cardWords, "b")}</div>");

        /// <summary>
        /// Same shape as <see cref="ResumedParagraphDocument"/>, except the second heading and its card are
        /// wrapped in <c>section2</c> and the heading carries <c>break-before:page</c> - making it a
        /// grandchild, rather than a direct child, of the box the driver's rewind resets when it re-enters
        /// the pass that placed <c>lead</c>/<c>h1</c>/<c>card1</c> (see #434).
        /// </summary>
        private static string ResumedParagraphWithNestedForcedBreakDocument(int leadWords, int cardWords) =>
            LayoutHarness.Wrap(
                $"<p id='lead'>{Filler(leadWords, "lead")}</p>"
                + "<h2 id='h1'>Head</h2>"
                + $"<div id='card1' style='break-inside:avoid'>{Filler(cardWords, "a")}</div>"
                + "<div id='section2' style='margin:0'>"
                + "<div id='section2lead' style='margin:0'>lead-in</div>"
                + "<h2 id='h2' style='break-before:page;margin:0'>Head</h2>"
                + $"<div id='card2' style='break-inside:avoid'>{Filler(cardWords, "b")}</div>"
                + "</div>");

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
