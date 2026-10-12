// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Provides access to the internal document data structures. This class prevents the public
/// interfaces from pollution with to much internal functions.
/// </summary>
internal sealed class PdfInternals  // TODO: PdfDocumentInternals... PdfPageInternals etc.
{
    internal PdfInternals(PdfDocument document)
    {
        _document = document;
    }
    readonly PdfDocument _document;

    /// <summary>
    /// Gets or sets the first document identifier.
    /// </summary>
    public string FirstDocumentId
    {
        get => _document._trailer.GetDocumentId(0);
        set { _document._trailer.SetDocumentId(0, value); }
    }

    /// <summary>
    /// Gets the first document identifier as GUID.
    /// </summary>
    public Guid FirstDocumentGuid => GuidFromString(_document._trailer.GetDocumentId(0));

    /// <summary>
    /// Gets or sets the second document identifier.
    /// </summary>
    public string SecondDocumentId
    {
        get => _document._trailer.GetDocumentId(1);
        set { _document._trailer.SetDocumentId(1, value); }
    }

    /// <summary>
    /// Gets the first document identifier as GUID.
    /// </summary>
    public Guid SecondDocumentGuid => GuidFromString(_document._trailer.GetDocumentId(0));

    Guid GuidFromString(string id)
    {
        if (id is null || id.Length != 16)
            return Guid.Empty;

        StringBuilder guid = new StringBuilder();
        for (int idx = 0; idx < 16; idx++)
            guid.AppendFormat("{0:X2}", (byte)id[idx]);

        return new Guid(guid.ToString());
    }

    /// <summary>
    /// Gets the catalog dictionary.
    /// </summary>
    public PdfCatalog Catalog => _document.Catalog;

    /// <summary>
    /// Gets the ExtGStateTable object.
    /// </summary>
    public PdfExtGStateTable ExtGStateTable => _document.ExtGStateTable;

    /// <summary>
    /// Returns the object with the specified Identifier, or null, if no such object exists.
    /// </summary>
    public PdfObject GetObject(PdfObjectId objectId)
    {
        return _document._irefTable[objectId].Value;
    }

    /// <summary>
    /// Returns the PdfReference of the specified object, or null, if the object is not in the
    /// document's object table.
    /// </summary>
    public static PdfReference GetReference(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Reference;
    }

    /// <summary>
    /// Gets the object identifier of the specified object.
    /// </summary>
    public static PdfObjectId GetObjectId(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.ObjectId;
    }

    /// <summary>
    /// Gets the object number of the specified object.
    /// </summary>
    public static int GetObjectNumber(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.ObjectNumber;
    }

    /// <summary>
    /// Gets the generation number of the specified object.
    /// </summary>
    public static int GenerationNumber(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GenerationNumber;
    }

    /// <summary>
    /// Gets all indirect objects ordered by their object identifier.
    /// </summary>
    public PdfObject[] GetAllObjects()
    {
        PdfReference[] irefs = _document._irefTable.AllReferences;
        int count = irefs.Length;
        PdfObject[] objects = new PdfObject[count];
        for (int idx = 0; idx < count; idx++)
            objects[idx] = irefs[idx].Value;
        return objects;
    }

    /// <summary>
    /// Adds an object to the PDF document. This operation and only this operation makes the object 
    /// an indirect object owned by this document.
    /// </summary>
    public void AddObject(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Owner is null)
            obj.Document = _document;
        else if (obj.Owner != _document)
            throw new InvalidOperationException("Object does not belong to this document.");
        _document._irefTable.Add(obj);
    }

    /// <summary>
    /// Removes an object from the PDF document.
    /// </summary>
    public void RemoveObject(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Reference is null)
            throw new InvalidOperationException("Only indirect objects can be removed.");
        if (obj.Owner != _document)
            throw new InvalidOperationException("Object does not belong to this document.");

        _document._irefTable.Remove(obj.Reference);
    }

    /// <summary>
    /// Returns an array containing the specified object as first element follows by its transitive
    /// closure. The closure of an object are all objects that can be reached by indirect references. 
    /// The transitive closure is the result of applying the calculation of the closure to a closure
    /// as long as no new objects came along. This is e.g. useful for getting all objects belonging 
    /// to the resources of a page.
    /// </summary>
    public PdfObject[] GetClosure(PdfObject obj)
    {
        return GetClosure(obj, int.MaxValue);
    }

    /// <summary>
    /// Returns an array containing the specified object as first element follows by its transitive
    /// closure limited by the specified number of iterations.
    /// </summary>
    public PdfObject[] GetClosure(PdfObject obj, int depth)
    {
        PdfReference[] references = _document._irefTable.TransitiveClosure(obj, depth);
        int count = references.Length + 1;
        PdfObject[] objects = new PdfObject[count];
        objects[0] = obj;
        for (int idx = 1; idx < count; idx++)
            objects[idx] = references[idx - 1].Value;
        return objects;
    }

    /// <summary>
    /// Writes a PdfItem into the specified stream.
    /// </summary>
    // This function exists to keep PdfWriter and PdfItem.WriteObject internal.
    public void WriteObject(Stream stream, PdfItem item)
    {
        // Never write an encrypted object
        PdfWriter writer = new PdfWriter(stream);
        writer.Options = PdfWriterOptions.OmitStream;
        item.WriteObject(writer);
    }

    /// <summary>
    /// The name of the custom value key.
    /// </summary>
    public string CustomValueKey = "/PeachPDF.PDF.CustomValue";
}
