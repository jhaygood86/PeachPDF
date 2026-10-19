using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// Tracks one <c>position: running(&lt;custom-ident&gt;)</c> box (css-gcpm-3), so a page margin
    /// box's <c>content: element(&lt;custom-ident&gt;)</c> can select which occupant of that name is
    /// "current" for a given page - the same first/start/last/first-except selection <see cref="NamedString"/>
    /// already implements for <c>string-set</c>/<c>string()</c>, applied to a whole box instead of a
    /// pre-evaluated string. <see cref="Box"/> is the live <see cref="CssBox"/> itself (not a captured
    /// value), because <c>element()</c> needs to re-lay it out for real against the margin box's own
    /// rect - see <c>RunningElementLayout</c>. <see cref="Y"/> is the document Y this box would have
    /// occupied had it not been removed from flow (its nearest preceding in-flow sibling's
    /// <c>ActualBottom</c>, or the parent's content-top when there is none) - the box itself never gets
    /// a real <c>Location</c>, since <see cref="CssBox.IsRunningPositioned"/> excludes it from placement
    /// entirely.
    /// </summary>
    internal sealed record RunningElement(
        string Name,
        CssBox Box,
        double Y = 0
    )
    {
        public double Y { get; set; } = Y;
    }
}
