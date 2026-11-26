namespace PeachBrackets.Scripting.Tests;

/// <summary>Hit testing, stroke pruning and the shadow rules of the 2D context.</summary>
public class CanvasGeometryTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=100 height=50");
        return log;
    }

    [Fact]
    public async Task IsPointInPath_CountsAPointOnTheEdgeAsInside_AndRejectsABadFillRule()
    {
        var log = await LogAsync("""
            g.rect(0, 0, 20, 20);
            Log.info([g.isPointInPath(0, 0), g.isPointInPath(20, 10), g.isPointInPath(10, -0.01), g.isPointInPath(NaN, 5),
                name(function () { g.isPointInPath(5, 5, 'gazonk'); }), name(function () { g.isPointInPath(null, 5, 5); })].join(' '));
            """);

        Assert.Equal(["true true false false TypeError TypeError"], log);
    }

    [Fact]
    public async Task AnEmptyOrZeroLengthSubpath_IsNotStroked_HoweverRoundItsCaps()
    {
        var log = await LogAsync("""
            g.lineWidth = 40; g.lineCap = 'round'; g.lineJoin = 'round'; g.strokeStyle = '#f00';
            g.beginPath(); g.moveTo(10, 10); g.moveTo(30, 10); g.stroke();
            g.beginPath(); g.moveTo(50, 25); g.lineTo(50, 25); g.stroke();
            g.beginPath(); g.moveTo(50, 25); g.lineTo(50, 25); g.closePath(); g.stroke();
            var none = g.getImageData(50, 25, 1, 1).data[3];
            g.beginPath(); g.moveTo(10, 40); g.lineTo(30, 40); g.stroke();
            Log.info(none + ' ' + g.getImageData(20, 40, 1, 1).data[3]);
            """);

        Assert.Equal(["0 255"], log);
    }

    [Fact]
    public async Task LineTo_ConvertsItsSecondArgument_EvenWhenTheFirstIsNotFinite()
    {
        var log = await LogAsync("""
            var converted = false;
            g.lineTo(Infinity, { valueOf: function () { converted = true; return 0; } });
            Log.info(String(converted));
            """);

        Assert.Equal(["true"], log);
    }

    [Fact]
    public async Task ARoundRectWithNegativeSize_StartsAtItsOrigin_AndMirrors()
    {
        var log = await LogAsync("""
            g.fillStyle = '#00f';
            g.roundRect(60, 40, -40, -30, [10, 0, 0, 0]);
            g.fill();
            // The first radius is the corner at (60, 40): rounded, so its very corner is not filled; the others are square.
            var d = function (x, y) { return g.getImageData(x, y, 1, 1).data[3]; };
            Log.info([d(59, 39), d(21, 11), d(59, 11), d(21, 39)].join(' '));
            """);

        Assert.Equal(["0 255 255 255"], log);
    }

    [Fact]
    public async Task AShadow_FallsInsideTheCanvas_WhenTheShapeThatCastsItIsOutside()
    {
        var log = await LogAsync("""
            g.shadowColor = '#f00'; g.shadowOffsetX = 100;
            g.fillStyle = '#0f0';
            g.fillRect(-90, 10, 40, 20);
            var d = g.getImageData(30, 20, 1, 1).data;
            Log.info(d[0] + ' ' + d[1] + ' ' + d[3]);
            """);

        Assert.Equal(["255 0 255"], log);
    }
}
