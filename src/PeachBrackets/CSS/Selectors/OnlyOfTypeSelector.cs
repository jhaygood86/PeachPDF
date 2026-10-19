namespace PeachPDF.CSS
{
    internal sealed class OnlyOfTypeSelector : SelectorBase
    {
        public OnlyOfTypeSelector()
            : base(Priority.OneClass, $"{PseudoClassNames.Separator}{PseudoClassNames.OnlyType}")
        {
        }
    }
}
