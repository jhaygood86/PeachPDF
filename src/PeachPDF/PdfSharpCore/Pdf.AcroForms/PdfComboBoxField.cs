namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// A combo box (drop-down) AcroForm field ("/FT /Ch" with the Combo flag, ISO 32000-1 §12.7.4.4)
    /// - PeachPDF's mapping for a classified <c>&lt;select&gt;</c> element.
    /// </summary>
    internal sealed class PdfComboBoxField : PdfAcroField
    {
        /// <summary>Bit 18 (ISO 32000-1 Table 230) - the choice field is a combo box rather than a scrollable list box.</summary>
        internal const int ComboFlag = 1 << 17;

        public PdfComboBoxField(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Subtype, "/Widget");
            FieldType = "/Ch";
            FieldFlags = ComboFlag;
        }

        /// <summary>The selected option's export value ("/V").</summary>
        public string Value
        {
            get { return Elements.GetString(Keys.V); }
            set { Elements.SetString(Keys.V, value ?? string.Empty, PdfStringEncoding.WinAnsiEncoding); }
        }

        /// <summary>
        /// Sets the "/Opt" array from the &lt;select&gt;'s &lt;option&gt; children - a plain string
        /// per option when its export value equals its display label (the common case), else a
        /// two-element [value, label] array (ISO 32000-1 Table 231).
        /// </summary>
        public void SetOptions(System.Collections.Generic.IReadOnlyList<(string Value, string Label)> options)
        {
            var array = new PdfArray(Owner);
            foreach (var (value, label) in options)
            {
                if (value == label)
                {
                    array.Elements.Add(new PdfString(label, PdfStringEncoding.WinAnsiEncoding));
                }
                else
                {
                    var pair = new PdfArray(Owner);
                    pair.Elements.Add(new PdfString(value, PdfStringEncoding.WinAnsiEncoding));
                    pair.Elements.Add(new PdfString(label, PdfStringEncoding.WinAnsiEncoding));
                    array.Elements.Add(pair);
                }
            }
            Elements.SetObject(Keys.Opt, array);
        }
    }
}
