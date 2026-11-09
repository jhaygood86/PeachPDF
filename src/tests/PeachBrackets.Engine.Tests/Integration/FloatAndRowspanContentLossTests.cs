using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// Two content-loss reductions where a word was laid out and then drawn on no page: a line beside a float that begins right of the
/// line's start, and the content of a row-spanning cell that is aligned in a band it does not fit under a repeated group. Both assert
/// that every word in the markup is visible exactly once, which is what a reader of the PDF sees.
/// </summary>
public class FloatAndRowspanContentLossTests
{
    // CSS 2.1 §9.5: a line box beside a float is shortened to the float's margin box whenever their vertical extents overlap, wherever the
    // float starts. Here the first float ends on page 1 and the second, which starts at 60% of the width, goes on to page 2: the paragraph's
    // last lines on page 2 are still beside it and were laid out over its words, which left the last word on no page.
    [Theory]
    [InlineData("<div style=\"float:left;width:60%\"> w1009_160 </div><div style=\"float:left;width:40%\"><p> w1009_202 w1009_203 w1009_204 w1009_205 w1009_206 w1009_207 w1009_208 w1009_209 w1009_210 </p></div><p> w1009_214 w1009_215 w1009_216 w1009_217 </p>")]
    [InlineData("<div style=\"float:left;width:60%;height:230pt\"> w1009_160 </div><div style=\"float:left;width:40%\"><p> w1009_202 w1009_203 w1009_204 w1009_205 w1009_206 w1009_207 w1009_208 w1009_209 w1009_210 w1009_211 w1009_212 w1009_213 w1009_214 w1009_215 w1009_216 w1009_217 w1009_218 w1009_219 w1009_220 w1009_221 w1009_222 w1009_223 w1009_224 w1009_225 w1009_226 w1009_227 w1009_228 w1009_229 w1009_230 w1009_231 w1009_232 w1009_233 w1009_234 w1009_235 w1009_236 w1009_237 w1009_238 w1009_239 w1009_240 w1009_241 w1009_242 w1009_243 w1009_244 w1009_245 w1009_246 w1009_247 w1009_248 w1009_249 w1009_250 w1009_251 w1009_252 w1009_253 w1009_254 w1009_255 w1009_256 w1009_257 w1009_258 w1009_259 w1009_260 w1009_261 </p></div><p> w1009_300 w1009_301 w1009_302 w1009_303 w1009_304 w1009_305 w1009_306 w1009_307 </p>")]
    [InlineData("<div style=\"float:left;width:50%;height:230pt\"> w1009_160 </div><div style=\"float:left;width:50%\"><p> w1009_202 w1009_203 w1009_204 w1009_205 w1009_206 w1009_207 w1009_208 w1009_209 w1009_210 w1009_211 w1009_212 w1009_213 w1009_214 w1009_215 w1009_216 w1009_217 w1009_218 w1009_219 w1009_220 w1009_221 w1009_222 w1009_223 w1009_224 w1009_225 w1009_226 w1009_227 w1009_228 w1009_229 w1009_230 w1009_231 w1009_232 w1009_233 w1009_234 w1009_235 w1009_236 w1009_237 w1009_238 w1009_239 w1009_240 w1009_241 w1009_242 w1009_243 w1009_244 w1009_245 w1009_246 w1009_247 w1009_248 w1009_249 w1009_250 w1009_251 w1009_252 w1009_253 w1009_254 w1009_255 w1009_256 w1009_257 w1009_258 w1009_259 w1009_260 w1009_261 </p></div><p> w1009_300 w1009_301 w1009_302 w1009_303 w1009_304 w1009_305 w1009_306 w1009_307 </p>")]
    [InlineData("<div style=\"float:left;width:60%\"> w1009_160 </div><div style=\"float:left;width:30%\"><p> w1009_202 w1009_203 w1009_204 w1009_205 w1009_206 w1009_207 w1009_208 w1009_209 w1009_210 </p></div><p> w1009_214 w1009_215 w1009_216 w1009_217 </p>")]
    public async Task ALineBesideAFloatThatStartsRightOfTheLineStart_KeepsEveryWordOnce(string body)
    {
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{size:300pt 160pt;margin:20pt}body{margin:0;font:10pt Arial}</style></head><body>"
                   + body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);

        Assert.Equal(PaintedWords.InMarkup(html), visible);
    }

    // css-tables-3 §6.1: a cell that is split has no free room to distribute. The extent measured for a row-spanning cell under a repeated
    // <thead>/<tfoot> includes the room that group takes on each page, so centring or bottom-aligning moved the content down by room that was
    // not there and the text after the page-tall block was drawn on no page.
    [Theory]
    [InlineData("middle", "thead")]
    [InlineData("bottom", "thead")]
    [InlineData("middle", "tfoot")]
    [InlineData("bottom", "tfoot")]
    public async Task ARowspanCellTallerThanAPageUnderARepeatedGroup_KeepsEveryWordOnce(string align, string group)
    {
        var repeated = $"<{group}><tr><th>HEAD1</th><th>HEAD2</th></tr></{group}>";
        var body = $"<tbody><tr><td rowspan=\"2\" style=\"vertical-align:{align}\"><div style=\"height:900px\">w2_1 w2_2</div> w2_3</td>"
                   + "<td><div style=\"height:40px\">w2_4</div></td></tr><tr><td><div style=\"height:40px\">w2_5</div></td></tr></tbody>";
        var table = group == "thead" ? repeated + body : body + repeated;

        var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>@page{size:300px 400px;margin:20px}body{margin:0;font:10px/12px Arial}</style></head><body>"
                   + "<table style=\"border-collapse:separate;border-spacing:0;table-layout:fixed;width:260px\"><colgroup><col style=\"width:100px\"><col style=\"width:160px\"></colgroup>"
                   + table + "</table></body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);

        Assert.Equal(PaintedWords.InMarkup(html), visible);
    }

    // The cell that ends inside its first band, with room to spare, keeps its alignment: only a split cell is left at the top.
    [Fact]
    public async Task AMiddleAlignedRowspanCellThatFitsItsBand_StillCentresItsContent()
    {
        const string Html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>@page{size:300px 400px;margin:20px}body{margin:0;font:10px/12px Arial}</style></head><body>"
                            + "<table style=\"border-collapse:separate;border-spacing:0;table-layout:fixed;width:260px\"><colgroup><col style=\"width:100px\"><col style=\"width:160px\"></colgroup>"
                            + "<thead><tr><th>HEAD</th><th>HEAD</th></tr></thead><tbody><tr><td id=\"cell\" rowspan=\"2\" style=\"vertical-align:middle\"><div id=\"content\">w3_1</div></td>"
                            + "<td><div style=\"height:100px\">w3_2</div></td></tr><tr><td><div style=\"height:100px\">w3_3</div></td></tr></tbody></table></body></html>";

        var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(Html, new PeachPDF.PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        var cell = LayoutHarness.FindById(root, "cell");
        var content = LayoutHarness.FindById(root, "content");

        Assert.NotNull(cell);
        Assert.NotNull(content);
        // Two 100px rows leave about 190px of room under a 12px line: centred, the content sits well below the cell's top.
        Assert.True(content!.Location.Y > cell!.ClientTop + 50, $"the content sits {content.Location.Y - cell.ClientTop} from the cell top");
    }
}
