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

using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Pdf.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Represents a ToUnicode map for composite font.
    /// </summary>
    internal sealed class PdfToUnicodeMap : PdfDictionary
    {
        public PdfToUnicodeMap(PdfDocument document)
            : base(document)
        { }

        public PdfToUnicodeMap(PdfDocument document, CMapInfo cmapInfo)
            : base(document)
        {
            _cmapInfo = cmapInfo;
        }

        /// <summary>
        /// Gets or sets the CMap info.
        /// </summary>
        public CMapInfo CMapInfo
        {
            get { return _cmapInfo; }
            set { _cmapInfo = value; }
        }
        CMapInfo _cmapInfo = null!;

        /// <summary>
        /// Creates the ToUnicode map from the CMapInfo.
        /// </summary>
        internal override void PrepareForSave()
        {
            base.PrepareForSave();

            // This code comes literally from PDF Reference
            string prefix =
              "/CIDInit /ProcSet findresource begin\n" +
              "12 dict begin\n" +
              "begincmap\n" +
              "/CIDSystemInfo << /Registry (Adobe)/Ordering (UCS)/Supplement 0>> def\n" +
              "/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n";
            string suffix = "endcmap CMapName currentdict /CMap defineresource pop end end";

            Dictionary<int, int> glyphIndexToCharacter = new Dictionary<int, int>();
            int lowIndex = 65536, hiIndex = -1;
            foreach (KeyValuePair<int, int> entry in _cmapInfo.CharacterToGlyphIndex)
            {
                int index = entry.Value;         // glyph index
                lowIndex = Math.Min(lowIndex, index);
                hiIndex = Math.Max(hiIndex, index);
                //glyphIndexToCharacter.Add(index, entry.Key);
                glyphIndexToCharacter[index] = entry.Key;   // Unicode scalar value (codepoint)
            }

            MemoryStream ms = new MemoryStream();
            StreamWriter wrt = new StreamWriter(ms, Encoding.UTF8);
            wrt.Write(prefix);

            wrt.WriteLine("1 begincodespacerange");
            wrt.WriteLine(String.Format("<{0:X4}><{1:X4}>", lowIndex, hiIndex));
            wrt.WriteLine("endcodespacerange");

            // Sorting seems not necessary. The limit is 100 entries, we will see.
            wrt.WriteLine(String.Format("{0} beginbfrange", glyphIndexToCharacter.Count));
            foreach (KeyValuePair<int, int> entry in glyphIndexToCharacter)
                wrt.WriteLine(String.Format("<{0:X4}><{0:X4}><{1}>", entry.Key, Utf16BigEndianHex(entry.Value)));
            wrt.WriteLine("endbfrange");

            wrt.Write(suffix);
            wrt.Dispose();

            // Compress like content streams
            byte[] bytes = ms.ToArray();
            ms.Dispose();
            if (Owner.Options.CompressContentStreams)
            {
                Elements.SetName("/Filter", "/FlateDecode");
                bytes = Filtering.FlateDecode.Encode(bytes, _document.Options.FlateEncodeMode);
            }
            //PdfStream stream = CreateStream(bytes);
            else
            {
                Elements.Remove("/Filter");
            }

            if (Stream == null)
                CreateStream(bytes);
            else
            {
                Stream.Value = bytes;
                Elements.SetInteger(PdfStream.Keys.Length, Stream.Length);
            }
        }

        /// <summary>
        /// Formats a Unicode scalar value as the UTF-16 (big-endian) hex string a ToUnicode
        /// <c>bfrange</c> destination expects: a BMP codepoint yields 4 hex digits, an astral codepoint
        /// yields its 8-hex surrogate pair.
        /// </summary>
        private static string Utf16BigEndianHex(int codepoint)
        {
            string utf16 = char.ConvertFromUtf32(codepoint);
            StringBuilder sb = new StringBuilder(utf16.Length * 4);
            foreach (char c in utf16)
                sb.Append(((int)c).ToString("X4"));
            return sb.ToString();
        }

        internal sealed class Keys : PdfStream.Keys
        {
            // No new keys.
        }
    }
}