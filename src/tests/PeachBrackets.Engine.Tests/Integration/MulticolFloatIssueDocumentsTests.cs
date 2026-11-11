using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>The reduced documents of the open float-in-multicol word-loss issues, verbatim: every word must be drawn exactly once.</summary>
public class MulticolFloatIssueDocumentsTests
{
    [Theory]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:300pt 120pt;margin:20pt}body{margin:0;font:10pt Arial} </style></head><body><div style="columns:3"><h1 style=""></h1><div style="float:left"> w7022_117 w7022_118 <div style="margin-top:90pt"></div></div><p style=""> w7022_126 w7022_127 </p></div></body></html>
""", "2316-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:300pt 250pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body> w1002_141 <div style="columns:2"><p style=""> w1002_146 w1002_147 w1002_148 w1002_149 w1002_150 w1002_151 w1002_152 w1002_153 w1002_154 w1002_155 w1002_156 w1002_157 w1002_158 w1002_159 </p><p style=""> w1002_160 w1002_161 w1002_162 w1002_163 w1002_164 w1002_165 w1002_166 w1002_167 w1002_168 w1002_169 w1002_170 w1002_171 w1002_172 w1002_173 w1002_174 </p><p style=""> w1002_175 w1002_176 w1002_177 w1002_178 w1002_179 w1002_180 w1002_181  w1002_182 w1002_183 w1002_184 w1002_185 </p><div style="float:right"> w1002_186 w1002_187 w1002_188  w1002_197 w1002_198 w1002_199 w1002_200 w1002_201 w1002_202 w1002_203 w1002_204 w1002_205  w1002_206 </div><div style="float:left"><p style=""> w1002_236 w1002_237 w1002_238 w1002_239 w1002_240 w1002_241 w1002_242 w1002_243 w1002_244 </p><p style=""> w1002_247 w1002_248 w1002_249 w1002_250 w1002_251 w1002_252 w1002_253  w1002_254 w1002_255 w1002_256 w1002_257 w1002_258  w1002_259  w1002_260 w1002_261 w1002_262  w1002_269  w1002_270 w1002_271 w1002_272  w1002_273 </p> w1002_274  w1002_275  w1002_276 w1002_277 w1002_278 w1002_279 w1002_280 w1002_281 w1002_282 w1002_283 w1002_284 </div><div style="clear:both"></div><p style=""> w1002_292 </p></div></body></html>
""", "2317-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:200pt 200pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><div style="float:left"><p style=""> w6048_1059 w6048_1060 w6048_1061 w6048_1062 w6048_1063 w6048_1064 w6048_1065 w6048_1066 w6048_1067 w6048_1068 w6048_1069 w6048_1070 w6048_1071 w6048_1072 w6048_1073 w6048_1074 w6048_1075 w6048_1076 w6048_1077 w6048_1078 </p></div><div style="columns:3"><p style=""> w6048_1186 </p></div></div></body></html>
""", "2318-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:300pt 160pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><p style="margin:0 0 3pt"> w5029_381 w5029_382 </p><div style="columns:3"><p style="margin:0 0 3pt"> w5029_548 w5029_549  w5029_552 w5029_553 w5029_554 w5029_555 w5029_556 w5029_557 w5029_558 w5029_559 w5029_560 w5029_561 w5029_562 </p><div style="float:right;height:90pt"></div><p style=""> w5029_623 </p></div><p style=""></p></div></body></html>
""", "2318-2", Skip = "#2318: open, a multi-column container inside a column that is filled in two outer columns still loses its last paragraph")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:400pt 200pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="float:right;width:60%;height:230pt"></div><div style="columns:4"><div style="columns:4"><p style=""></p><div style="float:right"><p style=""> w5071_146 </p></div><p style=""> w5071_161 </p><p style=""> w5071_185 w5071_186 </p></div></div></body></html>
""", "2318-3", Skip = "#2318: open, a multi-column container inside a column that is filled in two outer columns still loses its last paragraph")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:200pt 250pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><div style="float:right"> w5064_86 w5064_87 w5064_88 w5064_89 w5064_90 w5064_91 w5064_92 </div><p style=""></p><div style="float:left;width:50%"><span style="display:inline-block"> w5064_108 </span> w5064_115 </div><p style=""> w5064_129 w5064_130 </p></div></body></html>
""", "2326-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:300pt 250pt;margin:20pt}body{margin:0;font:10pt Arial} </style></head><body><div style="columns:3"><table style=""><tbody><tr><td style=""><div style="display:grid"><div style="border:1pt dotted #888"><div style="float:right"><p style=""> w6045_757 w6045_758 w6045_759 w6045_760 w6045_761 w6045_762 w6045_763  w6045_764 w6045_765 w6045_766 w6045_767 w6045_768 </p><p style=""> w6045_769 w6045_770 w6045_771 w6045_772 w6045_773 w6045_774 w6045_775 w6045_776 w6045_777 w6045_778 w6045_779 w6045_780  w6045_781  w6045_782 w6045_783 w6045_784 w6045_785 w6045_786 w6045_787 w6045_788 w6045_789  w6045_790 w6045_791 w6045_792 w6045_793 w6045_794 w6045_795 w6045_796 w6045_797 w6045_798 w6045_799 w6045_800 w6045_801 w6045_802 w6045_803 </p><p style="margin:0 0 3pt"> w6045_804 w6045_805 w6045_806  w6045_807 w6045_808 w6045_809 w6045_810  w6045_811 w6045_812  w6045_815 w6045_816 w6045_817 w6045_818 w6045_819 w6045_820 w6045_821 w6045_822 w6045_823 w6045_824 w6045_825 w6045_826 w6045_827 w6045_828  w6045_829  w6045_830 w6045_831 w6045_832 w6045_833 w6045_834 w6045_835 w6045_836 w6045_837  w6045_838 w6045_839 w6045_840  w6045_841 w6045_842 w6045_843  w6045_844 </p></div><p style=""> w6045_848  w6045_872 </p></div></div></td></tr></tbody></table></div></body></html>
""", "2320-1", Skip = "#2320: open, the document still loses or doubles a word")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:200pt 250pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><div style="float:right"> w5064_86 w5064_87 w5064_88 w5064_89 w5064_90 w5064_91 w5064_92 </div><p style=""></p><div style="float:left;width:50%"><span style="display:inline-block"> w5064_108 </span> w5064_115 </div><p style=""> w5064_129 w5064_130 </p></div></body></html>
""", "2320-2")]
    public async Task IssueDocument_DrawsEveryWordOnce(string html, string name)
    {
        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, painted);

        Assert.True(lost.Count == 0, $"{name}: lost {string.Join(' ', lost)}");
        Assert.True(doubled.Count == 0, $"{name}: doubled {string.Join(' ', doubled)}");
    }

    // A paragraph after a tall float that fills its column, on a page after the float's: the float's box (the column it is in on the earlier
    // page) still ends below the line's top, and the line, in the other column, was started at the float's far edge past the container, where
    // it is clipped. The fuzz-found reduction of the first of #2317's two mechanisms (the other, the float's own first fragment, is the skipped row).
    [Fact]
    public async Task ALineInAnotherColumnIsNotStartedAtAFloatOfTheNextColumn()
    {
        static string W(int p, int n) => string.Join(' ', Enumerable.Range(0, n).Select(i => $"w{p}_{p * 1000 + i}"));
        var body = $"<div style=\"columns:2\"><p>{W(10, 15)}</p><div style=\"float:left\"><p>{W(30, 40)}</p></div><p>{W(40, 1)}</p></div>";
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{size:300pt 250pt;margin:20pt}body{margin:0;font:12pt Arial}</style></head><body>" + body + "</body></html>";

        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // The sweep that found #2317's mechanisms: `columns: 2` on 300x250pt pages, 0 to 3 leading paragraphs, an optional right float and a left float of
    // several sizes (one or two paragraphs), then a one-word paragraph. 46 of the 128 documents lost words before the two fixes; a float placed after a
    // float that continues from one column into the next used to start above the column's top (its parent's target was wiped on its first pass).
    [Fact]
    public async Task TwoFloatsAfterParagraphsInColumns_EveryDocumentDrawsEveryWordOnce()
    {
        static string W(int p, int n) => string.Join(' ', Enumerable.Range(0, n).Select(i => $"w{p}_{p * 1000 + i}"));
        List<string> bad = [];

        foreach (var lead in new[] { 0, 1, 2, 3 })
        foreach (var rightWords in new[] { 0, 3, 14, 40 })
        foreach (var leftWords in new[] { 20, 40, 70, 110 })
        foreach (var leftParas in new[] { 1, 2 })
        {
            var inner = new System.Text.StringBuilder();
            for (var i = 0; i < lead; i++) inner.Append($"<p>{W(10 + i, 15)}</p>");
            if (rightWords > 0) inner.Append($"<div style=\"float:right\">{W(20, rightWords)}</div>");
            inner.Append("<div style=\"float:left\">");
            for (var i = 0; i < leftParas; i++) inner.Append($"<p>{W(30 + i, leftWords / leftParas)}</p>");
            inner.Append("</div><p>" + W(40, 1) + "</p>");
            var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{{size:300pt 250pt;margin:20pt}}body{{margin:0;font:12pt Arial}}</style></head><body><div style=\"columns:2\">{inner}</div></body></html>";

            var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);
            if (lost.Count + doubled.Count > 0) bad.Add($"lead={lead} right={rightWords} left={leftWords} paras={leftParas}: lost {lost.Count}, doubled {doubled.Count}");
        }

        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }
}
