using System.Collections.Generic;
using System.Linq;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Css
{
    internal sealed class StyleAndCondition : IStyleQueryCondition
    {
        private readonly List<IStyleQueryCondition> _conditions;

        internal StyleAndCondition(List<IStyleQueryCondition> conditions)
        {
            _conditions = conditions;
        }

        public bool Check(CssBox container) => _conditions.All(condition => condition.Check(container));
    }
}
