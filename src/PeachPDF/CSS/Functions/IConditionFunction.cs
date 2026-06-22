namespace PeachPDF.CSS
{
    internal interface IConditionFunction : IStylesheetNode
    {
        bool Check();
    }
}