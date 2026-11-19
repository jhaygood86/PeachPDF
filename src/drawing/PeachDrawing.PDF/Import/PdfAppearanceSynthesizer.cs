using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Makes the appearance of an annotation that has none of its own (no <c>/AP</c> normal stream), as a viewer must: a text-markup annotation
/// is drawn from its quadrilaterals and color, a form field from its value and <c>/MK</c> look. The content is in page space (the
/// annotation's own rectangle is applied inside it), so it is run under the page's transform with the resources returned beside it.
/// </summary>
internal static partial class PdfAppearanceSynthesizer
{
    /// <summary>A synthesized appearance: a content stream in page space and the resources it names (null for none).</summary>
    internal sealed record Result(byte[] Content, PdfDictionary? Resources);

    /// <summary>The appearance of <paramref name="annot"/>, or null when it is of a kind this does not draw.</summary>
    public static Result? Create(PdfSourceDocument doc, PdfDictionary annot, PdfDictionary? pageResources, PeachDrawing.Core.RenderContext context)
    {
        return doc.NameOf(annot.GetRaw("Subtype")) switch
        {
            "/Highlight" or "/Underline" or "/StrikeOut" or "/Squiggly" => TextMarkup(doc, annot),
            "/Widget" => Widget(doc, annot, context),
            "/Link" => LinkBorder(doc, annot),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- shared

    internal static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    internal static Result Build(PdfSourceDocument doc, string content, string? resources)
    {
        PdfDictionary? parsed = null;
        if (resources is not null)
        {
            var lexer = new PdfLexer(Encoding.Latin1.GetBytes(resources));
            parsed = doc.ParseItem(lexer, lexer.Next()) as PdfDictionary;
        }

        return new Result(Encoding.Latin1.GetBytes(content), parsed);
    }

    /// <summary>The operator that sets a color given as a PDF array of 1, 3 or 4 numbers (gray, RGB, CMYK), or <paramref name="fallback"/> when there is none.</summary>
    internal static string ColorOperator(PdfSourceDocument doc, PdfItem? array, bool fill, string fallback)
    {
        if (doc.Resolve(array) is not PdfArray { Elements.Count: 1 or 3 or 4 } c)
            return fallback;
        var values = string.Join(' ', System.Linq.Enumerable.Select(c.Elements, e => Num(doc.NumberOf(e))));
        return c.Elements.Count switch
        {
            1 => values + (fill ? " g " : " G "),
            3 => values + (fill ? " rg " : " RG "),
            _ => values + (fill ? " k " : " K "),
        };
    }

    // ---------------------------------------------------------------- link border

    /// <summary>
    /// The border of a link annotation (PDF 12.5.2): a rectangle in the <c>/C</c> color (black without one), as wide as <c>/BS /W</c> or the
    /// third number of <c>/Border</c> says (1 without either), dashed by <c>/BS /D</c> or the fourth entry of <c>/Border</c>. A width of zero
    /// draws nothing, which is how most producers say a link has no frame.
    /// </summary>
    static Result? LinkBorder(PdfSourceDocument doc, PdfDictionary annot)
    {
        if (doc.ArrayOf(annot, "Rect") is not { Elements.Count: >= 4 } rect)
            return null;
        double width = 1;
        PdfArray? dash = null;
        if (doc.DictOf(annot, "BS") is { } bs)
        {
            if (bs.ContainsKeyRaw("W")) width = doc.NumberOf(bs.GetRaw("W"), 1);
            if (doc.NameOf(bs.GetRaw("S")) == "/D") dash = doc.ArrayOf(bs, "D") ?? ParseArray(doc, "[3]");
        }
        else if (doc.ArrayOf(annot, "Border") is { Elements.Count: >= 3 } border)
        {
            width = doc.NumberOf(border.Elements[2], 1);
            if (border.Elements.Count >= 4 && doc.Resolve(border.Elements[3]) is PdfArray { Elements.Count: > 0 } pattern) dash = pattern;
        }

        if (!(width > 0)) return null;
        double x0 = Math.Min(doc.NumberOf(rect.Elements[0]), doc.NumberOf(rect.Elements[2])), y0 = Math.Min(doc.NumberOf(rect.Elements[1]), doc.NumberOf(rect.Elements[3]));
        double w = Math.Abs(doc.NumberOf(rect.Elements[2]) - doc.NumberOf(rect.Elements[0])), h = Math.Abs(doc.NumberOf(rect.Elements[3]) - doc.NumberOf(rect.Elements[1]));
        if (!(w > width) || !(h > width)) return null;

        var content = new StringBuilder(ColorOperator(doc, annot.GetRaw("C"), fill: false, "0 0 0 RG "));
        content.Append($"{Num(width)} w ");
        if (dash is not null) content.Append('[').Append(string.Join(' ', System.Linq.Enumerable.Select(dash.Elements, e => Num(doc.NumberOf(e))))).Append("] 0 d ");
        content.Append($"{Num(x0 + width / 2)} {Num(y0 + width / 2)} {Num(w - width)} {Num(h - width)} re S\n");
        return Build(doc, content.ToString(), null);
    }

    static PdfArray ParseArray(PdfSourceDocument doc, string text)
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes(text));
        return (PdfArray)doc.ParseItem(lexer, lexer.Next());
    }

    // ---------------------------------------------------------------- text markup

    /// <summary>
    /// Highlight (a multiplied fill of each quad's box, yellow by default), underline and strike-out (a line along the bottom or middle of
    /// it, black by default) and squiggly (a zigzag along the bottom), as PDFium draws them for an annotation without an appearance.
    /// </summary>
    static Result? TextMarkup(PdfSourceDocument doc, PdfDictionary annot)
    {
        if (doc.ArrayOf(annot, "QuadPoints") is not { Elements.Count: >= 8 } quads)
            return null;
        var subtype = doc.NameOf(annot.GetRaw("Subtype"));
        bool highlight = subtype == "/Highlight";
        double opacity = annot.ContainsKeyRaw("CA") ? Math.Clamp(doc.NumberOf(annot.GetRaw("CA"), 1), 0, 1) : 1;

        var content = new StringBuilder("/GS gs ");
        content.Append(highlight ? ColorOperator(doc, annot.GetRaw("C"), fill: true, "1 1 0 rg ") : ColorOperator(doc, annot.GetRaw("C"), fill: false, "0 0 0 RG "));

        for (int q = 0; q + 8 <= quads.Elements.Count; q += 8)
        {
            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
            for (int i = 0; i < 8; i += 2)
            {
                double x = doc.NumberOf(quads.Elements[q + i]), y = doc.NumberOf(quads.Elements[q + i + 1]);
                (left, right) = (Math.Min(left, x), Math.Max(right, x));
                (bottom, top) = (Math.Min(bottom, y), Math.Max(top, y));
            }

            const double LineWidth = 1, Step = 2;
            switch (subtype)
            {
                case "/Highlight":
                    content.Append($"{Num(left)} {Num(top)} m {Num(right)} {Num(top)} l {Num(right)} {Num(bottom)} l {Num(left)} {Num(bottom)} l h f\n");
                    break;
                case "/Underline":
                    content.Append($"{Num(left)} {Num(bottom + LineWidth)} m {Num(right)} {Num(bottom + LineWidth)} l S\n");
                    break;
                case "/StrikeOut":
                    content.Append($"{Num(left)} {Num((top + bottom) / 2)} m {Num(right)} {Num((top + bottom) / 2)} l S\n");
                    break;
                default:
                    {
                        double high = bottom + Step, x = left + Step;
                        bool up = false;
                        content.Append($"{Num(LineWidth)} w {Num(left)} {Num(high)} m ");
                        while (x < right)
                        {
                            content.Append($"{Num(x)} {Num(up ? high : bottom)} l ");
                            x += Step;
                            up = !up;
                        }

                        double remainder = right + Step - x;
                        content.Append(up ? $"{Num(right)} {Num(bottom + remainder)} l S\n" : $"{Num(right)} {Num(high - remainder)} l S\n");
                        break;
                    }
            }
        }

        var blend = highlight ? "Multiply" : "Normal";
        return Build(doc, content.ToString(), $"<< /ExtGState << /GS << /BM /{blend} /CA {Num(opacity)} /ca {Num(opacity)} >> >> >>");
    }
}
