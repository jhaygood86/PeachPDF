namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>grid-lanes</c> shorthand (CSS Grid 3): expands to <c>grid-template-rows</c> / <c>-columns</c> / <c>-areas</c> and <c>grid-lanes-direction</c>.
    /// </summary>
    internal sealed class GridLanesProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new GridLanesShorthandValueConverter().OrGlobalValue();

        internal GridLanesProperty()
            : base(PropertyNames.GridLanes)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
