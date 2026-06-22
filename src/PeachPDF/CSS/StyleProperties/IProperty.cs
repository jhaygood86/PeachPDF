namespace PeachPDF.CSS
{
    internal interface IProperty : IStylesheetNode
    {
        string Name { get; }
        string Value { get; }
        string Original { get; }
        bool IsImportant { get; }
    }
}