// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

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
        Elements.Add(annotation.Reference);
    }

    /// <summary>
    /// Removes an annotation from the document.
    /// </summary>
    public void Remove(PdfAnnotation annotation)
    {
        if (annotation.Owner != Owner)
            throw new InvalidOperationException("The annotation does not belong to this document.");

        Owner.Internals.RemoveObject(annotation);
        Elements.Remove(annotation.Reference);
    }

    /// <summary>
    /// Removes all the annotations from the current page.
    /// </summary>
    public void Clear()
    {
        for (int idx = Count - 1; idx >= 0; idx--)
            Page.Annotations.Remove(_page.Annotations[idx]);
    }

    /// <summary>
    /// Gets the number of annotations in this collection.
    /// </summary>
    public int Count => Elements.Count;

    /// <summary>
    /// Gets the <see cref="PeachPDF.PDF.Annotations.PdfAnnotation"/> at the specified index.
    /// </summary>
    public PdfAnnotation this[int index]
    {
        get
        {
            PdfReference iref;
            PdfDictionary dict;
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
        return (IEnumerator<PdfItem>)new AnnotationsIterator(this);
    }
    // THHO4STLA: AnnotationsIterator: Implementation does not work http://forum.pdfsharp.net/viewtopic.php?p=3285#p3285
    // Code using the enumerator like this will crash:
    //foreach (var annotation in page.Annotations)
    //{
    //    annotation.GetType();
    //}

    //!!!new 2015-10-15: use PdfItem instead of PdfAnnotation. 
    // TODO Should we change this to "public new IEnumerator<PdfAnnotation> GetEnumerator()"?

    class AnnotationsIterator : IEnumerator<PdfItem/*PdfAnnotation*/>
    {
        public AnnotationsIterator(PdfAnnotations annotations)
        {
            _annotations = annotations;
            _index = -1;
        }

        public PdfItem/*PdfAnnotation*/ Current
        {
            get => _annotations[_index];
        }

        object IEnumerator.Current
        {
            get => Current;
        }

        public bool MoveNext()
        {
            return ++_index < _annotations.Count;
        }

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
            //throw new NotImplementedException();
        }

        readonly PdfAnnotations _annotations;
        int _index;
    }
}
