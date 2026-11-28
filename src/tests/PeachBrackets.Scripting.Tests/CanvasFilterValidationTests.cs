namespace PeachBrackets.Scripting.Tests;

/// <summary>What <c>new CanvasFilter(...)</c> accepts and what it refuses (a TypeError), primitive by primitive.</summary>
public class CanvasFilterValidationTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function make(d) { try { new CanvasFilter(d); return 'ok'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task AFilterThatIsNotWhatItShouldBe_IsATypeError()
    {
        var log = await LogAsync("""
            var cases = [
              "1", "[1]", "[{ name: 'gaussianBlur', stdDeviation: 2 }, 5]",
              "{ name: 'gaussianBlur' }", "{ name: 'gaussianBlur', stdDeviation: 'x' }", "{ name: 'gaussianBlur', stdDeviation: [1, 2, 3] }", "{ name: 'gaussianBlur', stdDeviation: [1, 'a'] }", "{ name: 'gaussianBlur', stdDeviation: [1, 2] }",
              "{ name: 'colorMatrix', type: 'nonsense', values: [] }", "{ name: 'colorMatrix', type: 'matrix', values: [1, 2, 3] }", "{ name: 'colorMatrix', type: 'matrix', values: [1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,1,Infinity] }",
              "{ name: 'colorMatrix', type: 'saturate' }", "{ name: 'colorMatrix', type: 'luminanceToAlpha' }", "{ name: 'colorMatrix', type: 'hueRotate', values: [90] }",
              "{ name: 'convolveMatrix' }", "{ name: 'convolveMatrix', kernelMatrix: [] }", "{ name: 'convolveMatrix', kernelMatrix: [1] }", "{ name: 'convolveMatrix', kernelMatrix: [[1, 2], [3]] }", "{ name: 'convolveMatrix', kernelMatrix: [[1, NaN]] }",
              "{ name: 'convolveMatrix', kernelMatrix: [[], []] }", "{ name: 'convolveMatrix', kernelMatrix: [[0, 1, 0], [1, 1, 1], [0, 1, 0]], edgeMode: 'bounce' }", "{ name: 'convolveMatrix', kernelMatrix: [[0, 1, 0], [1, 1, 1], [0, 1, 0]], edgeMode: 'wrap', preserveAlpha: true }",
              "{ name: 'convolveMatrix', kernelMatrix: [[1]], edgeMode: 'none', targetX: 3 }", "{ name: 'convolveMatrix', kernelMatrix: [[]] }",
              "{ name: 'turbulence', baseFrequency: -1 }", "{ name: 'turbulence', baseFrequency: 0.1, numOctaves: -1 }", "{ name: 'turbulence', baseFrequency: 0.1, stitchTiles: 'maybe' }", "{ name: 'turbulence', baseFrequency: 0.1, type: 'plasma' }",
              "{ name: 'turbulence', baseFrequency: [0.1, 0.2], numOctaves: 3, seed: 5, stitchTiles: 'stitch', type: 'fractalNoise' }",
              "{ name: 'unknownPrimitive' }"
            ];
            Log.info(cases.map(function (src) { return make(eval('(' + src + ')')); }).join(' '));
            """);

        Assert.Equal(["TypeError TypeError TypeError TypeError TypeError TypeError TypeError ok TypeError TypeError TypeError TypeError ok ok TypeError TypeError TypeError TypeError TypeError TypeError TypeError ok ok ok TypeError TypeError TypeError TypeError ok ok"], log);
    }
}
