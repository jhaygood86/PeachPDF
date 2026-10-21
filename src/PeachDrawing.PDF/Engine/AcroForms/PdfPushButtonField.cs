namespace PeachDrawing.PDF.Engine.AcroForms;

/// <summary>A push button AcroForm field ("/FT /Btn" with the Pushbutton flag, ISO 32000-1 §12.7.4.2.2): it holds no value and has a caption.</summary>
internal sealed class PdfPushButtonField : PdfAcroField
{
    /// <summary>Bit 17 (ISO 32000-1 Table 226) - the button is a push button, with no state.</summary>
    internal const int PushButtonFlag = 1 << 16;

    public PdfPushButtonField(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Subtype, "/Widget");
        FieldType = "/Btn";
        FieldFlags = PushButtonFlag;
    }

    /// <summary>Sets the button's caption (the "/CA" of its appearance characteristics, "/MK").</summary>
    public void SetCaption(string caption)
    {
        var characteristics = new PdfDictionary(Owner);
        characteristics.Elements.SetString("/CA", caption, PdfStringEncoding.Unicode);
        Elements.SetObject("/MK", characteristics);
    }
}
