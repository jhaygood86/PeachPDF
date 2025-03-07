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

using System;
using System.ComponentModel;
using System.Threading;
using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.PdfSharpCore.Internal
{
    /// <summary>
    /// Static locking functions to make PDFsharp thread save.
    /// </summary>
    internal static class Lock
    {
        public static void EnterGdiPlus()
        {
            //if (_fontFactoryLockCount > 0)
            //    throw new InvalidOperationException("");

            Monitor.Enter(GdiPlus);
            _gdiPlusLockCount++;
        }

        public static void ExitGdiPlus()
        {
            _gdiPlusLockCount--;
            Monitor.Exit(GdiPlus);
        }

        static readonly object GdiPlus = new object();
        static int _gdiPlusLockCount;

        public static void EnterFontFactory()
        {
            Monitor.Enter(FontFactory);
            _fontFactoryLockCount++;
        }

        public static void ExitFontFactory()
        {
            _fontFactoryLockCount--;
            Monitor.Exit(FontFactory);
        }
        static readonly object FontFactory = new object();
        [ThreadStatic]
        static int _fontFactoryLockCount;
    }
}
