// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using PeachPDF.PDF.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a PDF name value.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfName : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfName"/> class.
    /// </summary>
    public PdfName()
    {
        _value = "/";  // Empty name.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfName"/> class.
    /// Parameter value always must start with a '/'.
    /// </summary>
    public PdfName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value[0] != '/')
            throw new ArgumentException("A PDF name must start with a slash (/).");

        _value = value;
    }

    /// <summary>
    /// Determines whether the specified object is equal to this name.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return _value.Equals(obj);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    /// <summary>
    /// Gets the name as a string.
    /// </summary>
    public string Value
    {
        // This class must behave like a value type. Therefore it cannot be changed (like System.String).
        get => _value;
    }
    readonly string _value;

    /// <summary>
    /// Returns the name. The string always begins with a slash.
    /// </summary>
    public override string ToString()
    {
        return _value;
    }

    /// <summary>
    /// Determines whether the specified name and string are equal.
    /// </summary>
    public static bool operator ==(PdfName? name, string? str)
    {
        if (ReferenceEquals(name, null))
            return str is null;

        return name._value == str;
    }

    /// <summary>
    /// Determines whether the specified name and string are not equal.
    /// </summary>
    public static bool operator !=(PdfName? name, string? str)
    {
        if (ReferenceEquals(name, null))
            return str is not null;

        return name._value != str;
    }

    /// <summary>
    /// Represents the empty name.
    /// </summary>
    public static readonly PdfName Empty = new PdfName("/");

    /// <summary>
    /// Writes the name including the leading slash.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        // TODO: what if unicode character are part of the name? 
        writer.Write(this);
    }

    /// <summary>
    /// Gets the comparer for this type.
    /// </summary>
    public static PdfNameKeyComparer Comparer => new PdfNameKeyComparer();

    /// <summary>
    /// Implements a comparer that compares PdfName objects.
    /// </summary>
    internal class PdfNameKeyComparer : IComparer<PdfName>
    {
        /// <summary>
        /// Compares two objects and returns a value indicating whether one is less than, equal to, or greater than the other.
        /// </summary>
        /// <param name="l">The first object to compare.</param>
        /// <param name="r">The second object to compare.</param>
        public int Compare(PdfName? l, PdfName? r)
        {

            if (l is not null)
            {
                if (r is not null)
                    return string.Compare(l._value, r._value, StringComparison.Ordinal);
                return -1;
            }
            if (r is not null)
                return 1;
            return 0;
        }
    }
}
