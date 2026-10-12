// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using PeachPDF.PDF.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

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
    public Dictionary<PdfObjectId, PdfReference> ObjectTable = new Dictionary<PdfObjectId, PdfReference>();

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

        ObjectTable.Add(value.ObjectId, value.Reference);
    }

    public void Remove(PdfReference iref)
    {
        ObjectTable.Remove(iref.ObjectId);
    }

    /// <summary>
    /// Gets a cross reference entry from an object identifier.
    /// Returns null if no object with the specified ID exists in the object table.
    /// </summary>
    public PdfReference this[PdfObjectId objectId]
    {
        get
        {
            PdfReference iref;
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

    //public PdfObject GetObject(PdfObjectId objectId)
    //{
    //  return this[objectId].Value;
    //}

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
    internal int _maxObjectNumber;

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
    /// Gets an array of all object identifiers. For debugging purposes only.
    /// </summary>
    internal PdfObjectId[] AllObjectIds
    {
        get
        {
            ICollection collection = ObjectTable.Keys;
            PdfObjectId[] objectIds = new PdfObjectId[collection.Count];
            collection.CopyTo(objectIds, 0);
            return objectIds;
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

    internal void HandleOrphanedReferences()
    { }

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

    /// <summary>
    /// Checks the logical consistence for debugging purposes (useful after reconstruction work).
    /// </summary>
    [Conditional("DEBUG_")]
    public void CheckConsistence()
    {
        Dictionary<PdfReference, object> ht1 = new Dictionary<PdfReference, object>();
        foreach (PdfReference iref in ObjectTable.Values)
        {
            Debug.Assert(!ht1.ContainsKey(iref), "Duplicate iref.");
            Debug.Assert(iref.Value is not null);
            ht1.Add(iref, null);
        }

        Dictionary<PdfObjectId, object> ht2 = new Dictionary<PdfObjectId, object>();
        foreach (PdfReference iref in ObjectTable.Values)
        {
            Debug.Assert(!ht2.ContainsKey(iref.ObjectId), "Duplicate iref.");
            ht2.Add(iref.ObjectId, null);
        }

        ICollection collection = ObjectTable.Values;
        int count = collection.Count;
        PdfReference[] irefs = new PdfReference[count];
        collection.CopyTo(irefs, 0);
        for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
                if (i != j)
                {
                    Debug.Assert(ReferenceEquals(irefs[i].Document, _document));
                    Debug.Assert(irefs[i] != irefs[j]);
                    Debug.Assert(!ReferenceEquals(irefs[i], irefs[j]));
                    Debug.Assert(!ReferenceEquals(irefs[i].Value, irefs[j].Value));
                    Debug.Assert(!Equals(irefs[i].ObjectId, irefs[j].Value.ObjectId));
                    Debug.Assert(irefs[i].ObjectNumber != irefs[j].Value.ObjectNumber);
                    Debug.Assert(ReferenceEquals(irefs[i].Document, irefs[j].Document));
                    GetType();
                }
    }

    ///// <summary>
    ///// The garbage collector for PDF objects.
    ///// </summary>
    //public sealed class GC
    //{
    //  PdfXRefTable xrefTable;
    //
    //  internal GC(PdfXRefTable xrefTable)
    //  {
    //    _xrefTable = xrefTable;
    //  }
    //
    //  public void Collect()
    //  { }
    //
    //  public PdfReference[] ReachableObjects()
    //  {
    //    Hash_table objects = new Hash_table();
    //    TransitiveClosure(objects, _xrefTable.document.trailer);
    //  }

    /// <summary>
    /// Calculates the transitive closure of the specified PdfObject, i.e. all indirect objects
    /// recursively reachable from the specified object.
    /// </summary>
    public PdfReference[] TransitiveClosure(PdfObject pdfObject)
    {
        return TransitiveClosure(pdfObject, short.MaxValue);
    }

    /// <summary>
    /// Calculates the transitive closure of the specified PdfObject with the specified depth, i.e. all indirect objects
    /// recursively reachable from the specified object in up to maximally depth steps.
    /// </summary>
    public PdfReference[] TransitiveClosure(PdfObject pdfObject, int depth)
    {
        CheckConsistence();
        Dictionary<PdfItem, object> objects = new Dictionary<PdfItem, object>();
        _overflow = new Dictionary<PdfItem, object>();
        TransitiveClosureImplementation(objects, pdfObject);
        TryAgain:
        if (_overflow.Count > 0)
        {
            PdfObject[] array = new PdfObject[_overflow.Count];
            _overflow.Keys.CopyTo(array, 0);
            _overflow = new Dictionary<PdfItem, object>();
            for (int idx = 0; idx < array.Length; idx++)
            {
                PdfObject obj = array[idx];
                TransitiveClosureImplementation(objects, obj);
            }
            goto TryAgain;
        }

        CheckConsistence();

        ICollection collection = objects.Keys;
        int count = collection.Count;
        PdfReference[] irefs = new PdfReference[count];
        collection.CopyTo(irefs, 0);

        return irefs;
    }

    Dictionary<PdfItem, object> _overflow = new Dictionary<PdfItem, object>();

    void TransitiveClosureImplementation(Dictionary<PdfItem, object> objects, PdfObject startObject)
    {
        var stack = new Stack<PdfObject>();
        stack.Push(startObject);

        while (stack.Count > 0)
        {
            PdfObject pdfObject = stack.Pop();

            IEnumerable enumerable;
            PdfDictionary dict;
            PdfArray array;
            if ((dict = pdfObject as PdfDictionary) is not null)
                enumerable = dict.Elements.Values;
            else if ((array = pdfObject as PdfArray) is not null)
                enumerable = array.Elements;
            else
            {
                Debug.Assert(false, "Should not come here.");
                continue;
            }

            foreach (PdfItem item in enumerable)
            {
                PdfReference iref = item as PdfReference;
                if (iref is not null)
                {
                    if (!ReferenceEquals(iref.Document, _document))
                        Debug.WriteLine(string.Format("Bad iref: {0}", iref.ObjectId.ToString()));
                    Debug.Assert(ReferenceEquals(iref.Document, _document) || iref.Document is null, "External object detected!");

                    if (!objects.ContainsKey(iref))
                    {
                        PdfObject value = iref.Value;
                        if (iref.Document is not null)
                        {
                            if (value is null)
                            {
                                iref = ObjectTable[iref.ObjectId];
                                Debug.Assert(iref.Value is not null);
                                value = iref.Value;
                            }
                            Debug.Assert(ReferenceEquals(iref.Document, _document));
                            objects.Add(iref, null);
                            if (value is PdfArray || value is PdfDictionary)
                                stack.Push(value);
                        }
                    }
                }
                else
                {
                    PdfObject nestedObject = item as PdfObject;
                    if (nestedObject is not null && (nestedObject is PdfDictionary || nestedObject is PdfArray))
                        stack.Push(nestedObject);
                }
            }
        }
    }

    /// <summary>
    /// Gets the cross reference to an objects used for undefined indirect references.
    /// </summary>
    public PdfReference DeadObject
    {
        get
        {
            if (_deadObject is null)
            {
                _deadObject = new PdfDictionary(_document);
                Add(_deadObject);
                _deadObject.Elements.Add("/DeadObjectCount", new PdfInteger());
            }
            return _deadObject.Reference;
        }
    }
    PdfDictionary _deadObject = null!;
}

///// <summary>
///// Represents the cross-reference table of a PDF document. 
///// It contains all indirect objects of a document.
///// </summary>
//internal sealed class PdfCrossReferenceStreamTable  // Must not be derive from PdfObject.
//{
//    public PdfCrossReferenceStreamTable(PdfDocument document)
//    {
//        _document = document;
//    }
//    readonly PdfDocument _document;

//    public class Item
//    {
//        public PdfReference Reference;

//        public readonly List<CrossReferenceStreamEntry> Entries = new List<CrossReferenceStreamEntry>();
//    }
//}

//struct CrossReferenceStreamEntry
//{
//    public int Type;

//    public int Field2;

//    public int Field3;
//}
