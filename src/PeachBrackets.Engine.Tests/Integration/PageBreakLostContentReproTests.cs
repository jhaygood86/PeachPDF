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
        private static async Task AssertEveryWordIsInTheTreeOnceAsync(string html, int? expectedPages = null)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

            var inTree = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .Select(w => w.Word.Text?.Trim() ?? string.Empty)
                .ToList();

            if (expectedPages is { } pages)
            {
                Assert.Equal(pages, container.FragmentTree.Fragmentainers.Count);
            }
            else
            {
                Assert.True(container.FragmentTree.Fragmentainers.Count > 1, "the document should need several pages");
            }

            var expected = PaintedWords.InMarkup(html);
            var lost = expected.Where(w => !inTree.Contains(w)).ToList();
            var doubled = expected.Where(w => inTree.Count(t => t == w) > 1).ToList();

            Assert.True(lost.Count == 0, $"lost: {string.Join(", ", lost)}");
            Assert.True(doubled.Count == 0, $"doubled: {string.Join(", ", doubled)}");

            // The tree can hold a word once and a page still paint it twice (a fragment reached by two walks).
            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var paintedTwice = painted.GroupBy(w => w).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(paintedTwice.Count == 0, $"painted twice: {string.Join(", ", paintedTwice)}");
        }


        /// <summary>
        /// Each authored word must be drawn once and be <i>visible</i>: inside its page's content area and inside the
        /// overflow clip of the box that holds it. A word the tree holds but a clip or the page foot hides is lost to a
        /// reader of the PDF, which is what a PDF text extraction (the way the issues were measured) reports.
        /// </summary>
        private static async Task AssertEveryWordIsVisibleOnceAsync(string html)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
            var visible = new List<string>();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var band = fragmentainer.Rect;

                foreach (var box in Flatten(fragmentainer.Root))
                {
                    foreach (var word in box.Words)
                    {
                        var r = word.Rect;
                        // A heading's first line at the top of a page has a glyph box about 0.7pt above its line box.
                        var inBand = r.Bottom <= band.Bottom + 0.5 && r.Top >= band.Top - 1.0
                            && r.Left >= band.Left - 1.0 && r.Right <= band.Right + 1.0;
                        var inClip = box.OverflowClip is not { } clip || (r.Bottom <= clip.Bottom + 0.5 && r.Top >= clip.Top - 1.0
                            && r.Left >= clip.Left - 1.0 && r.Right <= clip.Right + 1.0);
                        if (inBand && inClip) visible.Add(word.Word.Text?.Trim() ?? string.Empty);
                    }
                }
            }

            var expected = PaintedWords.InMarkup(html).Distinct().ToList();
            var lost = expected.Where(w => !visible.Contains(w)).ToList();
            var doubled = expected.Where(w => visible.Count(v => v == w) > 1).ToList();

            Assert.True(lost.Count == 0, $"not visible on any page: {string.Join(", ", lost)}");
            Assert.True(doubled.Count == 0, $"visible more than once: {string.Join(", ", doubled)}");
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

        private const string Page300x200 = "@page { size: 300pt 200pt; margin: 20pt; } body { margin: 0; font: 10pt/12pt Arial; }";

        private static string Document(string css, string body) =>
            "<!DOCTYPE html><html><head><style>" + css + "</style></head><body>" + body + "</body></html>";

        private static string Numbered(string prefix, int count, string separator) =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"w{prefix}_{i}{separator}"));

        [Fact]
        public async Task ABlockBesideAFloatTallerThanAPage_KeepsEveryLine()
        {
            // Issue 1339: the block's first 26 lines were drawn on no page.
            await AssertEveryWordIsInTheTreeOnceAsync(Document(Page300x200,
                $"<div style=\"float:right;width:80pt\">{Numbered("1", 30, "<br>")}</div><div>{Numbered("2", 30, "<br>")}</div>"));
        }

        [Fact]
        public async Task AnAbsolutelyPositionedColumnsBoxAsItsBlocksFirstChild_KeepsTheLastLines()
        {
            // Issue 1376: W18-W20 were drawn on no page.
            await AssertEveryWordIsInTheTreeOnceAsync(Document(Page300x200 + " p { margin: 0 }",
                "<div style=\"position:absolute;top:120pt;width:200pt;columns:2\">" +
                string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>w3_{i}</p>")) + "</div>"));
        }

        [Fact]
        public async Task ASubtreeWithContentOnALaterPage_IsNotPrunedFromIt()
        {
            // Issue 1487: fragment pruning dropped w384_1172..w384_1183 (and others) from the page that holds them.
            await AssertEveryWordIsInTheTreeOnceAsync(Document(
                "@page { size: 300pt 250pt; margin: 20pt; } body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; }",
                "<div style=\"height:145.5pt\">w384_1</div>" +
                "<div style=\"display:inline-block;width:150pt\"><table border=\"1\"><tr><td>w384_387 w384_388 lorem w384_389 w384_390 w384_391 w384_392</td><td>w384_393 lorem w384_394 lorem w384_395</td></tr></table></div>" +
                "<div style=\"column-count:2\"><ul><li>w384_470 lorem w384_471 w384_472 w384_473 w384_474 w384_475</li></ul></div>" +
                "w384_1097 w384_1098 lorem " +
                "<div style=\"margin-top:20pt;margin-bottom:20pt;padding:12pt\"><div style=\"display:flex\"><div><div style=\"padding:12pt;border:1pt solid #888;float:left;width:120pt\">" +
                "<p>w384_1099 lorem w384_1100 lorem w384_1101 w384_1102 lorem w384_1103 w384_1104</p><div style=\"margin-top:4pt;padding:2pt\">" +
                "<div style=\"margin-top:20pt;break-inside:avoid;display:flow-root\"><ul><li><h3>w384_1117 lorem w384_1118 lorem w384_1119 w384_1120 w384_1121 lorem w384_1122 lorem</h3>" +
                "w384_1123 w384_1124 w384_1125 lorem w384_1126 lorem<p>w384_1127 w384_1128 w384_1129 w384_1130 w384_1131 lorem w384_1132 w384_1133 w384_1134 w384_1135 w384_1136 w384_1137 lorem</p></li>" +
                "<li>w384_1138 lorem w384_1139 w384_1140 lorem w384_1141 lorem w384_1142 lorem w384_1143 w384_1144 lorem</li></ul></div>" +
                "<div>w384_1145 lorem w384_1146 w384_1147 w384_1148 lorem w384_1149 w384_1150 w384_1151 lorem w384_1152 w384_1153 w384_1154 w384_1155 w384_1156" +
                "<h3>w384_1157 w384_1158 w384_1159 w384_1160 lorem w384_1161 w384_1162 w384_1163</h3>w384_1164 lorem w384_1165 w384_1166 lorem w384_1167 w384_1168 w384_1169 w384_1170 w384_1171</div>" +
                "w384_1172 lorem w384_1173 w384_1174 lorem w384_1175 lorem w384_1176 lorem w384_1177 lorem w384_1178 lorem w384_1179 w384_1180 w384_1181 w384_1182 lorem w384_1183 lorem</div></div>" +
                "<div>w384_1254 w384_1255 w384_1256 lorem w384_1257 w384_1258</div></div></div></div>"));
        }

        [Fact]
        public async Task AColumnsContainerInABreakInsideAvoidBlock_DrawsEachColumnOnce()
        {
            // Issue 1436: text was drawn twice. The three pages against a browser's one is a separate pagination difference.
            await AssertEveryWordIsInTheTreeOnceAsync(Document(
                "@page{size:240pt 150.5pt;margin:20pt}body{margin:0;font:10pt/10pt Arial} h2{font-size:13pt}",
                "<div style='border:1px solid #666;padding:0pt;margin:0 0 4pt;break-inside:avoid;'><div style='columns:2;column-gap:6pt'>" +
                "w530_147 w530_148 w530_149 w530_150 w530_151 w530_152 w530_153 w530_154 w530_155 " +
                "<h3 style='font-size:13pt;margin:6pt 0 4pt;'>w530_164 w530_165 w530_166 w530_167 w530_168 w530_169 w530_170 w530_171 w530_172 w530_173 w530_174 w530_175 w530_176</h3></div></div>"));
        }

        [Fact]
        public async Task ALineMovedPastTheFootOfACappedScrollContainer_IsNotClippedAway()
        {
            // Issue 1373: the box (max-height 50pt) broke at the foot and widows moved lines to the next page, where
            // the clip still ended a cap's distance below the box's first fragment and hid w4_186.
            await AssertEveryWordIsVisibleOnceAsync(Document(Page300x200 + " p { margin: 0 }",
                "w4_0<p>w4_1</p>" + string.Concat(Enumerable.Repeat("<br>", 18)) +
                "w4_158 w4_159 w4_160 w4_161 w4_162<p>w4_163 w4_164</p>w4_165 w4_166 w4_167 w4_168 w4_169 w4_170" +
                "<div style=\"overflow:hidden;max-height:50pt\"><br><br><br>w4_186</div>" +
                "<div style=\"break-inside:avoid\"><ul>w4_187 w4_188 w4_189<li>w4_190 w4_191 w4_192 w4_193</li></ul>" + string.Concat(Enumerable.Repeat("<br>", 5)) +
                " w4_201 w4_202<div style=\"break-before:page\"></div></div><div style=\"float:right;width:60pt\"></div>"));
        }

        [Fact]
        public async Task AMiddleAlignedCellInARowTallerThanItsBand_KeepsItsLastHeadingLineOffTheFoot()
        {
            // Issue 1485: centring the cell moved the heading's second line 4.5pt over the page foot, so w890_643
            // was cut by the page and drawn on none.
            await AssertEveryWordIsVisibleOnceAsync(Document(
                "@page { size: 300pt 160pt; margin: 20pt; } body { margin: 0; font-family: Arial; font-size: 10pt; line-height: 12pt; }",
                "<p>w890_567 w890_568 lorem w890_569</p>w890_570 w890_571 <div><h3>w890_572 w890_573 w890_574 w890_575 lorem</h3></div>" +
                "<p>w890_576 w890_577 w890_578 w890_579 w890_580 w890_581 w890_582 w890_583 w890_584 w890_585 lorem</p>" +
                "w890_586 w890_587 w890_588 lorem w890_589 w890_590 lorem w890_591 w890_592 w890_593" +
                "<div style=\"margin-top:10pt;margin-bottom:0pt;padding:2pt;\"><p>w890_594 w890_595 w890_596 w890_597 lorem w890_598 w890_599 w890_600 lorem w890_601 w890_602 w890_603 lorem</p>" +
                "<p>w890_604 w890_605 w890_606 w890_607</p>w890_608 lorem w890_609<p>w890_610 w890_611 lorem w890_612 w890_613</p></div>" +
                "<h3>w890_614 w890_615 lorem w890_616 lorem w890_617 w890_618 w890_619 w890_620 w890_621 w890_622 w890_623 w890_624 lorem w890_625</h3>" +
                "<div style=\"margin-top:10pt;border:1pt solid #888\"><h3>w890_626 w890_627 w890_628 w890_629</h3></div>" +
                "<table border=\"1\"><tr><td><p>w890_630 w890_631 lorem w890_632 w890_633 w890_634 w890_635 w890_636 lorem w890_637 w890_638 w890_639 w890_640 w890_641 lorem</p>" +
                "<h3>w890_642 lorem w890_643</h3></td><td>w890_644 lorem w890_645 lorem</td></tr></table>"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AFlexItemInColumns_IsNotMovedTwiceWhenItsPassIsLaidOutAgain(bool innerColumns)
        {
            // Issue 1525: the last pass of the flex container was resumed twice from the same break token; the second
            // run moved the already-moved item by the container's move again, from x=20 to x=-158, off the page.
            // Short words keep the inner columns' text inside the page, which the issue's own words are not.
            var paragraphs = string.Concat(Enumerable.Range(1, 4).Select(p =>
                "<p style='margin:0 0 6pt'>" + string.Join(" ", Enumerable.Range(1, 40).Select(i => $"w{p}_{i:000}")) + "</p>"));
            var content = innerColumns ? $"<div style='columns:3;column-gap:8pt'>{paragraphs}</div>" : paragraphs;
            await AssertEveryWordIsVisibleOnceAsync(Document(Page300x200,
                $"<div style='columns:3;column-gap:8pt'><div style='display:flex;gap:4pt'><div style='flex:1'>{content}</div></div></div>"));
        }
    }
}
