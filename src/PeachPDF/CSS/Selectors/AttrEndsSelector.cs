namespace PeachPDF.CSS
{
    internal sealed class AttrEndsSelector : AttrSelectorBase
    {
        public AttrEndsSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}$={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
