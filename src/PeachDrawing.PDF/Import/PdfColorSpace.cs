using System;
using System.Collections.Generic;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// A PDF colour space reduced to what painting needs: how many components a colour has and how they become a
/// <see cref="PaintColor"/>. Device, Cal and ICC-based spaces map straight to RGB/gray/CMYK by component count (the
/// profile itself is not applied), Indexed looks its palette up, and Lab is converted through D50 XYZ. Spaces that
/// need a tint-transform function (Separation, DeviceN) and patterns are reported unsupported by returning null.
/// </summary>
internal sealed class PdfColorSpace
{
    PdfColorSpace(string name, int components, Func<double[], double, PaintColor> toColor, double[] initial)
    {
        Name = name;
        Components = components;
        ToColor = toColor;
        Initial = initial;
    }

    /// <summary>The family name (<c>DeviceRGB</c>, <c>Indexed</c>...).</summary>
    public string Name { get; }

    /// <summary>The number of components in a colour of this space.</summary>
    public int Components { get; }

    /// <summary>Converts components and an alpha (0..1) to a colour.</summary>
    public Func<double[], double, PaintColor> ToColor { get; }

    /// <summary>The colour a space starts with (ISO 32000-1, 8.6.5).</summary>
    public double[] Initial { get; }

    static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    static int Alpha(double a) => (int)Math.Round(Math.Clamp(a, 0, 1) * 255);

    /// <summary>DeviceGray.</summary>
    public static readonly PdfColorSpace Gray = new("DeviceGray", 1, (c, a) => { byte g = Byte(c[0]); return PaintColor.FromArgb(Alpha(a), g, g, g); }, [0]);

    /// <summary>The Pattern space: it has no components of its own, the colour is a pattern named by <c>scn</c>.</summary>
    public static readonly PdfColorSpace Pattern = new("Pattern", 0, (_, a) => PaintColor.FromArgb(Alpha(a), 0, 0, 0), []);

    /// <summary>DeviceRGB.</summary>
    public static readonly PdfColorSpace Rgb = new("DeviceRGB", 3, (c, a) => PaintColor.FromArgb(Alpha(a), Byte(c[0]), Byte(c[1]), Byte(c[2])), [0, 0, 0]);

    /// <summary>DeviceCMYK.</summary>
    public static readonly PdfColorSpace Cmyk = new("DeviceCMYK", 4, (c, a) => PaintColor.FromCmyk(Alpha(a), (float)c[0], (float)c[1], (float)c[2], (float)c[3]), [0, 0, 0, 1]);

    /// <summary>Resolves a colour space operand (a name, or an array like <c>[/ICCBased 5 0 R]</c>) against <paramref name="resources"/>.</summary>
    public static PdfColorSpace? Resolve(PdfSourceDocument doc, PdfItem? item, PdfDictionary? resources, int depth = 0)
    {
        if (depth > 8) return null;
        item = doc.Resolve(item);
        if (item is PdfName name)
        {
            switch (name.Value)
            {
                case "/DeviceGray" or "/G" or "/CalGray": return Gray;
                case "/DeviceRGB" or "/RGB" or "/CalRGB": return Rgb;
                case "/DeviceCMYK" or "/CMYK": return Cmyk;
                case "/Pattern": return Pattern;
            }
            var named = resources is null ? null : doc.DictOf(resources, "ColorSpace") is { } cs ? cs.GetRaw(name.Value.TrimStart('/')) : null;
            return named is null ? null : Resolve(doc, named, resources, depth + 1);
        }
        if (item is not PdfArray arr || arr.Elements.Count == 0) return null;
        string? family = doc.NameOf(arr.Elements[0]);
        switch (family)
        {
            case "/DeviceGray" or "/CalGray": return Gray;
            case "/DeviceRGB" or "/CalRGB": return Rgb;
            case "/DeviceCMYK": return Cmyk;
            case "/Pattern": return Pattern;
            case "/ICCBased" when arr.Elements.Count > 1 && doc.Resolve(arr.Elements[1]) is PdfDictionary icc:
                int n = doc.IntOf(icc.GetRaw("N"));
                return n switch
                {
                    1 => Gray,
                    3 => Rgb,
                    4 => Cmyk,
                    _ => doc.Resolve(icc.GetRaw("Alternate")) is { } alt ? Resolve(doc, alt, resources, depth + 1) : null,
                };
            case "/Lab":
                return Lab(doc, arr);
            case "/Indexed" or "/I" when arr.Elements.Count >= 4:
                return Indexed(doc, arr, resources, depth);
            default:
                return null;
        }
    }

    static PdfColorSpace? Indexed(PdfSourceDocument doc, PdfArray arr, PdfDictionary? resources, int depth)
    {
        var baseSpace = Resolve(doc, arr.Elements[1], resources, depth + 1);
        if (baseSpace is null) return null;
        int hival = doc.IntOf(arr.Elements[2]);
        byte[] table = doc.Resolve(arr.Elements[3]) switch
        {
            PdfString s => System.Text.Encoding.Latin1.GetBytes(s.Value),
            PdfDictionary { Stream: not null } d => doc.DecodeStream(d).Data,
            _ => [],
        };
        int n = baseSpace.Components;
        return new PdfColorSpace("Indexed", 1, (c, a) =>
        {
            int idx = Math.Clamp((int)Math.Round(c[0]), 0, hival);
            var comps = new double[n];
            for (int i = 0; i < n; i++)
            {
                int pos = idx * n + i;
                comps[i] = pos < table.Length ? table[pos] / 255.0 : 0;
            }
            return baseSpace.ToColor(comps, a);
        }, [0]);
    }

    static PdfColorSpace Lab(PdfSourceDocument doc, PdfArray arr)
    {
        double[] wp = [0.9505, 1, 1.089];
        double[] range = [-100, 100, -100, 100];
        if (arr.Elements.Count > 1 && doc.Resolve(arr.Elements[1]) is PdfDictionary d)
        {
            if (doc.ArrayOf(d, "WhitePoint") is { Elements.Count: >= 3 } w) wp = [doc.NumberOf(w.Elements[0]), doc.NumberOf(w.Elements[1]), doc.NumberOf(w.Elements[2])];
            if (doc.ArrayOf(d, "Range") is { Elements.Count: >= 4 } r) range = [doc.NumberOf(r.Elements[0]), doc.NumberOf(r.Elements[1]), doc.NumberOf(r.Elements[2]), doc.NumberOf(r.Elements[3])];
        }
        return new PdfColorSpace("Lab", 3, (c, a) =>
        {
            double l = Math.Clamp(c[0], 0, 100);
            double aa = Math.Clamp(c[1], range[0], range[1]);
            double bb = Math.Clamp(c[2], range[2], range[3]);
            double m = (l + 16) / 116, ll = m + aa / 500, nn = m - bb / 200;
            static double G(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - 4.0 / 29);
            double x = wp[0] * G(ll), y = wp[1] * G(m), z = wp[2] * G(nn);
            // XYZ (D50-ish white) to linear sRGB, then gamma.
            double r = 3.1339 * x - 1.6169 * y - 0.4906 * z;
            double g = -0.9785 * x + 1.9160 * y + 0.0333 * z;
            double b = 0.0720 * x - 0.2290 * y + 1.4057 * z;
            static double Gamma(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return PaintColor.FromArgb(Alpha(a), Byte(Gamma(r)), Byte(Gamma(g)), Byte(Gamma(b)));
        }, [0, 0, 0]);
    }
}
