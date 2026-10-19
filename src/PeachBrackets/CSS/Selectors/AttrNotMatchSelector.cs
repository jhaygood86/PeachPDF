namespace PeachPDF.CSS
{
    internal sealed class AttrNotMatchSelector : AttrSelectorBase
    {
        public AttrNotMatchSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}!={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
