// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.IO;
using System;
using System.Diagnostics;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Base class of all composite PDF objects.
/// </summary>
internal abstract class PdfObject : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObject"/> class.
    /// </summary>
    protected PdfObject()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObject"/> class.
    /// </summary>
    protected PdfObject(PdfDocument document)
    {
        // Calling a virtual member in a constructor is dangerous.
        // In PDFsharp Document is overridden in PdfPage and the code is checked to be save
        // when called for a not completely initialized object.
        Document = document;
    }

    /// <summary>
    /// Initializes a new instance from an existing object. Used for object type transformation.
    /// </summary>
    protected PdfObject(PdfObject obj)
        : this(obj.Owner)
    {
        // If the object that was transformed to an instance of a derived class was an indirect object
        // set the value of the reference to this.
        if (obj._iref is not null)
            obj._iref.Value = this;
    }

/// <summary>
    /// Sets the object and generation number.
    /// Setting the object identifier makes this object an indirect object, i.e. the object gets
    /// a PdfReference entry in the PdfReferenceTable.
    /// </summary>
    internal void SetObjectId(int objectNumber, int generationNumber)
    {
        PdfObjectId objectId = new PdfObjectId(objectNumber, generationNumber);

        // TODO: check imported
        if (_iref is null)
            _iref = _document._irefTable[objectId];
        if (_iref is null)
        {
            // ReSharper disable once ObjectCreationAsStatement because the new object is set to this object
            // in the constructor of PdfReference.
            new PdfReference(this);
            Debug.Assert(_iref is not null);
            _iref.ObjectId = objectId;
        }
        _iref.Value = this;
        _iref.Document = _document;
    }

    /// <summary>
    /// Gets the PdfDocument this object belongs to.
    /// </summary>
    public virtual PdfDocument Owner => _document;

    /// <summary>
    /// Sets the PdfDocument this object belongs to.
    /// </summary>
    internal virtual PdfDocument Document
    {
        set
        {
            if (!ReferenceEquals(_document, value))
            {
                if (_document is not null)
                    throw new InvalidOperationException("Cannot change document.");
                _document = value;
                if (_iref is not null)
                    _iref.Document = value;
            }
        }
    }
    internal PdfDocument _document = null!;

    /// <summary>
    /// Indicates whether the object is an indirect object.
    /// </summary>
    public bool IsIndirect
    {
        // An object is an indirect object if and only if is has an indirect reference value.
        get => _iref is not null;
    }

    /// <summary>
    /// Gets the PdfInternals object of this document, that grants access to some internal structures
    /// which are not part of the public interface of PdfDocument.
    /// </summary>
    public PdfObjectInternals Internals => _internals ?? (_internals = new PdfObjectInternals(this));
    PdfObjectInternals _internals = null!;

    /// <summary>
    /// When overridden in a derived class, prepares the object to get saved.
    /// </summary>
    internal virtual void PrepareForSave()
    { }

    /// <summary>
    /// Saves the stream position. 2nd Edition.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        Debug.Assert(false, "Must not come here!");
    }

    /// <summary>
    /// Gets the object identifier. Returns PdfObjectId.Empty for direct objects,
    /// i.e. never returns null.
    /// </summary>
    internal PdfObjectId ObjectId => _iref is not null ? _iref.ObjectId : PdfObjectId.Empty;

    /// <summary>
    /// Gets the object number.
    /// </summary>
    internal int ObjectNumber => ObjectId.ObjectNumber;

    /// <summary>
    /// Gets the generation number.
    /// </summary>
    internal int GenerationNumber => ObjectId.GenerationNumber;

    /// <summary>
    /// Gets the indirect reference of this object. If the value is null, this object is a direct object.
    /// </summary>
    public PdfReference? Reference
    {
        get => _iref;

        // Setting the reference outside PDFsharp is not considered as a valid operation.
        internal set { _iref = value; }
    }
    PdfReference? _iref;

    /// <summary>
    /// Gets the indirect reference of this object, which must be an indirect object.
    /// </summary>
    internal PdfReference RequiredReference =>
        _iref ?? throw new InvalidOperationException("The object is a direct object and has no indirect reference.");
}
