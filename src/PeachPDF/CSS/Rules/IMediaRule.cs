namespace PeachPDF.CSS
{
    internal interface IMediaRule : IConditionRule
    {
        MediaList Media { get; }
    }
}