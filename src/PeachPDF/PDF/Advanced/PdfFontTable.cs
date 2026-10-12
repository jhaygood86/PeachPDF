#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// https://www.pdfsharp.com/
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

using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachPDF.PDF.Drawing;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced
{
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
            PdfFont pdfFont;
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
}
