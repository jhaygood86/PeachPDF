using System.Text;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// An inline <c>&lt;math&gt;</c> parsed into source elements must produce exactly the document the same markup produces parsed into
    /// boxes (<see cref="HtmlParser.ParseMathAsBoxes"/>): the page content of every page, and the document's tagging when it is on. The
    /// corpus covers the places the markup's boxes used to be read from: selectors, generated content, named strings, bidi, anchors,
    /// and markup the source parser hands back to the ordinary path.
    /// </summary>
    public class MathSourceParityTests
    {
        private const string Formula =
            "<math><mrow><mi>x</mi><mo>=</mo><mfrac><mrow><mo>-</mo><mi>b</mi><mo>&#xB1;</mo><msqrt><msup><mi>b</mi><mn>2</mn></msup>"
            + "<mo>-</mo><mn>4</mn><mi>a</mi><mi>c</mi></msqrt></mrow><mrow><mn>2</mn><mi>a</mi></mrow></mfrac></mrow></math>";

        private const string Table =
            "<math><mtable><mtr><mtd><mn>1</mn></mtd><mtd><mn>2</mn></mtd></mtr><mtr><mtd><mn>3</mn></mtd><mtd><mi>x</mi></mtd></mtr></mtable></math>";

        public static TheoryData<string, string> Documents => new()
        {
            { "plain", $"<p>Before {Formula} after.</p>" },
            { "display", $"<p>Text</p>{Formula.Replace("<math>", "<math display='block'>")}<p>More</p>" },
            { "table", $"<p>{Table}</p>" },
            { "mathcolor", "<p><math><mi mathcolor='red'>x</mi><mo mathcolor='#00f'>+</mo><mn>1</mn></math></p>" },
            { "inherited colour", $"<div style='color: green'><p>{Formula}</p></div>" },
            { "colour on the host", $"<p style='color: #a00'>See {Formula}</p>" },
            { "indented markup", "<p><math>\n  <mrow>\n    <mi>a</mi>\n    <mo>+</mo>\n    <mi>b</mi>\n  </mrow>\n</math></p>" },
            { "semantics", "<p><math><semantics><mrow><mi>x</mi></mrow><annotation encoding='TeX'>x</annotation></semantics></math></p>" },
            { "entities and comments", "<p><math><mi>a&amp;b</mi><!-- c --><mo>&lt;</mo><mn>1</mn></math></p>" },
            { "text under math", "<p><math>loose <mi>x</mi> text</math></p>" },
            { "mixed token content", "<p><math><mtext>a<mspace width='1em'/>b</mtext></math></p>" },
            { "self-closing", "<p><math><mi>x</mi><mspace width='2em'/><mi/><mo>+</mo></math></p>" },
            { "empty math", "<p>a <math></math> b <math/> c</p>" },
            { "unclosed math", "<p>a <math><mi>x</mi>" },

            // Markup the source parser hands back to the ordinary path.
            { "html inside", "<p><math><mtext><b>bold</b> and <i>it</i></mtext></math></p>" },
            { "unknown element", "<p><math><mi>x</mi><foo><mi>y</mi></foo></math></p>" },
            { "uppercase", "<p><math><MI>x</MI><mo>+</mo></math></p>" },
            { "stray end tag", "<p><math><mi>x</mi></mn><mo>+</mo></math> after</p>" },
            { "paragraph end tag", "<div>text <math><mi>x</mi></p><mo>+</mo></math></div>" },
            { "end tag past the math", "<div><span><math><mi>x</mi></span> tail</div>" },
            { "svg inside", "<p><math><semantics><mi>x</mi><annotation-xml encoding='image/svg+xml'><svg width='10' height='10'><rect width='5' height='5'/></svg></annotation-xml></semantics></math></p>" },
            { "nested math", "<p><math><mrow><math><mi>y</mi></math></mrow></math></p>" },
            { "two maths", $"<p>{Formula} and {Table}</p>" },

            // Rules, attributes and selectors that reach the elements.
            { "rule on tokens", $"<style>mi {{ color: red }}</style><p>{Formula}</p>" },
            { "rule on everything", $"<style>math * {{ color: #070 }}</style><p>{Formula}</p>" },
            { "rule by class", "<style>.k { color: blue }</style><p><math><mi class='k'>x</mi><mi>y</mi></math></p>" },
            { "visual rules only", $"<style>mi {{ font-style: normal; padding: 0 1pt }} * {{ margin: 0; box-sizing: border-box }}</style><p>{Formula}</p>" },
            { "color attribute", "<p><math><mi color='red'>x</mi><mi>y</mi></math></p>" },
            { "style attribute", "<p><math><mi style='color: red'>x</mi><mi>y</mi></math></p>" },
            { "id attribute", "<p><a href='#eq'>see</a> <math><mi id='eq'>x</mi></math></p>" },
            { "before on tokens", $"<style>mi::before {{ content: '<' }}</style><p>{Formula}</p>" },
            { "before on the math", "<style>math::before { content: '[' } math::after { content: ']' }</style><p><math><mi>x</mi></math></p>" },
            { "has", $"<style>p:has(mi) {{ color: #a0a }} p:has(math) {{ background: #eef }}</style><p>{Formula}</p><p>none</p>" },
            { "empty and child selectors", "<style>mi:empty { color: red } mrow > mi:first-child { color: blue } mi + mo { color: green }</style><p><math><mrow><mi>x</mi><mo>+</mo><mi/></mrow></math></p>" },
            { "colour and an after box", "<style>math::after { content: ']' } mi { color: red }</style><p><math><mi>x</mi><mo>+</mo></math></p>" },
            { "after box and a before box", "<style>math::before { content: '[' } math::after { content: ']' } mi { color: red }</style><p><math><mi>x</mi></math></p>" },
            { "sixteen or more children", "<style>mi:nth-child(odd) { color: red } mi + mi { color: blue }</style><p><math>" + string.Concat(Enumerable.Range(0, 24).Select(i => $"<mi>{(char)('a' + i % 26)}</mi>")) + "</math></p>" },
            { "first letter rule", "<style>mrow::first-letter { color: red }</style><p><math><mrow>ab<mi>x</mi></mrow></math></p>" },
            { "math in foreignObject", "<p><svg width='100' height='40'><foreignObject width='100' height='40'><math><mi>x</mi></math></foreignObject></svg></p>" },
            { "adjacent math", "<style>math + math { color: red }</style><p><math><mi>x</mi></math><math><mi>y</mi></math></p>" },
            { "container query", "<style>@container (min-width: 1px) { mi { color: red } }</style><div style='container-type: inline-size'><math><mi>x</mi></math></div>" },
            { "media", "<style>@media print { mn { color: red } }</style><p><math><mn>1</mn><mi>x</mi></math></p>" },
            { "counters", "<style>mi { counter-increment: v } p::after { content: counter(v) }</style><p><math><mi>x</mi><mi>y</mi></math></p>" },
            { "custom properties", "<style>:root { --c: red } mi { --c: blue; font-weight: bold }</style><p><math><mi>x</mi></math></p>" },

            // Readers of an element's text.
            { "string-set", "<style>h1 { string-set: title content(text) } @page { @top-center { content: string(title) } }</style><h1>Equation <math><mi>x</mi><mo>+</mo><mn>2</mn></math></h1><p>text</p>" },
            { "first letter", "<style>p::first-letter { font-size: 200%; color: red }</style><p><math><mi>x</mi></math> then text</p>" },
            { "dir auto", "<p dir='auto'><math><mi>x</mi></math> שלום</p>" },
            { "dir auto hebrew token", "<p dir='auto'><math><mi>א</mi><mo>+</mo><mi>x</mi></math> abc</p>" },
            { "dir auto hebrew in a row", "<p dir='auto'><math><mrow>אב</mrow><mi>x</mi></math> abc</p>" },
            { "dir auto after a before box", "<style>p::before { content: 'a' }</style><p dir='auto'><math><mi>א</mi></math></p>" },
            { "dir auto with only latin math", "<p dir='auto'><math><mi>x</mi></math></p>" },
            { "rtl with math", "<p dir='rtl'>שלום <math><mi>x</mi><mo>+</mo><mn>1</mn></math> עולם</p>" },
            { "arabic token", "<p><math><mtext>مرحبا</mtext><mo>+</mo><mi>x</mi></math></p>" },
            { "bidi isolate", "<p>abc <bdi><math><mi>x</mi></math></bdi> אבג</p>" },
            { "text transform", "<p style='text-transform: uppercase'>a <math><mi>x</mi></math> b</p>" },
            { "hyphenation", "<p lang='en' style='hyphens: auto; width: 60pt'>Incomprehensibilities <math><mi>x</mi></math> misunderstandings</p>" },

            // Other contexts.
            { "in a list", $"<ul><li>{Formula}</li><li>{Table}</li></ul>" },
            { "in a table cell", $"<table><tr><td>{Formula}</td><td>plain</td></tr></table>" },
            { "floated", $"<div style='float: left; width: 120pt'>{Formula}</div><p>flow text {Formula}</p>" },
            { "flex item", $"<div style='display: flex; gap: 6pt'><div>{Formula}</div><div>{Table}</div></div>" },
            { "columns", $"<div style='columns: 2'><p>{Formula}</p><p>{Table}</p><p>{Formula}</p></div>" },
            { "page breaks", string.Concat(Enumerable.Range(0, 6).Select(i => $"<p style='margin-bottom: 120pt'>Row {i} {Formula}</p>")) },
            { "scaled", $"<p style='font-size: 24pt'>{Formula}</p><p style='font-size: 8pt'>{Formula}</p>" },
            { "sized", "<p><math style='width: 100pt; height: 30pt'><mi>x</mi></math></p>" },
            { "display none", "<p>a <math style='display: none'><mi>x</mi></math> b</p>" },
        };

        private static async Task<string> RenderAsync(string html, bool asBoxes, bool tagged)
        {
            HtmlParser.ParseMathAsBoxes.Value = asBoxes;
            try
            {
                var document = await new PdfGenerator().GeneratePdf(
                    $"<!DOCTYPE html><html lang='en'><body style='margin: 0'>{html}</body></html>",
                    new PdfGenerateConfig { PageSize = PageSize.A5, CompressContentStreams = false, EnableTaggedPdf = tagged });

                var content = new StringBuilder();
                for (var i = 0; i < document.PdfDocument.PageCount; i++)
                {
                    content.Append("\n%% page ").Append(i).Append('\n');

                    foreach (var item in document.PdfDocument.Pages[i].Contents.Elements)
                    {
                        if (item is PdfReference { Value: PdfDictionary { Stream: { } stream } })
                            content.Append(Encoding.Latin1.GetString(stream.Value));
                    }
                }

                return content.ToString();
            }
            finally
            {
                HtmlParser.ParseMathAsBoxes.Value = false;
            }
        }

        [Theory]
        [MemberData(nameof(Documents))]
        public async Task SourceElementsAndBoxes_ProduceTheSameDocument(string name, string html)
        {
            var asBoxes = await RenderAsync(html, asBoxes: true, tagged: false);
            var asSource = await RenderAsync(html, asBoxes: false, tagged: false);

            Assert.True(asBoxes == asSource, $"'{name}' differs between the two parses");
            Assert.True(asBoxes.Length > 100, $"'{name}' rendered nothing");
        }

        // What is attached to a tagged formula as its MathML source: the markup read back off whichever tree held it.
        [Theory]
        [MemberData(nameof(Documents))]
        public async Task SourceElementsAndBoxes_SerializeToTheSameMarkup(string name, string html)
        {
            async Task<List<string?>> SerializedAsync(bool asBoxes)
            {
                HtmlParser.ParseMathAsBoxes.Value = asBoxes;
                try
                {
                    var (root, _) = await LayoutHarness.LayoutAsync($"<!DOCTYPE html><html lang='en'><body>{html}</body></html>");
                    var found = new List<CssBoxMath>();
                    Collect(root, found);

                    return found.Select(math => math.SerializedSource).ToList();
                }
                finally
                {
                    HtmlParser.ParseMathAsBoxes.Value = false;
                }
            }

            var asBoxes = await SerializedAsync(true);
            var asSource = await SerializedAsync(false);

            Assert.True(asBoxes.SequenceEqual(asSource), $"'{name}': {string.Join(" | ", asBoxes)} != {string.Join(" | ", asSource)}");
        }

        private static void Collect(CssBox box, List<CssBoxMath> found)
        {
            if (box is CssBoxMath math) found.Add(math);

            foreach (var child in box.Boxes) Collect(child, found);
        }

        [Theory]
        [MemberData(nameof(Documents))]
        public async Task SourceElementsAndBoxes_ProduceTheSameTaggedDocument(string name, string html)
        {
            var asBoxes = await RenderAsync(html, asBoxes: true, tagged: true);
            var asSource = await RenderAsync(html, asBoxes: false, tagged: true);

            Assert.True(asBoxes == asSource, $"'{name}' differs between the two parses when tagged");
        }
    }
}
