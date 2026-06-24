namespace PeachPDF.CSS
{
    internal interface IMarginRule : IRule
    {
        string Name { get; }
        StyleDeclaration Style { get; }
    }
}