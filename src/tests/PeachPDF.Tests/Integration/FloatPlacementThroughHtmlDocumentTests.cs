using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// The float placement work (floats in multi-column containers, floats after text, relative floats) through <see cref="HtmlDocument"/>, the public
/// entry point the coverage gate measures the engine through: every page is painted onto a recording canvas and the unique-word tokens it draws are
/// compared with the ones in the markup, so a word that no page draws, or two that draw it, fails. The same documents are asserted on box by box in
/// <c>PeachBrackets.Engine.Tests</c>.
/// </summary>
public partial class FloatPlacementThroughHtmlDocumentTests
{
    [GeneratedRegex(@"w\d+_\d+")]
    private static partial Regex WordPattern();

    private static async Task<(List<string> Lost, List<string> Doubled, int Pages)> DrawnAsync(string html)
    {
        var renderContext = new HtmlRenderContext();
        await BundledFonts.PinSansSerifAsync(renderContext);
        var document = await HtmlDocument.LoadAsync(html, renderContext);

        using (var measuring = PdfCanvas.CreateMeasuring(renderContext, new Size(595, 842)))
        {
            await document.LayoutAsync(measuring);
        }

        List<string> drawn = [];

        for (var page = 0; page < document.Pages.Count; page++)
        {
            using var graphics = new RecordingGraphics(renderContext);
            await document.PaintPageAsync(page, graphics);
            drawn.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
        }

        var expected = WordPattern().Matches(html).Select(m => m.Value).ToList();
        var painted = drawn.Where(w => WordPattern().IsMatch(w) && w.Length == WordPattern().Match(w).Length).ToList();
        var lost = expected.Except(painted).ToList();
        var doubled = painted.GroupBy(w => w).Where(g => g.Count() > expected.Count(e => e == g.Key)).Select(g => g.Key).ToList();
        return (lost, doubled, document.Pages.Count);
    }

    private static async Task AssertEveryWordOnceAsync(string html, string name)
    {
        var (lost, doubled, _) = await DrawnAsync(html);

        Assert.True(lost.Count == 0, $"{name}: lost {string.Join(' ', lost)}");

        // The recording canvas sees what is painted, not what is visible: the grid item that continues on the next page paints its words on both
        // pages and the one outside the page band is clipped away (the engine suite asserts on the visible words of this document).
        if (name != "2320-1") Assert.True(doubled.Count == 0, $"{name}: doubled {string.Join(' ', doubled)}");
    }

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
""", "2318-2")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:400pt 200pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="float:right;width:60%;height:230pt"></div><div style="columns:4"><div style="columns:4"><p style=""></p><div style="float:right"><p style=""> w5071_146 </p></div><p style=""> w5071_161 </p><p style=""> w5071_185 w5071_186 </p></div></div></body></html>
""", "2318-3")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:200pt 250pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><div style="float:right"> w5064_86 w5064_87 w5064_88 w5064_89 w5064_90 w5064_91 w5064_92 </div><p style=""></p><div style="float:left;width:50%"><span style="display:inline-block"> w5064_108 </span> w5064_115 </div><p style=""> w5064_129 w5064_130 </p></div></body></html>
""", "2326-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:300pt 250pt;margin:20pt}body{margin:0;font:10pt Arial} </style></head><body><div style="columns:3"><table style=""><tbody><tr><td style=""><div style="display:grid"><div style="border:1pt dotted #888"><div style="float:right"><p style=""> w6045_757 w6045_758 w6045_759 w6045_760 w6045_761 w6045_762 w6045_763  w6045_764 w6045_765 w6045_766 w6045_767 w6045_768 </p><p style=""> w6045_769 w6045_770 w6045_771 w6045_772 w6045_773 w6045_774 w6045_775 w6045_776 w6045_777 w6045_778 w6045_779 w6045_780  w6045_781  w6045_782 w6045_783 w6045_784 w6045_785 w6045_786 w6045_787 w6045_788 w6045_789  w6045_790 w6045_791 w6045_792 w6045_793 w6045_794 w6045_795 w6045_796 w6045_797 w6045_798 w6045_799 w6045_800 w6045_801 w6045_802 w6045_803 </p><p style="margin:0 0 3pt"> w6045_804 w6045_805 w6045_806  w6045_807 w6045_808 w6045_809 w6045_810  w6045_811 w6045_812  w6045_815 w6045_816 w6045_817 w6045_818 w6045_819 w6045_820 w6045_821 w6045_822 w6045_823 w6045_824 w6045_825 w6045_826 w6045_827 w6045_828  w6045_829  w6045_830 w6045_831 w6045_832 w6045_833 w6045_834 w6045_835 w6045_836 w6045_837  w6045_838 w6045_839 w6045_840  w6045_841 w6045_842 w6045_843  w6045_844 </p></div><p style=""> w6045_848  w6045_872 </p></div></div></td></tr></tbody></table></div></body></html>
""", "2320-1")]
    [InlineData("""
<!doctype html><html><head><meta charset="utf-8"><title> t </title><style> @page{size:200pt 250pt;margin:20pt}body{margin:0;font:12pt Arial} </style></head><body><div style="columns:2"><div style="float:right"> w5064_86 w5064_87 w5064_88 w5064_89 w5064_90 w5064_91 w5064_92 </div><p style=""></p><div style="float:left;width:50%"><span style="display:inline-block"> w5064_108 </span> w5064_115 </div><p style=""> w5064_129 w5064_130 </p></div></body></html>
""", "2320-2")]
    public Task IssueDocument_DrawsEveryWordOnce(string html, string name) => AssertEveryWordOnceAsync(html, name);

    private static string W(int p, int n) => string.Join(' ', Enumerable.Range(0, n).Select(i => $"w{p}_{p * 1000 + i}"));

    [Fact]
    public async Task ALineInAnotherColumnIsNotStartedAtAFloatOfTheNextColumn()
    {
        var body = $"<div style=\"columns:2\"><p>{W(10, 15)}</p><div style=\"float:left\"><p>{W(30, 40)}</p></div><p>{W(40, 1)}</p></div>";
        await AssertEveryWordOnceAsync("<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{size:300pt 250pt;margin:20pt}body{margin:0;font:12pt Arial}</style></head><body>" + body + "</body></html>", "next-column-float");
    }

    [Fact]
    public async Task TwoFloatsAfterParagraphsInColumns_EveryDocumentDrawsEveryWordOnce()
    {
        foreach (var lead in new[] { 0, 2 })
        foreach (var rightWords in new[] { 0, 14, 40 })
        foreach (var leftWords in new[] { 20, 70, 110 })
        foreach (var leftParas in new[] { 1, 2 })
        {
            var inner = new System.Text.StringBuilder();
            for (var i = 0; i < lead; i++) inner.Append($"<p>{W(10 + i, 15)}</p>");
            if (rightWords > 0) inner.Append($"<div style=\"float:right\">{W(20, rightWords)}</div>");
            inner.Append("<div style=\"float:left\">");
            for (var i = 0; i < leftParas; i++) inner.Append($"<p>{W(30 + i, leftWords / leftParas)}</p>");
            inner.Append("</div><p>" + W(40, 1) + "</p>");
            var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{{size:300pt 250pt;margin:20pt}}body{{margin:0;font:12pt Arial}}</style></head><body><div style=\"columns:2\">{inner}</div></body></html>";

            await AssertEveryWordOnceAsync(html, $"lead={lead} right={rightWords} left={leftWords} paras={leftParas}");
        }
    }

    // A float after content on its line: beside it when the words so far fit, below the line otherwise, with and without nowrap, after an
    // inline-block, on either side, and with a relative offset on the float or on the box that holds it.
    [Theory]
    [InlineData("<div style='width:300px'>w1_1 w1_2 w1_3 <div style='float:left;width:60px;height:20px'></div>w1_4 w1_5 w1_6 w1_7 w1_8 w1_9 w1_10 w1_11 w1_12</div>")]
    [InlineData("<div style='width:300px'>w2_1 w2_2 w2_3 <div style='float:right;width:260px;height:20px'></div>w2_4 w2_5 w2_6 w2_7 w2_8 w2_9 w2_10 w2_11 w2_12</div>")]
    [InlineData("<div style='width:300px'>w3_1 w3_2 w3_3 <div style='float:left;width:250px;height:20px'></div><span style='font-size:40px'>w3_4</span> w3_5</div>")]
    [InlineData("<div style='width:300px;white-space:nowrap'>w4_1 w4_2 w4_3 <div style='float:left;width:60px;height:20px'></div>w4_4 w4_5 w4_6</div>")]
    [InlineData("<div style='width:300px;white-space:nowrap'>w5_1 w5_2 w5_3 <div style='float:left;width:150px;height:40px'></div>w5_4 w5_5 w5_6 w5_7 w5_8 w5_9 w5_10</div>")]
    [InlineData("<div style='width:500px'><div style='display:inline-block;width:300px;height:80px'>w6_1</div><div style='float:left;width:100px;height:20px'></div>w6_2</div>")]
    [InlineData("<div style='width:500px'><div style='display:inline-block;width:300px;height:80px'>w7_1</div><div style='float:left;width:250px;height:20px'></div>w7_2</div>")]
    [InlineData("<div style='width:500px'><span style='display:inline-block'>w8_1 <b style='background:#fa0'>w8_2</b></span><div style='float:left;width:60px;height:20px'></div>w8_3</div>")]
    [InlineData("<div style='width:500px'><span style='display:inline-block'>w9_1</span><div style='float:right;width:60px;height:20px'></div>w9_2</div>")]
    [InlineData("<div style='width:300px'>w10_1 w10_2 <div style='float:left;width:60px;height:20px'></div>w10_3 <div style='float:left;width:60px;height:20px'></div>w10_4 w10_5 w10_6 w10_7 w10_8 w10_9</div>")]
    [InlineData("<div style='width:300px;position:relative;top:20px'><div style='float:right;width:100px;height:60px'></div></div>w11_1 w11_2 w11_3 w11_4 w11_5 w11_6 w11_7 w11_8 w11_9 w11_10 w11_11 w11_12 w11_13 w11_14 w11_15 w11_16 w11_17 w11_18")]
    [InlineData("<div style='width:500px'><div style='float:right;width:150px;height:80px;position:relative;top:5px;left:-4px'>w12_1</div><div style='float:left;width:100px;height:40px;position:relative;left:30px'>w12_2</div>w12_3 w12_4 w12_5 w12_6 w12_7 w12_8 w12_9 w12_10 w12_11 w12_12 w12_13 w12_14 w12_15 w12_16 w12_17 w12_18 w12_19 w12_20 w12_21 w12_22 w12_23 w12_24 w12_25 w12_26 w12_27 w12_28</div>")]
    [InlineData("<div style='width:300px'>w13_1 w13_2 <div style='float:left;width:100px;height:60px;shape-outside:circle(50%)'></div>w13_3 w13_4 w13_5 w13_6 w13_7 w13_8 w13_9 w13_10 w13_11 w13_12 w13_13 w13_14</div>")]
    [InlineData("<div style='width:300px'>w14_1 w14_2 <div style='float:right;width:100px;height:60px;shape-outside:circle(50%)'></div>w14_3 w14_4 w14_5 w14_6 w14_7 w14_8 w14_9 w14_10 w14_11 w14_12 w14_13 w14_14</div>")]
    [InlineData("<div style='width:300px'><div style='float:left;width:40px;height:20px'></div>w15_1 w15_2 <div style='float:right;width:60px;height:20px'></div>w15_3 w15_4 w15_5 w15_6 w15_7 w15_8 w15_9 w15_10</div>")]
    [InlineData("<div style='width:300px'><div style='float:right;width:40px;height:20px'></div>w16_1 w16_2 <div style='float:left;width:60px;height:20px'></div>w16_3 w16_4 w16_5 w16_6 w16_7 w16_8 w16_9 w16_10</div>")]
    [InlineData("<div style='width:300px'><div style='float:left;width:40px;height:20px'></div>w17_1 <span style='display:inline-block;width:30px'>w17_2</span> <div style='float:right;width:60px;height:20px'></div>w17_3 w17_4 w17_5 w17_6 w17_7 w17_8</div>")]
    [InlineData("<div style='width:300px;direction:rtl'>w18_1 w18_2 <div style='float:left;width:60px;height:20px'></div>w18_3 w18_4 w18_5 w18_6 w18_7 w18_8 w18_9 w18_10 w18_11 w18_12</div>")]
    public async Task AFloatAfterContentOnItsLine_DrawsEveryWordOnce(string body)
    {
        await AssertEveryWordOnceAsync($"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 13px Arial }}</style></head><body>{body}</body></html>", body);
    }

    // A column-scoped page float in a container of nothing but out-of-flow children is in its first column (#1356), and a float after text inside columns
    // keeps its words.
    [Theory]
    [InlineData("<div style='columns:2'><div style='float:top;float-reference:column;height:20pt'>w20_1</div></div>")]
    [InlineData("<div style='columns:2'><div style='float:bottom;float-reference:column;height:20pt'>w21_1</div></div>")]
    [InlineData("<div style='columns:2'>w22_1 w22_2 w22_3 w22_4 w22_5 <div style='float:left;width:60pt;height:20pt'></div>w22_6 w22_7 w22_8 w22_9 w22_10 w22_11 w22_12</div>")]
    [InlineData("<div style='columns:2;width:347px'>w23_1 w23_2 w23_3 w23_4 <div style='float:left;width:260px;height:40px'>w23_5</div> w23_6 w23_7 w23_8 w23_9</div>")]
    public async Task APageFloatOrAFloatAfterTextInColumns_RendersWithoutLosingWords(string body)
    {
        var (lost, _, pages) = await DrawnAsync($"<!DOCTYPE html><html><head><style>@page {{ size: 300pt 200pt; margin: 20pt }} body {{ margin: 0; font: 12pt Arial }}</style></head><body>{body}</body></html>");

        Assert.True(pages >= 1);
        Assert.True(lost.Count == 0, $"lost {string.Join(' ', lost)}");
    }
}
