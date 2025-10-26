namespace PeachPDF.CSS
{
    public sealed class AttrBeginsSelector : AttrSelectorBase
    {
        public AttrBeginsSelector(string attribute, string value)
            : base(attribute, value, $"[{attribute}^={value.StylesheetString()}]")
        {
        }
    }
}