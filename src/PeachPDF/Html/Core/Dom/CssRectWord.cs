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

using PeachPDF.Text.Shaping.Arabic;
using PeachPDF.Text.Shaping.Use;

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
        /// This word's own resolved Arabic-family <see cref="ArabicJoiningForm"/> per codepoint of
        /// <see cref="PreMirrorText"/> (true logical order, never mutated - see
        /// <see cref="_preMirrorText"/>'s own remarks for why a stable source matters across repeated
        /// layout passes), one entry per <see cref="System.Text.Rune"/> rather than per UTF-16 char -
        /// null for a word with no Arabic-family joining codepoint in it at all (the overwhelming common
        /// case - see <see cref="CssBox.JoiningForms"/>, this word's own slice of it). Set once at
        /// construction, never mutated, and - unlike a plain RTL word's own text - never reversed either:
        /// see <see cref="DisplayOrderReversed"/> for why a joining word always shapes in this same true
        /// logical order regardless of its own display direction.
        /// </summary>
        private readonly ArabicJoiningForm[]? _logicalJoiningForms;

        /// <summary>
        /// This word's own resolved <see cref="UseCategory"/> per codepoint of <see cref="Text"/> (one
        /// entry per <see cref="System.Text.Rune"/>, matching <see cref="_logicalJoiningForms"/>'s own
        /// indexing convention) - null for a word with no codepoint in a USE-shaped script (Devanagari/
        /// Bengali/Gujarati/Tamil) in it at all (the overwhelming common case - see
        /// <see cref="CssBox.UseCategories"/>, this word's own slice of it). Set once at construction,
        /// never mutated. Unlike <see cref="_logicalJoiningForms"/>, none of these four scripts are ever
        /// reversed for display, so there is no analogous <see cref="DisplayOrderReversed"/> concern for
        /// this field - <c>GsubShaper</c>'s own USE stage still needs true logical order to resolve
        /// syllable/conjunct structure, but the resulting glyph list is never reversed afterward the way
        /// an Arabic-family joining word's is (only locally reordered within each syllable - see
        /// <c>PeachPDF.Text.Shaping.Use.UseReorderer</c>).
        /// </summary>
        private readonly UseCategory[]? _logicalUseCategories;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="owner">the CSS box owner of the word</param>
        /// <param name="text">the word chars </param>
        /// <param name="hasSpaceBefore">was there a whitespace before the word chars (before trim)</param>
        /// <param name="hasSpaceAfter">was there a whitespace after the word chars (before trim)</param>
        /// <param name="originalText">the pre-text-transform source text (see <see cref="CssRect.OriginalText"/>), if different from <paramref name="text"/></param>
        /// <param name="joiningForms">this word's own resolved joining forms in true logical order (see <see cref="_logicalJoiningForms"/>), if any</param>
        /// <param name="useCategories">this word's own resolved USE categories (see <see cref="_logicalUseCategories"/>), if any</param>
        public CssRectWord(CssBox owner, string text, bool hasSpaceBefore, bool hasSpaceAfter, string? originalText = null,
            ArabicJoiningForm[]? joiningForms = null, UseCategory[]? useCategories = null)
            : base(owner)
        {
            _text = text;
            _preMirrorText = text;
            HasSpaceBefore = hasSpaceBefore;
            HasSpaceAfter = hasSpaceAfter;
            OriginalText = originalText ?? text;
            _logicalJoiningForms = joiningForms;
            _logicalUseCategories = useCategories;
        }

        /// <summary>This word's own resolved OpenType script tag (<c>OpenTypeScriptTags</c>), or null
        /// when unresolved (a script absent from that curated table, or a word whose script resolved to
        /// a script-neutral value). Unlike <see cref="_logicalJoiningForms"/>, set post-construction by
        /// <c>CssBox.AppendWordsFromText</c>'s own script-boundary word split - the same
        /// settable-property pattern <see cref="CssRect.BidiLevel"/> already uses, since a single tag
        /// applies uniformly across every fragment <c>AddWord</c> may split this word into (small-caps
        /// case-runs, per-codepoint font fragments) the same way one <c>BidiLevel</c> does - the
        /// script-boundary split guarantees script-homogeneity across those fragments the same way the
        /// existing bidi-level-boundary split already guarantees level-homogeneity. Feeds
        /// <see cref="DerivedStyle.ActualTextShapingFeatures"/>'s per-word GSUB/GPOS script selection.</summary>
        internal string? ScriptTag { get; set; }

        /// <summary>This word's resolved joining forms, in true logical order (see
        /// <see cref="_logicalJoiningForms"/>) - null for a word with no Arabic-family joining codepoint
        /// in it. Unlike a plain RTL word's <see cref="Text"/>, this never reverses: <c>GsubShaper</c>
        /// needs the forms in the same true logical adjacency it shapes <see cref="Text"/> in (see
        /// <see cref="DisplayOrderReversed"/>).</summary>
        internal ArabicJoiningForm[]? EffectiveJoiningForms => _logicalJoiningForms;

        /// <summary>This word's resolved USE categories (see <see cref="_logicalUseCategories"/>) -
        /// null for a word with no codepoint in a USE-shaped script (Devanagari/Bengali/Gujarati/Tamil)
        /// in it.</summary>
        internal UseCategory[]? EffectiveUseCategories => _logicalUseCategories;

        /// <summary>
        /// Whether this word currently reads right-to-left on the page - set by
        /// <c>CssLayoutEngine.MirrorWordTextIfNeeded</c> once bidi placement resolves it, in place of
        /// that same method's ordinary <see cref="ReplaceText"/> character-level reversal/mirroring.
        /// An Arabic-family joining word (<see cref="EffectiveJoiningForms"/> non-null) never gets that
        /// treatment - <see cref="Text"/> stays true logical order permanently, because a real font's own
        /// contextual <c>rlig</c> rules (e.g. Arabic lam-alef) are keyed on true logical adjacency and
        /// silently stop matching once the text they'd apply to has been reversed. Instead, whoever
        /// shapes this word for display (paint, outline extraction, ToUnicode text extraction - all funnel
        /// through <c>CssBox.ResolveWordShapingFeatures</c>) requests
        /// <c>TextShapingFeatures.ReverseForDisplay</c>, so GSUB/GPOS still run in the logical
        /// order they need and only the resulting glyph list - never the source string - reverses, right
        /// before painting. A no-op read for every other word (plain RTL words keep the older
        /// text-level mirroring path unchanged).
        /// </summary>
        internal bool DisplayOrderReversed { get; private set; }

        /// <summary>Marks this word as currently reading right-to-left - see
        /// <see cref="DisplayOrderReversed"/>. Idempotent, and (like <see cref="ReplaceText"/>'s own
        /// mirroring) never reset back to false: a word's own bidi embedding level is resolved from the
        /// full paragraph text and does not change across a page-width reflow's repeated layout passes,
        /// so once a word is known to display right-to-left it stays that way for the object's lifetime.
        /// </summary>
        internal void MarkDisplayOrderReversed() => DisplayOrderReversed = true;

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