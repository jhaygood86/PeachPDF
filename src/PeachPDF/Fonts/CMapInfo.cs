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
    }
}
