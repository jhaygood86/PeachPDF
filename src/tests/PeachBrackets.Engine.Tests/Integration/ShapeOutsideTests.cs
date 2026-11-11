using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// css-shapes-1: <c>shape-outside</c> and <c>shape-margin</c> narrow the lines beside a float to its shape instead of its
/// margin box. Expected line starts were measured in Chrome for the same markup (a 100pt left float in a 200pt box, 10pt/12pt Arial).
/// </summary>
public class ShapeOutsideTests
{
    private static readonly string Text = string.Concat(Enumerable.Repeat("aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo pp qq rr ss tt uu vv ww xx yy zz ", 40));

    private static async Task<(int Top, double Left)[]> LinesAsync(string side, string floatStyle)
    {
        var (root, _) = await LayoutAsync(Wrap(
            $"<div id='c' style='width:200pt;font:10pt/12pt Arial'><div style='float:{side};width:100pt;height:100pt;{floatStyle}'></div>{Text}</div>"));
        var c = FindById(root, "c")!;
        var origin = c.Location;
        return [.. Descendants(c).SelectMany(b => b.Words)
            .GroupBy(w => (int)System.Math.Round(w.Top - origin.Y))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Min(w => w.Left) - origin.X))];
    }

    private static (int Top, double Left)[] Parse(string chrome) => [.. chrome.Split(' ')
        .Select(p => p.Split(':'))
        .Select(p => (int.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)))];

    [Theory]
    [InlineData("shape-outside:circle(50%)", "0:82.5 12:92.7 24:98.0 36:99.9 48:100.0 60:99.0 72:94.9 84:86.6 96:69.6")]
    [InlineData("shape-outside:circle(50%);shape-margin:10pt", "0:96.4 12:100.0 24:100.0 36:100.0 48:100.0 60:100.0 72:100.0 84:99.4 96:88.5")]
    [InlineData("shape-outside:ellipse(30pt 50pt at 40% 50%)", "0:59.5 12:65.6 24:68.8 36:70.0 48:70.0 60:69.4 72:66.9 84:62.0 96:51.8")]
    [InlineData("shape-outside:inset(10pt 20pt 30pt 5pt)", "0:80.0 12:80.0 24:80.0 36:80.0 48:80.0 60:80.0")]
    [InlineData("shape-outside:polygon(0 0, 100% 0, 50% 100%)", "0:100.0 12:94.0 24:88.0 36:82.0 48:76.0 60:70.0 72:64.0 84:58.0 96:52.0")]
    [InlineData("shape-outside:inset(0 round 40pt)", "0:88.6 12:96.7 24:99.8 36:100.0 48:100.0 60:100.0 72:98.1 84:92.0 96:77.4")]
    public async Task LinesBesideALeftFloatFollowItsShape(string style, string chrome)
    {
        var lines = await LinesAsync("left", style);

        foreach (var (top, left) in Parse(chrome))
        {
            var actual = lines.FirstOrDefault(l => l.Top == top);
            Assert.True(actual != default, $"no line at y={top}: {string.Join(" ", lines.Select(l => $"{l.Top}:{l.Left:F1}"))}");
            Assert.True(System.Math.Abs(actual.Left - left) < 0.8, $"line at y={top} starts at {actual.Left:F1}, Chrome {left:F1}");
        }
    }

    [Fact]
    public async Task InsetShapeLeavesLinesBelowItClear()
    {
        // The shape ends 30pt above the float's bottom: lines from y=72 start at the container edge, as in Chrome.
        var lines = await LinesAsync("left", "shape-outside:inset(10pt 20pt 30pt 5pt)");

        Assert.All(lines.Where(l => l.Top >= 72 && l.Top < 100), l => Assert.True(l.Left < 1, $"line at y={l.Top} starts at {l.Left:F1}"));
    }

    [Fact]
    public async Task WithoutAShapeTheMarginBoxIsUsed()
    {
        var lines = await LinesAsync("left", "shape-outside:none");

        Assert.All(lines.Where(l => l.Top < 100), l => Assert.Equal(100, l.Left, 0.5));
    }

    [Fact]
    public async Task AnInvalidShapeIsIgnored()
    {
        var lines = await LinesAsync("left", "shape-outside:circle(banana)");

        Assert.All(lines.Where(l => l.Top < 100), l => Assert.Equal(100, l.Left, 0.5));
    }

    [Fact]
    public async Task RightFloatShapeNarrowsTheLineFromTheRight()
    {
        var (root, _) = await LayoutAsync(Wrap(
            $"<div id='c' style='width:200pt;font:10pt/12pt Arial;text-align:right'><div style='float:right;width:100pt;height:100pt;shape-outside:circle(50%)'></div>{Text}</div>"));
        var c = FindById(root, "c")!;
        var lines = Descendants(c).SelectMany(b => b.Words)
            .GroupBy(w => (int)System.Math.Round(w.Top - c.Location.Y))
            .ToDictionary(g => g.Key, g => g.Max(w => w.Right) - c.Location.X);

        // Mirror of the left circle: a line's right edge is 200 - the circle's left reach (82.5 at the top, 100 at the middle), so the line ends at 117.5 and 100.
        Assert.Equal(200 - 82.5, lines[0], 1.0);
        Assert.Equal(100, lines[48], 1.0);
    }

    [Fact]
    public async Task ShapeMarginIsInheritedByNobody()
    {
        // Neither shape-outside nor shape-margin is inherited: a child of the float does not get its parent's shape.
        var (root, _) = await LayoutAsync(Wrap(
            "<div style='float:left;width:100pt;height:100pt;shape-outside:circle(50%);shape-margin:5pt'><div id='k'>x</div></div>"));

        var kid = FindById(root, "k")!;
        Assert.True(string.IsNullOrEmpty(kid.ShapeOutside) || kid.ShapeOutside == "none");
        Assert.True(string.IsNullOrEmpty(kid.ShapeMargin) || kid.ShapeMargin == "0");
    }

    // 100x100 PNGs: the first is opaque below the diagonal (x <= y); the second adds a 100/255-alpha band 20px wide to its right.
    private const string Triangle = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAGQAAABkCAYAAABw4pVUAAABa0lEQVR4nO3RQUEAQQwAsfWv7FzBBw+k7TyiIO977+d778Xw/kJKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUNKAUPOp4ghp1PUkLMpcsjJFD3kXMqEkFMpU0LOpEwKOZEyLWR9ysSQ1SlTQ9amTA5ZmTI9ZF3KhpBVKVtC1qRsClmRsi1kfMrGkNEpW0PGpmwOGZmyPWRcyoWQUSlXQsakXAoZkXIthE+5GEKnXA1hUy6HkCnXQ7iUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQrCUQv5fIaBCQIWACvkwv6qnLbVlTiivAAAAAElFTkSuQmCC";
    private const string TriangleWithBand = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAGQAAABkCAYAAABw4pVUAAABc0lEQVR4nO3RwU0lQAxAsSmWYn9Xy4UKENI6yTu4Ar/Pe/8+7339sZffeT8hpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAhpYAh51PEkNMpasjZFDnkZIoeci5lQsiplCkhZ1ImhZxImRayPmViyOqUqSFrUyaHrEyZHrIuZUPIqpQtIWtSNoWsSNkWMj5lY8jolK0hY1M2h4xM2R4yLuVCyKiUKyFjUi6FjEi5FsKnXAyhU66GsCmXQ8iU6yFcSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiFYSiH/XyGgQkCFgAr5YL4BvA/pC6APP1IAAAAASUVORK5CYII=";

    private static async Task<(int Top, double Left)[]> SmallFloatLinesAsync(string style)
    {
        var (root, _) = await LayoutAsync(Wrap(
            $"<div id='c' style='width:200pt;font:10pt/12pt Arial'><div style='float:left;width:75pt;height:75pt;{style}'></div>{Text}</div>"));
        var c = FindById(root, "c")!;
        return [.. Descendants(c).SelectMany(b => b.Words)
            .GroupBy(w => (int)System.Math.Round(w.Top - c.Location.Y))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Min(w => w.Left) - c.Location.X))];
    }

    private static void AssertLines((int Top, double Left)[] lines, string expected)
    {
        foreach (var (top, left) in Parse(expected))
        {
            var actual = lines.FirstOrDefault(l => l.Top == top);
            Assert.True(actual != default, $"no line at y={top}: {string.Join(" ", lines.Select(l => $"{l.Top}:{l.Left:F1}"))}");
            Assert.True(System.Math.Abs(actual.Left - left) < 1.0, $"line at y={top} starts at {actual.Left:F1}, expected {left:F1}");
        }
    }

    // Expected line starts for the image cases were measured in Chrome for the same markup.
    [Theory]
    [InlineData("shape-outside:url(%T)", "0:12 12:24 24:36 36:48 48:60 60:72 72:75")]
    [InlineData("shape-outside:url(%T);shape-margin:6pt", "0:20.3 12:32.3 24:44.3 36:56.3 48:68.3 60:75 72:75")]
    public async Task AnImageShapeFollowsItsAlpha(string style, string chrome) =>
        AssertLines(await SmallFloatLinesAsync(style.Replace("%T", Triangle)), chrome);

    [Theory]
    [InlineData("0", "0:27 12:39 24:51 36:63 48:75 60:75 72:75")]
    [InlineData("0.3", "0:27 12:39 24:51 36:63 48:75 60:75 72:75")]
    [InlineData("0.5", "0:12 12:24 24:36 36:48 48:60 60:72 72:75")]
    public async Task TheThresholdDecidesWhichAlphaIsPartOfTheShape(string threshold, string chrome) =>
        AssertLines(await SmallFloatLinesAsync($"shape-outside:url({TriangleWithBand});shape-image-threshold:{threshold}"), chrome);

    [Fact]
    public async Task AnImageThatIsNotAnImageLeavesTheRectangle()
    {
        var lines = await SmallFloatLinesAsync("shape-outside:url(data:image/png;base64,AAAA)");

        Assert.All(lines.Where(l => l.Top < 70), l => Assert.Equal(75, l.Left, 0.5));
    }

    [Fact]
    public async Task APathShapeIsPlacedAtTheReferenceBoxOrigin()
    {
        // A triangle below the diagonal, in CSS px: 100px is the 75pt box, so the right edge at a line's bottom row is that row's y.
        var lines = await SmallFloatLinesAsync("shape-outside:path(&quot;M 0 0 L 100 100 L 0 100 Z&quot;)");

        AssertLines(lines, "0:12 12:24 24:36 36:48 48:60 60:72 72:75");
    }
}
