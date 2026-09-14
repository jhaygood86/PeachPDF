using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A line belongs to the fragmentainer in which layout kept it (issue #446). The line box is the
    /// fragmentation unit; a word's content rectangle can extend beyond it because of negative leading, so
    /// the emitter must not turn either kind of overhang into membership of the next band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tolerance window is 0.5pt wide. These fixtures do not depend on platform font metrics to happen
    /// to reach it: they lay the document out once to measure where the last line box of the first page
    /// actually falls, then lay it out again on a band shortened so that same line overhangs by a quarter of
    /// a point. That lands inside the window on every platform, and the fixtures assert that it did before
    /// asserting anything else.
    /// </para>
    /// </remarks>
    public class BandMembershipToleranceTests
    {
        /// <summary>How far past the band bottom the tuned fixture puts the first page's last line.</summary>
        private const double Overhang = 0.25;

        private const string Paragraph =
            "<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F}</p>";

        /// <summary>
        /// The invariant, over a document whose first page ends inside the disagreement window: every word
        /// the document authored is claimed by exactly one fragment.
        /// </summary>
        [Theory]
        [InlineData(Paragraph)]
        [InlineData("<p style='orphans:1;widows:1;font-size:10pt;line-height:20pt'>{F} <b>bold words carried across the break</b> {F}</p>")]
        [InlineData("<div style='orphans:1;widows:1;font-size:10pt;line-height:20pt'><span style='color:red'>{F}</span></div>")]
        public async Task ALineOverhangingItsBandWithinTolerance_IsClaimedByOnePageOnly(string template)
        {
            var (root, container) = await LayoutTunedToTheWindowAsync(template);

            var authored = WordsIn(root);
            var claimed = ClaimedWords(container);

            Assert.NotEmpty(authored);
            Assert.True(claimed.Count == claimed.Distinct(ReferenceEqualityComparer.Instance).Count(),
                DescribeDoubleClaims(container));
            Assert.Equal(authored.Count, claimed.Count);
        }

        /// <summary>
        /// The same statement in the direction the abstract invariant cannot distinguish: the overhanging
        /// line belongs to the page layout kept it on, and to that page only.
        /// </summary>
        /// <remarks>
        /// The expected page is found by scanning the grid's raw band coordinates for the one that strictly
        /// contains the word's top, rather than by calling <c>SlotStartingAt</c>. Asking the production
        /// helper would restate the predicate under test verbatim, so the assertion would hold for any rule
        /// of that shape — including the over-broad one that also claimed lines layout never had the chance
        /// to move (issue #477). The fixture's overhang is a quarter point against a line more than ten
        /// points tall, so strict containment is unambiguous and needs no tolerance of its own.
        /// </remarks>
        [Fact]
        public async Task TheOverhangingLine_IsClaimedByThePageItsTopStartsIn_AndByNoOther()
        {
            var (_, container) = await LayoutTunedToTheWindowAsync(Paragraph);

            var overhanging = LinesOverhangingTheFirstBand(container).ToList();
            Assert.NotEmpty(overhanging);

            foreach (var line in overhanging)
            {
                var expected = BandStrictlyContaining(container, LineTop(line));

                // The rule is only doing work if the next band really is a candidate — that is, if the line
                // box overhangs into it at all.
                Assert.True(LineBottom(line) > container.PageBottomOf(expected),
                    "the line does not reach the next band, so it cannot show a double claim");

                Assert.All(line.Words,
                    word => Assert.Equal([expected], SlotsClaiming(container, word)));
            }
        }

        /// <summary>
        /// The one slot whose band contains <paramref name="y"/>, by raw coordinate comparison against the
        /// page grid — deliberately not <c>SlotStartingAt</c>, whose behaviour is what the caller asserts.
        /// </summary>
        private static int BandStrictlyContaining(HtmlContainerInt container, double y)
        {
            foreach (var slot in container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex))
            {
                if (y >= container.PageTopOf(slot) && y < container.PageBottomOf(slot)) return slot;
            }

            Assert.Fail($"no band strictly contains {y}");
            return -1;
        }

        /// <summary>
        /// Lays the fixture out twice: once to find where the first page's last line falls, then again on a
        /// band shortened so that line's bottom lands <see cref="Overhang"/> past it — inside the window
        /// where layout tolerates the overhang but a raw overlap test calls it the next band's.
        /// </summary>
        private static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutTunedToTheWindowAsync(
            string template)
        {
            const double margin = 10;
            const double probeHeight = 850;

            var (_, probe) = await LayoutHarness.LayoutAsync(
                Document(template), pageHeight: probeHeight, margin: margin);

            var lastBottom = LastLineBottomOnTheFirstPage(probe);
            var slack = probe.PageBottomOf(0) - lastBottom;

            Assert.True(slack >= 0, $"the probe's own last line already overhangs by {-slack}");

            var (root, container) = await LayoutHarness.LayoutAsync(
                Document(template), pageHeight: probeHeight - slack - Overhang, margin: margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            // The fixture is only meaningful if it landed in the window, so it says so itself rather than
            // silently degrading into a second copy of the ordinary case.
            var overhang = LastLineBottomOnTheFirstPage(container) - container.PageBottomOf(0);

            // Exclusive at the top: at exactly PageBoundaryEpsilon, FallsPast fires and the line becomes
            // StraddlingLineClaimTests' subject rather than this class's.
            Assert.True(overhang > 1e-6 && overhang < HtmlContainerInt.PageBoundaryEpsilon,
                $"the fixture must land strictly inside the tolerance window, not at an overhang of {overhang}");

            return (root, container);
        }

        /// <summary>The lowest line-box edge emitted into the first page.</summary>
        private static double LastLineBottomOnTheFirstPage(HtmlContainerInt container)
        {
            var bottoms = SourceLines(container)
                .Where(line => container.SlotStartingAt(LineTop(line)) == 0)
                .Select(LineBottom)
                .ToList();

            Assert.NotEmpty(bottoms);

            return bottoms.Max();
        }

        /// <summary>The source lines whose emitted line box crosses the first page's band end.</summary>
        private static IEnumerable<CssLineBox> LinesOverhangingTheFirstBand(HtmlContainerInt container)
            => SourceLines(container)
                .Where(line => container.SlotStartingAt(LineTop(line)) == 0
                               && LineBottom(line) > container.PageBottomOf(0));

        private static IEnumerable<CssLineBox> SourceLines(HtmlContainerInt container) =>
            LayoutHarness.Descendants(container.Root!)
                .SelectMany(box => box.LineBoxes)
                .Where(line => line.Words.Count > 0)
                .Distinct<CssLineBox>(ReferenceEqualityComparer.Instance);

        private static double LineTop(CssLineBox line) => line.Words.Min(word => word.Top);

        private static double LineBottom(CssLineBox line) =>
            LineTop(line) + line.OwnerBox.ActualLineHeight;

        private static List<int> SlotsClaiming(HtmlContainerInt container, CssRect word) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).SelectMany(b => b.Words).Any(w => ReferenceEquals(w.Word, word)))
                .Select(f => f.SlotIndex)
                .ToList();

        private static string DescribeDoubleClaims(HtmlContainerInt container)
        {
            var claims = new Dictionary<CssRect, List<int>>(ReferenceEqualityComparer.Instance);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(fragmentainer.Root).SelectMany(f => f.Words))
                {
                    if (!claims.TryGetValue(word.Word, out var slots))
                    {
                        claims[word.Word] = slots = [];
                    }

                    slots.Add(fragmentainer.SlotIndex);
                }
            }

            var doubled = claims.Where(c => c.Value.Count > 1).ToList();

            return $"{doubled.Count} words claimed more than once: " + string.Join("; ", doubled
                .Take(8)
                .Select(c => $"'{c.Key.Text}' by [{string.Join(",", c.Value)}], lives in "
                             + container.SlotStartingAt(c.Key.Top)));
        }

        private static string Document(string template) =>
            LayoutHarness.Wrap(template.Replace(
                "{F}", string.Join(" ", Enumerable.Range(0, 600).Select(i => $"w{i}")))
                + "<div style='break-before:page'>after</div>");

        private static List<CssRect> WordsIn(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).ToList();

        private static List<CssRect> ClaimedWords(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

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
    }
}
