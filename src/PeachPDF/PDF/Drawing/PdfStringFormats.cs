// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents predefined text layouts.
/// </summary>
internal static class PdfStringFormats
{
    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text left on the base line.
    /// This is the same as BaseLineLeft.
    /// </summary>
    public static PdfStringFormat Default => BaseLineLeft;

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text left on the base line.
    /// This is the same as Default.
    /// </summary>
    public static PdfStringFormat BaseLineLeft
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Near;
            format.LineAlignment = PdfLineAlignment.BaseLine;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text top left of the layout rectangle.
    /// </summary>
    public static PdfStringFormat TopLeft
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Near;
            format.LineAlignment = PdfLineAlignment.Near;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text center left of the layout rectangle.
    /// </summary>
    public static PdfStringFormat CenterLeft
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Near;
            format.LineAlignment = PdfLineAlignment.Center;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text bottom left of the layout rectangle.
    /// </summary>
    public static PdfStringFormat BottomLeft
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Near;
            format.LineAlignment = PdfLineAlignment.Far;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that centers the text in the middle of the base line.
    /// </summary>
    public static PdfStringFormat BaseLineCenter
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Center;
            format.LineAlignment = PdfLineAlignment.BaseLine;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that centers the text at the top of the layout rectangle.
    /// </summary>
    public static PdfStringFormat TopCenter
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Center;
            format.LineAlignment = PdfLineAlignment.Near;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that centers the text in the middle of the layout rectangle.
    /// </summary>
    public static PdfStringFormat Center
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Center;
            format.LineAlignment = PdfLineAlignment.Center;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that centers the text at the bottom of the layout rectangle.
    /// </summary>
    public static PdfStringFormat BottomCenter
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Center;
            format.LineAlignment = PdfLineAlignment.Far;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text in right on the base line.
    /// </summary>
    public static PdfStringFormat BaseLineRight
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Far;
            format.LineAlignment = PdfLineAlignment.BaseLine;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text top right of the layout rectangle.
    /// </summary>
    public static PdfStringFormat TopRight
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Far;
            format.LineAlignment = PdfLineAlignment.Near;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text center right of the layout rectangle.
    /// </summary>
    public static PdfStringFormat CenterRight
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Far;
            format.LineAlignment = PdfLineAlignment.Center;
            return format;
        }
    }

    /// <summary>
    /// Gets a new PdfStringFormat object that aligns the text at the bottom right of the layout rectangle.
    /// </summary>
    public static PdfStringFormat BottomRight
    {
        get
        {
            // Create new format to allow changes.
            PdfStringFormat format = new PdfStringFormat();
            format.Alignment = PdfStringAlignment.Far;
            format.LineAlignment = PdfLineAlignment.Far;
            return format;
        }
    }
}
