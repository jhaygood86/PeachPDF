using PeachPDF.Html.Core.Animation;

namespace PeachPDF.Html.Core.Dom
{
    internal partial class CssBox
    {
        /// <summary>
        /// The animations this box names, for an element of an inline <c>&lt;svg&gt;</c> (null when it names none in play). The elements of an SVG
        /// are not laid out, so no animated declaration is written onto the box; <c>SvgTreeBuilder</c> asks for each property it reads. Planned
        /// on first use, once the cascade has computed the box's <c>animation-*</c> properties.
        /// </summary>
        internal AnimationApplier.ElementPlan? GetSvgAnimationPlan()
        {
            // Nearly every document has no @keyframes and nearly every element names no animation: answer that before the box grows any state.
            if (HtmlContainer is not { Keyframes.Count: > 0 } || AnimationApplier.IsNone(AnimationName)) return null;

            var rare = _rare ??= new BoxRareState();

            if (!rare.SvgAnimationPlanBuilt)
            {
                rare.SvgAnimationPlan = AnimationApplier.PlanElement(this);
                rare.SvgAnimationPlanBuilt = true;
            }

            return rare.SvgAnimationPlan;
        }
    }
}
