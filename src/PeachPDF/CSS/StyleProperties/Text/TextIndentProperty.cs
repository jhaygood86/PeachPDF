namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>text-indent</c> property (CSS Text Module Level 3 §3). The value is validated by the
    /// shared <see cref="TextIndentGrammar"/> and the authored text preserved for the layout engine,
    /// which resolves the used indent and the <c>hanging</c>/<c>each-line</c> flags per line.
    /// </summary>
    internal sealed class TextIndentProperty : Property
    {
        private static readonly IValueConverter StyleConverter = new TextIndentValueConverter().OrDefault();

        internal TextIndentProperty()
            : base(PropertyNames.TextIndent, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
