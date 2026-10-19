using System.IO;
using System.Linq;

namespace PeachBrackets.Css.Conditions
{
    internal sealed class AndCondition : StylesheetNode, IConditionFunction
    {
        public bool Check(PeachDrawing.Core.RenderContext renderContext)
        {
            var conditions = Children.OfType<IConditionFunction>();

            return conditions.All(condition => condition.Check(renderContext));
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var conditions = Children.OfType<IConditionFunction>();
            var first = true;

            foreach (var condition in conditions)
            {
                if (first)
                    first = false;
                else
                    writer.Write(" and ");

                condition.ToCss(writer, formatter);
            }
        }
    }
}