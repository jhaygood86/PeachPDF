using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>One shown glyph as the interpreter reports it when reading text: what it stands for and where it sits on the page.</summary>
/// <param name="Text">the Unicode text the glyph stands for, possibly several characters (a ligature) or none</param>
/// <param name="Start">where the glyph's baseline starts, from the top-left of the page, y down</param>
/// <param name="End">where the glyph's advance ends on the baseline</param>
/// <param name="Up">the font size along the direction the glyph points up, on the page</param>
internal readonly record struct PdfTextGlyph(string Text, Vector2 Start, Vector2 End, Vector2 Up);

/// <summary>Reads the text of a page by running its content with a sink for glyphs instead of a canvas to paint on.</summary>
internal static class PdfTextExtractor
{
    /// <summary>The text of <paramref name="page"/>, read with fonts loaded through <paramref name="host"/>.</summary>
    public static PdfPageText Extract(PdfSourceDocument doc, PdfDictionary page, Canvas host)
    {
        var glyphs = new List<PdfTextGlyph>();
        var interpreter = new PdfContentInterpreter(doc, host) { TextSink = glyphs.Add, TextBase = PdfPagePainter.PageToCanvas(doc, page) };
        try { interpreter.Run(doc.GetPageContent(page), doc.InheritedAttribute(page, "Resources") as PdfDictionary); }
        finally { interpreter.Finish(); }
        return glyphs.Count == 0 ? PdfPageText.Empty : PdfPageText.Build(glyphs);
    }
}
