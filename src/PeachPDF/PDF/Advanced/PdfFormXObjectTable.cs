// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Drawing;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

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
    /// Gets a PdfFormXObject from an PdfForm. Because the returned objects must be unique, always
    /// a new instance of PdfFormXObject is created if none exists for the specified form. 
    /// </summary>
    public PdfFormXObject GetForm(PdfForm form)
    {
        // If the form already has a PdfFormXObject, return it.
        if (form._pdfForm is not null)
        {
            Debug.Assert(form.IsTemplate, "An PdfForm must not have a PdfFormXObject.");
            if (ReferenceEquals(form._pdfForm.Owner, Owner))
                return form._pdfForm;
            //throw new InvalidOperationException("Because of a current limitation of PDFsharp an PdfForm object can be used only within one single PdfDocument.");

            // Dispose PdfFromXObject when document has changed
            form._pdfForm = null!;
        }

        Debug.Assert(form.GetType() == typeof(PdfForm));
        form._pdfForm = new PdfFormXObject(Owner, form);
        return form._pdfForm;
    }
}
