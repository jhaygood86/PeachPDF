using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// One counter as a box sees it. <paramref name="ScopeParent"/> is set only for a <c>list-item</c> counter
    /// created by a box that a <c>display: contents</c> element lifted out of its element parent: the box tree
    /// no longer says where that counter's scope ends, so the element it is confined to is recorded here (CSS
    /// Lists 3 §4.4.1 - the counter is in scope for its instantiating element, that element's following
    /// siblings and all of their descendants, i.e. for everything inside the element's parent, which is what
    /// <paramref name="ScopeParent"/> names). Null for every other counter, whose scope the box tree already
    /// expresses.
    /// </summary>
    internal record CssCounter(
        string Name,
        int Value,
        bool IsReversed,
        bool IsNewScope,
        CssCounter? ParentScope,
        CssBox? ScopeParent = null
    );
}
