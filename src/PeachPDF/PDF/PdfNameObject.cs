// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;

namespace PeachPDF.PDF;

/// <summary>
/// Represents an indirect name value. This type is not used by PeachPDF.PDF. If it is imported from
/// an external PDF file, the value is converted into a direct object. Acrobat sometime uses indirect
/// names to save space, because an indirect reference to a name may be shorter than a long name.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfNameObject : PdfObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNameObject"/> class.
    /// </summary>
    public PdfNameObject()
    {
        _value = "/";  // Empty name.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNameObject"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The value.</param>
    public PdfNameObject(PdfDocument document, string value)
        : base(document)
    {
        if (value == null)
            throw new ArgumentNullException("value");
        if (value.Length == 0 || value[0] != '/')
            throw new ArgumentException("A PDF name must start with a slash (/).");

        _value = value;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return _value.Equals(obj);
    }

    /// <summary>
    /// Serves as a hash function for this type.
    /// </summary>
    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    /// <summary>
    /// Gets or sets the name value.
    /// </summary>
    public string Value
    {
        get { return _value; }
        set { _value = value; }
    }
    string _value;

    /// <summary>
    /// Returns the name. The string always begins with a slash.
    /// </summary>
    public override string ToString()
    {
        // TODO: Encode characters.
        return _value;
    }

    /// <summary>
    /// Determines whether a name is equal to a string.
    /// </summary>
    public static bool operator ==(PdfNameObject name, string str)
    {
        return name._value == str;
    }

    /// <summary>
    /// Determines whether a name is not equal to a string.
    /// </summary>
    public static bool operator !=(PdfNameObject name, string str)
    {
        return name._value != str;
    }

    /// <summary>
    /// Writes the name including the leading slash.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteBeginObject(this);
        writer.Write(new PdfName(_value));
        writer.WriteEndObject();
    }
}
