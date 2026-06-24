namespace PeachPDF.CSS
{
    internal interface IStyleRule : IRule
    {
        string SelectorText { get; set; }
        StyleDeclaration Style { get; }
        ISelector Selector { get; set; }
    }
}