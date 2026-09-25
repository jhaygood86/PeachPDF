namespace PeachPDF.CSS
{
    internal sealed class AttrBeginsSelector : AttrSelectorBase
    {
        public AttrBeginsSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}^={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
