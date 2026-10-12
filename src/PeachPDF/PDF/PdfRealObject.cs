// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents an indirect real value. This type is not used by PeachPDF.PDF. If it is imported from
/// an external PDF file, the value is converted into a direct object.
/// </summary>
internal sealed class PdfRealObject : PdfNumberObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRealObject"/> class.
    /// </summary>
    public PdfRealObject()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRealObject"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfRealObject(double value)
    {
        _value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRealObject"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The value.</param>
    public PdfRealObject(PdfDocument document, double value)
        : base(document)
    {
        _value = value;
    }

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public double Value
    {
        get => _value;
        set => _value = value;
    }
    double _value;

    /// <summary>
    /// Returns the real as a culture invariant string.
    /// </summary>
    public override string ToString()
    {
        return _value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the real literal.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteBeginObject(this);
        writer.Write(_value);
        writer.WriteEndObject();
    }
}
