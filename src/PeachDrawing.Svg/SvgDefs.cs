using PeachDrawing.Core;
using PeachDrawing.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.Svg;

/// <summary>
/// What every canvas of one SVG document shares: the <c>&lt;defs&gt;</c> block (gradients, clip paths, patterns, masks, filters, images,
/// tiles), the id allocator, the number formatting and the fonts the document's <c>&lt;text&gt;</c> uses.
/// </summary>
internal sealed class SvgDefs(int precision)
{
    private readonly StringBuilder _defs = new();
    private readonly Dictionary<string, string> _byContent = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> _byObject = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Typeface, FontUse> _fonts = new();
    private int _next;

    /// <summary>One typeface the document draws as <c>&lt;text&gt;</c>: its CSS family name and the glyphs of it used.</summary>
    internal sealed class FontUse(string name, bool eligible)
    {
        public string Name { get; } = name;

        /// <summary>Whether the typeface exports as an OpenType font a browser can use (a bare CFF table cannot be).</summary>
        public bool Eligible { get; } = eligible;

        public HashSet<int> Glyphs { get; } = [0];
    }

    /// <summary>The number of decimals coordinates are written with.</summary>
    public int Precision { get; } = Math.Clamp(precision, 0, 9);

    /// <summary>True when nothing has been defined.</summary>
    public bool IsEmpty => _defs.Length == 0;

    /// <summary>The text of the definitions block's content.</summary>
    public string Content => _defs.ToString();

    /// <summary>A fresh id with the given prefix.</summary>
    public string NextId(string prefix) => prefix + (++_next).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Defines <paramref name="template"/> (which holds <c>{id}</c> where its own id goes) once: the same text defined again returns the id
    /// it already has.
    /// </summary>
    public string Define(string prefix, string template)
    {
        if (_byContent.TryGetValue(template, out var existing))
            return existing;

        var id = NextId(prefix);
        _byContent[template] = id;
        _defs.Append(template.Replace("{id}", id, StringComparison.Ordinal)).Append('\n');
        return id;
    }

    /// <summary>Defines something once per object (an image, a tile): <paramref name="build"/> gets the new id and returns the text.</summary>
    public string DefineOnce(object key, string prefix, Func<string, string> build)
    {
        if (_byObject.TryGetValue(key, out var existing))
            return existing;

        var id = NextId(prefix);
        _byObject[key] = id;
        _defs.Append(build(id)).Append('\n');
        return id;
    }

    private readonly Dictionary<(Typeface Typeface, int Glyph, double Scale, double Skew), string> _glyphs = new();

    /// <summary>The id of a glyph definition, built by <paramref name="build"/> (given the new id) the first time it is asked for.</summary>
    public string GlyphId(Typeface typeface, int glyph, double scale, double skew, Func<string, string> build)
    {
        var key = (typeface, glyph, scale, skew);
        if (_glyphs.TryGetValue(key, out var existing))
            return existing;

        var id = NextId("g");
        _glyphs[key] = id;
        _defs.Append(build(id)).Append('\n');
        return id;
    }

    /// <summary>The font use record of <paramref name="typeface"/>, created (with <paramref name="eligible"/>) on first use.</summary>
    public FontUse UseFont(Typeface typeface, Func<bool> eligible)
    {
        if (!_fonts.TryGetValue(typeface, out var use))
        {
            use = new FontUse(NextId("f"), eligible());
            _fonts[typeface] = use;
        }

        return use;
    }

    /// <summary>The fonts drawn as text, in first-use order.</summary>
    public IEnumerable<KeyValuePair<Typeface, FontUse>> Fonts => _fonts;

    /// <summary>A number the way SVG reads it: invariant, rounded to <see cref="Precision"/> decimals, no exponent, no negative zero.</summary>
    public string Num(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        var rounded = Math.Round(value, Precision, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "0" : rounded.ToString("0.#########", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A unit-free factor (a scale, a skew, the linear part of a matrix): rounded to eight decimals, not to <see cref="Precision"/>, which
    /// would turn the 0.0496 that fits a 4032-pixel photo into 200 units into 0.05 and make it 1% too big.
    /// </summary>
    public static string Factor(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        var rounded = Math.Round(value, 8, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "0" : rounded.ToString("0.########", CultureInfo.InvariantCulture);
    }

    /// <summary>A <c>matrix(...)</c> transform value for <paramref name="m"/>.</summary>
    public string Matrix(Matrix3x2Value m) =>
        $"matrix({Factor(m.A)} {Factor(m.B)} {Factor(m.C)} {Factor(m.D)} {Num(m.E)} {Num(m.F)})";

    /// <summary>Escapes <paramref name="text"/> for use as attribute or element content.</summary>
    public static string Escape(string text)
    {
        var needs = false;
        foreach (var c in text)
        {
            if (c is '&' or '<' or '>' or '"' || c < 0x20 && c is not ('\t' or '\n' or '\r'))
            {
                needs = true;
                break;
            }
        }

        if (!needs)
            return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c is not ('\t' or '\n' or '\r'))
                        break; // not valid in XML 1.0
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>The <c>#rrggbb</c> of a colour (a CMYK colour is converted, as the raster canvas does).</summary>
    public static string Hex(PaintColor color)
    {
        var (r, g, b) = color.ToRgb();
        return string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}");
    }

    /// <summary>The alpha of a colour, 0..1.</summary>
    public static double Alpha(PaintColor color) => color.A / 255d;
}

/// <summary>The six numbers of an SVG <c>matrix()</c>, in its own naming (a, b, c, d, e, f).</summary>
internal readonly record struct Matrix3x2Value(double A, double B, double C, double D, double E, double F)
{
    public static Matrix3x2Value From(System.Numerics.Matrix3x2 m) => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;
}
