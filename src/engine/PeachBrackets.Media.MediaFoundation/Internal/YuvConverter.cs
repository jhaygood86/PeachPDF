using System;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>What a stream says about how its YUV becomes RGB.</summary>
/// <param name="Kr">the matrix's red coefficient</param>
/// <param name="Kb">the matrix's blue coefficient</param>
/// <param name="FullRange">whether Y runs 0..255 and the chroma is centered on 128 over 0..255 (else 16..235 and 16..240)</param>
/// <param name="ManagePrimaries">whether the picture is Rec. 601 content that a browser color-manages into sRGB (see <see cref="YuvConverter"/>)</param>
/// <param name="Space">the RGB space the converted picture is in</param>
internal readonly record struct Colorimetry(double Kr, double Kb, bool FullRange, bool ManagePrimaries, MediaColorSpace Space)
{
    /// <summary>What a browser assumes for a stream that does not say: Rec. 601 below 577 lines and Rec. 709 above, limited range.</summary>
    public static Colorimetry Assumed(int height) => height <= 576 ? Rec601(false, manage: true) : Rec709(false);

    public static Colorimetry Rec601(bool fullRange, bool manage) => new(0.299, 0.114, fullRange, manage, MediaColorSpace.Srgb);

    public static Colorimetry Rec709(bool fullRange) => new(0.2126, 0.0722, fullRange, false, MediaColorSpace.Srgb);

    public static Colorimetry Smpte240M(bool fullRange) => new(0.212, 0.087, fullRange, false, MediaColorSpace.Srgb);

    public static Colorimetry Rec2020(bool fullRange) => new(0.2627, 0.0593, fullRange, false, MediaColorSpace.Rec2020);
}

/// <summary>
/// NV12 (a plane of Y and a plane of interleaved U and V at half the height and width) to premultiplied RGBA, by the matrix and range a stream asks for. Media Foundation's
/// own conversion takes a picture of 576 lines or fewer as Rec. 601 whatever it is tagged, so it is done here, where the tag decides.
/// </summary>
/// <remarks>
/// A Rec. 601 picture (what a small stream with no colorimetry is) is color-managed the way a browser does it: the RGB values are taken as gamma 2.0, moved from SMPTE 170M
/// primaries to Rec. 709 / sRGB in linear light, and encoded with the sRGB curve. A pure Rec. 601 red then shows as (247, 37, 0), as it does in a browser. Gamma 2.0 and not
/// Rec. 601's own piecewise curve because a browser leaves dark values where they are, which the piecewise curve would lift.
/// </remarks>
internal static unsafe class YuvConverter
{
    private static readonly float[] s_linear = BuildLinear();
    private static readonly byte[] s_encode = BuildEncode();

    // SMPTE 170M (SMPTE C) primaries to Rec. 709 primaries, both with a D65 white, in linear light.
    private const float R0 = 0.939542f, R1 = 0.050181f, R2 = 0.010277f;
    private const float G0 = 0.017772f, G1 = 0.965793f, G2 = 0.016435f;
    private const float B0 = -0.001622f, B1 = -0.004370f, B2 = 1.005991f;

    private static float[] BuildLinear()
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
            table[i] = (i / 255f) * (i / 255f);

        return table;
    }

    // Linear light to sRGB-encoded 8 bits, at 4096 steps.
    private static byte[] BuildEncode()
    {
        var table = new byte[4097];
        for (var i = 0; i <= 4096; i++)
        {
            var linear = i / 4096.0;
            var encoded = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            table[i] = (byte)Math.Clamp((int)Math.Round(encoded * 255), 0, 255);
        }

        return table;
    }

    /// <summary>
    /// Finds how rows and planes are laid out in a contiguous NV12 buffer of <paramref name="used"/> bytes holding a <paramref name="width"/> x <paramref name="height"/> picture.
    /// A decoder pads the coded picture to its block size (a 300 x 200 picture arrives 304 bytes to a row and 208 rows), and neither the media type's stride (300) nor the 2-D buffer's
    /// pitch (320) says so, but the size of the buffer does: it is exactly stride * rows * 3 / 2. Returns false for a buffer that fits no layout.
    /// </summary>
    public static bool TryFindLayout(int used, int width, int height, out int stride, out int rows)
    {
        foreach (var alignment in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
        {
            var candidateRows = ((height + alignment - 1) / alignment) * alignment;
            foreach (var strideAlignment in new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 })
            {
                var candidateStride = ((width + strideAlignment - 1) / strideAlignment) * strideAlignment;
                if ((long)candidateStride * candidateRows * 3 / 2 == used)
                {
                    stride = candidateStride;
                    rows = candidateRows;
                    return true;
                }
            }
        }

        stride = 0;
        rows = 0;
        return false;
    }

    /// <summary>Converts a contiguous NV12 buffer; null when its layout cannot be worked out.</summary>
    public static byte[]? Nv12ToRgba(byte* data, int used, int width, int height, Colorimetry c)
    {
        if (width < 1 || height < 1 || !TryFindLayout(used, width, height, out var stride, out var rows))
            return null;

        var chroma = data + ((long)stride * rows);
        var rgba = new byte[width * height * 4];

        double yScale, yOffset, cScale;
        if (c.FullRange)
        {
            yScale = 1;
            yOffset = 0;
            cScale = 1 / 255.0;
        }
        else
        {
            yScale = 255.0 / 219;
            yOffset = 16;
            cScale = 1 / 224.0;
        }

        var kg = 1 - c.Kr - c.Kb;
        var rv = 2 * (1 - c.Kr);
        var bu = 2 * (1 - c.Kb);
        var gu = -bu * c.Kb / kg;
        var gv = -rv * c.Kr / kg;

        // Fixed point, 16 bits.
        var fy = (int)Math.Round(yScale * 65536);
        var frv = (int)Math.Round(rv * 255 * cScale * 65536);
        var fbu = (int)Math.Round(bu * 255 * cScale * 65536);
        var fgu = (int)Math.Round(gu * 255 * cScale * 65536);
        var fgv = (int)Math.Round(gv * 255 * cScale * 65536);
        var offset = (int)Math.Round(yOffset);

        var o = 0;
        for (var y = 0; y < height; y++)
        {
            var luma = data + ((long)y * stride);
            var uv = chroma + ((long)(y >> 1) * stride);
            for (var x = 0; x < width; x++)
            {
                var yy = (luma[x] - offset) * fy;
                var cb = uv[(x & ~1)] - 128;
                var cr = uv[(x & ~1) + 1] - 128;
                var r = (yy + (frv * cr)) >> 16;
                var g = (yy + (fgu * cb) + (fgv * cr)) >> 16;
                var b = (yy + (fbu * cb)) >> 16;
                r = r < 0 ? 0 : r > 255 ? 255 : r;
                g = g < 0 ? 0 : g > 255 ? 255 : g;
                b = b < 0 ? 0 : b > 255 ? 255 : b;

                if (c.ManagePrimaries)
                {
                    var lr = s_linear[r];
                    var lg = s_linear[g];
                    var lb = s_linear[b];
                    r = s_encode[(int)(Math.Clamp((R0 * lr) + (R1 * lg) + (R2 * lb), 0f, 1f) * 4096)];
                    g = s_encode[(int)(Math.Clamp((G0 * lr) + (G1 * lg) + (G2 * lb), 0f, 1f) * 4096)];
                    b = s_encode[(int)(Math.Clamp((B0 * lr) + (B1 * lg) + (B2 * lb), 0f, 1f) * 4096)];
                }

                rgba[o++] = (byte)r;
                rgba[o++] = (byte)g;
                rgba[o++] = (byte)b;
                rgba[o++] = 255;
            }
        }

        return rgba;
    }
}
