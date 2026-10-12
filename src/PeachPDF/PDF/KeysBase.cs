// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Diagnostics.CodeAnalysis;

namespace PeachPDF.PDF;

/// <summary>
/// Base class for all dictionary Keys classes.
/// </summary>
internal class KeysBase
{
    internal static PdfDictionaryMetadata CreateMeta([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type)
    {
        return new PdfDictionaryMetadata(type);
    }
}
