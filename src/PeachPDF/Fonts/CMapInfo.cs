#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PeachPDF.PdfSharpCore.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

using PeachPDF.Fonts.OpenType;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PeachPDF.Fonts
{
    /// <summary>
    /// Helper class that determines the characters used in a particular font.
    /// </summary>
    internal class CMapInfo
    {
        public CMapInfo(OpenTypeDescriptor descriptor)
        {
            Debug.Assert(descriptor != null);
            _descriptor = descriptor;
        }
        internal OpenTypeDescriptor _descriptor;

        /// <summary>
        /// Adds the characters of the specified string to the hashtable, keyed by Unicode scalar value
        /// (codepoint). Astral characters are handled as a single codepoint, not a surrogate pair.
        /// </summary>
        public void AddChars(string text)
        {
            if (text != null)
            {
                bool symbol = _descriptor.FontFace.cmap.symbol;
                foreach (Rune rune in text.EnumerateRunes())
                {
                    int codepoint = rune.Value;
                    if (!CharacterToGlyphIndex.ContainsKey(codepoint))
                    {
                        Rune lookup = rune;
                        if (symbol && codepoint <= 0xFFFF)
                        {
                            // Remap for symbol fonts (BMP-only).
                            lookup = new Rune(codepoint | (_descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));
                        }
                        int glyphIndex = _descriptor.CharCodeToGlyphIndex(lookup);
                        CharacterToGlyphIndex.Add(codepoint, glyphIndex);
                        GlyphIndices[glyphIndex] = null;
                    }
                }
            }
        }

        /// <summary>
        /// Shapes <paramref name="text"/> (GSUB ligature substitution, when the font and
        /// <paramref name="features"/> call for it) and registers every resulting glyph for
        /// embedding/subsetting. Unlike <see cref="AddChars"/>, a ligature glyph has no single
        /// Unicode scalar to key on, so its source text is recorded in <see cref="LigatureGlyphToText"/>
        /// instead of <see cref="CharacterToGlyphIndex"/> - <see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfToUnicodeMap"/>
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
        public void AddShapedText(string text, TextShapingFeatures features, string? logicalText = null)
        {
            if (text == null)
                return;

            var source = logicalText != null && logicalText.Length == text.Length && logicalText != text
                ? logicalText
                : text;

            foreach (ShapedGlyph glyph in _descriptor.Shape(text, features))
            {
                GlyphIndices[glyph.GlyphIndex] = null;
                LigatureGlyphToText[glyph.GlyphIndex] = source.Substring(glyph.ClusterStart, glyph.ClusterLength);
            }
        }

        /// <summary>
        /// Adds the glyphIndices to the hashtable.
        /// </summary>
        public void AddGlyphIndices(string glyphIndices)
        {
            if (glyphIndices != null)
            {
                int length = glyphIndices.Length;
                for (int idx = 0; idx < length; idx++)
                {
                    int glyphIndex = glyphIndices[idx];
                    GlyphIndices[glyphIndex] = null;
                }
            }
        }

        public int[] GetGlyphIndices()
        {
            int[] indices = new int[GlyphIndices.Count];
            GlyphIndices.Keys.CopyTo(indices, 0);
            Array.Sort(indices);
            return indices;
        }

        public Dictionary<int, int> CharacterToGlyphIndex = new Dictionary<int, int>();
        public Dictionary<int, object> GlyphIndices = new Dictionary<int, object>();

        /// <summary>Glyph index to source text, for glyphs <see cref="AddShapedText"/> produced that
        /// represent more than one character (GSUB ligatures) or an astral codepoint.</summary>
        public Dictionary<int, string> LigatureGlyphToText = new Dictionary<int, string>();
    }
}
