// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using System.Collections.Generic;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Base class for all dictionaries that map resource names to objects.
/// </summary>
internal sealed class PdfResourceMap : PdfDictionary //, IEnumerable
{
    public PdfResourceMap()
    { }

    public PdfResourceMap(PdfDocument document)
        : base(document)
    { }

    internal PdfResourceMap(PdfDictionary dict)
        : base(dict)
    { }

    //    public int Count
    //    {
    //      get {return resources.Count;}
    //    }
    //
    //    public PdfObject this[string key]
    //    {
    //      get {return resources[key] as PdfObject;}
    //      set {resources[key] = value;}
    //    }

    /// <summary>
    /// Adds all imported resource names to the specified hashtable.
    /// </summary>
    internal void CollectResourceNames(Dictionary<string, object> usedResourceNames)
    {
        // ?TODO: Imported resources (e.g. fonts) can be reused, but I think this is rather difficult. Will be an issue in PDFsharp 2.0.
        PdfName[] names = Elements.KeyNames;
        foreach (PdfName name in names)
            usedResourceNames.Add(name.ToString(), null);
    }
}
