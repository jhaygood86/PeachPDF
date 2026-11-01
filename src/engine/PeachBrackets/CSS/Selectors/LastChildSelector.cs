namespace PeachBrackets.Css
{
    internal sealed class LastChildSelector : ChildSelector
    {
        public LastChildSelector()
            : base(PseudoClassNames.NthLastChild)
        {
        }
    }
}