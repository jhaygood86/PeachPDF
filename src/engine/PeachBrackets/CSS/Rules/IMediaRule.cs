namespace PeachBrackets.Css
{
    internal interface IMediaRule : IConditionRule
    {
        MediaList Media { get; }
    }
}