namespace PeachPDF.CSS
{
    internal interface ISelector : IStylesheetNode
    {
        Priority Specificity { get; }
        string Text { get; }
    }
}