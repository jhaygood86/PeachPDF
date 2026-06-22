namespace PeachPDF.CSS
{
    internal interface ISupportsRule : IConditionRule
    {
        IConditionFunction Condition { get; }
    }
}