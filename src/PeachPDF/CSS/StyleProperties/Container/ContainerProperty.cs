namespace PeachPDF.CSS
{
    /// <summary>The <c>container</c> shorthand (CSS Containment 3 SS2): <c>&lt;'container-name'&gt; [ / &lt;'container-type'&gt; ]?</c>.</summary>
    internal sealed class ContainerProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.ContainerShorthandConverter.OrDefault();

        internal ContainerProperty()
            : base(PropertyNames.Container)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
