namespace PeachBrackets.Css
{
    internal interface IRuleCreator
    {
        IRule AddNewRule(RuleType ruleType);
    }
}