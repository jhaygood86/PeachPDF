namespace PeachPDF.CSS
{
    internal interface IConditionFunction : IStylesheetNode
    {
        bool Check(PeachDrawing.Core.RenderContext renderContext);
    }
}