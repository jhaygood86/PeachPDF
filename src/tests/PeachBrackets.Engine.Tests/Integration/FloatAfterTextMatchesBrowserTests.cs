using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A float placed after text on its line (CSS 2.1 section 9.5, 9.5.1 rule 6): a browser moves the line beside the float when it fits there, and puts the
/// float below the line when it does not, the text after it going on along the line it was on (#1784). Each row is a generated document whose word
/// positions were measured in Chromium (the first twelve words, in CSS pixels: <c>index:x:y</c>); a corpus of 294 such documents (a float of three widths and
/// two heights on either side, after 3 to 20 words and before 3 to 25, with and without <c>white-space: nowrap</c>, and two left floats after one run) differed from
/// Chromium in 790 words before this and in none after it. A float inside a multi-column container is not covered.
/// </summary>
public class FloatAfterTextMatchesBrowserTests
{
    private const double Px = 0.75;

    private static IEnumerable<CssRect> WordsOf(CssBox box)
    {
        foreach (var word in box.Words) yield return word;

        foreach (var child in box.Boxes)
        {
            foreach (var word in WordsOf(child)) yield return word;
        }
    }

    [Theory]
    [InlineData("t1=3 left w=60 h=20 t2=10 nowrap=0", "<div style='width:300px;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><div style='float:left;width:60px;height:20px'></div><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><span class='w' data-i='13'>w013 </span></div>", "1:60.0:0.0;2:94.7:0.0;3:129.4:0.0;4:164.1:0.0;5:198.8:0.0;6:233.5:0.0;7:268.2:0.0;8:60.0:15.0;9:94.7:15.0;10:129.4:15.0;11:164.1:15.0;12:197.8:15.0")]
    [InlineData("t1=3 right w=260 h=20 t2=10 nowrap=0", "<div style='width:300px;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><div style='float:right;width:260px;height:20px'></div><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><span class='w' data-i='13'>w013 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:0.0:15.0;10:0.0:30.0;11:0.0:45.0;12:33.7:45.0")]
    [InlineData("t1=8 left w=60 h=20 t2=3 nowrap=0", "<div style='width:300px;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><div style='float:left;width:60px;height:20px'></div><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:60.0:15.0;10:94.7:15.0;11:129.4:15.0")]
    [InlineData("t1=12 right w=150 h=40 t2=10 nowrap=0", "<div style='width:300px;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><div style='float:right;width:150px;height:40px'></div><span class='w' data-i='13'>w013 </span><span class='w' data-i='14'>w014 </span><span class='w' data-i='15'>w015 </span><span class='w' data-i='16'>w016 </span><span class='w' data-i='17'>w017 </span><span class='w' data-i='18'>w018 </span><span class='w' data-i='19'>w019 </span><span class='w' data-i='20'>w020 </span><span class='w' data-i='21'>w021 </span><span class='w' data-i='22'>w022 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:0.0:15.0;10:34.7:15.0;11:69.4:15.0;12:103.1:15.0")]
    [InlineData("t1=3 left w=60 h=20 t2=3 nowrap=1", "<div style='width:300px;white-space:nowrap;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><div style='float:left;width:60px;height:20px'></div><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span></div>", "1:60.0:0.0;2:94.7:0.0;3:129.4:0.0;4:164.1:0.0;5:198.8:0.0;6:233.5:0.0")]
    [InlineData("t1=3 left w=150 h=40 t2=10 nowrap=1", "<div style='width:300px;white-space:nowrap;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><div style='float:left;width:150px;height:40px'></div><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><span class='w' data-i='13'>w013 </span></div>", "1:150.0:0.0;2:184.7:0.0;3:219.4:0.0;4:254.1:0.0;5:288.8:0.0;6:323.5:0.0;7:358.2:0.0;8:392.9:0.0;9:427.6:0.0;10:462.3:0.0;11:497.0:0.0;12:530.8:0.0")]
    [InlineData("t1=12 left w=60 h=20 t2=10 nowrap=1", "<div style='width:300px;white-space:nowrap;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><div style='float:left;width:60px;height:20px'></div><span class='w' data-i='13'>w013 </span><span class='w' data-i='14'>w014 </span><span class='w' data-i='15'>w015 </span><span class='w' data-i='16'>w016 </span><span class='w' data-i='17'>w017 </span><span class='w' data-i='18'>w018 </span><span class='w' data-i='19'>w019 </span><span class='w' data-i='20'>w020 </span><span class='w' data-i='21'>w021 </span><span class='w' data-i='22'>w022 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:277.6:0.0;10:312.3:0.0;11:347.0:0.0;12:380.8:0.0")]
    [InlineData("two-left t1=12 t2=10", "<div style='width:300px'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><div style='float:left;width:90px;height:30px'></div><div style='float:left;width:90px;height:30px'></div><span class='w' data-i='13'>w013 </span><span class='w' data-i='14'>w014 </span><span class='w' data-i='15'>w015 </span><span class='w' data-i='16'>w016 </span><span class='w' data-i='17'>w017 </span><span class='w' data-i='18'>w018 </span><span class='w' data-i='19'>w019 </span><span class='w' data-i='20'>w020 </span><span class='w' data-i='21'>w021 </span><span class='w' data-i='22'>w022 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:90.0:15.0;10:124.7:15.0;11:159.4:15.0;12:193.1:15.0")]
    [InlineData("t1=20 left w=260 h=40 t2=25 nowrap=0", "<div style='width:300px;'><span class='w' data-i='1'>w001 </span><span class='w' data-i='2'>w002 </span><span class='w' data-i='3'>w003 </span><span class='w' data-i='4'>w004 </span><span class='w' data-i='5'>w005 </span><span class='w' data-i='6'>w006 </span><span class='w' data-i='7'>w007 </span><span class='w' data-i='8'>w008 </span><span class='w' data-i='9'>w009 </span><span class='w' data-i='10'>w010 </span><span class='w' data-i='11'>w011 </span><span class='w' data-i='12'>w012 </span><span class='w' data-i='13'>w013 </span><span class='w' data-i='14'>w014 </span><span class='w' data-i='15'>w015 </span><span class='w' data-i='16'>w016 </span><span class='w' data-i='17'>w017 </span><span class='w' data-i='18'>w018 </span><span class='w' data-i='19'>w019 </span><span class='w' data-i='20'>w020 </span><div style='float:left;width:260px;height:40px'></div><span class='w' data-i='21'>w021 </span><span class='w' data-i='22'>w022 </span><span class='w' data-i='23'>w023 </span><span class='w' data-i='24'>w024 </span><span class='w' data-i='25'>w025 </span><span class='w' data-i='26'>w026 </span><span class='w' data-i='27'>w027 </span><span class='w' data-i='28'>w028 </span><span class='w' data-i='29'>w029 </span><span class='w' data-i='30'>w030 </span><span class='w' data-i='31'>w031 </span><span class='w' data-i='32'>w032 </span><span class='w' data-i='33'>w033 </span><span class='w' data-i='34'>w034 </span><span class='w' data-i='35'>w035 </span><span class='w' data-i='36'>w036 </span><span class='w' data-i='37'>w037 </span><span class='w' data-i='38'>w038 </span><span class='w' data-i='39'>w039 </span><span class='w' data-i='40'>w040 </span><span class='w' data-i='41'>w041 </span><span class='w' data-i='42'>w042 </span><span class='w' data-i='43'>w043 </span><span class='w' data-i='44'>w044 </span><span class='w' data-i='45'>w045 </span></div>", "1:0.0:0.0;2:34.7:0.0;3:69.4:0.0;4:104.1:0.0;5:138.8:0.0;6:173.5:0.0;7:208.2:0.0;8:242.9:0.0;9:0.0:15.0;10:34.7:15.0;11:69.4:15.0;12:103.1:15.0")]
    public async Task TheWordsAreWhereABrowserPutsThem(string name, string body, string expected)
    {
        var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 13px Arial }}</style></head><body>{body}</body></html>";
        var renderContext = new HtmlRenderContext();
        await BundledFonts.PinSansSerifAsync(renderContext);
        var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
        await container.SetHtml(html, null);

        var size = new Size(600, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        var words = WordsOf(container.Root!)
            .Where(w => w.Text?.Trim() is { Length: 4 } t && t[0] == 'w' && int.TryParse(t.AsSpan(1), out _))
            .ToDictionary(w => int.Parse(w.Text!.Trim().AsSpan(1)));

        // A line's top differs from the span's by a constant: the first word's is taken as the origin.
        var firstTop = double.NaN;

        foreach (var item in expected.Split(';'))
        {
            var parts = item.Split(':');
            var index = int.Parse(parts[0]);
            var x = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var y = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            var word = words[index];

            if (double.IsNaN(firstTop)) firstTop = word.Top / Px - y;

            Assert.True(Math.Abs(word.Left / Px - x) <= 1.0, $"{name}: w{index:000} is at x={word.Left / Px:0.#}px, a browser has it at {x:0.#}px");
            Assert.True(Math.Abs(word.Top / Px - firstTop - y) <= 3.0, $"{name}: w{index:000} is at y={word.Top / Px - firstTop:0.#}px, a browser has it at {y:0.#}px");
        }
    }
}
