namespace PeachDrawing.PDF.Engine.AcroForms;

/// <summary>An unsigned signature AcroForm field ("/FT /Sig", ISO 32000-1 §12.7.4.5): a place a reader can sign, with no signature value.</summary>
internal sealed class PdfSignatureField : PdfAcroField
{
    public PdfSignatureField(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Subtype, "/Widget");
        FieldType = "/Sig";
    }
}
