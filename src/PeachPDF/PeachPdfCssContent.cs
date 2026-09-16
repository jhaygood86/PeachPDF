using System;
using System.Threading.Tasks;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF
{
    /// <summary>
    /// An opaque, pre-parsed stylesheet produced by <see cref="PdfGenerator.ParseStyleSheet(string, bool)"/>. Pass an instance
    /// to a <c>GeneratePdf</c>/<c>AddPdfPages</c> overload's <c>cssData</c> parameter to reuse the same parsed CSS
    /// across multiple renders instead of re-parsing identical stylesheet text on every call.
    /// </summary>
    public class PeachPdfCssContent
    {
        private readonly CssData _cssData;
        private readonly RAdapter _adapter;

        internal PeachPdfCssContent(CssData cssData, RAdapter adapter)
        {
            _cssData = cssData;
            _adapter = adapter;
        }

        internal CssData CssData => _cssData;

        /// <summary>
        /// Parses an additional stylesheet and merges its rules into this content's underlying style
        /// data. Sheets added later take precedence over earlier ones and over the base sheet this
        /// content was created from (matching the CSS cascade's source order), so this can be called
        /// repeatedly to layer multiple user stylesheets on top of one another.
        /// </summary>
        /// <remarks>
        /// <c>@import</c> rules inside <paramref name="stylesheet"/> are not resolved (there is no
        /// document context here), the same limitation as <see cref="PdfGenerator.ParseStyleSheet(string, bool)"/>.
        /// </remarks>
        /// <param name="stylesheet">the stylesheet source to parse and add</param>
        public async Task AddStyleSheet(string stylesheet)
        {
            if (string.IsNullOrEmpty(stylesheet))
            {
                return;
            }

            var parser = new CssParser(_adapter, null);
            await parser.ParseStyleSheet(_cssData, stylesheet);

            // The selector index is built lazily and cached; adding a stylesheet after it has been
            // queried would otherwise leave the new rules invisible to matching.
            _cssData.InvalidateIndex();
        }

        /// <summary>
        /// Genuinely zero-copy counterpart of <see cref="AddStyleSheet(string)"/> - see
        /// <see cref="CSS.StylesheetParser.Parse(ReadOnlyMemory{char})"/>'s own remarks for why
        /// <see cref="ReadOnlyMemory{T}"/>, not <see cref="ReadOnlySpan{T}"/> (a ref struct that cannot cross
        /// this method's own <see langword="await"/>), is what makes this possible.
        /// </summary>
        /// <param name="stylesheet">the stylesheet source to parse and add</param>
        public async Task AddStyleSheet(ReadOnlyMemory<char> stylesheet)
        {
            if (stylesheet.IsEmpty)
            {
                return;
            }

            var parser = new CssParser(_adapter, null);
            await parser.ParseStyleSheet(_cssData, stylesheet);

            _cssData.InvalidateIndex();
        }
    }
}
