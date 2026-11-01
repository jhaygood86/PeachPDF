using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// A Type 3 font (PDF 9.6.5): each glyph is a content stream of its own, drawn with the interpreter under the font matrix. A glyph that
/// starts with <c>d1</c> is a shape painted in the current fill colour; one that starts with <c>d0</c> sets its own colours.
/// </summary>
internal sealed class PdfType3Font
{
    readonly Dictionary<int, string> _names = new();
    readonly Dictionary<int, double> _widths = new();
    readonly PdfDictionary _charProcs;
    readonly PdfSourceDocument _doc;

    PdfType3Font(PdfSourceDocument doc, PdfDictionary charProcs, Matrix3x2 fontMatrix, PdfDictionary? resources)
    {
        _doc = doc;
        _charProcs = charProcs;
        FontMatrix = fontMatrix;
        Resources = resources;
    }

    /// <summary>Carries glyph space onto text space (one unit of text space is one unit of font size).</summary>
    public Matrix3x2 FontMatrix { get; }

    /// <summary>The resources the glyph streams use; null when the font has none of its own (the page's apply).</summary>
    public PdfDictionary? Resources { get; }

    /// <summary>Reads <paramref name="fontDict"/>; returns null and a reason in <paramref name="why"/> when it has no glyph procedures.</summary>
    public static PdfType3Font? Load(PdfSourceDocument doc, PdfDictionary fontDict, out string? why)
    {
        why = null;
        if (doc.DictOf(fontDict, "CharProcs") is not { } procs) { why = "Type3 font without CharProcs"; return null; }
        var matrix = new Matrix3x2(0.001f, 0, 0, 0.001f, 0, 0);
        if (doc.ArrayOf(fontDict, "FontMatrix") is { Elements.Count: >= 6 } m)
            matrix = new Matrix3x2((float)doc.NumberOf(m.Elements[0]), (float)doc.NumberOf(m.Elements[1]), (float)doc.NumberOf(m.Elements[2]),
                (float)doc.NumberOf(m.Elements[3]), (float)doc.NumberOf(m.Elements[4]), (float)doc.NumberOf(m.Elements[5]));

        var font = new PdfType3Font(doc, procs, matrix, doc.DictOf(fontDict, "Resources"));
        if (doc.Resolve(fontDict.GetRaw("Encoding")) is PdfDictionary enc && doc.ArrayOf(enc, "Differences") is { } diff)
        {
            int code = 0;
            foreach (var item in diff.Elements)
            {
                var r = doc.Resolve(item);
                if (r is PdfInteger or PdfReal) code = doc.IntOf(r);
                else if (r is PdfName name) font._names[code++] = name.Value.TrimStart('/');
            }
        }
        int first = doc.IntOf(fontDict.GetRaw("FirstChar"));
        if (doc.ArrayOf(fontDict, "Widths") is { } widths)
            for (int i = 0; i < widths.Elements.Count; i++) font._widths[first + i] = doc.NumberOf(widths.Elements[i]);
        return font;
    }

    /// <summary>One shown code: its advance in text space (per unit of font size) and its glyph stream, if it has one.</summary>
    public readonly record struct Glyph(int Code, double Width, PdfDictionary? Procedure, bool IsWordSpace);

    /// <summary>Splits a shown string (one byte per char) into glyphs.</summary>
    public IEnumerable<Glyph> Decode(string shown)
    {
        foreach (char ch in shown)
        {
            int code = ch & 0xFF;
            PdfDictionary? proc = _names.TryGetValue(code, out var name) ? _doc.Resolve(_charProcs.GetRaw(name)) as PdfDictionary : null;
            // A width is in glyph space; the font matrix's horizontal scale carries it to text space.
            double width = _widths.TryGetValue(code, out var w) ? w * FontMatrix.M11 : 0;
            yield return new Glyph(code, width, proc, code == 32);
        }
    }
}
