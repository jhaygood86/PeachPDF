#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
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

using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// The bytes of a font file.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal class XFontSource
    {
        // Implementation Notes
        // 
        // * XFontSource represents a single font (file) in memory.
        // * An XFontSource hold a reference to it OpenTypeFontface.
        // * To prevent large heap fragmentation this class must exists only once.
        // * TODO: ttcf

        // Signature of a true type collection font.
        const uint ttcf = 0x66637474;

        XFontSource(byte[] bytes, ulong key)
        {
            _fontName = null;
            _bytes = bytes;
            _key = key;
        }

        // FontHelper.CalcChecksum is an O(n) scan of the whole buffer - memoizing it by buffer identity
        // means a font whose bytes we've already seen (the common case: FontFactory's own FontSourcesByKey
        // cache below is process-wide, and FontResolver.GetFont now returns a stable byte[] per system
        // font path too - see FontResolver.cs) never pays that scan again just to recompute the very key
        // that would have found the existing cache entry. ConditionalWeakTable so a byte[] this process
        // stops referencing elsewhere doesn't keep its checksum alive forever.
        private static readonly ConditionalWeakTable<byte[], object> _checksumCache = new();

        private static ulong GetOrComputeChecksum(byte[] bytes)
        {
            if (_checksumCache.TryGetValue(bytes, out var boxed))
                return (ulong)boxed;

            var key = FontHelper.CalcChecksum(bytes);
            _checksumCache.AddOrUpdate(bytes, key);
            return key;
        }

        /// <summary>
        /// Gets an existing font source or creates a new one.
        /// A new font source is cached in font factory.
        /// </summary>
        public static XFontSource GetOrCreateFrom(byte[] bytes)
        {
            ulong key = GetOrComputeChecksum(bytes);
            XFontSource fontSource;
            if (!FontFactory.TryGetFontSourceByKey(key, out fontSource))
            {
                fontSource = new XFontSource(bytes, key);
                // Theoretically the font source could be created by a differend thread in the meantime.
                fontSource = FontFactory.CacheFontSource(fontSource);
            }
            return fontSource;
        }
        public static XFontSource CreateCompiledFont(byte[] bytes)
        {
            XFontSource fontSource = new XFontSource(bytes, 0);
            return fontSource;
        }

        /// <summary>
        /// Gets or sets the fontface.
        /// </summary>
        internal OpenTypeFontface Fontface
        {
            get { return _fontface; }
            set
            {
                _fontface = value;
                _fontName = value.name.FullFontName;
            }
        }
        OpenTypeFontface _fontface = null!;

        /// <summary>
        /// Gets the key that uniquely identifies this font source.
        /// </summary>
        internal ulong Key
        {
            get
            {
                if (_key == 0)
                    _key = FontHelper.CalcChecksum(Bytes);
                return _key;
            }
        }
        ulong _key;

        public void IncrementKey()
        {
            // HACK: Depends on implementation of CalcChecksum.
            // Increment check sum and keep length untouched.
            _key += 1ul << 32;
        }

        /// <summary>
        /// Gets the name of the font's name table.
        /// </summary>
        public string FontName
        {
            get { return _fontName; }
        }
        string _fontName = null!;

        /// <summary>
        /// Gets the bytes of the font.
        /// </summary>
        public byte[] Bytes
        {
            get { return _bytes; }
        }
        readonly byte[] _bytes;

        public override int GetHashCode()
        {
            return (int)((Key >> 32) ^ Key);
        }

        public override bool Equals(object? obj)
        {
            XFontSource fontSource = obj as XFontSource;
            if (fontSource == null)
                return false;
            return Key == fontSource.Key;
        }

        /// <summary>
        /// Gets the DebuggerDisplayAttribute text.
        /// </summary>
        // ReSha rper disable UnusedMember.Local
        internal string DebuggerDisplay
        // ReShar per restore UnusedMember.Local
        {
            // The key is converted to a value a human can remember during debugging.
            get { return String.Format(CultureInfo.InvariantCulture, "XFontSource: '{0}', keyhash={1}", FontName, Key % 99991 /* largest prime number less than 100000 */); }
        }
    }
}