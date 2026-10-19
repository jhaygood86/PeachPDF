namespace PeachBrackets.Css
{
    internal sealed class OnlyOfTypeSelector : SelectorBase
    {
        public OnlyOfTypeSelector()
            : base(Priority.OneClass, $"{PseudoClassNames.Separator}{PseudoClassNames.OnlyType}")
        {
        }
    }
}
