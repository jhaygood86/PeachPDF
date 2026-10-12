// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Internal;
using PeachPDF.PDF.IO;
using System.Diagnostics;

namespace PeachPDF.PDF;

/// <summary>
/// Represents an indirect text string value. This type is not used by PeachPDF.PDF. If it is imported from
/// an external PDF file, the value is converted into a direct object.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfStringObject : PdfObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStringObject"/> class.
    /// </summary>
    public PdfStringObject()
    {
        _flags = PdfStringFlags.LatinOneByteEncoding;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStringObject"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The value.</param>
    public PdfStringObject(PdfDocument document, string value)
        : base(document)
    {
        _value = value;
        _flags = PdfStringFlags.LatinOneByteEncoding;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStringObject"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="encoding">The encoding.</param>
    public PdfStringObject(string value, PdfStringEncoding encoding)
    {
        _value = value;
        //if ((flags & PdfStringFlags.EncodingMask) == 0)
        //  flags |= PdfStringFlags.PDFDocEncoding;
        _flags = (PdfStringFlags)encoding;
    }

    internal PdfStringObject(string value, PdfStringFlags flags)
    {
        _value = value;
        //if ((flags & PdfStringFlags.EncodingMask) == 0)
        //  flags |= PdfStringFlags.PDFDocEncoding;
        _flags = flags;
    }

    /// <summary>
    /// Gets the number of characters in this string.
    /// </summary>
    public int Length => _value is null ? 0 : _value.Length;

    /// <summary>
    /// Gets or sets the encoding.
    /// </summary>
    public PdfStringEncoding Encoding
    {
        get => (PdfStringEncoding)(_flags & PdfStringFlags.EncodingMask);
        set { _flags = (_flags & ~PdfStringFlags.EncodingMask) | ((PdfStringFlags)value & PdfStringFlags.EncodingMask); }
    }

    /// <summary>
    /// Gets a value indicating whether the string is a hexadecimal literal.
    /// </summary>
    public bool HexLiteral
    {
        get => (_flags & PdfStringFlags.HexLiteral) != 0;
        set { _flags = value ? _flags | PdfStringFlags.HexLiteral : _flags & ~PdfStringFlags.HexLiteral; }
    }
    PdfStringFlags _flags;

    /// <summary>
    /// Gets or sets the value as string
    /// </summary>
    public string Value
    {
        get => _value ?? "";
        set { _value = value ?? ""; }
    }
    string _value = null!;

    /// <summary>
    /// Gets or sets the string value for encryption purposes.
    /// </summary>
    internal byte[] EncryptionValue
    {
        // TODO: Unicode case is not handled!
        get => _value is null ? new byte[0] : PdfEncoders.LatinOneByteEncoding.GetBytes(_value);
        set { _value = PdfEncoders.LatinOneByteEncoding.GetString(value, 0, value.Length); }
    }

    /// <summary>
    /// Returns the string.
    /// </summary>
    public override string ToString()
    {
        return _value;
    }

    /// <summary>
    /// Writes the string literal with encoding DOCEncoded.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteBeginObject(this);
        writer.Write(new PdfString(_value, _flags));
        writer.WriteEndObject();
    }
}
