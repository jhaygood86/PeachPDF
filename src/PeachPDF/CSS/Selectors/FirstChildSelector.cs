namespace PeachPDF.CSS

{
    internal sealed class FirstChildSelector : ChildSelector
    {
        public FirstChildSelector()
            : base(PseudoClassNames.NthChild)
        {
        }
    }
}