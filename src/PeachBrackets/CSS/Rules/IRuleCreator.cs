namespace PeachPDF.CSS
{
    internal interface IRuleCreator
    {
        IRule AddNewRule(RuleType ruleType);
    }
}