// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

// With this define each iref object gets a unique number (uid) to make them distinguishable in the debugger

using PeachPDF.PDF.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents an indirect reference to a PdfObject.
/// </summary>
[DebuggerDisplay("iref({ObjectNumber}, {GenerationNumber})")]
internal sealed class PdfReference : PdfItem
{
    // About PdfReference 
    // 
    // * A PdfReference holds either the ObjectId or the PdfObject or both.
    // 
    // * Each PdfObject has a PdfReference if and only if it is an indirect object. Direct objects have
    //   no PdfReference, because they are embedded in a parent objects.
    //
    // * PdfReference objects are used to reference PdfObject instances. A value in a PDF dictionary
    //   or array that is a PdfReference represents an indirect reference. A value in a PDF dictionary or
    //   or array that is a PdfObject represents a direct (or embeddded) object.
    //
    // * When a PDF file is imported, the PdfXRefTable is filled with PdfReference objects keeping the
    //   ObjectsIds and file positions (offsets) of all indirect objects.
    //
    // * Indirect objects can easily be renumbered because they do not rely on their ObjectsIds.
    //
    // * During modification of a document the ObjectId of an indirect object has no meaning,
    //   except that they must be different in pairs.

    /// <summary>
    /// Initializes a new PdfReference instance for the specified indirect object.
    /// </summary>
    public PdfReference(PdfObject pdfObject)
    {
        if (pdfObject.Reference != null)
            throw new InvalidOperationException("Must not create iref for an object that already has one.");
        _value = pdfObject;
        pdfObject.Reference = this;
    }

    /// <summary>
    /// Initializes a new PdfReference instance from the specified object identifier and file position.
    /// </summary>
    public PdfReference(PdfObjectId objectId, long position)
    {
        _objectId = objectId;
        _position = position;
    }

    /// <summary>
    /// Writes the object in PDF iref table format.
    /// </summary>
    internal void WriteXRefEnty(PdfWriter writer)
    {
        // PDFsharp does not yet support PDF 1.5 object streams.

        // Each line must be exactly 20 bytes long, otherwise Acrobat repairs the file.
        string text = String.Format("{0:0000000000} {1:00000} n\n",
          _position, _objectId.GenerationNumber); // InUse ? 'n' : 'f');
        writer.WriteRaw(text);
    }

    /// <summary>
    /// Writes an indirect reference.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.Write(this);
    }

    /// <summary>
    /// Gets or sets the object identifier.
    /// </summary>
    public PdfObjectId ObjectId
    {
        get { return _objectId; }
        set
        {
            // Ignore redundant invokations.
            if (_objectId == value)
                return;

            _objectId = value;
            if (Document != null)
            {
                //PdfXRefTable table = Document.xrefTable;
                //table.Remove(this);
                //objectId = value;
                //table.Add(this);
            }
        }
    }
    PdfObjectId _objectId;

    /// <summary>
    /// Gets the object number of the object identifier.
    /// </summary>
    public int ObjectNumber
    {
        get { return _objectId.ObjectNumber; }
    }

    /// <summary>
    /// Gets the generation number of the object identifier.
    /// </summary>
    public int GenerationNumber
    {
        get { return _objectId.GenerationNumber; }
    }

    /// <summary>
    /// Gets or sets the file position of the related PdfObject.
    /// </summary>
    public long Position
    {
        get { return _position; }
        set { _position = value; }
    }
    long _position;

    //public bool InUse
    //{
    //  get {return inUse;}
    //  set {inUse = value;}
    //}
    //bool inUse;

    /// <summary>
    /// Gets or sets the referenced PdfObject.
    /// </summary>
    public PdfObject Value
    {
        get { return _value; }
        set
        {
            Debug.Assert(value != null, "The value of a PdfReference must never be null.");
            Debug.Assert(value.Reference == null || ReferenceEquals(value.Reference, this), "The reference of the value must be null or this.");
            _value = value;
            // value must never be null
            value.Reference = this;
        }
    }
    PdfObject _value = null!;

    /// <summary>
    /// Hack for dead objects.
    /// </summary>
    internal void SetObject(PdfObject value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets or sets the document this object belongs to.
    /// </summary>
    public PdfDocument Document
    {
        get { return _document; }
        set { _document = value; }
    }
    PdfDocument _document = null!;

    /// <summary>
    /// Gets a string representing the object identifier.
    /// </summary>
    public override string ToString()
    {
        return _objectId + " R";
    }

    internal static PdfReferenceComparer Comparer
    {
        get { return new PdfReferenceComparer(); }
    }

    /// <summary>
    /// Implements a comparer that compares PdfReference objects by their PdfObjectId.
    /// </summary>
    internal class PdfReferenceComparer : IComparer<PdfReference>
    {
        public int Compare(PdfReference? x, PdfReference? y)
        {
            PdfReference l = x;
            PdfReference r = y;
            if (l != null)
            {
                if (r != null)
                    return l._objectId.CompareTo(r._objectId);
                return -1;
            }
            if (r != null)
                return 1;
            return 0;
        }
    }

}
