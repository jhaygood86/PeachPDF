namespace PeachBrackets.Css
{
    internal interface IConditionRule : IGroupingRule
    {
        string ConditionText { get; set; }
    }
}