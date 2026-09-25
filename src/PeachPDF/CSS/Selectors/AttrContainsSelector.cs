namespace PeachPDF.CSS
{
    internal sealed class AttrContainsSelector : AttrSelectorBase
    {
        public AttrContainsSelector(string attribute, string value,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
            : base(attribute, value, $"[{attribute}*={value.StylesheetString()}{Flag(caseSensitivity)}]", caseSensitivity)
        {
        }
    }
}
