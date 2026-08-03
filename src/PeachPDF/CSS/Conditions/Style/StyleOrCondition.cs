using System.Collections.Generic;
using System.Linq;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.CSS
{
    internal sealed class StyleOrCondition : IStyleQueryCondition
    {
        private readonly List<IStyleQueryCondition> _conditions;

        internal StyleOrCondition(List<IStyleQueryCondition> conditions)
        {
            _conditions = conditions;
        }

        public bool Check(CssBox container) => _conditions.Any(condition => condition.Check(container));
    }
}
