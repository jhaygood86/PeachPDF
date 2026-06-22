namespace PeachPDF.CSS
{
    internal sealed class FirstColumnSelector : ChildSelector
    {
        public FirstColumnSelector()
            : base(PseudoClassNames.NthColumn)
        {
        }
    }
}