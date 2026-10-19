namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// The value half of one <c>font-feature-settings</c> <c>&lt;feature-tag-value&gt;</c> entry -
    /// <c>[ &lt;integer&gt; | on | off ]?</c>, defaulting to <c>1</c> when omitted (a bare tag means
    /// "on"). Shared between <see cref="FontFeatureSettingsProperty"/>'s own <c>&lt;string&gt;</c>-tagged
    /// grammar and <see cref="PrinceOpenTypeConverter"/>'s ident-tagged one, so the two
    /// grammars' identical value half isn't parsed by two independent converters.
    /// </summary>
    internal static class FontFeatureTagListGrammar
    {
        // "on"/"off" are matched here (not resolved to 1/0) - the plain-keyword converter this
        // codebase's Or(...) combinator produces serializes the literal keyword text either way, so
        // the numeric resolution of on->1/off->0 happens once, downstream, wherever the cascaded
        // string is actually consumed (see DerivedStyle.ActualFontFeatureSettings).
        public static readonly IValueConverter FeatureValueConverter =
            IntegerConverter.Or(Keywords.On).Or(Keywords.Off);
    }
}
