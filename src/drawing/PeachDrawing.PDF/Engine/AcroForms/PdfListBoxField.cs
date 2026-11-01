namespace PeachDrawing.PDF.Engine.AcroForms;

/// <summary>
/// A scrollable list box AcroForm field ("/FT /Ch" without the Combo flag, ISO 32000-1 §12.7.4.4): the choices stay visible and, with the
/// multiple-selection flag, more than one can be chosen.
/// </summary>
internal sealed class PdfListBoxField : PdfAcroField
{
    /// <summary>Bit 22 (ISO 32000-1 Table 230) - more than one option may be selected at once.</summary>
    internal const int MultiSelectFlag = 1 << 21;

    public PdfListBoxField(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Subtype, "/Widget");
        FieldType = "/Ch";
    }

    /// <summary>Sets the selection ("/V" and the matching "/I" indices): one string for a single choice, an array for several, nothing for none.</summary>
    public void SetSelection(System.Collections.Generic.IReadOnlyList<string> exportValues, System.Collections.Generic.IReadOnlyList<int> indices)
    {
        if (exportValues.Count == 1)
        {
            Elements.SetString(Keys.V, exportValues[0], PdfStringEncoding.WinAnsiEncoding);
        }
        else if (exportValues.Count > 1)
        {
            var array = new PdfArray(Owner);
            foreach (var value in exportValues)
                array.Elements.Add(new PdfString(value, PdfStringEncoding.WinAnsiEncoding));
            Elements.SetObject(Keys.V, array);
        }

        if (indices.Count > 0)
        {
            var array = new PdfArray(Owner);
            foreach (var index in indices)
                array.Elements.Add(new PdfInteger(index));
            Elements.SetObject("/I", array);
        }
    }

    /// <summary>Sets the "/Opt" array: a plain string per option when its export value equals its label, else a [value, label] pair.</summary>
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
