namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// A checkbox AcroForm field ("/FT /Btn", ISO 32000-1 §12.7.4.2.3) - a single terminal field
    /// with its own two-state appearance dictionary ("/AP /N &lt;&lt; /Yes ... /Off ... &gt;&gt;"),
    /// unlike <see cref="PdfRadioButtonField"/> which is a group of separate widgets.
    /// </summary>
    internal sealed class PdfCheckBoxField : PdfAcroField
    {
        public PdfCheckBoxField(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Subtype, "/Widget");
            FieldType = "/Btn";
        }

        /// <summary>The checkbox's export value ("/V") when checked - "/Off" when unchecked.</summary>
        public string Value
        {
            get { return Elements.GetName(Keys.V); }
            set { Elements.SetName(Keys.V, value); }
        }
    }
}
