namespace PeachDrawing.PDF.Engine.Annotations;

/// <summary>
/// An annotation of any subtype other than a link or a form field widget (a highlight, a note, a stamp, a drawing...), described by its
/// subtype, the common entries of ISO 32000-1 Table 164 and the few subtype entries that decide where a mark goes.
/// </summary>
internal sealed class PdfGenericAnnotation : PdfAnnotation
{
    public PdfGenericAnnotation(PdfDocument document, string subtype)
        : base(document)
    {
        Elements.SetName(Keys.Subtype, "/" + subtype.TrimStart('/'));
    }

    public string? Text
    {
        set { if (value is not null) Elements.SetString(Keys.Contents, value, PdfStringEncoding.Unicode); }
    }

    public void SetString(string key, string? value)
    {
        if (value is not null) Elements.SetString(key, value, PdfStringEncoding.Unicode);
    }

    public void SetName(string key, string value) => Elements.SetName(key, "/" + value.TrimStart('/'));

    public void SetNumber(string key, double value) => Elements.SetReal(key, value);

    public void SetNumbers(string key, System.Collections.Generic.IEnumerable<double> values)
    {
        var array = new PdfArray(Owner);
        foreach (var value in values)
            array.Elements.Add(new PdfReal(value));
        Elements.SetObject(key, array);
    }

    /// <summary>Sets an array of arrays of numbers (an ink list: one array of coordinates per stroke).</summary>
    public void SetNumberLists(string key, System.Collections.Generic.IEnumerable<System.Collections.Generic.IEnumerable<double>> lists)
    {
        var outer = new PdfArray(Owner);
        foreach (var list in lists)
        {
            var inner = new PdfArray(Owner);
            foreach (var value in list)
                inner.Elements.Add(new PdfReal(value));
            outer.Elements.Add(inner);
        }
        Elements.SetObject(key, outer);
    }
}
