// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Drawing;
using System.Diagnostics;

namespace PeachPDF.PDF;

/// <summary>
/// Represents trim margins added to the page.
/// </summary>
[DebuggerDisplay("(Left={left.Millimeter}mm, Right={right.Millimeter}mm, Top={top.Millimeter}mm, Bottom={bottom.Millimeter}mm)")]
internal sealed class TrimMargins
{
    ///// <summary>
    ///// Clones this instance.
    ///// </summary>

    /// <summary>
    /// Sets all four crop margins simultaneously.
    /// </summary>
    public double All
    {
        set
        {
            _left = value;
            _right = value;
            _top = value;
            _bottom = value;
        }
    }

    /// <summary>
    /// Gets or sets the left crop margin.
    /// </summary>
    public double Left
    {
        get => _left;
        set => _left = value;
    }
    double _left;

    /// <summary>
    /// Gets or sets the right crop margin.
    /// </summary>
    public double Right
    {
        get => _right;
        set => _right = value;
    }
    double _right;

    /// <summary>
    /// Gets or sets the top crop margin.
    /// </summary>
    public double Top
    {
        get => _top;
        set => _top = value;
    }
    double _top;

    /// <summary>
    /// Gets or sets the bottom crop margin.
    /// </summary>
    public double Bottom
    {
        get => _bottom;
        set => _bottom = value;
    }
    double _bottom;

    /// <summary>
    /// Gets a value indicating whether this instance has at least one margin with a value other than zero.
    /// </summary>
    public bool AreSet => _left != 0 || _right != 0 || _top != 0 || _bottom != 0;
}
