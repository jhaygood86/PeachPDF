using PeachPDF.CSS;
using System.Collections.Generic;

namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The CSS <c>unicode-bidi</c>/<c>direction</c> integration into UAX#9 (CSS Writing Modes Level 3
    /// §5.2): maps a box/run's own resolved <c>unicode-bidi</c> value to the synthetic explicit push(es)
    /// (<see cref="BidiIsolateOverride"/>) that stand in for the real Unicode control character(s)
    /// <c>unicode-bidi</c> is defined in terms of, over that box/run's own text range. Shared by every
    /// consumer that maps CSS onto <see cref="BidiResolver"/> - the HTML box tree
    /// (<c>CssBidiParagraphResolver</c>) and SVG text (<c>SvgRenderer</c>) alike - so the mapping table
    /// is written down exactly once.
    /// </summary>
    internal static class CssUnicodeBidiMapping
    {
        private static readonly IReadOnlyList<BidiExplicitPush> NoPushes = [];

        public static IReadOnlyList<BidiExplicitPush> MapToPushes(UnicodeMode mode, DirectionMode direction)
        {
            var rtl = direction == DirectionMode.Rtl;

            return mode switch
            {
                UnicodeMode.Embed => [rtl ? BidiExplicitPush.Rle : BidiExplicitPush.Lre],
                UnicodeMode.BidirectionalOverride => [rtl ? BidiExplicitPush.Rlo : BidiExplicitPush.Lro],
                UnicodeMode.Isolate => [rtl ? BidiExplicitPush.Rli : BidiExplicitPush.Lri],
                UnicodeMode.IsolateOverride =>
                [
                    rtl ? BidiExplicitPush.Rli : BidiExplicitPush.Lri,
                    rtl ? BidiExplicitPush.Rlo : BidiExplicitPush.Lro
                ],
                UnicodeMode.Plaintext => [BidiExplicitPush.Fsi],
                _ => NoPushes
            };
        }
    }
}
