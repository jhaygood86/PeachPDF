using System.Collections.Generic;
using System.Linq;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Css
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
