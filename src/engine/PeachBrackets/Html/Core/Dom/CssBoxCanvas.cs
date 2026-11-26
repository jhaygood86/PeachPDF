using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System.Globalization;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS box for a <c>canvas</c> element: a replaced element as large as its bitmap (the <c>width</c> and <c>height</c> attributes, 300x150 CSS px
    /// by default) unless the style says otherwise. What it shows is what a script drew, kept on the container (see <c>ScriptCanvasPainting</c>).
    /// </summary>
    internal sealed class CssBoxCanvas : CssBox
    {
        private readonly CssRectImage _contentWord;

        public CssBoxCanvas(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _contentWord = new CssRectImage(this);
            Words.Add(_contentWord);
        }

        /// <summary>The phantom word that carries the box's replaced content, whose rectangle the drawing is placed in.</summary>
        internal CssRectImage ContentWord => _contentWord;

        /// <summary>What a script drew on this canvas, or null.</summary>
        internal ScriptHosting.ScriptCanvasPainting? Painting =>
            HtmlTag is { SourceOrdinal: >= 0 } tag && HtmlContainer is { } container && container.CanvasPaintings.TryGetValue(tag.SourceOrdinal, out var painting) ? painting : null;

        internal override ValueTask MeasureWordsSize(Canvas g)
        {
            if (!_wordsSizeMeasured)
            {
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            var width = Painting?.Width ?? Attribute("width", CssLayoutEngine.DefaultObjectWidth);
            var height = Painting?.Height ?? Attribute("height", CssLayoutEngine.DefaultObjectHeight);
            CssLayoutEngine.MeasureIntrinsicSize(_contentWord, width, height, hasIntrinsicRatio: width > 0 && height > 0);
            return ValueTask.CompletedTask;
        }

        // A non-negative whole number of CSS pixels read by the HTML rules (leading digits, junk ignored), or the default when there is none or it is too large.
        private double Attribute(string name, double fallback) =>
            Utils.HtmlIntegerParser.TryParseNonNegative(HtmlTag?.TryGetAttribute(name), out var value) && value <= int.MaxValue ? value : fallback;
    }
}
