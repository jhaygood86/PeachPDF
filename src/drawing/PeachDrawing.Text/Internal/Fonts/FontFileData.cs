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

        FontFileData(byte[] bytes, FontContentHash key)
        {
            _fontName = null;
            _bytes = bytes;
            _key = key;
        }

        // Hashing is an O(n) pass over the whole buffer - memoizing it by buffer identity
        // means a font whose bytes we've already seen (the common case: FontFactory's own FontSourcesByKey
        // cache below is process-wide, and FontResolver.GetFont now returns a stable byte[] per system
        // font path too - see FontResolver.cs) never pays that pass again just to recompute the very key
        // that would have found the existing cache entry. ConditionalWeakTable so a byte[] this process
        // stops referencing elsewhere doesn't keep its hash alive forever.
        private static readonly ConditionalWeakTable<byte[], object> _hashCache = new();

        /// <summary>
        /// The content hash of <paramref name="bytes"/>, computed once per buffer (by reference) and remembered for as long as the
        /// buffer lives.
        /// </summary>
        public static FontContentHash GetOrComputeHash(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (_hashCache.TryGetValue(bytes, out var boxed))
                return (FontContentHash)boxed;

            var hash = FontContentHash.Compute(bytes);
            _hashCache.AddOrUpdate(bytes, hash);
            return hash;
        }

        /// <summary>
        /// Gets an existing font source or creates a new one.
        /// A new font source is cached in font factory.
        /// </summary>
        public static FontFileData GetOrCreateFrom(byte[] bytes)
        {
            FontContentHash key = GetOrComputeHash(bytes);
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
            FontFileData fontSource = new FontFileData(bytes, default);
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
        internal FontContentHash Key
        {
            get
            {
                if (_key.IsEmpty)
                    // Only a compiled font (CreateCompiledFont) gets here, and it stands outside the cache: its bytes may be
                    // changed by whoever made it, so its hash is not remembered by buffer the way the cached fonts' are.
                    _key = FontContentHash.Compute(Bytes);
                return _key;
            }
        }
        FontContentHash _key;

        /// <summary>The <see cref="Key"/> as text (32 lowercase hexadecimal digits), made once.</summary>
        internal string KeyText => _keyText ??= Key.ToString();
        string _keyText;

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
            return Key.GetHashCode();
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
            // The first digits of the key are enough for a human to tell fonts apart during debugging.
            get { return String.Format(CultureInfo.InvariantCulture, "FontFileData: '{0}', keyhash={1}", FontName, KeyText.Substring(0, 8)); }
        }
    }
}