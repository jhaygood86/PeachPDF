namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// A single-line text AcroForm field ("/FT /Tx", ISO 32000-1 §12.7.4.3) - covers PeachPDF's
    /// "text" field kind (see FormFieldMapper.FormFieldKind.Text), which itself collapses every
    /// text-like HTML input type (text/email/password/number/...) to this one PDF field type.
    /// </summary>
    internal sealed class PdfTextField : PdfAcroField
    {
        /// <summary>Bit 24 (ISO 32000-1 Table 228) - the field does not scroll to accommodate more text than fits in its rect.</summary>
        internal const int DoNotScrollFlag = 1 << 23;

        /// <summary>Bit 25 (ISO 32000-1 Table 228) - the field is divided into MaxLen equally spaced character cells.</summary>
        internal const int CombFlag = 1 << 24;

        public PdfTextField(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Subtype, "/Widget");
            FieldType = "/Tx";
        }

        /// <summary>The field's current text value ("/V").</summary>
        public string Value
        {
            get { return Elements.GetString(Keys.V); }
            set { Elements.SetString(Keys.V, value ?? string.Empty, PdfStringEncoding.WinAnsiEncoding); }
        }

        /// <summary>The comb cell count ("/MaxLen") - only meaningful together with <see cref="CombFlag"/>.</summary>
        public int MaxLen
        {
            get { return Elements.GetInteger(Keys.MaxLen); }
            set { Elements.SetInteger(Keys.MaxLen, value); }
        }
    }
}
