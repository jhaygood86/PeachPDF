namespace PeachBrackets.Css
{
    internal interface ISelector : IStylesheetNode
    {
        Priority Specificity { get; }
        string Text { get; }
    }
}