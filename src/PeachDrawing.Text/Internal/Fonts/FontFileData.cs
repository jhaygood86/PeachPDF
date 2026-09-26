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

using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// The bytes of a font file.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal class FontFileData
    {
        // Implementation Notes
        // 
        // * FontFileData represents a single font (file) in memory.
        // * An FontFileData hold a reference to it OpenTypeFontface.
        // * To prevent large heap fragmentation this class must exists only once.
        // * TODO: ttcf

        // Signature of a true type collection font.
        const uint ttcf = 0x66637474;

        FontFileData(byte[] bytes, ulong key)
        {
            _fontName = null;
            _bytes = bytes;
            _key = key;
        }

        // CalcChecksum is an O(n) scan of the whole buffer - memoizing it by buffer identity
        // means a font whose bytes we've already seen (the common case: FontFactory's own FontSourcesByKey
        // cache below is process-wide, and FontResolver.GetFont now returns a stable byte[] per system
        // font path too - see FontResolver.cs) never pays that scan again just to recompute the very key
        // that would have found the existing cache entry. ConditionalWeakTable so a byte[] this process
        // stops referencing elsewhere doesn't keep its checksum alive forever.
        private static readonly ConditionalWeakTable<byte[], object> _checksumCache = new();

        /// <summary>
        /// Calculates an Adler32 checksum combined with the buffer length
        /// in a 64 bit unsigned integer.
        /// </summary>
        public static ulong CalcChecksum(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            const uint prime = 65521; // largest prime smaller than 65536
            uint s1 = 0;
            uint s2 = 0;
            int length = buffer.Length;
            int offset = 0;
            while (length > 0)
            {
                int n = 3800;
                if (n > length)
                    n = length;
                length -= n;
                while (--n >= 0)
                {
                    s1 += buffer[offset++];
                    s2 = s2 + s1;
                }
                s1 %= prime;
                s2 %= prime;
            }
            ulong ul1 = (ulong)s2 << 16;
            ul1 = ul1 | s1;
            ulong ul2 = (ulong)buffer.Length;
            return (ul1 << 32) | ul2;
        }

        private static ulong GetOrComputeChecksum(byte[] bytes)
        {
            if (_checksumCache.TryGetValue(bytes, out var boxed))
                return (ulong)boxed;

            var key = CalcChecksum(bytes);
            _checksumCache.AddOrUpdate(bytes, key);
            return key;
        }

        /// <summary>
        /// Gets an existing font source or creates a new one.
        /// A new font source is cached in font factory.
        /// </summary>
        public static FontFileData GetOrCreateFrom(byte[] bytes)
        {
            ulong key = GetOrComputeChecksum(bytes);
            FontFileData fontSource;
            if (!FontFactory.TryGetFontSourceByKey(key, out fontSource))
            {
                fontSource = new FontFileData(bytes, key);
                // Theoretically the font source could be created by a differend thread in the meantime.
                fontSource = FontFactory.CacheFontSource(fontSource);
            }
            return fontSource;
        }
        public static FontFileData CreateCompiledFont(byte[] bytes)
        {
            FontFileData fontSource = new FontFileData(bytes, 0);
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
                    _key = CalcChecksum(Bytes);
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
            FontFileData fontSource = obj as FontFileData;
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
            get { return String.Format(CultureInfo.InvariantCulture, "FontFileData: '{0}', keyhash={1}", FontName, Key % 99991 /* largest prime number less than 100000 */); }
        }
    }
}