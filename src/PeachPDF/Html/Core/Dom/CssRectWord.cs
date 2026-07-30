// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a word inside an inline box
    /// </summary>
    internal sealed class CssRectWord : CssRect
    {
        private string _text;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="owner">the CSS box owner of the word</param>
        /// <param name="text">the word chars </param>
        /// <param name="hasSpaceBefore">was there a whitespace before the word chars (before trim)</param>
        /// <param name="hasSpaceAfter">was there a whitespace after the word chars (before trim)</param>
        /// <param name="originalText">the pre-text-transform source text (see <see cref="CssRect.OriginalText"/>), if different from <paramref name="text"/></param>
        public CssRectWord(CssBox owner, string text, bool hasSpaceBefore, bool hasSpaceAfter, string? originalText = null)
            : base(owner)
        {
            _text = text;
            HasSpaceBefore = hasSpaceBefore;
            HasSpaceAfter = hasSpaceAfter;
            OriginalText = originalText ?? text;
        }

        /// <summary>
        /// was there a whitespace before the word chars (before trim)
        /// </summary>
        public override bool HasSpaceBefore { get; }

        /// <summary>
        /// was there a whitespace after the word chars (before trim)
        /// </summary>
        public override bool HasSpaceAfter { get; }

        /// <summary>
        /// Gets a bool indicating if this word is composed only by spaces.
        /// Spaces include tabs and line breaks
        /// </summary>
        public override bool IsSpaces
        {
            get
            {
                foreach (var c in Text)
                {
                    if (!char.IsWhiteSpace(c))
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Gets if the word is composed by only a line break
        /// </summary>
        public override bool IsLineBreak => Text == "\n";

        /// <summary>
        /// Gets the text of the word
        /// </summary>
        public override string Text => _text;

        /// <summary>
        /// Rewrites this word's text in place - used by <c>CssLayoutEngine</c>'s per-line bidi reordering
        /// step to apply L2 character reversal + L4 mirroring (<c>BidiMirrorResolver.ApplyMirroring</c>)
        /// to an RTL word once its final visual position is known. A plain method rather than a settable
        /// <see cref="Text"/> property, since every other <see cref="CssRect"/> subclass's <c>Text</c> is
        /// never meant to be writable at all (the base declares it nullable and get-only for exactly
        /// that reason - most subclasses, e.g. an image, never carry text).
        /// </summary>
        internal void ReplaceText(string text) => _text = text;

        /// <summary>
        /// Represents this word for debugging purposes
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{Text.Replace(' ', '-').Replace("\n", "\\n")} ({Text.Length} char{(Text.Length != 1 ? "s" : string.Empty)})";
        }
    }
}