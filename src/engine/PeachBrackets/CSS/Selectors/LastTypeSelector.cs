namespace PeachBrackets.Css
{
    internal sealed class LastTypeSelector : ChildSelector
    {
        public LastTypeSelector()
            : base(PseudoClassNames.NthLastOfType)
        {
        }
    }
}