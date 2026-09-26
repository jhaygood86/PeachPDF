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

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// Global table of all glyph typefaces.
    /// </summary>
    internal class TypefaceCache
    {
        // A FontResolver instance's own typeface-key-keyed glyph typeface cache, used only for its custom
        // (AddFont/@font-face) families so two PdfGenerators registering different bytes under one family name
        // never share a typeface. Held weakly against the resolver, so it is collected along with it.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FontResolver, Dictionary<string, Typeface>> InstanceCaches = new();

        internal static Dictionary<string, Typeface> ForInstance(FontResolver resolver) => InstanceCaches.GetOrCreateValue(resolver);

        TypefaceCache()
        {
            _glyphTypefacesByKey = new ConcurrentDictionary<string, Typeface>();
        }

        public static bool TryGetGlyphTypeface(string key, out Typeface glyphTypeface)
        {
            try
            {
                FontLock.Enter();
                bool result = Singleton._glyphTypefacesByKey.TryGetValue(key, out glyphTypeface);
                return result;
            }
            finally { FontLock.Exit(); }
        }

        public static void AddGlyphTypeface(Typeface glyphTypeface)
        {
            try
            {
                FontLock.Enter();
                TypefaceCache cache = Singleton;
                cache._glyphTypefacesByKey.TryAdd(glyphTypeface.Key, glyphTypeface);
            }
            finally { FontLock.Exit(); }
        }

        /// <summary>
        /// Gets the singleton.
        /// </summary>
        static TypefaceCache Singleton
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
                            _singleton = new TypefaceCache();
                    }
                    finally { FontLock.Exit(); }
                }
                return _singleton;
            }
        }
        static volatile TypefaceCache _singleton = null!;

        /// <summary>
        /// Maps typeface key to glyph typeface.
        /// </summary>
        readonly ConcurrentDictionary<string, Typeface> _glyphTypefacesByKey;
    }
}