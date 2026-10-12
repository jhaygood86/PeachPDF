// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

/// <summary>
/// Value creation flags. Specifies whether and how a value that does not exist is created.
/// </summary>
// ReSharper disable InconsistentNaming
internal enum ValueCreationFlags
// ReSharper restore InconsistentNaming
{
    /// <summary>
    /// Don't create the value.
    /// </summary>
    None,

    /// <summary>
    /// Create the value as direct object.
    /// </summary>
    Create,

    /// <summary>
    /// Create the value as indirect object.
    /// </summary>
    CreateIndirect,
}
