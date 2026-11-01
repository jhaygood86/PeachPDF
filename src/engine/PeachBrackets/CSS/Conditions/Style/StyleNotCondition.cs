using PeachBrackets.Html.Dom;

namespace PeachBrackets.Css
{
    internal sealed class StyleNotCondition : IStyleQueryCondition
    {
        private readonly IStyleQueryCondition _content;

        internal StyleNotCondition(IStyleQueryCondition content)
        {
            _content = content;
        }

        public bool Check(CssBox container) => !_content.Check(container);
    }
}
