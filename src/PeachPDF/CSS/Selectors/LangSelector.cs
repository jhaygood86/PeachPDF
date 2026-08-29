namespace PeachPDF.CSS
{
    /// <summary>
    /// The CSS 2.1 §5.11.4 language pseudo-class ":lang(C)" - matches an element whose language (the
    /// nearest ancestor-or-self "lang" attribute) is C, or has C as a hyphen-delimited prefix.
    /// </summary>
    internal sealed class LangSelector : SelectorBase
    {
        public LangSelector(string languageRange)
            : base(Priority.OneClass, $"{PseudoClassNames.Separator}{PseudoClassNames.Lang}({languageRange})")
        {
            LanguageRange = languageRange;
        }

        public string LanguageRange { get; }
    }
}
