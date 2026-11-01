namespace PeachBrackets.Css
{
    internal interface ISupportsRule : IConditionRule
    {
        IConditionFunction Condition { get; }
    }
}