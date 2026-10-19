namespace PeachBrackets.Css
{
    internal interface IMarginRule : IRule
    {
        string Name { get; }
        StyleDeclaration Style { get; }
    }
}