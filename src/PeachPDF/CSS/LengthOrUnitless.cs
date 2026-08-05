using System;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The value side of <c>line-height</c>'s <c>normal | &lt;number&gt; | &lt;length-percentage&gt;</c>
    /// grammar (CSS Inline Layout 3 §4) once the keyword (<c>normal</c>) side is factored out by the outer
    /// <see cref="CssKeywordOrValue{TEnum,TValue}"/>: either a <see cref="CSS.LengthOrCalc"/> (the
    /// <c>&lt;length-percentage&gt;</c> case, itself already a union of a resolved <see cref="Length"/> and
    /// a deferred relative-unit <c>calc()</c> — see its own doc comment), or a bare unitless multiplier
    /// (e.g. <c>line-height: 1.5</c>), which CSS2.1 §10.8.1 defines as the element's own font size scaled
    /// by that number — recomputed per box, not resolved at cascade time, so no separate deferred-calc case
    /// is needed on this side: an absolute-only <c>calc()</c> number expression has already folded to a
    /// literal numeric string by Layer A's <c>CalcSerializer</c> (<c>CalcCategory.Number</c> always folds,
    /// unlike <c>CalcCategory.LengthPercentage</c>) before this type is ever constructed.
    /// <para>
    /// The "exactly one is set" invariant is enforced by the constructor, mirroring
    /// <see cref="LengthOrCalc"/>/<see cref="CssKeywordOrValue{TEnum,TValue}"/> — <c>default(LengthOrUnitless)</c>
    /// (both null) bypasses it the same way, but the only construction site,
    /// <c>CssValueParser.TryParseLengthOrUnitless</c> (namespace <c>PeachPDF.Html.Core.Parse</c>), always
    /// goes through this constructor.
    /// </para>
    /// </summary>
    internal readonly record struct LengthOrUnitless
    {
        public LengthOrUnitless(LengthOrCalc? lengthOrCalc, double? unitless)
        {
            if (lengthOrCalc is null == unitless is null)
                throw new ArgumentException("Exactly one of lengthOrCalc or unitless must be provided.");

            LengthOrCalc = lengthOrCalc;
            Unitless = unitless;
        }

        public LengthOrCalc? LengthOrCalc { get; init; }
        public double? Unitless { get; init; }

        public bool IsUnitless => Unitless is not null;
    }
}
