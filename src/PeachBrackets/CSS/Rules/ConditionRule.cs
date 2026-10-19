namespace PeachPDF.CSS
{
    internal abstract class ConditionRule : GroupingRule
    {
        internal ConditionRule(RuleType type, StylesheetParser parser)
            : base(type, parser)
        {
        }
    }
}