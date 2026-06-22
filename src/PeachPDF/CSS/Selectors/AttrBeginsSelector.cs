namespace PeachPDF.CSS
{
    internal sealed class AttrBeginsSelector : AttrSelectorBase
    {
        public AttrBeginsSelector(string attribute, string value)
            : base(attribute, value, $"[{attribute}^={value.StylesheetString()}]")
        {
        }
    }
}