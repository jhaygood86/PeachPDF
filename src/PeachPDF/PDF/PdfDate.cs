// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a direct date value.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfDate : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDate"/> class.
    /// </summary>
    public PdfDate(DateTime value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value as DateTime.
    /// </summary>
    public DateTime Value =>
        // This class must behave like a value type. Therefore it cannot be changed (like System.String).
        _value;

    readonly DateTime _value;

    /// <summary>
    /// Returns the value in the PDF date format.
    /// </summary>
    public override string ToString()
    {
        // Invariant culture: a PDF date is not localized, and the current culture's calendar (th-TH is
        // Buddhist, ar-SA Hijri) would otherwise write a different year into it.
        var delta = _value.ToString("zzz", CultureInfo.InvariantCulture).Replace(':', '\'');
        return $"D:{_value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}{delta}'";
    }

    /// <summary>
    /// Writes the value in the PDF date format.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteDocString(ToString());
    }
}
