namespace PeachPDF.CSS
{
    internal interface IDocumentFunction : IStylesheetNode
    {
        string Name { get; }
        string Data { get; }
    }
}