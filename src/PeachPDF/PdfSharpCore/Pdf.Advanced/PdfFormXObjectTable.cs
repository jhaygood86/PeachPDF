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
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Creates the PdfFormXObject for each form drawn into the current document.
    /// </summary>
    internal sealed class PdfFormXObjectTable : PdfResourceTable
    {
        // Holds no PdfFormXObject objects: they are not cached, because each holds a transformation matrix
        // that makes it unique. This class only creates the PdfFormXObject for a form on demand.

        /// <summary>
        /// Initializes a new instance of this class, which is a singleton for each document.
        /// </summary>
        public PdfFormXObjectTable(PdfDocument document)
            : base(document)
        { }

        /// <summary>
        /// Gets a PdfFormXObject from an XPdfForm. Because the returned objects must be unique, always
        /// a new instance of PdfFormXObject is created if none exists for the specified form. 
        /// </summary>
        public PdfFormXObject GetForm(XForm form)
        {
            // If the form already has a PdfFormXObject, return it.
            if (form._pdfForm != null)
            {
                Debug.Assert(form.IsTemplate, "An XPdfForm must not have a PdfFormXObject.");
                if (ReferenceEquals(form._pdfForm.Owner, Owner))
                    return form._pdfForm;
                //throw new InvalidOperationException("Because of a current limitation of PDFsharp an XPdfForm object can be used only within one single PdfDocument.");

                // Dispose PdfFromXObject when document has changed
                form._pdfForm = null;
            }

            Debug.Assert(form.GetType() == typeof(XForm));
            form._pdfForm = new PdfFormXObject(Owner, form);
            return form._pdfForm;
        }
    }
}
