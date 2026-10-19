using System;
using System.Threading.Tasks;
using PeachBrackets.Building;

namespace PeachPDF
{
    /// <summary>
    /// An opaque, pre-parsed stylesheet produced by <see cref="PdfGenerator.ParseStyleSheet(string, bool)"/>. Pass an instance
    /// to a <c>GeneratePdf</c>/<c>AddPdfPages</c> overload's <c>cssData</c> parameter to reuse the same parsed CSS
    /// across multiple renders instead of re-parsing identical stylesheet text on every call.
    /// </summary>
    public class PeachPdfCssContent
    {
        internal PeachPdfCssContent(StyleSheet styleSheet)
        {
            StyleSheet = styleSheet;
        }

        internal StyleSheet StyleSheet { get; }

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
        public Task AddStyleSheet(string stylesheet) => StyleSheet.AddAsync(stylesheet);

        /// <summary>
        /// Genuinely zero-copy counterpart of <see cref="AddStyleSheet(string)"/> - see
        /// the stylesheet parser's own remarks for why
        /// <see cref="ReadOnlyMemory{T}"/>, not <see cref="ReadOnlySpan{T}"/> (a ref struct that cannot cross
        /// this method's own <see langword="await"/>), is what makes this possible.
        /// </summary>
        /// <param name="stylesheet">the stylesheet source to parse and add</param>
        public Task AddStyleSheet(ReadOnlyMemory<char> stylesheet) => StyleSheet.AddAsync(stylesheet);
    }
}
