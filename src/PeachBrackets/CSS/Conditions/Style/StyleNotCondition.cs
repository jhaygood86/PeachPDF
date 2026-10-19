using PeachPDF.Html.Core.Dom;

namespace PeachPDF.CSS
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
