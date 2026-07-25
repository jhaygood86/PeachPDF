using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Which side of the sheet a page falls on — the axis
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">CSS Fragmentation Level 3 §3.1</see>'s
    /// <c>left</c>/<c>right</c>/<c>recto</c>/<c>verso</c> forced breaks select against.
    /// </summary>
    internal enum PageSide
    {
        /// <summary>No side is required — any page will do.</summary>
        Any,

        /// <summary>A left (verso) page, i.e. one <c>@page :left</c> matches.</summary>
        Left,

        /// <summary>A right (recto) page, i.e. one <c>@page :right</c> matches.</summary>
        Right
    }

    /// <summary>
    /// Classifies a cascaded <c>break-before</c>/<c>break-after</c>/<c>break-inside</c> value, per
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">CSS Fragmentation Level 3 §3.1/§3.2</see>.
    /// </summary>
    /// <remarks>
    /// One home for every question layout asks about a break value. Before this existed the forced-break
    /// test lived in two places — <c>CssBox</c>'s own <c>private</c> copy and an open-coded duplicate in
    /// <see cref="DomUtils.GetPrecedingKeepWithNextRun"/> — so widening one without the other silently
    /// broke keep-with-next around whichever value had just been added.
    /// </remarks>
    internal static class BreakValues
    {
        /// <summary>
        /// Whether <paramref name="value"/> forces a <i>page</i> break: <c>page</c>, plus the four
        /// directional values, which force one or two of them (§3.1).
        /// </summary>
        /// <remarks>
        /// Deliberately narrow. <c>column</c> and <c>region</c> are forced break values too, but for
        /// fragmentation contexts a page break is not a substitute for — a column break must not
        /// paginate. <c>always</c> never reaches here: it is a legacy <c>page-break-*</c> value only, and
        /// <see cref="CssUtils"/> rewrites it to <c>page</c> on the way in.
        /// </remarks>
        internal static bool IsForcedPageBreak(string? value) =>
            value is CssConstants.Page or CssConstants.Left or CssConstants.Right
                  or CssConstants.Recto or CssConstants.Verso;

        /// <summary>
        /// The side <paramref name="value"/> demands on its own, or <see cref="PageSide.Any"/>.
        /// </summary>
        /// <remarks>
        /// PeachPDF's page progression is left-to-right, so <c>recto</c> is the right-hand page and
        /// <c>verso</c> the left-hand one.
        /// </remarks>
        private static PageSide SideOf(string? value) => value switch
        {
            CssConstants.Left or CssConstants.Verso => PageSide.Left,
            CssConstants.Right or CssConstants.Recto => PageSide.Right,
            _ => PageSide.Any
        };

        /// <summary>
        /// The side required at the break point between a box (<paramref name="breakBefore"/>) and its
        /// preceding sibling (<paramref name="previousBreakAfter"/>).
        /// </summary>
        /// <remarks>
        /// A directional value beats a plain <c>page</c> on the other side of the pair, since §3.1 asks
        /// that all specified breaking requirements be honored and a directional break satisfies both.
        /// Two <i>conflicting</i> directional values are unsatisfiable; the later box's own
        /// <c>break-before</c> wins, pending the full combination and propagation rules.
        /// </remarks>
        internal static PageSide RequiredSide(string? breakBefore, string? previousBreakAfter) =>
            SideOf(breakBefore) is var own && own is not PageSide.Any ? own : SideOf(previousBreakAfter);

        /// <summary>
        /// Whether <paramref name="value"/> forbids a break in the <i>page</i> fragmentation context
        /// (§3.1 for <c>break-before</c>/<c>break-after</c>, §3.2 for <c>break-inside</c>).
        /// </summary>
        /// <remarks>
        /// <c>avoid</c> forbids a break in every context, so it covers this one; <c>avoid-page</c> names
        /// it. <c>avoid-column</c> and <c>avoid-region</c> name other contexts and must <b>not</b>
        /// suppress a page break — a hint about column breaks silently changing pagination is exactly
        /// the defect this predicate exists to prevent.
        /// </remarks>
        internal static bool AvoidsPageBreak(string? value) =>
            value is CssConstants.Avoid or CssConstants.AvoidPage;

        /// <summary>
        /// Whether pagination slot <paramref name="slotIndex"/> prints on <paramref name="side"/>.
        /// </summary>
        /// <remarks>
        /// Slot <c>k</c> is page <c>k + 1</c> — the same mapping <c>PageGeometryTable.Compute</c> uses to
        /// resolve <c>@page :left</c>/<c>:right</c> — and the parity itself comes from
        /// <see cref="PageRuleResolver.IsRightPage"/>, so a directional break and the page selectors it
        /// is named after cannot disagree about which pages are which.
        /// </remarks>
        internal static bool SlotIsOn(int slotIndex, PageSide side) => side switch
        {
            PageSide.Left => !PageRuleResolver.IsRightPage(slotIndex + 1),
            PageSide.Right => PageRuleResolver.IsRightPage(slotIndex + 1),
            _ => true
        };
    }
}
