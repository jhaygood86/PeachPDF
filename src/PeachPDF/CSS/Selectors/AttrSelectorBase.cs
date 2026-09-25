namespace PeachPDF.CSS
{
    internal abstract class AttrSelectorBase : SelectorBase, IAttrSelector
    {
        protected AttrSelectorBase(string attribute, string value, string text,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default) : base(Priority.OneClass, text)
        {
            Attribute = attribute;
            Value = value;
            CaseSensitivity = caseSensitivity;
        }
        public string Attribute { get; }
        public string Value { get; }
        public AttrCaseSensitivity CaseSensitivity { get; }

        /// <summary>The serialized modifier (Selectors 4 §6.3), including its leading space, or empty for none.</summary>
        protected static string Flag(AttrCaseSensitivity caseSensitivity) => caseSensitivity switch
        {
            AttrCaseSensitivity.Insensitive => " i",
            AttrCaseSensitivity.Sensitive => " s",
            _ => string.Empty
        };
    }
}
