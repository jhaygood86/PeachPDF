namespace PeachBrackets.Css
{
    internal sealed class LastColumnSelector : ChildSelector
    {
        public LastColumnSelector()
            : base(PseudoClassNames.NthLastColumn)
        {
        }
    }
}