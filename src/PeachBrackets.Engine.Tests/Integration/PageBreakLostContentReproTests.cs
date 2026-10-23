using PeachPDF;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Reductions of fuzz-found documents that lost words at a page break (a float taller than the page, a table cell holding
    /// one, a relaxed <c>break-inside: avoid</c>). Each was reported as a bug, was fixed by the work that let tall floats and
    /// avoided boxes continue across pages, and had no test; they are pinned here so the words stay on a page.
    /// </summary>
    public class PageBreakLostContentReproTests
    {
        /// <summary>
        /// Each authored word's count in the finished fragment tree: one when the document keeps it, none when a page break lost
        /// it. Read off the tree rather than the painted band so that a heading's first line, whose glyph box rises 0.7pt above
        /// the band at the top of a page, is not mistaken for a lost word.
        /// </summary>
        private static async Task AssertEveryWordIsInTheTreeOnceAsync(string html)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

            var inTree = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .Select(w => w.Word.Text?.Trim() ?? string.Empty)
                .ToList();

            Assert.True(container.FragmentTree.Fragmentainers.Count > 1, "the document should need several pages");

            var expected = PaintedWords.InMarkup(html);
            var lost = expected.Where(w => !inTree.Contains(w)).ToList();
            var doubled = expected.Where(w => inTree.Count(t => t == w) > 1).ToList();

            Assert.True(lost.Count == 0, $"lost: {string.Join(", ", lost)}");
            Assert.True(doubled.Count == 0, $"doubled: {string.Join(", ", doubled)}");
        }

        private static IEnumerable<PeachBrackets.Html.Fragments.BoxFragment> Flatten(PeachBrackets.Html.Fragments.BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        private const string Document1481 = @"<!DOCTYPE html><html><head><style>
@page { size: 300pt 160pt; margin: 20pt; }
body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; }
</style></head><body>
<div style=""height:58.6pt"">w554_1</div>
<table border=""1""><tr><td><div style=""break-inside:avoid;float:left;width:120pt""><p>w554_638 w554_639 w554_640 w554_641 w554_642 w554_643 w554_644 w554_645</p><div style=""padding:2pt""><p>w554_753 w554_754 w554_755 w554_756 w554_757 w554_758 w554_759 w554_760</p>w554_761 w554_762 w554_763 w554_764 w554_765 w554_766</div></div><div>w554_767 w554_768 w554_769 w554_770 w554_771 w554_772 w554_773 w554_774 w554_775 w554_776 w554_777</div><ul><li>w554_778 w554_779 w554_780<div style=""margin-top:30pt;padding:12pt;""><p>w554_835</p><h3>w554_840 w554_841 w554_842</h3></div></li><li>w554_880 w554_881 w554_882 w554_883</li></ul></td><td>w554_884 w554_885</td></tr></table>
</body></html>";

        [Fact]
        public async Task AFloatTallerThanThePageInATableCell_KeepsEveryWordOnAPage()
        {
            await AssertEveryWordIsInTheTreeOnceAsync(Document1481);
        }

        private const string Document1482 = @"<!DOCTYPE html><html><head><style>
@page { size: 300pt 200pt; margin: 20pt; }
body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; }
</style></head><body>
<div style=""height:53.4pt"">w382_1</div>
<div>w382_33 w382_34 w382_35 w382_36</div>
<div style=""margin-top:4pt;padding:2pt;float:left;width:120pt""><ul><li><div style=""margin-top:30pt""><ul><li>w382_37 w382_38 w382_39 w382_40 w382_41 w382_42 w382_43<p>w382_44 w382_45 w382_46</p></li><li>w382_47 w382_48 w382_49 w382_50 w382_51 w382_52</li></ul></div></li></ul></div>
<ul><li><h3>w382_458</h3><table border=""1""><tr><td><div><h3>w382_644 w382_645 w382_646 w382_647 w382_648 w382_649 w382_650 w382_651</h3></div></td><td>w382_689 w382_690</td></tr><tr><td>w382_695 w382_696 w382_697</td></tr></table><p>w382_698 w382_699 w382_700 w382_701 w382_702</p></li></ul>
</body></html>";

        [Fact]
        public async Task AParagraphAfterATableBesideATallFloat_KeepsEveryWordOnAPage()
        {
            await AssertEveryWordIsInTheTreeOnceAsync(Document1482);
        }

        private const string Document1483 = @"<!DOCTYPE html><html><head><style>
@page { size: 300pt 160pt; margin: 20pt; }
body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; } p,h3,ul{margin-top:0}
</style></head><body>
<div style=""margin-bottom:4pt;border:1pt solid #888""><div style=""margin-bottom:20pt;padding:12pt;border:1pt solid #888;float:left;width:120pt""><p>w5535_1 w5535_2 w5535_3 lorem w5535_4 lorem</p>w5535_28 w5535_29 w5535_30 w5535_31 w5535_32 lorem w5535_33 w5535_34 w5535_76 lorem w5535_77 w5535_78 w5535_79</div><p>w5535_192</p></div>
<div><p>w5535_193 w5535_194 w5535_195 lorem w5535_196 w5535_197 lorem w5535_198 w5535_199 lorem</p><h3>w5535_200 w5535_201 w5535_202 w5535_203 w5535_204 w5535_205 lorem w5535_206 w5535_207 lorem w5535_208 w5535_209 w5535_210 lorem w5535_211 lorem</h3></div>
</body></html>";

        [Fact]
        public async Task ABlockBesideAnOverhangingFloat_KeepsEveryWordOnAPage()
        {
            await AssertEveryWordIsInTheTreeOnceAsync(Document1483);
        }
    }
}
