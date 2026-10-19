// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Represents the cross-reference table of a PDF document. 
/// It contains all indirect objects of a document.
/// </summary>
internal sealed class PdfCrossReferenceTable  // Must not be derive from PdfObject.
{
    public PdfCrossReferenceTable(PdfDocument document)
    {
        _document = document;
    }
    readonly PdfDocument _document;

    /// <summary>
    /// Represents the relation between PdfObjectId and PdfReference for a PdfDocument.
    /// </summary>
    private readonly Dictionary<PdfObjectId, PdfReference> ObjectTable = [];

    /// <summary>
    /// Gets the number of entries in the table.
    /// </summary>
    public int Count => ObjectTable.Count;

    internal bool IsUnderConstruction
    {
        get => _isUnderConstruction;
        set => _isUnderConstruction = value;
    }
    bool _isUnderConstruction;

    /// <summary>
    /// Adds a cross reference entry to the table. Used when parsing the trailer.
    /// </summary>
    public void Add(PdfReference iref)
    {
        if (iref.ObjectId.IsEmpty)
            iref.ObjectId = new PdfObjectId(GetNewObjectNumber());

        if (ObjectTable.ContainsKey(iref.ObjectId))
            return;
        //     throw new InvalidOperationException("Object already in table.");

        ObjectTable.Add(iref.ObjectId, iref);
    }

    /// <summary>
    /// Adds a PdfObject to the table.
    /// </summary>
    public void Add(PdfObject value)
    {
        if (value.Owner is null)
            value.Document = _document;
        else
            Debug.Assert(value.Owner == _document);

        if (value.ObjectId.IsEmpty)
            value.SetObjectId(GetNewObjectNumber(), 0);

        if (ObjectTable.ContainsKey(value.ObjectId))
            return;
        // throw new InvalidOperationException("Object already in table.");

        ObjectTable.Add(value.ObjectId, value.RequiredReference);
    }

    public void Remove(PdfReference iref)
    {
        ObjectTable.Remove(iref.ObjectId);
    }

    /// <summary>
    /// Gets a cross reference entry from an object identifier.
    /// Returns null if no object with the specified ID exists in the object table.
    /// </summary>
    public PdfReference? this[PdfObjectId objectId]
    {
        get
        {
            PdfReference? iref;
            ObjectTable.TryGetValue(objectId, out iref);
            return iref;
        }
    }

    /// <summary>
    /// Indicates whether the specified object identifier is in the table.
    /// </summary>
    public bool Contains(PdfObjectId objectId)
    {
        return ObjectTable.ContainsKey(objectId);
    }

    //    /// <summary>
    //    /// Gets the entry for the specified object, or null, if the object is not in
    //    /// this XRef table.
    //    /// </summary>
    //    internal PdfReference GetEntry(PdfObjectId objectId)
    //    {
    //      return this[objectId];
    //    }

    /// <summary>
    /// Returns the next free object number.
    /// </summary>
    public int GetNewObjectNumber()
    {
        // New objects are numbered consecutively. If a document is imported, maxObjectNumber is
        // set to the highest object number used in the document.
        return ++_maxObjectNumber;
    }
    private int _maxObjectNumber;

    /// <summary>
    /// Writes the xref section in pdf stream.
    /// </summary>
    internal void WriteObject(PdfWriter writer)
    {
        writer.WriteRaw("xref\n");

        PdfReference[] irefs = AllReferences;

        int count = irefs.Length;
        writer.WriteRaw(string.Format("0 {0}\n", count + 1));
        writer.WriteRaw(string.Format("{0:0000000000} {1:00000} {2} \n", 0, 65535, "f"));
        //PdfEncoders.WriteAnsi(stream, text);

        for (int idx = 0; idx < count; idx++)
        {
            PdfReference iref = irefs[idx];

            // Acrobat is very pedantic; it must be exactly 20 bytes per line.
            writer.WriteRaw(string.Format("{0:0000000000} {1:00000} {2} \n", iref.Position, iref.GenerationNumber, "n"));
        }
    }

    /// <summary>
    /// Gets an array of all cross references ordered ascendingly by their object identifier.
    /// </summary>
    internal PdfReference[] AllReferences
    {
        get
        {
            Dictionary<PdfObjectId, PdfReference>.ValueCollection collection = ObjectTable.Values;
            List<PdfReference> list = new List<PdfReference>(collection);
            list.Sort(PdfReference.Comparer);
            PdfReference[] irefs = new PdfReference[collection.Count];
            list.CopyTo(irefs, 0);
            return irefs;
        }
    }

    /// <summary>
    /// Removes all objects that cannot be reached from the trailer.
    /// Returns the number of removed objects.
    /// </summary>
    internal int Compact()
    {
        // TODO: remove PdfBooleanObject, PdfIntegerObject etc.
        int removed = ObjectTable.Count;
        //CheckConsistence();
        // TODO: Is this really so easy?
        PdfReference[] irefs = TransitiveClosure(_document._trailer);

        _maxObjectNumber = 0;
        ObjectTable.Clear();
        foreach (PdfReference iref in irefs)
        {
            ObjectTable.Add(iref.ObjectId, iref);
            _maxObjectNumber = Math.Max(_maxObjectNumber, iref.ObjectNumber);
        }
        //CheckConsistence();
        removed -= ObjectTable.Count;
        return removed;
    }

    /// <summary>
    /// Renumbers the objects starting at 1.
    /// </summary>
    internal void Renumber()
    {
        //CheckConsistence();
        PdfReference[] irefs = AllReferences;
        ObjectTable.Clear();
        // Give all objects a new number.
        int count = irefs.Length;
        for (int idx = 0; idx < count; idx++)
        {
            PdfReference iref = irefs[idx];
            iref.ObjectId = new PdfObjectId(idx + 1);
            // Rehash with new number.
            ObjectTable.Add(iref.ObjectId, iref);
        }
        _maxObjectNumber = count;
        //CheckConsistence();
    }

    ///// <summary>
    ///// The garbage collector for PDF objects.
    ///// </summary>

    /// <summary>
    /// Calculates the transitive closure of the specified PdfObject, i.e. all indirect objects
    /// recursively reachable from the specified object, in the order they are discovered.
    /// </summary>
    public PdfReference[] TransitiveClosure(PdfObject pdfObject) => TransitiveClosure(pdfObject, short.MaxValue);

    /// <summary>
    /// Calculates the transitive closure of the specified PdfObject. The depth is kept for compatibility: the walk
    /// has never been limited by it.
    /// </summary>
    public PdfReference[] TransitiveClosure(PdfObject pdfObject, int depth)
    {
        var seen = new HashSet<PdfReference>();
        var found = new List<PdfReference>();
        var stack = new Stack<PdfObject>();
        stack.Push(pdfObject);

        while (stack.Count > 0)
        {
            IEnumerable<PdfItem> items;
            switch (stack.Pop())
            {
                case PdfDictionary dictionary:
                    items = dictionary.Elements.Values;
                    break;
                case PdfArray array:
                    items = array.Elements;
                    break;
                default:
                    Debug.Assert(false, "Should not come here.");
                    continue;
            }

            foreach (PdfItem item in items)
            {
                if (item is PdfReference iref)
                {
                    Debug.Assert(ReferenceEquals(iref.Document, _document) || iref.Document is null, "External object detected!");
                    if (iref.Document is null || seen.Contains(iref))
                        continue;

                    PdfObject? value = iref.Value;
                    if (value is null)
                    {
                        iref = ObjectTable[iref.ObjectId];
                        Debug.Assert(iref.Value is not null);
                        value = iref.Value;
                    }

                    seen.Add(iref);
                    found.Add(iref);
                    if (value is PdfArray or PdfDictionary)
                        stack.Push(value);
                }
                else if (item is PdfDictionary or PdfArray)
                {
                    stack.Push((PdfObject)item);
                }
            }
        }

        return found.ToArray();
    }
}
