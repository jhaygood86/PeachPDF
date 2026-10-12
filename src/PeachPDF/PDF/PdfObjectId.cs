// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Identifies an indirect PDF object by its object number and generation number.
/// </summary>
[DebuggerDisplay("id=({ToString(),nq})")]
internal readonly struct PdfObjectId : IEquatable<PdfObjectId>, IComparable<PdfObjectId>, IComparable
{
    public PdfObjectId(int objectNumber) : this(objectNumber, 0)
    {
    }

    public PdfObjectId(int objectNumber, int generationNumber)
    {
        Debug.Assert(objectNumber >= 1, "Object number out of range.");
        ObjectNumber = objectNumber;
        GenerationNumber = (ushort)generationNumber;
    }

    /// <summary>
    /// Gets the object number.
    /// </summary>
    public int ObjectNumber { get; }

    /// <summary>
    /// Gets the generation number.
    /// </summary>
    public ushort GenerationNumber { get; }

    /// <summary>
    /// Indicates whether this is the empty id that belongs to no object.
    /// </summary>
    public bool IsEmpty => ObjectNumber == 0;

    /// <summary>
    /// The empty id.
    /// </summary>
    public static PdfObjectId Empty => default;

    public bool Equals(PdfObjectId other) => ObjectNumber == other.ObjectNumber && GenerationNumber == other.GenerationNumber;

    public override bool Equals(object? obj) => obj is PdfObjectId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ObjectNumber, GenerationNumber);

    public static bool operator ==(PdfObjectId left, PdfObjectId right) => left.Equals(right);

    public static bool operator !=(PdfObjectId left, PdfObjectId right) => !left.Equals(right);

    public int CompareTo(PdfObjectId other)
    {
        int byNumber = ObjectNumber.CompareTo(other.ObjectNumber);
        return byNumber != 0 ? byNumber : GenerationNumber.CompareTo(other.GenerationNumber);
    }

    int IComparable.CompareTo(object? obj) => obj is PdfObjectId other ? CompareTo(other) : 1;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{ObjectNumber} {GenerationNumber}");
}
