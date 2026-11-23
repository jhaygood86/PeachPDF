// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Text;
using PeachDrawing.Text.Export;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Helper class that determines the characters used in a particular font.
/// </summary>
internal class CharacterMapInfo
{
    public CharacterMapInfo(Typeface typeface)
    {
        Debug.Assert(typeface is not null);
        _typeface = typeface;
    }
    readonly Typeface _typeface;

    /// <summary>
    /// Adds the characters of the specified string to the hashtable, keyed by Unicode scalar value
    /// (codepoint). Astral characters are handled as a single codepoint, not a surrogate pair.
    /// </summary>
    public void AddChars(string text)
    {
        if (text is not null)
        {
            var metrics = _typeface.Metrics;
            bool symbol = metrics.IsSymbolic;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;
                if (!CharacterToGlyphIndex.ContainsKey(codepoint))
                {
                    Rune lookup = rune;
                    if (symbol && codepoint <= 0xFFFF)
                    {
                        // Remap for symbol fonts (BMP-only).
                        lookup = new Rune(codepoint | (metrics.FirstCharIndex & 0xFF00));
                    }
                    _typeface.TryMapRune(lookup, out ushort mapped);
                    int glyphIndex = mapped;
                    CharacterToGlyphIndex.Add(codepoint, glyphIndex);
                    GlyphIndices.Add(glyphIndex);
                }
            }
        }
    }

    /// <summary>
    /// Shapes <paramref name="text"/> (GSUB ligature substitution, when the font and
    /// <paramref name="features"/> call for it) and registers every resulting glyph for
    /// embedding/subsetting. Unlike <see cref="AddChars"/>, a ligature glyph has no single
    /// Unicode scalar to key on, so its source text is recorded in <see cref="LigatureGlyphToText"/>
    /// instead of <see cref="CharacterToGlyphIndex"/> - <c>PdfToUnicodeMap</c>
    /// merges both when building the PDF ToUnicode map.
    ///
    /// <paramref name="logicalText"/> is <paramref name="text"/>'s true logical-order source,
    /// <b>positionally aligned</b> with it - same length, same UTF-16 index for the same glyph's
    /// cluster - when a caller supplies one because the two differ: null (the common case: LTR
    /// text, or any word never reversed/mirrored/reordered for display) means they're the same, and
    /// every glyph's ToUnicode destination is simply its own substring of <paramref name="text"/>, as
    /// before this parameter existed. When they differ, shaping still runs on <paramref name="text"/>
    /// (the real glyph IDs/positions depend on the visually-correct, already-transformed string), but
    /// each glyph's ToUnicode destination is instead the same cluster range read from
    /// <paramref name="logicalText"/>, so copy/pasting a reversed/mirrored/reordered RTL run out of
    /// the PDF recovers its true source characters in their true order - confirmed as a real
    /// extraction defect against real PDFium/MuPDF output otherwise (a parenthesized Hebrew word's
    /// parentheses landing in the wrong position on extraction).
    ///
    /// Building a correctly-aligned <paramref name="logicalText"/> is the caller's job, since only
    /// the caller knows which transform actually produced <paramref name="text"/>: a whole-run L2
    /// reversal + L4 mirroring (<c>BidiMirrorResolver.ApplyMirroring</c>) is undone position-wise by
    /// <c>BidiMirrorResolver.ReverseRunes</c> (reverse the stable pre-transform source, without
    /// mirroring - mirroring only changes a character's *value*, reversal alone already recovers its
    /// *position*); a per-character physical list reorder (SVG's own bidi pass) instead has each
    /// transformed character's own true source directly at hand already, positioned according to
    /// wherever that character ended up. Both end up needing to satisfy the exact same
    /// positional-alignment contract this method reads. A theoretical astral-codepoint RTL character
    /// (no real script in current use) reversed via <c>ReverseRunes</c> stays alignment-correct since
    /// that reversal is itself already Rune-based (surrogate pairs move as one unit).
    /// </summary>
    public int[]? AddShapedText(string text, ShapeSettings features, string? logicalText = null)
    {
        if (text is null)
            return null;

        var source = logicalText is not null && logicalText.Length == text.Length && logicalText != text
            ? logicalText
            : text;

        var glyphs = Shaper.Shape(_typeface, text, features).Glyphs;
        int[]? cids = null;

        for (var i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            GlyphIndices.Add(glyph.GlyphIndex);

            // A glyph the shaper produced from no source characters of its own - the component glyphs a
            // decomposing substitution adds beside the glyph that carries the character (an Arabic letter's
            // dots, say) - has an empty cluster, and so no text of its own to extract. An empty destination
            // is not valid ToUnicode (ISO 32000-2, 9.10.3; PdfPig throws on it), and leaving the glyph
            // unmapped makes readers fall back to reading its code as a character (MuPDF and PDFium extract
            // a glyph 45 as '-'), so it maps to an invisible character instead; the glyph that does carry
            // the character keeps the real text.
            if (glyph.ClusterLength <= 0)
            {
                if (!LigatureGlyphToText.ContainsKey(glyph.GlyphIndex))
                    LigatureGlyphToText[glyph.GlyphIndex] = NoText;
                if (cids is not null)
                    cids[i] = glyph.GlyphIndex;
                continue;
            }

            var cid = MapGlyphToText(glyph.GlyphIndex, source, glyph.ClusterStart, glyph.ClusterLength);
            if (cid != glyph.GlyphIndex && cids is null)
            {
                cids = new int[glyphs.Count];
                for (var k = 0; k < i; k++)
                    cids[k] = glyphs[k].GlyphIndex;
            }
            if (cids is not null)
                cids[i] = cid;
        }

        return cids;
    }

    /// <summary>
    /// <see cref="MapGlyphToText(int, string)"/> for a piece of <paramref name="source"/>, without cutting the piece out
    /// unless it is going to be kept. A word drawn again maps every one of its glyphs to the text they already map to, so
    /// comparing in place spares a string per glyph of every word drawn.
    /// </summary>
    private int MapGlyphToText(int glyphIndex, string source, int start, int length)
    {
        var piece = source.AsSpan(start, length);
        var isSpace = piece.Length == 1 && piece[0] == ' ';

        if (LigatureGlyphToText.TryGetValue(glyphIndex, out var existing))
        {
            if (!isSpace && existing == " ")
                return glyphIndex;

            // Already mapped to exactly this text: setting it again changes nothing.
            if (existing.AsSpan().SequenceEqual(piece))
                return glyphIndex;

            // Two different letters drawn with one glyph (an Arabic letter's body glyph is shared by the letters
            // that differ only in their dots): one ToUnicode destination per code cannot be right for both, so the
            // later one is drawn with a code of its own that maps to the same glyph.
            if (!isSpace && existing != NoText && TryGetVariantCid(glyphIndex, piece.ToString(), out var variant))
                return variant;
        }

        LigatureGlyphToText[glyphIndex] = isSpace ? " " : piece.ToString();
        return glyphIndex;
    }

    /// <summary>
    /// The code (CID) that stands for <paramref name="glyphIndex"/> drawn as <paramref name="text"/> when the glyph itself already
    /// stands for other text. Codes are taken from the top of the 16-bit range down, clear of every glyph index, and the descendant
    /// font maps them back to the glyph with a <c>/CIDToGIDMap</c> stream. False when the font cannot carry such a map or the
    /// range is used up, which leaves the glyph with the text drawn last, as before.
    /// </summary>
    private bool TryGetVariantCid(int glyphIndex, string text, out int cid)
    {
        if (VariantCidByGlyphAndText.TryGetValue((glyphIndex, text), out cid))
            return true;

        if (!VariantsAllowed || _nextVariantCid <= (GlyphIndices.Count == 0 ? 0 : GlyphIndices.Max()))
        {
            cid = 0;
            return false;
        }

        cid = _nextVariantCid--;
        VariantCidByGlyphAndText[(glyphIndex, text)] = cid;
        VariantCidToGlyph[cid] = glyphIndex;
        VariantCidToText[cid] = text;
        return true;
    }

    /// <summary>
    /// Whether the embedded subset of this font is a TrueType or whole-face program, which a <c>/CIDToGIDMap</c> can remap, rather than
    /// a compact CFF program, whose CIDs are its charset's and cannot. Decided once, with a trial export of the glyphs drawn so far.
    /// </summary>
    private bool VariantsAllowed => _variantsAllowed ??= CanRemapCids();

    private bool CanRemapCids()
    {
        try
        {
            var trial = TypefaceExporter.ExportSubset(_typeface, new HashSet<int>(GlyphIndices) { 0 }, keepCharacterMap: false);
            return !(trial.HasCffOutlines && trial.Format == ExportedFontFormat.CompactFontFormat);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool? _variantsAllowed;
    private int _nextVariantCid = 0xFFFF;

    /// <summary>The variant code for a glyph drawn as some text its own code does not stand for.</summary>
    internal Dictionary<(int Glyph, string Text), int> VariantCidByGlyphAndText { get; } = [];

    /// <summary>Each variant code and the glyph it draws.</summary>
    internal Dictionary<int, int> VariantCidToGlyph { get; } = [];

    /// <summary>Each variant code and the text it stands for.</summary>
    internal Dictionary<int, string> VariantCidToText { get; } = [];

    /// <summary>
    /// Records <paramref name="sourceText"/> as the ToUnicode destination of
    /// <paramref name="glyphIndex"/>. A glyph has one destination per font, so when two source
    /// sequences share a glyph, the one drawn last wins - except that U+0020 SPACE, once recorded,
    /// is never replaced, and always replaces. Many fonts draw U+00A0 NO-BREAK SPACE (and other
    /// spaces) with the space glyph, and every word separator is shown with that glyph, so a single
    /// <c>&amp;nbsp;</c> drawn after the last ordinary space would otherwise turn every word break
    /// of the document into U+00A0 on extraction - breaking <c>split(' ')</c>, search and copy and
    /// paste. The no-break space then extracts as an ordinary space, as it does from Chrome's PDFs
    /// in pypdf and PDFium.
    /// </summary>
    private void MapGlyphToText(int glyphIndex, string sourceText)
    {
        if (sourceText != " " && LigatureGlyphToText.TryGetValue(glyphIndex, out var existing) && existing == " ")
            return;

        LigatureGlyphToText[glyphIndex] = sourceText;
    }

    /// <summary>
    /// Adds the glyphIndices to the hashtable.
    /// </summary>
    public void AddGlyphIndices(string glyphIndices)
    {
        if (glyphIndices is not null)
        {
            int length = glyphIndices.Length;
            for (int idx = 0; idx < length; idx++)
            {
                int glyphIndex = glyphIndices[idx];
                GlyphIndices.Add(glyphIndex);
            }
        }
    }

    /// <summary>
    /// Registers one already-shaped glyph for font subsetting and maps its CID back to the source
    /// text it represents. Used by color-font vector painting's invisible text show, where shaping
    /// has already happened and repeating it merely to populate the PDF font would risk divergence.
    /// Per-occurrence <c>/ActualText</c> remains authoritative when the same glyph ID represents
    /// more than one source sequence in a document.
    /// </summary>
    public void AddShapedGlyph(int glyphIndex, string sourceText)
    {
        GlyphIndices.Add(glyphIndex);
        MapGlyphToText(glyphIndex, sourceText);
    }

    public int[] GetGlyphIndices()
    {
        int[] indices = new int[GlyphIndices.Count];
        GlyphIndices.CopyTo(indices);
        Array.Sort(indices);
        return indices;
    }

    public Dictionary<int, int> CharacterToGlyphIndex = [];
    public HashSet<int> GlyphIndices { get; } = [];

    /// <summary>Glyph index to source text, for glyphs <see cref="AddShapedText"/> produced that
    /// represent more than one character (GSUB ligatures) or an astral codepoint.</summary>
    public Dictionary<int, string> LigatureGlyphToText = [];

    /// <summary>What a glyph that carries no character of its own maps to: U+200B ZERO WIDTH SPACE.</summary>
    internal const string NoText = "\u200B";
}
