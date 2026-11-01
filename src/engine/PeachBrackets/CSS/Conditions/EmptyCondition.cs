using System.IO;

namespace PeachBrackets.Css
{
    internal sealed class EmptyCondition : StylesheetNode, IConditionFunction
    {
        public bool Check(PeachDrawing.Core.RenderContext renderContext)
        {
            return true;
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
        }
    }
}