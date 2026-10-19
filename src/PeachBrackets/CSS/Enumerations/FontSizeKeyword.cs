namespace PeachPDF.CSS
{
    /// <summary>The keyword side of <c>font-size</c>'s <c>&lt;absolute-size&gt; | &lt;relative-size&gt; |
    /// &lt;length-percentage&gt;</c> grammar, for its <see cref="CssKeywordOrValue{TEnum,TValue}"/>.
    /// <c>Smaller</c>/<c>Larger</c> are relative to the parent's own resolved size (CSS Fonts 4
    /// <c>&lt;relative-size&gt;</c>); the other seven are fixed offsets from the CSS initial
    /// (<c>&lt;absolute-size&gt;</c>). Deliberately excludes CSS Fonts 4's <c>xxx-large</c> - see the
    /// <c>font-size</c> JSON entry's own comment.</summary>
    internal enum FontSizeKeyword
    {
        XxSmall,
        XSmall,
        Small,
        Medium,
        Large,
        XLarge,
        XxLarge,
        Smaller,
        Larger
    }
}
