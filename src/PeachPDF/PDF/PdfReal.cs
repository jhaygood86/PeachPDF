// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a direct real value.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfReal : PdfNumber
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReal"/> class.
    /// </summary>
    public PdfReal()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReal"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfReal(double value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value as double.
    /// </summary>
    public double Value
    {
        // This class must behave like a value type. Therefore it cannot be changed (like System.String).
        get { return _value; }
    }
    readonly double _value;

    /// <summary>
    /// Returns the real number as string.
    /// </summary>
    public override string ToString()
    {
        return _value.ToString(PdfNumberFormats.ThreeDecimals, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the real value with up to three digits.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.Write(this);
    }
}
