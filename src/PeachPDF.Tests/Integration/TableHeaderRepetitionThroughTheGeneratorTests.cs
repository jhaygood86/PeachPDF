using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A repeating <c>&lt;thead&gt;</c> above, and a repeating <c>&lt;tfoot&gt;</c> below, a row that
    /// continues mid-cell — laid out the way the real generator lays a document out, with an
    /// <c>@page</c> rule and therefore parsed twice
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see>,
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture is the <c>paged_media_table_row_continuation</c> showcase's own markup, because that is
    /// the document the defect was visible in. Two green
    /// <see cref="TableCellBreakTokenTests"/> theories over the same behaviour were not enough to know the
    /// header was wrong here: they run through <see cref="LayoutHarness"/>, which parses once, has no
    /// <c>@page</c> rule and computes its own content band. Nothing else in the suite lays a document out
    /// through <see cref="PdfGeneratorLayoutHarness"/>' path with a repeating header, which is why the
    /// showcase corpus caught what the suite did not.
    /// </para>
    /// <para>
    /// Asserted on the fragment tree rather than on the PDF's content stream: a token can be present while
    /// the thing it draws is in the wrong place, which is the whole of what went wrong here.
    /// </para>
    /// </remarks>
    public class TableHeaderRepetitionThroughTheGeneratorTests
    {
        /// <summary>The showcase's markup, verbatim but for the word count being a parameter.</summary>
        private static string Fixture(int clauses) =>
            """
            <!DOCTYPE html>
            <html><head><style>
            @page { size: a6; margin: 12mm }
            body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
            h1 { font-size: 11pt; margin: 0 0 0.4em }
            p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
            table { width: 100%; border-collapse: collapse }
            th, td { border: 0.75pt solid #94a3b8; padding: 4pt 5pt; vertical-align: top }
            thead th { background: #e2e8f0; font-size: 8pt; text-align: left }
            tfoot td { background: #e2e8f0; font-size: 8pt; text-align: left }
            td.note { width: 30%; color: #6b7280; font-size: 8pt }
            td.body { line-height: 1.5; text-align: justify }
            </style></head><body>
            <h1>One row, several pages</h1>
            <p class="intro">The right-hand cell holds more than a page of text, so the row continues onto
            the next page.</p>
            <table>
              <thead><tr><th>Note</th><th>Clause</th></tr></thead>
              <tfoot><tr><td>Continues</td><td>Overleaf</td></tr></tfoot>
              <tbody><tr>
                <td class="note">Finishes on the first page.</td>
                <td class="body">
            """
            + string.Join(" ", Enumerable.Range(1, clauses).Select(i => $"clause{i}"))
            + """
                </td>
              </tr></tbody>
            </table>
            </body></html>
            """;

        /// <summary>How many <c>clause</c> words the fixture authors — the one place the count lives.</summary>
        private const int Clauses = 260;

        private static async Task<HtmlContainerInt> LayoutFixtureAsync()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Fixture(Clauses), new PdfGenerateConfig { PageSize = PageSize.A6 });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            return container;
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        private static List<TextFragment> WordsIn(FragmentainerFragment fragmentainer) =>
            Flatten(fragmentainer.Root).SelectMany(f => f.Words).ToList();

        /// <summary>
        /// The header is drawn on every page of this document, and once on each — a second proxy for one
        /// page would sit exactly on top of the first, invisible on the page but real in the tree.
        /// </summary>
        /// <remarks>
        /// Every page rather than "every page the table covers", which is the weaker statement: this
        /// fixture's table reaches the last page, so the two coincide and the stronger one is free.
        /// </remarks>
        [Fact]
        public async Task ARepeatingHeader_IsDrawnOnceOnEveryPageTheTableCovers()
        {
            var container = await LayoutFixtureAsync();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var words = WordsIn(fragmentainer);

                Assert.Single(words, w => w.Word.Text == "Note");
                Assert.Single(words, w => w.Word.Text == "Clause");
            }
        }

        /// <summary>
        /// The header sits <i>above</i> the continued cell's content rather than on top of it. This is the
        /// half no count states: the header repeated and every word was claimed exactly once while six of
        /// them were hidden underneath it on each page after the first.
        /// </summary>
        [Fact]
        public async Task AResumedCellsContent_StartsBelowTheRepeatedHeader()
        {
            var container = await LayoutFixtureAsync();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers.Skip(1))
            {
                var words = WordsIn(fragmentainer);

                var header = Assert.Single(words, w => w.Word.Text == "Clause");
                var body = words.Where(w => w.Word.Text!.StartsWith("clause")).ToList();

                Assert.NotEmpty(body);

                Assert.All(body, word => Assert.True(word.Rect.Top >= header.Rect.Bottom,
                    $"'{word.Word.Text}' starts at {word.Rect.Top} in slot {fragmentainer.SlotIndex}, "
                    + $"above the repeated header's bottom edge at {header.Rect.Bottom}"));
            }
        }

        /// <summary>
        /// The footer is drawn on every page of this document, and once on each — the mirror of
        /// <see cref="ARepeatingHeader_IsDrawnOnceOnEveryPageTheTableCovers"/>, which used to measure one
        /// footer over the whole document however many pages the row continued onto.
        /// </summary>
        [Fact]
        public async Task ARepeatingFooter_IsDrawnOnceOnEveryPageTheTableCovers()
        {
            var container = await LayoutFixtureAsync();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var words = WordsIn(fragmentainer);

                Assert.Single(words, w => w.Word.Text == "Continues");
                Assert.Single(words, w => w.Word.Text == "Overleaf");
            }
        }

        /// <summary>
        /// The continued cell's content ends <i>above</i> the repeated footer rather than under it — the
        /// half no count states, mirrored to the other end of the band.
        /// </summary>
        /// <remarks>
        /// Stated of every page but the last, since the last carries the table's own closing footer under
        /// its final row, which has always been placed correctly.
        /// </remarks>
        [Fact]
        public async Task AResumedCellsContent_EndsAboveTheRepeatedFooter()
        {
            var container = await LayoutFixtureAsync();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers.SkipLast(1))
            {
                var words = WordsIn(fragmentainer);

                var footer = Assert.Single(words, w => w.Word.Text == "Overleaf");
                var body = words.Where(w => w.Word.Text!.StartsWith("clause")).ToList();

                Assert.NotEmpty(body);

                Assert.All(body, word => Assert.True(word.Rect.Bottom <= footer.Rect.Top,
                    $"'{word.Word.Text}' ends at {word.Rect.Bottom} in slot {fragmentainer.SlotIndex}, "
                    + $"below the repeated footer's top edge at {footer.Rect.Top}"));
            }
        }

        /// <summary>
        /// Every authored word reaches exactly one page. The reservation above moves content down a
        /// fragmentainer's worth over the document, which is exactly the kind of change that drops the
        /// last words or claims a line twice.
        /// </summary>
        [Fact]
        public async Task TheContinuingRow_ClaimsEveryWordExactlyOnce()
        {
            var container = await LayoutFixtureAsync();

            var claims = new Dictionary<string, int>();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in WordsIn(fragmentainer).Where(w => w.Word.Text!.StartsWith("clause")))
                {
                    claims[word.Word.Text!] = claims.GetValueOrDefault(word.Word.Text!) + 1;
                }
            }

            var authored = Enumerable.Range(1, Clauses).Select(i => $"clause{i}").ToList();

            Assert.All(authored, word => Assert.Equal(1, claims.GetValueOrDefault(word)));
            Assert.Equal(authored.Count, claims.Count);
        }
    }
}
