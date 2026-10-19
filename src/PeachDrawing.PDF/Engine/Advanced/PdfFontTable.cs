// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.PDF.Engine.Drawing;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachDrawing.PDF.Engine.Advanced;

internal enum FontType
{
    /// <summary>
    /// TrueType with WinAnsi encoding.
    /// </summary>
    TrueType = 1,

    /// <summary>
    /// TrueType with Identity-H or Identity-V encoding (unicode).
    /// </summary>
    Type0 = 2,
}

/// <summary>
/// Contains all used fonts of a document.
/// </summary>
internal sealed class PdfFontTable : PdfResourceTable
{
    /// <summary>
    /// Initializes a new instance of this class, which is a singleton for each document.
    /// </summary>
    public PdfFontTable(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Gets a PdfFont from an TypefaceFont. If no PdfFont already exists, a new one is created.
    /// </summary>
    public PdfFont GetFont(TypefaceFont font)
    {
        string selector = Selectors.GetValue(font.Typeface, ComputeKey);
        PdfFont? pdfFont;
        if (!_fonts.TryGetValue(selector, out pdfFont))
        {
            pdfFont = new PdfType0Font(Owner, font);
            //pdfFont.Document = _document;
            Debug.Assert(pdfFont.Owner == Owner);
            _fonts[selector] = pdfFont;
        }
        return pdfFont;
    }

    /// <summary>
    /// Tries to gets a PdfFont from the font dictionary.
    /// Returns null if no such PdfFont exists.
    /// </summary>
    public PdfFont TryGetFont(string idName)
    {
        Debug.Assert(false);
        //FontSelector selector = new FontSelector(idName);
        string selector = null;
        PdfFont pdfFont;
        _fonts.TryGetValue(selector, out pdfFont);
        return pdfFont;
    }

    internal static string ComputeKey(Typeface typeface)
    {
        // Include the font source's content hash, not just its internal FullFaceName: two DIFFERENT
        // font files can share one internal name (a common webfont-subset pattern), and keying the
        // per-document embed cache on the name alone would merge them into a single embedded font -
        // the second silently rendering with the first's glyphs. The hash makes identity content-
        // addressed (identical bytes still coalesce to one embed; different bytes never do).
        string key = typeface.FullName.ToLowerInvariant() + "/" + typeface.ContentHash + "/" + typeface.VariationKey +
            (typeface.IsBold ? "/b" : "") + (typeface.IsItalic ? "/i" : "") + "/Unicode";
        return key;
    }

    /// <summary>
    /// Map from PdfFontSelector to PdfFont.
    /// </summary>
    /// <summary>Each typeface's embed key, worked out once however many sizes of it are drawn.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Typeface, string> Selectors = new();

    readonly Dictionary<string, PdfFont> _fonts = new Dictionary<string, PdfFont>();

    public void PrepareForSave()
    {
        foreach (PdfFont font in _fonts.Values)
            font.PrepareForSave();
    }
}
