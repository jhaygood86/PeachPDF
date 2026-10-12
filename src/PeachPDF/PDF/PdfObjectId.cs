// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a PDF object identifier, a pair of object and generation number.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay}")]
internal struct PdfObjectId : IComparable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObjectId"/> class.
    /// </summary>
    /// <param name="objectNumber">The object number.</param>
    public PdfObjectId(int objectNumber)
    {
        Debug.Assert(objectNumber >= 1, "Object number out of range.");
        _objectNumber = objectNumber;
        _generationNumber = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObjectId"/> class.
    /// </summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generationNumber">The generation number.</param>
    public PdfObjectId(int objectNumber, int generationNumber)
    {
        Debug.Assert(objectNumber >= 1, "Object number out of range.");
        //Debug.Assert(generationNumber >= 0 && generationNumber <= 65535, "Generation number out of range.");
        _objectNumber = objectNumber;
        _generationNumber = (ushort)generationNumber;
    }

    /// <summary>
    /// Gets or sets the object number.
    /// </summary>
    public int ObjectNumber
    {
        get { return _objectNumber; }
    }
    readonly int _objectNumber;

    /// <summary>
    /// Gets or sets the generation number.
    /// </summary>
    public int GenerationNumber
    {
        get { return _generationNumber; }
    }
    readonly ushort _generationNumber;

    /// <summary>
    /// Indicates whether this object is an empty object identifier.
    /// </summary>
    public bool IsEmpty
    {
        get { return _objectNumber == 0; }
    }

    /// <summary>
    /// Indicates whether this instance and a specified object are equal.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is PdfObjectId)
        {
            PdfObjectId id = (PdfObjectId)obj;
            if (_objectNumber == id._objectNumber)
                return _generationNumber == id._generationNumber;
        }
        return false;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return _objectNumber ^ _generationNumber;
    }

    /// <summary>
    /// Determines whether the two objects are equal.
    /// </summary>
    public static bool operator ==(PdfObjectId left, PdfObjectId right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether the tow objects not are equal.
    /// </summary>
    public static bool operator !=(PdfObjectId left, PdfObjectId right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Returns the object and generation numbers as a string.
    /// </summary>
    public override string ToString()
    {
        return _objectNumber.ToString(CultureInfo.InvariantCulture) + " " + _generationNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates an empty object identifier.
    /// </summary>
    public static PdfObjectId Empty
    {
        get { return new PdfObjectId(); }
    }

    /// <summary>
    /// Compares the current object id with another object.
    /// </summary>
    public int CompareTo(object? obj)
    {
        if (obj is PdfObjectId)
        {
            PdfObjectId id = (PdfObjectId)obj;
            if (_objectNumber == id._objectNumber)
                return _generationNumber - id._generationNumber;
            return _objectNumber - id._objectNumber;
        }
        return 1;
    }

    /// <summary>
    /// Gets the DebuggerDisplayAttribute text.
    /// </summary>
    internal string DebuggerDisplay
    {
        get { return String.Format("id=({0})", ToString()); }
    }
}
