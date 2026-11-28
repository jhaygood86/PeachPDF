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

    [Fact]
    public async Task AnArcFromZeroToATurnAgainstItsDirection_IsAWholeCircle()
    {
        var log = await LogAsync("""
            g.fillStyle = '#f00';
            g.beginPath(); g.moveTo(50, 25); g.arc(50, 25, 10, 0, 2 * Math.PI, true); g.fill();
            g.beginPath(); g.arc(20, 25, 5, 2 * Math.PI, 0, false); g.fill();
            Log.info([g.getImageData(50, 25, 1, 1).data[3], g.getImageData(20, 25, 1, 1).data[3], g.getImageData(80, 25, 1, 1).data[3]].join(' '));
            """);

        Assert.Equal(["255 255 0"], log);
    }

    [Fact]
    public async Task IsPointInStroke_FollowsTheStrokesDashesCapsAndScale()
    {
        var log = await LogAsync("""
            g.setLineDash([10, 10]); g.lineWidth = 4;
            g.beginPath(); g.moveTo(10, 10); g.lineTo(70, 10);
            var onDash = g.isPointInStroke(15, 10), inGap = g.isPointInStroke(25, 10), pastEnd = g.isPointInStroke(71, 10), besideIt = g.isPointInStroke(15, 14);
            g.setLineDash([]); g.lineCap = 'square';
            var square = g.isPointInStroke(71, 10);
            g.scale(10, 10); g.lineWidth = 0.4; g.lineCap = 'butt';
            var scaled = g.isPointInStroke(15, 11.5) + ' ' + g.isPointInStroke(15, 13);
            Log.info([onDash, inGap, pastEnd, besideIt, square, scaled].join(' '));
            """);

        Assert.Equal(["true false false false true true false"], log);
    }

    [Fact]
    public async Task AClosedLineOutAndBack_IsStrokedWithJoinsAndWithoutCaps()
    {
        var log = await LogAsync("""
            g.strokeStyle = '#f00'; g.lineWidth = 20;
            g.lineCap = 'round'; g.strokeRect(50, 25, 40, 0);
            var capped = g.getImageData(45, 25, 1, 1).data[3];
            g.clearRect(0, 0, 100, 50);
            g.lineCap = 'butt'; g.lineJoin = 'round'; g.strokeRect(50, 25, 40, 0);
            Log.info(capped + ' ' + g.getImageData(45, 25, 1, 1).data[3]);
            """);

        Assert.Equal(["0 255"], log);
    }

    [Fact]
    public async Task ThePathBeingBuilt_StaysWhereItLiesOnTheCanvasWhenTheTransformChanges()
    {
        var log = await LogAsync("""
            g.beginPath(); g.rect(10, 10, 10, 10);
            g.translate(100, 100); g.scale(2, 2);
            var moved = [g.isPointInPath(15, 15), g.isPointInPath(115, 115)];
            g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.restore();
            g.fillStyle = '#f00'; g.setTransform(1, 0, 0, 1, 0, 0); g.fill();
            Log.info(moved.join(' ') + ' ' + g.getImageData(15, 15, 1, 1).data[3] + ' ' + g.getImageData(115, 115, 1, 1).data[3]);
            """);

        Assert.Equal(["true false 255 0"], log);
    }

    [Fact]
    public async Task AScaleTooLargeForASingleFloat_StillLetsAPointBeFoundInAPath()
    {
        var log = await LogAsync("""
            g.scale(Number.MAX_VALUE, Number.MAX_VALUE);
            g.beginPath(); g.rect(-10, -10, 20, 20);
            Log.info([g.isPointInPath(0, 0, 'nonzero'), g.isPointInPath(0, 0, 'evenodd')].join(' '));
            """);

        Assert.Equal(["true true"], log);
    }
}
