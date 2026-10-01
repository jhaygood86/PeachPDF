using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Fragments
{
    /// <summary>
    /// <c>FragmentEmitter</c>'s pruning skips a subtree it has concluded has nothing left to show, and every
    /// word such a wrong conclusion skips is drawn on no page. Each fixture here is a reduced fuzz document
    /// in which pruning dropped words that the same layout places, one per way the conclusion went wrong;
    /// each asserts that every word reaches the fragment tree.
    /// </summary>
    /// <remarks>
    /// The fixtures keep the reduced documents' own words and measure them in Liberation Sans, which has
    /// Arial's metrics, because the line breaks decide which box ends up on which page.
    /// </remarks>
    public class FragmentPruningKeepsContentTests
    {
        private const string Font = "PruningKeepsContentSans";

        // A plain inline box's ActualBottom is not geometry: the inline flow places only its line rectangles
        // and words. The text box inside this float read 57.5pt while its words were two pages further
        // down, so it was concluded finished and pruned from the page that holds them.
        [Fact]
        public async Task TextInAFloatBesidePageBreaks_IsDrawn()
        {
            await AssertEveryWordIsPlaced(160,
                "<div style='border:1pt solid #888;display:flow-root'>w20_267 lorem w20_268 w20_269 w20_270 w20_271 w20_272 w20_273 w20_274 w20_275</div>" +
                "<div style='margin-bottom:10pt;float:left;width:120pt'><div style='overflow:hidden;height:300pt'></div>w20_783 lorem w20_784 w20_785 lorem</div>" +
                "<div>w20_786 lorem w20_787 lorem w20_788 lorem w20_789 lorem w20_790</div>" +
                "<p>w20_791 lorem w20_792 lorem w20_793 w20_794 lorem</p>" +
                "<div style='padding:2pt;display:flow-root'><p>w20_1060 w20_1061 w20_1062 w20_1063 w20_1064</p></div>");
        }

        // A box empty throughout a pass's range because its content lies further down, inside a capped scroll
        // container laid out in one piece, was concluded finished at the end of that pass.
        [Fact]
        public async Task ContentBelowAPassesRange_IsDrawn()
        {
            await AssertEveryWordIsPlaced(250,
                "<div style='height:137.8pt'>w130_1</div>" +
                "<div style='border:1pt solid #888;break-inside:avoid'>w130_2 w130_3 w130_4 w130_5 w130_6 w130_7 w130_8<div style='border:1pt solid #888;display:flow-root'><p>w130_65 w130_66 w130_67 w130_68 w130_69 w130_70</p><p>w130_71 w130_72 w130_73 w130_74</p></div></div>" +
                "<div style='border:1pt solid #888;overflow:scroll;max-height:100pt'><p>w130_195 w130_196 w130_197 w130_198 w130_199 w130_200 w130_201 w130_202</p><div style='border:1pt solid #888'><ul><li><p>w130_203 w130_204 w130_205 w130_206 w130_207 w130_208 w130_209 w130_210 w130_211 w130_212</p><div style='padding:12pt;display:flow-root'>w130_227</div></li></ul></div></div>" +
                "w130_470 w130_471 w130_472 w130_473 w130_474 w130_475 w130_476 w130_477 w130_478");
        }

        // A box the pass stopped inside has not had its height applied, so its ActualBottom is its top. Read as
        // "ended inside this range" by the end-of-pass check the test above needs, the flow-root box around
        // this flex container was concluded finished, and the flex item's text on the pages after it was
        // pruned. Guards the break-chain exclusion that check needs; it fails with that exclusion removed.
        [Fact]
        public async Task ABoxThePassStoppedInside_KeepsItsLaterContent()
        {
            await AssertEveryWordIsPlaced(200,
                "w50557_332 w50557_333 w50557_334 w50557_335 w50557_336 w50557_337 w50557_338 w50557_339 w50557_340 w50557_341" +
                "<p>w50557_342 w50557_343 w50557_344 w50557_345 w50557_346 w50557_347 w50557_348</p>" +
                "<h3>w50557_349 w50557_350 w50557_351 w50557_352 w50557_353 w50557_354 w50557_355 w50557_356 w50557_357</h3>" +
                "<div style='margin-bottom:10pt;overflow:scroll;max-height:40pt'><p>w50557_358 w50557_359 w50557_360 w50557_361 w50557_362 w50557_363</p></div>" +
                "<p>w50557_470 w50557_471 w50557_472 w50557_473 w50557_474 w50557_475 w50557_476 w50557_477 w50557_478 w50557_479</p>" +
                "<div style='padding:5pt;display:flow-root'><div style='display:flex'><div style='flex:1'><div style='padding:12pt;border:1pt solid #888'>w50557_524 w50557_525 w50557_526<div>w50557_535 w50557_536 w50557_537 w50557_538 w50557_539 w50557_540</div><p>w50557_541 w50557_542 w50557_543 w50557_544</p></div></div>w50557_626 w50557_627</div></div>");
        }

        // A plain inline box has no geometry of its own to judge by (see SettledBottomOf), and a box inside
        // columns and a capped scroll container is judged against slots the pass has not reached. This is the
        // smallest document found in which removing that judgement lost words. It is a fuzz reduction and still
        // loses 11 of its 136 words to other causes, so it asserts the 68 words that this pruning alone dropped.
        [Fact]
        public async Task FloatBesideColumnsOfCappedBoxes_KeepsTheWordsPruningDropped()
        {
            static string Words(int from, int to) => string.Join(' ', Enumerable.Range(from, to - from + 1).Select(i => $"w723_{i}"));

            var dropped = ("132 133 134 136 137 138 139 140 141 142 143 144 146 147 148 149 150 151 152 153 154 155 157 158 159 160 161 162 163 164 " +
                           "165 166 168 169 172 173 174 175 176 177 178 180 181 182 183 184 185 186 187 188 189 191 192 193 194 195 196 197 198 199 " +
                           "200 201 202 203 204 205 206 207").Split(' ').Select(i => $"w723_{i}");

            await AssertWordsArePlaced(170,
                "<style>p{margin:0 0 4pt} td{vertical-align:top}</style>" +
                "<div style='float:right;width:94pt;margin:3pt'><div><div style='overflow:scroll'><div style='display:grid;grid-template-columns:1fr 1fr'>" +
                $"<div><div style='height:29pt'>w723_33</div></div><div><p>{Words(34, 44)}</p></div><div><p>{Words(45, 53)}</p></div></div></div></div></div>" +
                "<div style='columns:3;column-gap:8pt'><div style='overflow:auto;height:51pt;max-height:260pt;padding:5pt'><div>" +
                $"<div style='break-inside:avoid'><p>{Words(93, 131)}</p><p>{Words(132, 143)}</p></div>" +
                $"<div style='overflow:hidden;padding:5pt'><table><tr><td><p>{Words(144, 169)}</p></td><td>{Words(170, 171)}</td></tr></table>" +
                $"<p>{Words(172, 176)}</p><p>{Words(177, 206)}</p></div></div><div style='height:37pt'>w723_207</div></div></div>",
                dropped);
        }

        private static async Task AssertEveryWordIsPlaced(double pageHeight, string body)
        {
            await AssertWordsArePlaced(pageHeight, body, Regex.Matches(body, @"w\d+_\d+").Select(m => m.Value).Distinct());
        }

        private static async Task AssertWordsArePlaced(double pageHeight, string body, IEnumerable<string> expected)
        {
            var html = "<!DOCTYPE html><html><head></head>" +
                       $"<body style='margin:0;font-family:\"{Font}\";font-size:10pt;line-height:12pt'>{body}</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: pageHeight, margin: 20,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, Font));

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Select(w => w.Word.Text)
                .ToHashSet();

            Assert.All(expected, word => Assert.Contains(word, placed));
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
    }
}
