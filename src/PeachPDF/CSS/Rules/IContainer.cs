namespace PeachPDF.CSS
{
    internal interface IContainerRule : IConditionRule
    {
        string Name { get; set; }
        MediaList Media { get; }
    }
}