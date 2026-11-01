// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Collections.Generic;

namespace PeachDrawing.PDF.Engine.Advanced;

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

}
