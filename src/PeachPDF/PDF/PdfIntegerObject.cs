// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents an indirect integer value. This type is not used by PeachPDF.PDF. If it is imported from
/// an external PDF file, the value is converted into a direct object.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfIntegerObject : PdfNumberObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIntegerObject"/> class.
    /// </summary>
    public PdfIntegerObject()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIntegerObject"/> class.
    /// </summary>
    public PdfIntegerObject(int value)
    {
        _value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIntegerObject"/> class.
    /// </summary>
    public PdfIntegerObject(PdfDocument document, int value)
        : base(document)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value as integer.
    /// </summary>
    public int Value
    {
        get => _value;
        //set {_value = value;}
    }
    readonly int _value;

    /// <summary>
    /// Returns the integer as string.
    /// </summary>
    public override string ToString()
    {
        return _value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the integer literal.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteBeginObject(this);
        writer.Write(_value);
        writer.WriteEndObject();
    }
}
