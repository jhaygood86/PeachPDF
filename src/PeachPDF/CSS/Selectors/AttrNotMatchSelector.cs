namespace PeachPDF.CSS
{
    public sealed class AttrNotMatchSelector : AttrSelectorBase
    {
        public AttrNotMatchSelector(string attribute, string value)
            : base(attribute, value, $"[{attribute}!={value.StylesheetString()}]")
        {
        }
    }
}