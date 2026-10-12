// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Advanced;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Annotations;

/// <summary>
/// Represents the annotations array of a page.
/// </summary>
internal sealed class PdfAnnotations : PdfArray
{
    internal PdfAnnotations(PdfDocument document)
        : base(document)
    { }

    internal PdfAnnotations(PdfArray array)
        : base(array)
    { }

    /// <summary>
    /// Adds the specified annotation.
    /// </summary>
    /// <param name="annotation">The annotation.</param>
    public void Add(PdfAnnotation annotation)
    {
        annotation.Document = Owner;
        Owner._irefTable.Add(annotation);
        Elements.Add(annotation.RequiredReference);
    }

    /// <summary>
    /// Removes an annotation from the document.
    /// </summary>
    public void Remove(PdfAnnotation annotation)
    {
        if (annotation.Owner != Owner)
            throw new InvalidOperationException("The annotation does not belong to this document.");

        Owner.Internals.RemoveObject(annotation);
        Elements.Remove(annotation.RequiredReference);
    }

    /// <summary>
    /// Removes all the annotations from the current page.
    /// </summary>
    public void Clear()
    {
        for (int idx = Count - 1; idx >= 0; idx--)
            Page.Annotations.Remove(_page.Annotations[idx]!);
    }

    /// <summary>
    /// Gets the number of annotations in this collection.
    /// </summary>
    public int Count => Elements.Count;

    /// <summary>
    /// Gets the <see cref="PeachPDF.PDF.Annotations.PdfAnnotation"/> at the specified index.
    /// </summary>
    public PdfAnnotation? this[int index]
    {
        get
        {
            PdfReference? iref;
            PdfDictionary? dict;
            PdfItem item = Elements[index];
            if ((iref = item as PdfReference) is not null)
            {
                Debug.Assert(iref.Value is PdfDictionary, "Reference to dictionary expected.");
                dict = (PdfDictionary)iref.Value;
            }
            else
            {
                Debug.Assert(item is PdfDictionary, "Dictionary expected.");
                dict = (PdfDictionary)item;
            }
            return dict as PdfAnnotation;
        }
    }

    /// <summary>
    /// Gets the page the annotations belongs to.
    /// </summary>
    internal PdfPage Page
    {
        get => _page;
        set => _page = value;
    }
    PdfPage _page = null!;

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    public override IEnumerator<PdfItem> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index]!;
    }
}
