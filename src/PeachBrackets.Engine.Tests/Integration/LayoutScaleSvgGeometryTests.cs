using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// Inline SVG artwork drawn at a layout scale other than 1 must land where and as large as at 1: the page-space bounds of what each
/// content stream paints (paths filled or stroked, images), read back with a small content-stream interpreter, must not move.
/// </summary>
public class LayoutScaleSvgGeometryTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAFklEQVR4nGNkYGiwYWCAIBYQAQO4OQBKpAH2m4wgfgAAAABJRU5ErkJggg==";

    private static async Task<string> PdfAtAsync(string svg, double pixelsPerInch)
    {
        var generator = new PeachPDF.PdfGenerator();
        var config = new PeachPDF.PdfGenerateConfig { PageSize = PeachPDF.PageSize.A4, CompressContentStreams = false, PixelsPerInch = pixelsPerInch };
        var document = await generator.GeneratePdf($"<!DOCTYPE html><html><body style='margin:0'>{svg}</body></html>", config);
        using var stream = new MemoryStream();
        document.Save(stream);
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    /// <summary>Walks every content stream, tracking q/Q/cm, and reports <paramref name="onPaint"/> for each painting operator and image draw.</summary>
    private static List<double[]> Interpret(string pdf, bool images)
    {
        var result = new List<double[]>();
        foreach (Match stream in Regex.Matches(pdf, @"stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline))
        {
            var ctm = Matrix3x2.Identity;
            var saved = new Stack<Matrix3x2>();
            var points = new List<Vector2>();
            var operands = new List<string>();
            foreach (var token in stream.Groups[1].Value.Split([' ', '', '
'], StringSplitOptions.RemoveEmptyEntries))
            {
                // An operand is a number or a name; anything else is an operator, which takes the operands read since the last one.
                if (token[0] == '/' || float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    operands.Add(token);
                    continue;
                }

                float Arg(int i) => float.Parse(operands[i], CultureInfo.InvariantCulture);
                Vector2 Map(int i) => Vector2.Transform(new Vector2(Arg(i), Arg(i + 1)), ctm);

                switch (token)
                {
                    case "q": saved.Push(ctm); break;
                    case "Q": if (saved.Count > 0) ctm = saved.Pop(); break;
                    case "cm" when operands.Count == 6:
                        ctm = new Matrix3x2(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5)) * ctm;
                        break;
                    case "m" or "l" when operands.Count == 2:
                        points.Add(Map(0));
                        break;
                    case "re" when operands.Count == 4:
                        points.AddRange([Map(0), Vector2.Transform(new Vector2(Arg(0) + Arg(2), Arg(1) + Arg(3)), ctm)]);
                        break;
                    case "c" when operands.Count == 6:
                        points.AddRange([Map(0), Map(2), Map(4)]);
                        break;
                    case "f" or "f*" or "S" or "B" or "B*" or "b" or "s" when !images:
                        if (points.Count > 0)
                            result.Add(Bounds(points));
                        points.Clear();
                        break;
                    case "n": points.Clear(); break;
                    case "Do" when images && operands.Count == 1 && operands[0].StartsWith("/I", StringComparison.Ordinal):
                        result.Add(Bounds([Vector2.Transform(Vector2.Zero, ctm), Vector2.Transform(Vector2.One, ctm)]));
                        break;
                }

                operands.Clear();
            }
        }

        return result;
    }

    private static double[] Bounds(List<Vector2> points) =>
        [Math.Round(points.Min(p => p.X), 1), Math.Round(points.Min(p => p.Y), 1), Math.Round(points.Max(p => p.X), 1), Math.Round(points.Max(p => p.Y), 1)];

    private static async Task AssertSameAtEveryScaleAsync(string svg, bool images)
    {
        var at72 = Interpret(await PdfAtAsync(svg, 72), images);
        Assert.NotEmpty(at72);

        foreach (var ppi in new[] { 96d, 144d })
        {
            var other = Interpret(await PdfAtAsync(svg, ppi), images);
            Assert.Equal(at72.Count, other.Count);
            for (var i = 0; i < at72.Count; i++)
                Assert.All(at72[i].Zip(other[i]), pair => Assert.Equal(pair.First, pair.Second, 1.0));
        }
    }

    [Theory]
    [InlineData("<svg width='200' height='100'><svg x='20' y='10' width='80' height='50' viewBox='0 0 40 25'><rect width='40' height='25' fill='blue'/></svg></svg>")]
    [InlineData("<svg width='200' height='100'><g transform='translate(40,20) scale(1.5)'><rect width='60' height='30' fill='red'/></g></svg>")]
    [InlineData("<svg width='200' height='100'><defs><rect id='r' width='40' height='20'/></defs><use href='#r' x='30' y='30' fill='green'/></svg>")]
    [InlineData("<svg width='200' height='100'><defs><symbol id='s' viewBox='0 0 10 10'><rect width='10' height='10' fill='purple'/></symbol></defs><use href='#s' x='30' y='10' width='60' height='60'/></svg>")]
    [InlineData("<svg width='200' height='100'><rect x='20' y='10' width='120' height='70' fill='none' stroke='black' stroke-width='6'/></svg>")]
    [InlineData("<svg width='200' height='100'><text x='10' y='50' font-size='24' text-decoration='underline line-through'>Under</text></svg>")]
    [InlineData("<svg width='200' height='100' viewBox='0 0 100 50'><rect x='10' y='10' width='30' height='20' fill='red'/></svg>")]
    [InlineData("<svg width='200' height='100'><defs><marker id='mk' markerWidth='10' markerHeight='10' refX='5' refY='5'><rect width='10' height='10' fill='red'/></marker></defs><path d='M20 20 L100 80' stroke='black' marker-end='url(#mk)'/></svg>")]
    [InlineData("<svg width='200' height='100'><foreignObject x='20' y='10' width='120' height='60'><div xmlns='http://www.w3.org/1999/xhtml' style='background:#9f9;width:100px;height:30px'></div></foreignObject></svg>")]
    public Task PaintedGeometry_DoesNotDependOnPixelsPerInch(string svg) => AssertSameAtEveryScaleAsync(svg, images: false);

    [Theory]
    [InlineData("x='20' y='10' width='80' height='60'")]
    [InlineData("x='20' y='10' width='120' height='60' preserveAspectRatio='xMidYMid slice'")]
    public Task EmbeddedImage_KeepsItsPlacement(string attributes) =>
        AssertSameAtEveryScaleAsync($"<svg width='200' height='100'><image href='{Png}' {attributes}/></svg>", images: true);
}
