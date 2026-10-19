using PeachBrackets.Building;
using System;

namespace PeachPDF.Layout
{
    /// <summary>Builds a paragraph's individually-styled runs of text, each a new inline child of <paramref name="box"/>.</summary>
    internal sealed class TextSpanContainerBuilder(DocumentNode box) : ITextSpanContainer
    {
        public ITextSpanContainer Alignment(TextAlignment alignment)
        {
            var value = alignment switch
            {
                TextAlignment.Left => "left",
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                TextAlignment.Start => "start",
                TextAlignment.End => "end",
                _ => "left"
            };
            box.SetTextAlign(value);
            return this;
        }

        public ITextSpan Span(string text)
        {
            var span = box.AppendBlock();
            span.Text = text;
            return new TextStyleApplier(span);
        }

        public void Element(Action<IContainer> content)
        {
            ArgumentNullException.ThrowIfNull(content);
            var element = box.AppendBlock();
            element.SetStyle("display", "inline-block");
            content(new ContainerBuilder(element));
        }

        public ITextSpan CurrentPageNumber() => CounterSpan("counter(page)");

        public ITextSpan TotalPages() => CounterSpan("counter(pages)");

        public ITextSpan PageNumberWithinSection(string sectionId) => SectionCounterSpan(sectionId, isTotal: false);

        public ITextSpan TotalPagesWithinSection(string sectionId) => SectionCounterSpan(sectionId, isTotal: true);

        /// <summary>
        /// A span whose text is a <c>content: counter(...)</c> value instead of a literal string -
        /// resolved per page by <c>RunningElementLayout.RefreshPageCounterContent</c>, which already runs
        /// generically over every box in a running (header/footer) subtree before each page's own layout
        /// pass, re-applying and re-parsing any <c>content</c> mentioning <c>page</c>/<c>pages</c>. No
        /// text is set here - unlike <see cref="Span"/>, this box's <c>CssBox.Text</c> starts (and,
        /// outside a running subtree, stays) null; that refresh is what ever gives it real text.
        /// </summary>
        private ITextSpan CounterSpan(string contentValue)
        {
            var span = box.AppendBlock();
            span.SetStyle("content", contentValue);
            return new TextStyleApplier(span);
        }

        /// <summary>
        /// A span resolved by <c>CssBox.SectionPageCounterSectionId</c> rather than a CSS
        /// <c>content</c> value - see that field's own doc comment for why an ordinary <c>counter()</c>
        /// value can't express this. Same "no text until the per-page refresh runs" shape as
        /// <see cref="CounterSpan"/>.
        /// </summary>
        private ITextSpan SectionCounterSpan(string sectionId, bool isTotal)
        {
            ArgumentNullException.ThrowIfNull(sectionId);
            var span = box.AppendBlock();
            span.SetSectionPageCounter(sectionId, isTotal);
            return new TextStyleApplier(span);
        }
    }
}
