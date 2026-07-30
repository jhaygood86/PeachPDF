using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The set of CSS properties cascaded onto a <see cref="CssBox"/> from any source (author/UA
    /// stylesheets, inline <c>style=""</c>, translated presentational HTML attributes) - the "specified
    /// value" half of what <c>CssBoxProperties</c> used to hold. Values calculated FROM these (the
    /// <c>Actual*</c> family) live on <see cref="DerivedStyle"/> instead; layout-engine output
    /// (<c>Location</c>/<c>Size</c>/etc.) and non-cascaded box state (<c>UsedPageName</c>,
    /// <c>SubgridContext</c>) stay directly on <see cref="CssBox"/>.
    /// <para>
    /// A thin container over 17 "area" records (<see cref="BorderArea"/>, <see cref="FontArea"/>, etc.,
    /// declared in <c>ComputedStyleAreas.cs</c>), each grouping the properties of roughly one CSS module.
    /// Splitting storage this way means: (1) changing one property only clones the small area it belongs
    /// to, not all ~130 properties; and (2) <see cref="CssBox.InheritStyle"/> can adopt a whole area
    /// instance directly by reference from the parent when every property in it is unchanged anywhere in
    /// the ancestor chain - no clone at all, since the copy-on-write discipline guarantees a parent's area
    /// instance is never mutated after being handed down. A subtree that never touches, say, any font
    /// property ends up with every box's <see cref="Font"/> literally <c>ReferenceEquals</c> the same
    /// object.
    /// </para>
    /// <para>
    /// Every property on <see cref="ComputedStyle"/> itself and on every area is <c>{ get; init; }</c> -
    /// there is no public way to mutate an existing instance. <see cref="Default"/> (and each area's own
    /// <c>Default</c>) is therefore safe to share across every <see cref="CssBox"/> that hasn't diverged
    /// from it yet; "changing" a value always means producing a new instance through
    /// <see cref="ComputedStyleCow.SetPropertyValue{TSelf,TValue}"/>, the only place in the codebase
    /// allowed to use a <c>with</c> expression on these records.
    /// </para>
    /// </summary>
    internal sealed record ComputedStyle
    {
        /// <summary>What a fresh, unstyled <see cref="CssBox"/> starts with - every area at its own <c>Default</c>.</summary>
        internal static readonly ComputedStyle Default = new();

        /// <summary>
        /// Specified (not var()-resolved) values of this box's CSS custom properties (--foo), keyed by
        /// their case-sensitive name. Null when no custom property has been declared or inherited. Not an
        /// area - it's a mutable dictionary, not a CSS module, and its existing @property-inherits:false-
        /// aware filtered-clone logic in <see cref="CssBox.InheritStyle"/> already handles it correctly;
        /// the whole-instance-reuse-on-inherit optimization the areas get is deliberately NOT extended to
        /// it, since a shared dictionary reference plus <c>DomParser</c>'s existing in-place
        /// <c>box.CustomProperties[k] = v</c> mutation would let a child's write corrupt its parent's copy.
        /// </summary>
        public Dictionary<string, string>? CustomProperties { get; init; }

        public BorderArea Border { get; init; } = BorderArea.Default;
        public VisualEffectsArea VisualEffects { get; init; } = VisualEffectsArea.Default;
        public GeneratedContentArea GeneratedContent { get; init; } = GeneratedContentArea.Default;
        public BoxModelArea BoxModel { get; init; } = BoxModelArea.Default;
        public BreakArea Break { get; init; } = BreakArea.Default;
        public PaginationArea Pagination { get; init; } = PaginationArea.Default;
        public BackgroundArea Background { get; init; } = BackgroundArea.Default;
        public DisplayPositioningArea DisplayPositioning { get; init; } = DisplayPositioningArea.Default;
        public TableArea Table { get; init; } = TableArea.Default;
        public TextArea Text { get; init; } = TextArea.Default;
        public TextDecorationArea TextDecoration { get; init; } = TextDecorationArea.Default;
        public FontArea Font { get; init; } = FontArea.Default;
        public ListArea List { get; init; } = ListArea.Default;
        public FlexArea Flex { get; init; } = FlexArea.Default;
        public GridArea Grid { get; init; } = GridArea.Default;
        public MultiColumnArea MultiColumn { get; init; } = MultiColumnArea.Default;
    }
}
