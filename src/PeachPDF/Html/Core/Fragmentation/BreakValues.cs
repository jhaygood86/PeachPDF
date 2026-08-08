using PeachPDF.CSS;
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
    /// Which kind of fragmentainer a break question is being asked about, per
    /// <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>'s "a column in multi-column
    /// layout, or a page in paged media".
    /// </summary>
    /// <remarks>
    /// Every break value names the context it speaks for — <c>column</c> and <c>avoid-column</c> say
    /// nothing about pages, and <c>avoid-page</c> says nothing about columns — so a value cannot be
    /// classified without knowing which fragmentainer is being filled. There is no <c>Region</c> member
    /// because PeachPDF establishes no region context, which is what makes <c>region</c>/<c>avoid-region</c>
    /// inert by construction rather than by omission.
    /// </remarks>
    internal enum FragmentationContext
    {
        /// <summary>A page in paged media — the document's own fragmentation context.</summary>
        Page,

        /// <summary>A column of a multi-column container.</summary>
        Column
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
            value is Keywords.Page or Keywords.Left or Keywords.Right
                  or Keywords.Recto or Keywords.Verso;

        /// <summary>
        /// Whether <paramref name="value"/> forces a break in <paramref name="context"/> (§3.1).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A page break is also a column break — a column cannot span pages — so every value
        /// <see cref="IsForcedPageBreak"/> accepts forces one in either context. The reverse does not
        /// hold: <c>column</c> forces a break only where a column fragmentation context is actually being
        /// filled, and is otherwise ignored, since there is no column boundary for it to name.
        /// </para>
        /// <para>
        /// <c>region</c> is forced in no context PeachPDF establishes, so it stays inert here rather than
        /// being special-cased away.
        /// </para>
        /// </remarks>
        internal static bool IsForcedBreak(string? value, FragmentationContext context) =>
            IsForcedPageBreak(value)
            || (context is FragmentationContext.Column && value is Keywords.Column);

        /// <summary>
        /// The side <paramref name="value"/> demands on its own, or <see cref="PageSide.Any"/>.
        /// </summary>
        /// <remarks>
        /// PeachPDF's page progression is left-to-right, so <c>recto</c> is the right-hand page and
        /// <c>verso</c> the left-hand one.
        /// </remarks>
        internal static PageSide SideOf(string? value) => value switch
        {
            Keywords.Left or Keywords.Verso => PageSide.Left,
            Keywords.Right or Keywords.Recto => PageSide.Right,
            _ => PageSide.Any
        };

        /// <summary>
        /// The side required at the break point between a box (<paramref name="breakBefore"/>) and its
        /// preceding sibling (<paramref name="previousBreakAfter"/>).
        /// </summary>
        /// <remarks>
        /// A directional value beats a plain <c>page</c> on the other side of the pair, since §3.1 asks
        /// that all specified breaking requirements be honored and a directional break satisfies both.
        /// Two <i>conflicting</i> directional values are unsatisfiable, and §3.1 says which one to keep:
        /// the value on the latest element in flow, which at this break point is this box's own
        /// <c>break-before</c> rather than the preceding sibling's <c>break-after</c>.
        /// </remarks>
        /// <seealso cref="BreakPropagation.ForcedBreakBeforeAt"/>
        internal static PageSide RequiredSide(string? breakBefore, string? previousBreakAfter) =>
            SideOf(breakBefore) is var own && own is not PageSide.Any ? own : SideOf(previousBreakAfter);

        /// <summary>
        /// Whether <paramref name="value"/> forbids a break in <paramref name="context"/>
        /// (§3.1 for <c>break-before</c>/<c>break-after</c>, §3.2 for <c>break-inside</c>).
        /// </summary>
        /// <remarks>
        /// <c>avoid</c> forbids a break in every context, so it covers both; the targeted values cover
        /// exactly the one they name. Asking this of the wrong context is the defect the predicate exists
        /// to prevent: a hint about <i>column</i> breaks must never suppress a page break, which is what
        /// made <c>avoid-column</c> deliberately inert for as long as the only fragmentainer modelled was
        /// the page. <c>avoid-region</c> names a context PeachPDF does not establish, so it forbids
        /// nothing here.
        /// </remarks>
        internal static bool AvoidsBreak(string? value, FragmentationContext context) => context switch
        {
            FragmentationContext.Column => value is Keywords.Avoid or Keywords.AvoidColumn,
            _ => value is Keywords.Avoid or Keywords.AvoidPage
        };

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
