namespace PeachBrackets.Css
{
    internal interface IDocumentFunction : IStylesheetNode
    {
        string Name { get; }
        string Data { get; }
    }
}