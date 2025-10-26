namespace PeachPDF.CSS
{
    public sealed class AttrHyphenSelector : AttrSelectorBase
    {
        public AttrHyphenSelector(string attribute, string value)
            : base(attribute, value, $"[{attribute}|={value.StylesheetString()}]")
        {
        }
    }
}