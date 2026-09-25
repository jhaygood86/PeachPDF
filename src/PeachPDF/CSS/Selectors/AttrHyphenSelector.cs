namespace PeachPDF.CSS
{
    internal sealed class AttrHyphenSelector : AttrSelectorBase
    {
        public AttrHyphenSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}|={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
