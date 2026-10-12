// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System.Diagnostics;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a direct boolean value.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfBoolean : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfBoolean"/> class.
    /// </summary>
    public PdfBoolean()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfBoolean"/> class.
    /// </summary>
    public PdfBoolean(bool value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value of this instance as boolean value.
    /// </summary>
    public bool Value
    {
        // This class must behave like a value type. Therefore it cannot be changed (like System.String).
        get { return _value; }
    }
    readonly bool _value;

    /// <summary>
    /// A pre-defined value that represents <c>true</c>.
    /// </summary>
    public static readonly PdfBoolean True = new PdfBoolean(true);

    /// <summary>
    /// A pre-defined value that represents <c>false</c>.
    /// </summary>
    public static readonly PdfBoolean False = new PdfBoolean(false);

    /// <summary>
    /// Returns 'false' or 'true'.
    /// </summary>
    public override string ToString()
    {
        return _value ? bool.TrueString : bool.FalseString;
    }

    /// <summary>
    /// Writes 'true' or 'false'.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.Write(this);
    }
}
