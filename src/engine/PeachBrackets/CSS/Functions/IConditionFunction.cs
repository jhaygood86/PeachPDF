namespace PeachBrackets.Css
{
    internal interface IConditionFunction : IStylesheetNode
    {
        bool Check(PeachDrawing.Core.RenderContext renderContext);
    }
}