using System;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The value side of a keyword-or-value length property whose grammar accepts <c>calc()</c> (e.g.
    /// <c>word-spacing</c>/<c>letter-spacing</c>/<c>row-gap</c>/<c>column-gap</c>'s
    /// <c>normal | &lt;length&gt;</c>): either resolved to a concrete <see cref="Length"/> at cascade time
    /// (the common case - an absolute-only <c>calc()</c> has already folded to a literal length by Layer
    /// A's <c>CalcSerializer</c> before this is ever constructed), or - when the authored text is a
    /// <c>calc()</c> mixing relative units (<c>em</c>/<c>rem</c>/<c>%</c>/<c>cq*</c>) that can't fold
    /// without layout context - the <c>calc()</c>'s own text, evaluated lazily by
    /// <c>CssValueParser.ParseLength</c> at consumption time (where a real <c>CssBox</c> supplies that
    /// context). Mirrors <c>GridTrackSize</c>'s identical "keep calc's reconstructed text, Layer B
    /// evaluates it" pattern for grid track sizes (see <c>GridTrackListGrammar</c>'s
    /// <c>IsLengthPercentageCalc</c> handling) - the same trick, ported to this union's value side.
    /// <para>
    /// The "exactly one is set" invariant is enforced by the constructor, mirroring
    /// <see cref="CssKeywordOrValue{TEnum,TValue}"/> - <c>default(LengthOrCalc)</c> (both null) bypasses it
    /// the same way, but the only construction site, <c>CssValueParser.TryParseLengthOrCalc</c>
    /// (namespace <c>PeachPDF.Html.Core.Parse</c>), always goes through this constructor.
    /// </para>
    /// </summary>
    internal readonly record struct LengthOrCalc
    {
        public LengthOrCalc(Length? length, string? calcText)
        {
            if (length is null == calcText is null)
                throw new ArgumentException("Exactly one of length or calcText must be provided.");

            Length = length;
            CalcText = calcText;
        }

        public Length? Length { get; init; }
        public string? CalcText { get; init; }

        public bool IsCalc => CalcText is not null;
    }
}
