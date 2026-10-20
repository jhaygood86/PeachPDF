using System;
using System.Collections.Generic;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;

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

    /// <summary>Converts a run of 8-bit device values (<see cref="Components"/> per colour) to 8-bit sRGB (three per colour).</summary>
    public delegate void BulkConverter(ReadOnlySpan<byte> device, Span<byte> rgb, int count);

    /// <summary>
    /// For a space whose colours go through an embedded ICC profile: converts many 8-bit colours at once, which is how an image is
    /// converted. Null for the device spaces (and for a profile that could not be used, which is then read as its device alternate).
    /// </summary>
    public BulkConverter? Bulk { get; private set; }

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
                if (n is 1 or 3 && FromIccProfile(doc, icc, n) is { } converting) return converting;
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

    sealed record IccEntry(PdfColorSpace? Space);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, IccEntry> IccCache = new();

    static readonly Lazy<PeachImage.IccColorProfile?> SRgbProfile =
        new(() => PeachImage.IccColorProfile.TryCreate(PdfAResources.SRgbIccProfile, out var p) ? p : null);

    /// <summary>
    /// A gray or RGB space that converts through the embedded profile to sRGB, with PeachImage's ICC engine. Null when the profile cannot be
    /// read or is not the kind its component count says: the caller then reads the space as its device alternate. CMYK profiles are left
    /// as device CMYK, which a PDF canvas keeps as CMYK.
    /// </summary>
    static PdfColorSpace? FromIccProfile(PdfSourceDocument doc, PdfDictionary icc, int n)
    {
        if (IccCache.TryGetValue(icc, out var cached)) return cached.Space;
        PdfColorSpace? space = null;
        try
        {
            var want = n == 3 ? PeachImage.IccColorSpace.Rgb : PeachImage.IccColorSpace.Gray;
            if (icc.Stream is not null && SRgbProfile.Value is { } srgb
                && PeachImage.IccColorProfile.TryCreate(doc.DecodeStream(icc).Data, out var profile) && profile is not null
                && profile.ChannelCount == n && profile.DataColorSpace == want)
            {
                const PeachImage.IccRenderingIntent intent = PeachImage.IccRenderingIntent.RelativeColorimetric;
                space = new PdfColorSpace(n == 3 ? "ICCBased(RGB)" : "ICCBased(Gray)", n, (c, a) =>
                {
                    Span<byte> device = stackalloc byte[3];
                    for (int i = 0; i < n; i++) device[i] = (byte)Math.Round(Math.Clamp(c[i], 0, 1) * 255);
                    Span<byte> rgb = stackalloc byte[3];
                    profile.ConvertTo(srgb, device[..n], rgb, 1, intent, true);
                    return PaintColor.FromArgb(Alpha(a), rgb[0], rgb[1], rgb[2]);
                }, n == 3 ? [0, 0, 0] : [0])
                {
                    Bulk = (device, rgb, count) => profile.ConvertTo(srgb, device, rgb, count, intent, true),
                };
            }
        }
        catch (Exception)
        {
            // An unreadable or hostile profile is no reason to fail the page: it is read as its device alternate.
            space = null;
        }

        IccCache.TryAdd(icc, new IccEntry(space));
        return space;
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
            // XYZ to linear sRGB with the matrix for the space's own white (D65 or D50), then the sRGB gamma.
            bool d65 = wp[0] > 0.93;
            double r = d65 ? 3.2406 * x - 1.5372 * y - 0.4986 * z : 3.1339 * x - 1.6169 * y - 0.4906 * z;
            double g = d65 ? -0.9689 * x + 1.8758 * y + 0.0415 * z : -0.9785 * x + 1.9160 * y + 0.0333 * z;
            double b = d65 ? 0.0557 * x - 0.2040 * y + 1.0570 * z : 0.0720 * x - 0.2290 * y + 1.4057 * z;
            static double Gamma(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return PaintColor.FromArgb(Alpha(a), Byte(Gamma(r)), Byte(Gamma(g)), Byte(Gamma(b)));
        }, [0, 0, 0]);
    }
}
