using PeachDrawing.Text.Unicode;
using PeachPDF.CSS;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// The CSS <c>unicode-bidi</c>/<c>direction</c> integration into UAX#9 (CSS Writing Modes Level 3
    /// §5.2): maps a box/run's own resolved <c>unicode-bidi</c> value to the synthetic explicit push(es)
    /// (<see cref="EmbeddingSpan"/>) that stand in for the real Unicode control character(s)
    /// <c>unicode-bidi</c> is defined in terms of, over that box/run's own text range. Shared by every
    /// consumer that maps CSS onto <see cref="Bidi"/> - the HTML box tree
    /// (<c>CssBidiParagraphResolver</c>) and SVG text (<c>SvgRenderer</c>) alike - so the mapping table
    /// is written down exactly once.
    /// </summary>
    internal static class CssUnicodeBidiMapping
    {
        private static readonly IReadOnlyList<ExplicitPush> NoPushes = [];

        public static IReadOnlyList<ExplicitPush> MapToPushes(UnicodeMode mode, DirectionMode direction)
        {
            var rtl = direction == DirectionMode.Rtl;

            return mode switch
            {
                UnicodeMode.Embed => [rtl ? ExplicitPush.Rle : ExplicitPush.Lre],
                UnicodeMode.BidirectionalOverride => [rtl ? ExplicitPush.Rlo : ExplicitPush.Lro],
                UnicodeMode.Isolate => [rtl ? ExplicitPush.Rli : ExplicitPush.Lri],
                UnicodeMode.IsolateOverride =>
                [
                    rtl ? ExplicitPush.Rli : ExplicitPush.Lri,
                    rtl ? ExplicitPush.Rlo : ExplicitPush.Lro
                ],
                UnicodeMode.Plaintext => [ExplicitPush.Fsi],
                _ => NoPushes
            };
        }
    }
}
