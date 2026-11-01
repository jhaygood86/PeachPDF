namespace PeachBrackets.Css
{
    internal interface IImportRule : IRule
    {
        string Href { get; set; }
        MediaList Media { get; }
    }
}