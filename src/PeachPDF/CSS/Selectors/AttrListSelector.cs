namespace PeachPDF.CSS
{
    internal sealed class AttrListSelector : AttrSelectorBase
    {
        public AttrListSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}~={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
