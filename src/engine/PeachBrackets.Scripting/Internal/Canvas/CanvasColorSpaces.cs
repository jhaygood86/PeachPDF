using PeachDrawing.Core;
using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PeachBrackets.Scripting.Internal.Canvas;

/// <summary>
/// The predefined color spaces of a canvas (<c>srgb</c>, <c>display-p3</c>, <c>srgb-linear</c>, <c>display-p3-linear</c>): converting a color between them, and what a picture made of floats
/// is in. A canvas keeps its pixels in its own space; a color or a picture in another is converted when it is drawn.
/// </summary>
internal static class CanvasColorSpaces
{
    public static readonly string[] Names = ["srgb", "display-p3", "srgb-linear", "display-p3-linear"];

    private static readonly double[] SrgbToP3 = [0.8224621, 0.1775380, 0.0000000, 0.0331941, 0.9668058, 0.0000000, 0.0170827, 0.0723974, 0.9105199];

    private static readonly double[] P3ToSrgb = [1.2249401, -0.2249404, 0.0000000, -0.0420569, 1.0420571, 0.0000000, -0.0196376, -0.0786361, 1.0982735];

    private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Encode(double v) => v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    // The transfer function continued as an odd function, so a color outside the gamut keeps its sign.
    private static double SignedDecode(double v) => v < 0 ? -Decode(-v) : Decode(v);

    private static double SignedEncode(double v) => v < 0 ? -Encode(-v) : Encode(v);

    public static bool IsLinear(string space) => space.EndsWith("-linear", StringComparison.Ordinal);

    public static bool IsP3(string space) => space.StartsWith("display-p3", StringComparison.Ordinal);

    /// <summary>Converts one color (red, green, blue as coordinates) between two spaces; with <paramref name="clip"/> the result is brought into 0 to 1.</summary>
    public static void Convert(double[] rgb, string from, string to, bool clip)
    {
        if (from != to)
        {
            var fromLinear = IsLinear(from);
            var toLinear = IsLinear(to);
            for (var c = 0; c < 3; c++)
                rgb[c] = fromLinear ? rgb[c] : SignedDecode(rgb[c]);

            if (IsP3(from) != IsP3(to))
            {
                var m = IsP3(from) ? P3ToSrgb : SrgbToP3;
                (rgb[0], rgb[1], rgb[2]) = ((m[0] * rgb[0]) + (m[1] * rgb[1]) + (m[2] * rgb[2]), (m[3] * rgb[0]) + (m[4] * rgb[1]) + (m[5] * rgb[2]), (m[6] * rgb[0]) + (m[7] * rgb[1]) + (m[8] * rgb[2]));
            }

            for (var c = 0; c < 3; c++)
                rgb[c] = toLinear ? rgb[c] : SignedEncode(rgb[c]);
        }

        if (clip)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = Math.Clamp(rgb[c], 0, 1);
        }
    }

    /// <summary>Converts straight RGBA bytes between two spaces.</summary>
    public static void ConvertBytes(byte[] pixels, string from, string to)
    {
        if (from == to)
            return;

        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = pixels[i + c] / 255.0;

            Convert(rgb, from, to, clip: true);
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Round(rgb[c] * 255);
        }
    }

    /// <summary>Converts premultiplied RGBA bytes between two spaces, in place (each pixel is made straight, converted and premultiplied again).</summary>
    public static void ConvertPremultipliedBytes(Span<byte> pixels, string from, string to)
    {
        if (from == to)
            return;

        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 0)
                continue;

            for (var c = 0; c < 3; c++)
                rgb[c] = Math.Min(1.0, pixels[i + c] / (double)a);

            Convert(rgb, from, to, clip: true);
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Round(Math.Clamp(rgb[c], 0, 1) * a);
        }
    }

    /// <summary>Converts straight RGBA floats between two spaces, in place; the result is not brought into 0 to 1 unless <paramref name="clip"/> says so.</summary>
    public static void ConvertFloats(float[] pixels, string from, string to, bool clip)
    {
        if (from == to && !clip)
            return;

        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = pixels[i + c];

            Convert(rgb, from, to, clip);
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (float)rgb[c];
        }
    }

    /// <summary>The color of an eight-bit sRGB color in a space, as an eight-bit color (what an unorm8 canvas of that space keeps).</summary>
    public static PaintColor ConvertPaintColor(PaintColor color, string to)
    {
        if (to == "srgb")
            return color;

        var rgb = new double[] { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
        Convert(rgb, "srgb", to, clip: true);
        return PaintColor.FromArgb(color.A, (int)Math.Round(rgb[0] * 255), (int)Math.Round(rgb[1] * 255), (int)Math.Round(rgb[2] * 255));
    }

    /// <summary>
    /// The coordinates of a <c>color()</c> in the spaces a canvas knows, in extended sRGB (so a color outside the sRGB gamut keeps its place there): the red, green, blue and alpha. False for text
    /// that is not a <c>color()</c> of <c>srgb</c>, <c>srgb-linear</c> or <c>display-p3</c>.
    /// </summary>
    public static bool TryParseExtended(string text, out (double R, double G, double B, double A) color)
    {
        color = default;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("color(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
            return false;

        var inside = trimmed[6..^1].Trim();
        var slash = inside.IndexOf('/');
        var alphaText = slash >= 0 ? inside[(slash + 1)..].Trim() : null;
        var parts = (slash >= 0 ? inside[..slash] : inside).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return false;

        var space = parts[0].ToLowerInvariant();
        if (space is not ("srgb" or "srgb-linear" or "display-p3"))
            return false;

        var rgb = new double[3];
        for (var c = 0; c < 3; c++)
        {
            if (!TryNumber(parts[c + 1], out rgb[c]))
                return false;
        }

        var alpha = 1.0;
        if (alphaText is not null && !TryNumber(alphaText, out alpha))
            return false;

        Convert(rgb, space, "srgb", clip: false);
        color = (rgb[0], rgb[1], rgb[2], Math.Clamp(alpha, 0, 1));
        return true;
    }

    private static bool TryNumber(string text, out double value)
    {
        if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            value = percent / 100;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // The color space a picture of floats made by a script is in (a picture of eight bits is sRGB unless the canvas it was read from was in another).
    private static readonly ConditionalWeakTable<Image, SpaceTag> Tags = new();

    private sealed class SpaceTag(string space, float[]? floats)
    {
        public string Space { get; } = space;

        /// <summary>The picture's pixels as straight floats, when it was made of floats.</summary>
        public float[]? Floats { get; } = floats;
    }

    public static void Tag(Image image, string space, float[]? floats = null)
    {
        Tags.Remove(image);
        Tags.Add(image, new SpaceTag(space, floats));
    }

    public static string SpaceOf(Image image) => Tags.TryGetValue(image, out var tag) ? tag.Space : "srgb";

    public static float[]? FloatsOf(Image image) => Tags.TryGetValue(image, out var tag) ? tag.Floats : null;
}
