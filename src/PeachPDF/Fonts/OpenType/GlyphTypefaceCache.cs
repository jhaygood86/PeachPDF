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

using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>
    /// Global table of all glyph typefaces.
    /// </summary>
    internal class GlyphTypefaceCache
    {
        GlyphTypefaceCache()
        {
            _glyphTypefacesByKey = new ConcurrentDictionary<string, XGlyphTypeface>();
        }

        public static bool TryGetGlyphTypeface(string key, out XGlyphTypeface glyphTypeface)
        {
            try
            {
                FontLock.Enter();
                bool result = Singleton._glyphTypefacesByKey.TryGetValue(key, out glyphTypeface);
                return result;
            }
            finally { FontLock.Exit(); }
        }

        public static void AddGlyphTypeface(XGlyphTypeface glyphTypeface)
        {
            try
            {
                FontLock.Enter();
                GlyphTypefaceCache cache = Singleton;
                cache._glyphTypefacesByKey.TryAdd(glyphTypeface.Key, glyphTypeface);
            }
            finally { FontLock.Exit(); }
        }

        /// <summary>
        /// Gets the singleton.
        /// </summary>
        static GlyphTypefaceCache Singleton
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_singleton == null)
                {
                    try
                    {
                        FontLock.Enter();
                        if (_singleton == null)
                            _singleton = new GlyphTypefaceCache();
                    }
                    finally { FontLock.Exit(); }
                }
                return _singleton;
            }
        }
        static volatile GlyphTypefaceCache _singleton = null!;

        /// <summary>
        /// Maps typeface key to glyph typeface.
        /// </summary>
        readonly ConcurrentDictionary<string, XGlyphTypeface> _glyphTypefacesByKey;
    }
}