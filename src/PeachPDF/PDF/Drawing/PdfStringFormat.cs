// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents the text layout information.
/// </summary>
internal class PdfStringFormat
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStringFormat"/> class.
    /// </summary>
    public PdfStringFormat()
    {
    }

    //TODO public StringFormat(StringFormat format);
    //public StringFormat(StringFormatFlags options);
    //public StringFormat(StringFormatFlags options, int language);
    //public object Clone();
    //public void Dispose();
    //private void Dispose(bool disposing);
    //protected override void Finalize();
    //public float[] GetTabStops(out float firstTabOffset);
    //public void SetDigitSubstitution(int language, StringDigitSubstitute substitute);
    //public void SetMeasurableCharacterRanges(CharacterRange[] ranges);
    //public void SetTabStops(float firstTabOffset, float[] tabStops);
    //public override string ToString();

    /// <summary>
    /// Gets or sets horizontal text alignment information.
    /// </summary>
    public PdfStringAlignment Alignment
    {
        get => _alignment;
        set
        {
            _alignment = value;
        }
    }
    PdfStringAlignment _alignment;

    //public int DigitSubstitutionLanguage { get; }
    //public StringDigitSubstitute DigitSubstitutionMethod { get; }
    //public StringFormatFlags FormatFlags { get; set; }
    //public static StringFormat GenericDefault { get; }
    //public static StringFormat GenericTypographic { get; }
    //public HotkeyPrefix HotkeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the line alignment.
    /// </summary>
    public PdfLineAlignment LineAlignment
    {
        get => _lineAlignment;
        set
        {
            _lineAlignment = value;

        }
    }
    PdfLineAlignment _lineAlignment;
}
