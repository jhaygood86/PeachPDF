// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Represents an array of PDF content streams of a page.
/// </summary>
internal sealed class PdfContents : PdfArray
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfContents"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfContents(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Appends a new content stream and returns it.
    /// </summary>
    public PdfContent AppendContent()
    {
        Debug.Assert(Owner is not null);

        PdfContent content = new PdfContent(Owner);
        Owner._irefTable.Add(content);
        Debug.Assert(content.Reference is not null);
        Elements.Add(content.Reference);
        return content;
    }

    /// <summary>
    /// Prepends a new content stream and returns it.
    /// </summary>
    public PdfContent PrependContent()
    {
        Debug.Assert(Owner is not null);

        PdfContent content = new PdfContent(Owner);
        Owner._irefTable.Add(content);
        Debug.Assert(content.Reference is not null);
        Elements.Insert(0, content.Reference);
        return content;
    }

    internal override void WriteObject(PdfWriter writer)
    {
        // Save two bytes in PDF stream...
        if (Elements.Count == 1)
            Elements[0].WriteObject(writer);
        else
            base.WriteObject(writer);
    }

    /// <summary>
    /// Gets the enumerator.
    /// </summary>
    public new IEnumerator<PdfContent> GetEnumerator()
    {
        for (int index = 0; index < Elements.Count; index++)
            yield return (PdfContent)((PdfReference)Elements[index]).Value;
    }

}
