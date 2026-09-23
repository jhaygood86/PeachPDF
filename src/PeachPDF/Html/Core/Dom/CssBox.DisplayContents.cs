using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// <c>display: contents</c> (<see href="https://www.w3.org/TR/css-display-3/#valdef-display-contents">CSS
    /// Display 3 §2.5</see>): the element generates no box of its own and is treated, for box generation and
    /// layout, as if it had been replaced by its contents. Only the box tree is affected - selector matching,
    /// inheritance and the element's semantics are not - so the removed box is kept as a <i>shell</i>: out of
    /// every <see cref="Boxes"/> list (layout never sees it), but reachable through
    /// <see cref="HtmlContainerInt.DisplayContentsShells"/> for everything that addresses the element rather
    /// than lays it out (id lookups, cross-references, <c>string-set</c>, the tagged-PDF structure tree, the
    /// canvas background of a <c>&lt;body&gt;</c>).
    /// </summary>
    internal partial class CssBox
    {
        /// <summary>
        /// Whether this box computed to <c>display: contents</c>. Set by <c>DomParser.CascadeApplyStyles</c>
        /// when it records the box, before its children are spliced into its parent.
        /// </summary>
        internal bool IsDisplayContentsShell { get; set; }

        /// <summary>
        /// The children this shell handed to its parent, at the moment they were spliced - non-null exactly
        /// once <see cref="LiftDisplayContentsChildren"/> has run. A snapshot, not a live list: the
        /// restructuring passes that run afterwards can drop a whitespace-only text box or wrap a child in
        /// an anonymous box, so a reader must tolerate an entry whose <see cref="ParentBox"/> is now null
        /// (no longer in the tree) and follow the rest as usual.
        /// </summary>
        internal List<CssBox>? DisplayContentsLiftedChildren { get; private set; }

        /// <summary>
        /// The <c>display: contents</c> elements this box was lifted out of, outermost first. Null for a box
        /// that never was. Consulted by the tagged-PDF builder, which re-opens each ancestor's structure
        /// element around the box's own.
        /// </summary>
        internal List<CssBox>? DisplayContentsAncestors { get; private set; }

        /// <summary>
        /// The element a lifted <c>::before</c>/<c>::after</c> was generated for, once its
        /// <see cref="ParentBox"/> is no longer that element. Null everywhere else - see
        /// <see cref="OriginatingElement"/>.
        /// </summary>
        internal CssBox? OriginatingBox { get; private set; }

        /// <summary>
        /// The element this box's <c>attr()</c>, <c>content()</c> and counter context belongs to: the
        /// originating element for a pseudo-element (which is its <see cref="ParentBox"/> unless it was
        /// lifted out of a <c>display: contents</c> parent), else the box itself.
        /// </summary>
        internal CssBox OriginatingElement => IsPseudoElement ? OriginatingBox ?? ParentBox ?? this : this;

        /// <summary>
        /// Points a shell whose parent was a synthetic root at the box that took that root's children
        /// (<c>IContainer.Html</c> grafts a fragment's top-level boxes onto a wrapper and discards the root), so
        /// its <see cref="HtmlContainer"/>, geometry fallback and counter anchor still reach the real tree.
        /// </summary>
        internal void RetargetShellParent(CssBox parent) => _parentBox = parent;

        /// <summary>
        /// What the element's box would contain: <see cref="DisplayContentsLiftedChildren"/> for a spliced
        /// shell, else <see cref="Boxes"/>.
        /// </summary>
        internal IReadOnlyList<CssBox> ContentChildren => DisplayContentsLiftedChildren ?? (IReadOnlyList<CssBox>)Boxes;

        /// <summary>
        /// Replaces this box in its parent's <see cref="Boxes"/>, at its own index, by its own children.
        /// The box keeps its <see cref="ParentBox"/> (so inheritance-shaped lookups such as
        /// <c>rem</c> resolution and <see cref="HtmlContainer"/> still see a chain) but is no longer listed
        /// by it.
        /// </summary>
        /// <remarks>
        /// Run innermost shell first: a nested shell is spliced into its still-attached outer shell, and
        /// the outer one then lifts it (already replaced by its own children) along with its other
        /// children, so each lifted box ends up with its ancestors outermost-first.
        /// </remarks>
        /// <returns>The lifted children, so the caller can re-normalize them against their new parent.</returns>
        internal IReadOnlyList<CssBox> LiftDisplayContentsChildren()
        {
            var parent = _parentBox;
            var index = parent?.Boxes.IndexOf(this) ?? -1;

            if (parent is null || index < 0)
            {
                DisplayContentsLiftedChildren = [];
                return [];
            }

            var lifted = Boxes.ToArray();
            DisplayContentsLiftedChildren = [.. lifted];
            Boxes.Clear();

            parent.Boxes.RemoveAt(index);
            parent.Boxes.InsertRange(index, lifted);

            foreach (var child in lifted)
            {
                child._parentBox = parent;
                (child.DisplayContentsAncestors ??= []).Insert(0, this);

                if (child.IsBeforePseudoElement || child.IsAfterPseudoElement)
                {
                    child.OriginatingBox ??= this;
                }
            }

            return lifted;
        }
    }
}
