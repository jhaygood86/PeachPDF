namespace PeachBrackets.Css
{
    internal sealed class FirstTypeSelector : ChildSelector
    {
        public FirstTypeSelector()
            : base(PseudoClassNames.NthOfType)
        {
        }
    }
}