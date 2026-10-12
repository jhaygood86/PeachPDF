// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a direct integer value.
/// </summary>
[DebuggerDisplay("({Value})")]
internal sealed class PdfInteger : PdfNumber, IConvertible, IFormattable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfInteger"/> class.
    /// </summary>
    public PdfInteger()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfInteger"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfInteger(int value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value as integer.
    /// </summary>
    public int Value
    {
        // This class must behave like a value type. Therefore it cannot be changed (like System.String).
        get => _value;
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
    /// Writes the integer as string.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.Write(this);
    }

    sbyte IConvertible.ToSByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    ulong IConvertible.ToUInt64(IFormatProvider? provider)
    {
        return Convert.ToUInt64(_value);
    }

    double IConvertible.ToDouble(IFormatProvider? provider)
    {
        return _value;
    }

    DateTime IConvertible.ToDateTime(IFormatProvider? provider)
    {
        // TODO:  Add PdfInteger.ToDateTime implementation
        return new DateTime();
    }

    float IConvertible.ToSingle(IFormatProvider? provider)
    {
        return _value;
    }

    bool IConvertible.ToBoolean(IFormatProvider? provider)
    {
        return Convert.ToBoolean(_value);
    }

    int IConvertible.ToInt32(IFormatProvider? provider)
    {
        return _value;
    }

    ushort IConvertible.ToUInt16(IFormatProvider? provider)
    {
        return Convert.ToUInt16(_value);
    }

    short IConvertible.ToInt16(IFormatProvider? provider)
    {
        return Convert.ToInt16(_value);
    }

    string IConvertible.ToString(IFormatProvider? provider)
    {
        return _value.ToString(provider);
    }

    byte IConvertible.ToByte(IFormatProvider? provider)
    {
        return Convert.ToByte(_value);
    }

    char IConvertible.ToChar(IFormatProvider? provider)
    {
        return Convert.ToChar(_value);
    }

    long IConvertible.ToInt64(IFormatProvider? provider)
    {
        return _value;
    }

    /// <summary>
    /// Returns TypeCode for 32-bit integers.
    /// </summary>
    public TypeCode GetTypeCode()
    {
        return TypeCode.Int32;
    }

    decimal IConvertible.ToDecimal(IFormatProvider? provider)
    {
        return _value;
    }

    object IConvertible.ToType(Type conversionType, IFormatProvider? provider)
    {
        // TODO:  Add PdfInteger.ToType implementation
        return null!;
    }

    uint IConvertible.ToUInt32(IFormatProvider? provider)
    {
        return Convert.ToUInt32(_value);
    }

    string IFormattable.ToString(string? format, IFormatProvider? provider)
    {
        return _value.ToString(format, provider);
    }

}
