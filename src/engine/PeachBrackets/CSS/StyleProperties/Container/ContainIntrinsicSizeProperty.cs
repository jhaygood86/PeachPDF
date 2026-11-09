namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>contain-intrinsic-size</c> shorthand (css-sizing-4 §5.1): <c>[ auto? [ none | &lt;length&gt; ] ]{1,2}</c>, the width and then the height; one
    /// value sets both.
    /// </summary>
    internal sealed class ContainIntrinsicSizeProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.ContainIntrinsicSizeConverter.OrDefault();

        internal ContainIntrinsicSizeProperty()
            : base(PropertyNames.ContainIntrinsicSize)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
