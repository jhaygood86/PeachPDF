// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Internal;
using PeachDrawing.PDF.Engine.IO;
using System;
using System.Diagnostics;
using System.Globalization;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Represents a PDF rectangle value, that is internally an array with 4 real values.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay}")]
internal sealed class PdfRectangle : PdfItem
{
    // This class must behave like a value type. Therefore it cannot be changed (like System.String).

    /// <summary>
    /// Initializes a new instance of the PdfRectangle class.
    /// </summary>
    public PdfRectangle()
    { }

    /// <summary>
    /// Initializes a new instance of the PdfRectangle class with two points specifying
    /// two diagonally opposite corners. Notice that in contrast to GDI+ convention the 
    /// 3rd and the 4th parameter specify a point and not a width. This is so much confusing
    /// that this function is for internal use only.
    /// </summary>
    internal PdfRectangle(double x1, double y1, double x2, double y2)
    {
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
    }

    /// <summary>
    /// Initializes a new instance of the PdfRectangle class with two points specifying
    /// two diagonally opposite corners.
    /// </summary>
    public PdfRectangle(PaintPoint pt1, PaintPoint pt2)
    {
        _x1 = pt1.X;
        _y1 = pt1.Y;
        _x2 = pt2.X;
        _y2 = pt2.Y;
    }

    /// <summary>
    /// Initializes a new instance of the PdfRectangle class with the specified location and size.
    /// </summary>
    public PdfRectangle(PaintPoint pt, Size size)
    {
        _x1 = pt.X;
        _y1 = pt.Y;
        _x2 = pt.X + size.Width;
        _y2 = pt.Y + size.Height;
    }

    /// <summary>
    /// Initializes a new instance of the PdfRectangle class with the specified Rect.
    /// </summary>
    public PdfRectangle(Rect rect)
    {
        _x1 = rect.X;
        _y1 = rect.Y;
        _x2 = rect.X + rect.Width;
        _y2 = rect.Y + rect.Height;
    }

/// <summary>
    /// Tests whether all coordinate are zero.
    /// </summary>
    public bool IsEmpty
    {
        // ReSharper disable CompareOfFloatsByEqualityOperator
        get => _x1 == 0 && _y1 == 0 && _x2 == 0 && _y2 == 0;
        // ReSharper restore CompareOfFloatsByEqualityOperator
    }

    /// <summary>
    /// Tests whether the specified object is a PdfRectangle and has equal coordinates.
    /// </summary>
    public override bool Equals(object? obj)
    {
        // ReSharper disable CompareOfFloatsByEqualityOperator
        PdfRectangle? rectangle = obj as PdfRectangle;
        if (rectangle is not null)
        {
            PdfRectangle rect = rectangle;
            return rect._x1 == _x1 && rect._y1 == _y1 && rect._x2 == _x2 && rect._y2 == _y2;
        }
        return false;
        // ReSharper restore CompareOfFloatsByEqualityOperator
    }

    /// <summary>
    /// Serves as a hash function for a particular type.
    /// </summary>
    public override int GetHashCode()
    {
        // This code is from System.Drawing...
        return (int)(((((uint)_x1) ^ ((((uint)_y1) << 13) |
          (((uint)_y1) >> 0x13))) ^ ((((uint)_x2) << 0x1a) |
          (((uint)_x2) >> 6))) ^ ((((uint)_y2) << 7) |
          (((uint)_y2) >> 0x19)));
    }

    /// <summary>
    /// Tests whether two structures have equal coordinates.
    /// </summary>
    public static bool operator ==(PdfRectangle? left, PdfRectangle? right)
    {
        // ReSharper disable CompareOfFloatsByEqualityOperator
        // use: if (Object.ReferenceEquals(left, null))
        if (left is not null)
        {
            if (right is not null)
                return left._x1 == right._x1 && left._y1 == right._y1 && left._x2 == right._x2 && left._y2 == right._y2;
            return false;
        }
        return right is null;
        // ReSharper restore CompareOfFloatsByEqualityOperator
    }

    /// <summary>
    /// Tests whether two structures differ in one or more coordinates.
    /// </summary>
    public static bool operator !=(PdfRectangle? left, PdfRectangle? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Gets or sets the x-coordinate of the first corner of this PdfRectangle.
    /// </summary>
    public double X1 => _x1;
    readonly double _x1;

    /// <summary>
    /// Gets or sets the y-coordinate of the first corner of this PdfRectangle.
    /// </summary>
    public double Y1 => _y1;
    readonly double _y1;

    /// <summary>
    /// Gets or sets the x-coordinate of the second corner of this PdfRectangle.
    /// </summary>
    public double X2 => _x2;
    readonly double _x2;

    /// <summary>
    /// Gets or sets the y-coordinate of the second corner of this PdfRectangle.
    /// </summary>
    public double Y2 => _y2;
    readonly double _y2;

    /// <summary>
    /// Gets X2 - X1.
    /// </summary>
    public double Width => _x2 - _x1;

    /// <summary>
    /// Gets Y2 - Y1.
    /// </summary>
    public double Height => _y2 - _y1;

    /// <summary>
    /// Gets or sets the coordinates of the first point of this PdfRectangle.
    /// </summary>
    public PaintPoint Location => new PaintPoint(_x1, _y1);

    /// <summary>
    /// Gets or sets the size of this PdfRectangle.
    /// </summary>
    public Size Size => new Size(_x2 - _x1, _y2 - _y1);

    /// <summary>
    /// Determines if the specified point is contained within this PdfRectangle.
    /// </summary>
    public bool Contains(PaintPoint pt)
    {
        return Contains(pt.X, pt.Y);
    }

    /// <summary>
    /// Determines if the specified point is contained within this PdfRectangle.
    /// </summary>
    public bool Contains(double x, double y)
    {
        // Treat rectangle inclusive/inclusive.
        return _x1 <= x && x <= _x2 && _y1 <= y && y <= _y2;
    }

    /// <summary>
    /// Determines if the rectangular region represented by rect is entirely contained within this PdfRectangle.
    /// </summary>
    public bool Contains(Rect rect)
    {
        return _x1 <= rect.X && (rect.X + rect.Width) <= _x2 &&
          _y1 <= rect.Y && (rect.Y + rect.Height) <= _y2;
    }

    /// <summary>
    /// Determines if the rectangular region represented by rect is entirely contained within this PdfRectangle.
    /// </summary>
    public bool Contains(PdfRectangle rect)
    {
        return _x1 <= rect._x1 && rect._x2 <= _x2 &&
          _y1 <= rect._y1 && rect._y2 <= _y2;
    }

    /// <summary>
    /// Returns the rectangle as an Rect object.
    /// </summary>
    public Rect ToXRect()
    {
        return new Rect(_x1, _y1, Width, Height);
    }

    /// <summary>
    /// Returns the rectangle as a string in the form "[x1 y1 x2 y2]".
    /// </summary>
    public override string ToString()
    {
        const string format = PdfNumberFormats.ThreeDecimals;
        return PdfEncoders.Format("[{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "}]", _x1, _y1, _x2, _y2);
    }

    /// <summary>
    /// Writes the rectangle.
    /// </summary>
    internal override void WriteObject(PdfWriter writer)
    {
        writer.Write(this);
    }

    /// <summary>
    /// Gets the DebuggerDisplayAttribute text.
    /// </summary>
    // ReSharper disable UnusedMember.Local
    string DebuggerDisplay
    // ReSharper restore UnusedMember.Local
    {
        get
        {
            const string format = PdfNumberFormats.TenDecimals;
            return string.Format(CultureInfo.InvariantCulture,
                "X1={0:" + format + "}, X2={1:" + format + "}, Y1={2:" + format + "}, Y2={3:" + format + "}", _x1, _y1, X2, _y2);
        }
    }

    /// <summary>
    /// Represents an empty PdfRectangle.
    /// </summary>
    public static readonly PdfRectangle Empty = new PdfRectangle();
}
