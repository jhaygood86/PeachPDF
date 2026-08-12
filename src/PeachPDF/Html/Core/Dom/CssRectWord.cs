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
        /// This word's text as constructed - after <see cref="CssBox.TextTransform"/> but before any bidi
        /// L4 mirroring - kept stable so <see cref="ReplaceText"/> always has an unmirrored value to
        /// mirror from. A box tree can be laid out more than once against the same word objects (e.g.
        /// <c>HtmlContainerInt</c>'s variable-page-width reflow re-runs <c>LayoutDocument</c>, and each
        /// pass re-derives line boxes and re-applies bidi reordering) - mirroring is an involution, so
        /// applying it to its own (already-mirrored) previous output on a second pass would silently
        /// restore the pre-mirror text instead of leaving it mirrored.
        /// </summary>
        private readonly string _preMirrorText;

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
            _preMirrorText = text;
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
        /// This word's stable, unmirrored text - what <c>PeachPDF.Text.Bidi.BidiMirrorResolver.ApplyMirroring</c>
        /// should always mirror <i>from</i>, regardless of how many times layout has already mirrored
        /// this word via <see cref="ReplaceText"/> (see <see cref="_preMirrorText"/>).
        /// </summary>
        internal string PreMirrorText => _preMirrorText;

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
        /// Set only on a hyphenation-created prefix (<c>CssLayoutEngine.TryHyphenateWord</c>): the
        /// single word it was split from, still carrying that word's own
        /// <see cref="CssRect.HyphenationCandidates"/>. Lets a discarded fragmentainer line's split be
        /// undone by restoring this in place of the prefix/suffix pair - see
        /// <see cref="HyphenationSuffix"/> and issue #344.
        /// </summary>
        internal CssRect? PreSplitWord { get; set; }

        /// <summary>
        /// Set only on a hyphenation-created prefix: the suffix <c>TryHyphenateWord</c> created
        /// alongside it. A resumed fragmentainer pass re-walks the same word list a split mutated in
        /// place (<c>CssBox.Words</c>), so a split made against one pass's remaining line width would
        /// otherwise survive into the next pass's fresh, undivided width unchanged - re-flowed as two
        /// words with a hyphen neither the original word nor a fresh hyphenation decision would have
        /// produced. <c>CssLayoutEngine.CreateLineBoxes</c> uses this (together with
        /// <see cref="PreSplitWord"/>) to merge the pair back into one word whenever the line the split
        /// was made for is itself discarded at a break, so the resumed pass decides afresh instead of
        /// re-flowing a stale split.
        /// </summary>
        internal CssRectWord? HyphenationSuffix { get; set; }

        /// <summary>
        /// Set only on a hyphenation-created suffix: the prefix <c>TryHyphenateWord</c> created it
        /// alongside - the mirror image of <see cref="HyphenationSuffix"/>/<see cref="PreSplitWord"/>.
        /// Lets <c>hyphenate-limit-last</c> (CSS Text 4 §6.3.5) recognize, from the word that opens a
        /// fragmentainer's discarded line, that the line just kept before it ends in a hyphen that may
        /// have to be taken back.
        /// </summary>
        internal CssRectWord? HyphenationPrefix { get; set; }

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