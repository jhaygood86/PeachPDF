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

using System;
using System.IO;
using System.IO.Compression;

namespace PeachPDF.PdfSharpCore.Pdf.Filters
{
    /// <summary>
    /// Implements the FlateDecode filter using System.IO.Compression.ZLibStream.
    /// </summary>
    public class FlateDecode : Filter
    {
        // Reference: 3.3.3  LZWDecode and FlateDecode Filters / Page 71

        /// <summary>
        /// Encodes the specified data.
        /// </summary>
        public override byte[] Encode(byte[] data)
        {
            return Encode(data, PdfFlateEncodeMode.Default);
        }

        /// <summary>
        /// Encodes the specified data.
        /// </summary>
        public byte[] Encode(byte[] data, PdfFlateEncodeMode mode)
        {
            CompressionLevel compressionLevel = mode switch
            {
                PdfFlateEncodeMode.BestCompression => CompressionLevel.SmallestSize,
                PdfFlateEncodeMode.BestSpeed => CompressionLevel.Fastest,
                _ => CompressionLevel.Optimal
            };

            var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, compressionLevel, leaveOpen: true))
                zlib.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        /// <summary>
        /// Decodes the specified data.
        /// </summary>
        public override byte[] Decode(byte[] data, FilterParms parms)
        {
            if (data.Length == 0) return data;

            var msInput = new MemoryStream(data);
            var msOutput = new MemoryStream();

            using (var zlib = new ZLibStream(msInput, CompressionMode.Decompress))
                zlib.CopyTo(msOutput);

            if (parms.DecodeParms != null)
                return StreamDecoder.Decode(msOutput.ToArray(), parms.DecodeParms);
            return msOutput.ToArray();
        }
    }
}
