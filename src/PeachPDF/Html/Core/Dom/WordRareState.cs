using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>The per-word state only an unusual word has (hyphenation, overflow-wrap pieces, scaled or per-codepoint fonts, first-line variants, script tags). See <see cref="CssRect"/>.</summary>
    internal sealed class WordRareState
    {
        internal CssBox? FirstLineStyle;
        internal double? OverflowWrapMinWidth;
        internal string? FirstLineText;
        internal double FontSizeScale = 1.0;
        internal ScaledFontKind ScaledFontKind = ScaledFontKind.None;
        internal bool UsesPerCodepointFont;
        internal bool IsUprightOrientation;
        internal bool SuppressWrapBefore = false;
        internal IReadOnlyList<int>? HyphenationCandidates;
        internal CssRect? PreSplitWord;
        internal CssRectWord? HyphenationSuffix;
        internal CssRectWord? HyphenationPrefix;
        internal CssRectWord? PreOverflowWrapWord;
        internal CssRectWord? OverflowWrapSuffix;
        internal CssRectWord? OverflowWrapPrefix;
    }
}
