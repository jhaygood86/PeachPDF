// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Text;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Fragments;
// CssBox declares a property literally named ContainerType, which shadows the CSS-OM enum type of the
// same name within this file - alias it so the enum stays referenceable (same trick ComputedStyleAreas
// uses for WritingMode).
using ContainerTypeEnum = PeachPDF.CSS.ContainerType;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a CSS Box of text or replaced elements.
    /// </summary>
    /// <remarks>
    /// The Box can contains other boxes, that's the way that the CSS Tree
    /// is composed.
    /// 
    /// To know more about boxes visit CSS spec:
    /// http://www.w3.org/TR/CSS21/box.html
    /// </remarks>
    internal partial class CssBox : IDisposable, ICssDomNode
    {
        #region Fields and Consts

        /// <summary>
        /// A page-boundary visibility clip-intersection whose width/height comes out merely
        /// microscopically positive (e.g. ~1e-13) rather than exactly zero is floating-point noise, not
        /// real visible area - accumulated rounding across the several arithmetic steps a relocated
        /// box's Y goes through (layout, ScrollOffset translation, clip intersection) routinely lands a
        /// hair off exact zero in either direction. <see cref="RRect.IsEmpty"/>'s strict <c>&lt;= 0</c>
        /// check only catches the exactly-zero-or-negative case; this epsilon (a millionth of a point -
        /// far below anything a page layout or PDF viewer could ever meaningfully distinguish, but many
        /// orders of magnitude above the observed rounding noise) is for the paint-time visibility culls
        /// that need to treat "merely touching the clip edge" the same as "no real overlap." See GitHub
        /// issue #113.
        /// </summary>
        private const double VisibilityClipEpsilon = 1e-6;

        private static uint _idCounter = 0;

        /// <summary>
        /// the parent css box of this css box in the hierarchy
        /// </summary>
        private CssBox? _parentBox;

        /// <summary>
        /// the root container for the hierarchy
        /// </summary>
        protected HtmlContainerInt? _htmlContainer;

        /// <summary>
        /// the inner text of the box
        /// </summary>
        private string? _text;

        /// <summary>
        /// Do not use or alter this flag
        /// </summary>
        /// <remarks>
        /// Flag that indicates that CssTable algorithm already made fixes on it.
        /// </remarks>
        internal bool _tableFixed;

        protected bool _wordsSizeMeasured;
        public CssImage? ContentImage { get; internal set; }


        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parentBox">optional: the parent of this css box in html</param>
        /// <param name="tag">optional: the html tag associated with this css box</param>
        public CssBox(CssBox? parentBox, HtmlTag? tag)
        {
            _derivedStyle = new DerivedStyle(this);

            if (parentBox != null)
            {
                _parentBox = parentBox;
                _parentBox.Boxes.Add(this);
            }

            Id = ++_idCounter;
            HtmlTag = tag;
        }

        public uint Id { get; }

        public static void ClearCounter()
        {
            _idCounter = 0;
        }

        /// <summary>
        /// Gets the HtmlContainer of the Box.
        /// WARNING: May be null.
        /// </summary>
        public HtmlContainerInt? HtmlContainer
        {
            get { return _htmlContainer ??= _parentBox?.HtmlContainer; }
            set => _htmlContainer = value;
        }

        /// <summary>The document's <c>@font-palette-values</c> registry, or null when none/unavailable.</summary>
        internal IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? FontPaletteValuesRegistry
            => HtmlContainer?.FontPaletteValues;

        /// <summary>
        /// Gets or sets the parent box of this box
        /// </summary>
        public CssBox? ParentBox
        {
            get => _parentBox;
            set
            {
                //Remove from last parent
                _parentBox?.Boxes.Remove(this);

                _parentBox = value;

                //Add to new parent
                _parentBox?.Boxes.Add(this);
            }
        }

        /// <summary>
        /// Gets the children boxes of this box
        /// </summary>
        public List<CssBox> Boxes { get; } = [];

        public Dictionary<string, CssCounter> Counters { get; } = [];

        /// <summary>
        /// Names of counters for which <see cref="CssCounterEngine"/> has already applied this box's
        /// own counter-reset/counter-increment/counter-set contribution (as opposed to
        /// <see cref="Counters"/> merely holding a value inherited/copied from a parent or preceding
        /// sibling in scope, not yet finalized with this box's own contribution). Needed because a
        /// box can be reached by more than one independent resolution chain - its own top-down
        /// ancestor walk, and also as the "last child in scope" of a later sibling resolving its
        /// inheritance - and without this guard the second visit would silently re-apply (e.g.
        /// double-increment) an already-finalized counter.
        /// </summary>
        internal HashSet<string> FinalizedCounterNames { get; } = [];

        public Dictionary<string, NamedString> NamedStrings { get; } = [];

        /// <summary>
        /// The <c>page:</c>-selector tracking entry this box registered with <see cref="HtmlContainerInt"/>
        /// (if any), retained so a later ancestor reposition (<see cref="OffsetTop(double)"/>) can keep it in sync -
        /// mirrors <see cref="NamedStrings"/>'s same purpose for string-set.
        /// </summary>
        internal NamedPageElement? RegisteredNamedPageElement { get; set; }

        /// <summary>
        /// Is the box is of "br" element.
        /// </summary>
        public bool IsBrElement => HtmlTag != null && HtmlTag.Name.Equals("br", StringComparison.InvariantCultureIgnoreCase);

        public bool IsRoot { get; set; }

        public bool IsBeforePseudoElement { get; set; }

        public bool IsAfterPseudoElement { get; set; }

        /// <summary>
        /// Is this box a synthesized <c>::marker</c> pseudo-element (see <see cref="CssData"/>'s
        /// selector-matching synthesis, and <c>DomParser.EnsureListItemMarkers</c> for the computed-
        /// <c>Display: list-item</c> case selector matching can't cover). It is always a
        /// <see cref="CssBoxMarker"/>, which owns its own content resolution, sizing, positioning and
        /// painting - a real, cascaded box, the same as <see cref="IsBeforePseudoElement"/>/
        /// <see cref="IsAfterPseudoElement"/> boxes.
        /// </summary>
        public bool IsMarkerPseudoElement { get; set; }

        /// <summary>
        /// Is this box a synthesized <c>::first-letter</c> pseudo-element - unlike
        /// <see cref="IsBeforePseudoElement"/>/<see cref="IsAfterPseudoElement"/>/
        /// <see cref="IsMarkerPseudoElement"/> (all inserted as a new child of the matched element
        /// itself, since their content is author-declared), this box replaces a real descendant text
        /// box possibly several inline levels below the matched element (see
        /// <see cref="FirstLetterOriginatingBox"/>) - see <c>CssData.DoesSelectorMatch</c>'s
        /// <c>CssConstants.FirstLetter</c> case for the split logic.
        /// </summary>
        public bool IsFirstLetterPseudoElement { get; set; }

        /// <summary>
        /// For a synthesized <see cref="IsFirstLetterPseudoElement"/> box, the real element <c>E</c>
        /// that <c>E::first-letter</c> matched - used only for selector re-matching (see
        /// <c>CssData.DoesSelectorMatch</c>'s <c>referenceBox</c> logic), so a rule like
        /// <c>p::first-letter</c> re-matches against the real <c>&lt;p&gt;</c>, not this box's
        /// structural <see cref="ParentBox"/> (which may be a nested inline element,
        /// e.g. <c>&lt;b&gt;</c>, several levels below <c>E</c>). <see cref="ParentBox"/>
        /// itself deliberately stays the real structural parent so ordinary style inheritance (e.g.
        /// that nested <c>&lt;b&gt;</c>'s bold weight) still applies correctly to this box.
        /// </summary>
        public CssBox? FirstLetterOriginatingBox { get; set; }

        /// <summary>
        /// Idempotency guard set on the real matched element <c>E</c> (not the split box, since the
        /// split point may be several levels below <c>E</c> and isn't necessarily among its direct
        /// children) once <c>::first-letter</c> synthesis has been attempted for it.
        /// </summary>
        public bool FirstLetterProcessed { get; set; }

        /// <summary>
        /// Set during <see cref="CssData.DoesSelectorMatch(CSS.CompoundSelector, ICssDomNode?)"/> when some
        /// rule's <c>*::first-letter</c> selector matches this box. The actual DFS-and-split (see
        /// <c>DomParser.ApplyFirstLetterPseudoElements</c>) is deliberately deferred to a separate
        /// pass run after the whole document's cascade completes, rather than performed immediately
        /// here like <see cref="IsBeforePseudoElement"/>/<see cref="IsAfterPseudoElement"/>/
        /// <see cref="IsMarkerPseudoElement"/> are - finding the right descendant to split needs to
        /// know which descendants are block-level, and <c>Display</c> isn't reliably resolved for any
        /// of this box's descendants until their own cascade pass has run (this box's own cascade
        /// pass, where selector matching happens, completes *before* recursing into children).
        /// </summary>
        internal bool MatchesFirstLetterSelector { get; set; }

        /// <summary>
        /// When non-null, this box establishes an inline formatting context (e.g. a <c>&lt;p&gt;</c>)
        /// whose <c>::first-line</c> is styled by some rule - a fully-cascaded, detached shadow
        /// <see cref="CssBox"/> (never attached to the real tree) holding the resolved subset of
        /// properties CSS2.1 allows on <c>::first-line</c> (font, color, background,
        /// text-decoration, word/letter-spacing, vertical-align). Unlike <c>::before</c>/<c>::after</c>/
        /// <c>::marker</c>/<c>::first-letter</c>, no box is spliced into the real tree - "the first
        /// formatted line" is a layout-time-only concept (which words end up on it depends on line-
        /// wrapping, not known until <see cref="CssLayoutEngine.FlowBox"/> runs), so this is consulted
        /// there and at paint time per-word (see <see cref="CssRect.FirstLineStyle"/>) instead. Resolved
        /// once, in <c>DomParser.CascadeApplyStyles</c>, right after this box's own normal cascade
        /// completes (its own properties are needed as this shadow box's inherited baseline).
        /// </summary>
        internal CssBox? ResolvedFirstLineStyle { get; set; }

        /// <summary>
        /// Idempotency guard for <see cref="ResolvedFirstLineStyle"/>'s resolution, since a box's own
        /// cascade phase (where it's set) can run more than once is never expected in practice, but
        /// this mirrors <see cref="FirstLetterProcessed"/>'s defensive convention.
        /// </summary>
        internal bool FirstLineProcessed { get; set; }

        /// <summary>
        /// Set (on the real <c>&lt;body&gt;</c> or <c>&lt;html&gt;</c> box, whichever was chosen) by
        /// <see cref="HtmlContainerInt.ResolveCanvasBackground"/> per CSS2.1 §14.2: that box's
        /// background has been "promoted" to fill the whole page canvas on every page (see
        /// <see cref="FragmentPainter.PaintCanvasBackground"/>), so this box's own normal background
        /// pass must no-op instead of painting the same background a second time at its own (possibly
        /// much smaller than the page) laid-out rect.
        /// </summary>
        internal bool SuppressOwnBackgroundPaint { get; set; }

        /// <summary>
        /// Whether this box declares any background of its own (a visible <c>background-color</c> and/or
        /// at least one <c>background-image</c>/gradient layer) - used by
        /// <c>PdfGenerator.ResolveCanvasBackground</c> to decide, per CSS2.1 §14.2, whether
        /// <c>&lt;body&gt;</c>'s own background should be promoted to fill the page canvas, falling back
        /// to <c>&lt;html&gt;</c>'s only when body has none.
        /// </summary>
        internal bool HasOwnBackground => RenderUtils.IsColorVisible(ActualBackgroundColor) || BackgroundImages is { Count: > 0 };

        public bool IsPseudoElement => IsBeforePseudoElement || IsAfterPseudoElement || IsMarkerPseudoElement || IsFirstLetterPseudoElement;

        /// <summary>
        /// is the box "Display" is "Inline", is this is an inline box and not block.
        /// </summary>
        public bool IsInline => DerivedStyle.ActualDisplay is CssConstants.Inline or CssConstants.InlineBlock or CssConstants.InlineTable or CssConstants.InlineFlex or CssConstants.InlineGrid;

        /// <summary>
        /// is the box "Display" is "Block", is this is a block box and not inline.
        /// </summary>
        public bool IsBlock => DerivedStyle.ActualDisplay == CssConstants.Block;

        public bool IsFloated => Float.Value is Floating.Left or Floating.Right;

        public bool IsOutOfFlow => IsFloated || Position.Value is PositionMode.Absolute or PositionMode.Fixed;

        /// <summary>
        /// Is the css box clickable (by default only an "a" element with an href is clickable) - per
        /// WHATWG, an element is a hyperlink purely by virtue of being an &lt;a&gt; with an href
        /// attribute; a coexisting id/name (e.g. `&lt;a id="toc-1" href="#ch1"&gt;`, both a link source
        /// and a fragment target - a common real-world pattern) has no bearing on that and must not
        /// exclude it, since this also drives real PDF link-annotation generation
        /// (<see cref="DomUtils.GetAllLinkBoxes"/>) and tagged-PDF /Link mapping, not just :link matching.
        /// </summary>
        public virtual bool IsClickable => HtmlTag is { Name: HtmlConstants.A } && HtmlTag.HasAttribute("href");

        /// <summary>
        /// Gets a value indicating whether this instance or one of its parents has Position = fixed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is fixed; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsFixed
        {
            get
            {
                if (Position.Value == PositionMode.Fixed)
                    return true;

                if (this.ParentBox == null)
                    return false;

                CssBox parent = this;

                while (!(parent.ParentBox == null || parent == parent.ParentBox))
                {
                    parent = parent.ParentBox;

                    if (parent.Position.Value == PositionMode.Fixed)
                        return true;
                }

                return false;
            }
        }

        public virtual bool IsTableRowGroupBox => DerivedStyle.ActualDisplay is CssConstants.TableRowGroup or CssConstants.TableHeaderGroup or CssConstants.TableFooterGroup;

        /// <summary>
        /// Maps page number → last row bottom Y on that page. Set by CssLayoutEngineTable when rows break across pages.
        /// Used during paint to clip the table box border to the actual content height on each page.
        /// </summary>
        internal Dictionary<int, double>? PageBreakBottoms { get; set; }

        /// <summary>
        /// Where this table's row loop stopped, or null when it reached the end of its body rows. Null on
        /// every box that is not a table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The second thing a table pass hands the next, alongside <see cref="Fragmentation.TableSetup"/>,
        /// and it is already a <see cref="BreakToken"/>. The row loop is the only thing that ever sees a
        /// cell stop, because a box's record is readable only at the instant its layout returns; this is
        /// where the loop says so.
        /// </para>
        /// <para>
        /// The same record is also published as this box's <see cref="PendingBreakToken"/>, and that is
        /// the copy the rest of layout acts on: <see cref="PerformLayoutImp"/> returns early on it,
        /// <see cref="PublishBreakToTheContextRoot"/> hands it to the fragmentation context, and the
        /// parent's child loop stops and wraps it into a link naming this table, which is how the driver
        /// comes back for the rows after the stop.
        /// </para>
        /// <para>
        /// The two are not redundant, because they have different lifetimes. <see cref="BeginLayoutPass"/>
        /// clears <see cref="PendingBreakToken"/> at the top of every layout of this box, so it answers
        /// only at the instant that layout returns; this field is cleared by the engine's own constructor
        /// instead, so it still says what the last run concluded once the whole document is laid out —
        /// which is the only form of the answer anything after layout can ask for.
        /// </para>
        /// </remarks>
        internal TableBreakToken? TableContinuation { get; set; }

        /// <summary>
        /// What <c>CssLayoutEngineTable</c> settled once for this table, kept here because the engine is
        /// constructed afresh every time it runs. Null on every box that is not a table, and on a table
        /// until its first layout.
        /// </summary>
        /// <remarks>
        /// Replaced wholesale by every layout of this table that does not continue an earlier fragmentainer
        /// pass, which is what keeps a re-layout — the per-page-width reflow loop, <c>ShrinkToFit</c>, a
        /// §4.3 relocation — starting from the markup. A resumed pass over a table that has settled nothing
        /// replaces it too, since there is nothing to inherit and nothing an earlier pass could be
        /// destroyed by. See <see cref="Fragmentation.TableSetup"/> for what a resumed pass inherits from it
        /// and why each of those things is destructive when done twice.
        /// </remarks>
        internal TableSetup? TableSetup { get; set; }

        /// <summary>
        /// The vertical line segments (in absolute document coordinates) to draw between adjacent
        /// columns of a multi-column container — one segment per gap per page-row actually used.
        /// Set by <see cref="CssLayoutEngineColumns"/>, painted by <see cref="FragmentPainter"/>.
        /// </summary>
        internal List<(double X, double Top, double Bottom)>? ColumnRuleSegments { get; set; }

        public virtual bool IsTableCell => DerivedStyle.ActualDisplay is CssConstants.TableCell;

        /// <summary>
        /// Gets the containing block-box of this box. (The nearest parent box with display=block)
        /// </summary>
        public CssBox ContainingBlock
        {
            get
            {
                if (ParentBox == null)
                {
                    return this; //This is the initial containing block.
                }

                var box = ParentBox;
                while (!box.IsBlock &&
                       box.DerivedStyle.ActualDisplay != CssConstants.ListItem &&
                       box.DerivedStyle.ActualDisplay != CssConstants.Table &&
                       box.DerivedStyle.ActualDisplay != CssConstants.TableCell &&
                       box.DerivedStyle.ActualDisplay != CssConstants.Flex &&
                       box.DerivedStyle.ActualDisplay != CssConstants.InlineFlex &&
                       box.ParentBox != null)
                {
                    box = box.ParentBox;
                }

                //Comment this following line to treat always superior box as block
                if (box == null)
                    throw new Exception("There's no containing block on the chain");

                return box;
            }
        }

        /// <summary>
        /// Which kind of <c>@container</c> eligibility <see cref="FindNearestQueryContainer"/> should
        /// check. CSS Containment 3 SS7.2: a <c>size</c>/<c>inline-size</c> query container is required for
        /// size-feature conditions (<c>width</c>/<c>height</c>/etc.), but a style-feature (<c>style()</c>)
        /// query is eligible against any element that declares <c>container-type</c> at all - including
        /// its initial <c>normal</c> value, which only opts an element out of *size* containment, not out
        /// of being a query container for style queries.
        /// </summary>
        internal enum QueryKind { Size, Style }

        /// <summary>
        /// Walks ancestors (starting at <see cref="ParentBox"/>, never this box itself) for the nearest
        /// one eligible as an <c>@container</c> query container, per CSS Containment 3 SS7.2. With
        /// <paramref name="name"/> given, the ancestor's own <see cref="CssBox.ContainerName"/> list
        /// (whitespace-separated <c>&lt;custom-ident&gt;</c>s, case-sensitive) must contain it; with no
        /// name, the nearest eligible ancestor wins regardless of its own name. Returns <c>null</c> when no
        /// eligible ancestor exists - CSS Containment 3's "unknown" query-container state, which callers
        /// must treat as falsy for <c>@container</c> (a deliberate divergence from how <c>@media</c>
        /// features fall back to permissive-true on missing viewport geometry - a missing container here
        /// is invalid authoring, not a missing render context).
        /// </summary>
        internal CssBox? FindNearestQueryContainer(string? name, QueryKind kind = QueryKind.Size)
        {
            var candidate = ParentBox;
            while (candidate is not null)
            {
                // Style queries: every element is container-type-bearing (normal is the initial value),
                // so only size queries actually restrict eligibility to Size/InlineSize containers.
                var isEligible = kind != QueryKind.Size
                    || candidate.ContainerType.Value is ContainerTypeEnum.Size or ContainerTypeEnum.InlineSize;

                if (isEligible && (name is null || ContainerNameMatches(candidate.ContainerName, name)))
                    return candidate;

                candidate = candidate.ParentBox;
            }

            return null;
        }

        private static bool ContainerNameMatches(string containerName, string queriedName)
        {
            if (string.IsNullOrEmpty(containerName) || containerName == CssConstants.None) return false;

            foreach (var part in containerName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // <custom-ident> is case-sensitive.
                if (string.Equals(part, queriedName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// The nearest unnamed ancestor <c>@container</c> size query container's current content-box
        /// size, resolved for <c>cqw</c>/<c>cqi</c>/<c>cqh</c>/<c>cqb</c>/<c>cqmin</c>/<c>cqmax</c> unit
        /// resolution (CSS Containment 3 SS6.2) - shared by every length-resolution call site (<see
        /// cref="Parse.CssValueParser.ParseLength(string, double, CssBox)"/>, font-size) so there is one
        /// place that decides how a box's nearest container's size is measured, not one per caller.
        /// Block size is only ever non-null for a <c>size</c> container - an <c>inline-size</c> container
        /// doesn't track the block axis at all.
        /// </summary>
        internal (double? InlineSizePt, double? BlockSizePt) GetContainerRelativeUnitBasis()
        {
            var container = FindNearestQueryContainer(name: null);
            if (container is null) return (null, null);

            double? inlinePt = container.ClientRight - container.ClientLeft;
            double? blockPt = container.ContainerType.Value == ContainerTypeEnum.Size
                ? container.ClientBottom - container.ClientTop
                : null;

            return (inlinePt, blockPt);
        }

        public bool IsHeightCalculated { get; set; } = false;

        /// <summary>
        /// Gets the actual top's Margin
        /// </summary>
        public double ActualMarginTop =>
            MarginTop.Value is { IsValue: true, Value: { } marginTop }
                ? CssValueParser.ParseLength(marginTop, ContainingBlock.Size.Width, this)
                : 0;

        /// <summary>
        /// Gets the actual Margin on the left
        /// </summary>
        public double ActualMarginLeft => CssLayoutEngine.GetActualMarginLeft(this);

        /// <summary>
        /// Gets the actual Margin of the bottom
        /// </summary>
        public double ActualMarginBottom =>
            MarginBottom.Value is { IsValue: true, Value: { } marginBottom }
                ? CssValueParser.ParseLength(marginBottom, ContainingBlock.Size.Width, this)
                : 0;

        /// <summary>
        /// Gets the actual Margin on the right
        /// </summary>
        public double ActualMarginRight => CssLayoutEngine.GetActualMarginRight(this);

        /// <summary>
        /// Gets the HTMLTag that hosts this box
        /// </summary>
        public HtmlTag? HtmlTag { get; }

        /// <summary>
        /// Gets if this box represents an image
        /// </summary>
        public bool IsImage => Words is [{ IsImage: true }];

        /// <summary>
        /// Tells if the box is empty or contains just blank spaces, checked recursively through
        /// <see cref="Boxes"/> - a box's own <see cref="Words"/> collection only ever holds content it
        /// owns directly, so an anonymous wrapper box (e.g. the one <c>DomParser.CorrectInlineBoxesParent</c>
        /// generates around a run of inline content when its siblings force <c>ContainsVariantBoxes</c>,
        /// or any other box whose real content lives on a child rather than itself) would otherwise
        /// always read as "empty" here even when it wraps a real image or text run. A word's own
        /// <c>IsSpaces</c> is false for a replaced element's image word, so this recursion covers
        /// replaced content the same way it covers text.
        /// </summary>
        public bool IsSpaceOrEmpty
        {
            get
            {
                foreach (CssRect word in Words)
                {
                    if (!word.IsSpaces)
                    {
                        return false;
                    }
                }

                foreach (var childBox in Boxes)
                {
                    if (!childBox.IsSpaceOrEmpty)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Gets or sets the inner text of the box
        /// </summary>
        public string? Text
        {
            get => _text;
            set
            {
                _text = value is not null ? HtmlUtils.FixNewLines(value) : null;
                Words.Clear();
            }
        }

        /// <summary>
        /// One resolved UAX#9 embedding level per character of <see cref="Text"/> - null for a box outside
        /// any paragraph <see cref="CssBidiParagraphResolver.AssignBidiLevels"/> reached (only ever the
        /// case for text set after that pass already ran, e.g. <c>DomParser.CorrectLineBreaksBlocks</c>'s
        /// synthetic <c>"\n"</c> for a standalone <c>&lt;br&gt;</c>), in which case <see cref="ParseToWords"/>
        /// falls back to one level for the whole box, from its own resolved <see cref="Direction"/>. Set once,
        /// before <see cref="ParseToWords"/> runs on this box, so it can additionally split words at a level
        /// boundary the way it already splits at whitespace/hyphen/CJK boundaries.
        /// </summary>
        internal byte[]? BidiLevels { get; set; }

        /// <summary>
        /// Gets the line-boxes of this box (if block box)
        /// </summary>
        internal List<CssLineBox> LineBoxes { get; } = [];

        /// <summary>
        /// Gets the rectangles where this box should be painted
        /// </summary>
        internal Dictionary<CssLineBox, RRect> Rectangles { get; } = [];

        /// <summary>
        /// Gets the BoxWords of text in the box
        /// </summary>
        internal List<CssRect> Words { get; } = [];

        /// <summary>
        /// Gets the first word of the box
        /// </summary>
        internal CssRect FirstWord => Words[0];

        /// <summary>
        /// Gets or sets the first linebox where content of this box appear
        /// </summary>
        internal CssLineBox? FirstHostingLineBox { get; set; }

        /// <summary>
        /// Gets or sets the last linebox where content of this box appear
        /// </summary>
        internal CssLineBox? LastHostingLineBox { get; set; }

        /// <summary>
        /// Create new css box for the given parent with the given html tag.<br/>
        /// </summary>
        /// <param name="tag">the html tag to define the box</param>
        /// <param name="parent">the box to add the new box to it as child</param>
        /// <returns>the new box</returns>
        public static CssBox CreateBox(HtmlTag tag, CssBox? parent = null)
        {
            ArgumentNullException.ThrowIfNull(tag);

            return tag.Name.ToLowerInvariant() switch
            {
                HtmlConstants.Img => new CssBoxImage(parent, tag),
                HtmlConstants.Iframe => new CssBoxFrame(parent, tag),
                HtmlConstants.Hr => new CssBoxHr(parent, tag),
                HtmlConstants.Svg => new CssBoxSvg(parent, tag),
                HtmlConstants.Object => new CssBoxObject(parent, tag),
                HtmlConstants.Video => new CssBoxVideo(parent, tag),
                _ => new CssBox(parent, tag)
            };
        }

        /// <summary>
        /// Create new css box for the given parent with the given optional html tag and insert it either
        /// at the end or before the given optional box.<br/>
        /// If no html tag is given the box will be anonymous.<br/>
        /// If no before box is given the new box will be added at the end of parent boxes collection.<br/>
        /// If before box doesn't exists in parent box exception is thrown.<br/>
        /// </summary>
        /// <remarks>
        /// To learn more about anonymous inline boxes visit: http://www.w3.org/TR/CSS21/visuren.html#anonymous
        /// </remarks>
        /// <param name="parent">the box to add the new box to it as child</param>
        /// <param name="tag">optional: the html tag to define the box</param>
        /// <param name="before">optional: to insert as specific location in parent box</param>
        /// <returns>the new box</returns>
        public static CssBox CreateBox(CssBox parent, HtmlTag? tag = null, CssBox? before = null)
        {
            ArgumentNullException.ThrowIfNull(parent);

            var newBox = new CssBox(parent, tag);
            newBox.InheritStyle();

            if (before != null)
            {
                newBox.SetBeforeBox(before);
            }
            return newBox;
        }

        /// <summary>
        /// Create new css block box.
        /// </summary>
        /// <returns>the new block box</returns>
        public static CssBox CreateBlock()
        {
            return new CssBox(null, null)
            {
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.Block, DisplayMode.Block)
            };
        }

        /// <summary>
        /// Create new css block box for the given parent with the given optional html tag and insert it either
        /// at the end or before the given optional box.<br/>
        /// If no html tag is given the box will be anonymous.<br/>
        /// If no before box is given the new box will be added at the end of parent boxes collection.<br/>
        /// If before box doesn't exists in parent box exception is thrown.<br/>
        /// </summary>
        /// <remarks>
        /// To learn more about anonymous block boxes visit CSS spec:
        /// http://www.w3.org/TR/CSS21/visuren.html#anonymous-block-level
        /// </remarks>
        /// <param name="parent">the box to add the new block box to it as child</param>
        /// <param name="tag">optional: the html tag to define the box</param>
        /// <param name="before">optional: to insert as specific location in parent box</param>
        /// <returns>the new block box</returns>
        public static CssBox CreateBlock(CssBox parent, HtmlTag? tag = null, CssBox? before = null)
        {
            ArgumentNullException.ThrowIfNull(parent);

            var newBox = CreateBox(parent, tag, before);
            newBox.Display = CssProperty<DisplayMode>.FromValue(CssConstants.Block, DisplayMode.Block);
            return newBox;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.
        /// </summary>
        /// <param name="g">Device context to use</param>
        /// <remarks>
        /// The adapter for a caller that is <i>not</i> one of this box's frame's child loops — a layout
        /// engine measuring an item, the out-of-flow walk, the document root. It names the frame on the
        /// box's behalf, because where a block-level box goes is the frame's question and not the box's
        /// (<see cref="LayoutBlockChild"/>); a child its frame's own loop reaches is entered there instead.
        /// The root has no frame above it, so it stands in for its own.
        /// </remarks>
        public ValueTask PerformLayout(RGraphics g) => (ParentBox ?? this).LayoutBlockChild(g, this);

        /// <summary>
        /// Set this box in 
        /// </summary>
        /// <param name="before"></param>
        public void SetBeforeBox(CssBox before)
        {
            int index = _parentBox!.Boxes.IndexOf(before);
            if (index < 0)
                throw new Exception("before box doesn't exist on parent");

            _parentBox.Boxes.Remove(this);
            _parentBox.Boxes.Insert(index, this);
        }

        /// <summary>
        /// Move all child boxes from <paramref name="fromBox"/> to this box.
        /// </summary>
        /// <param name="fromBox">the box to move all its child boxes from</param>
        public void SetAllBoxes(CssBox fromBox)
        {
            foreach (var childBox in fromBox.Boxes)
                childBox._parentBox = this;

            Boxes.AddRange(fromBox.Boxes);
            fromBox.Boxes.Clear();
        }

        /// <summary>
        /// Splits the text into words and saves the result
        /// </summary>
        public void ParseToWords()
        {
            Words.Clear();

            var text = ApplyTextTransform(_text!, TextTransform);
            var startIdx = 0;
            var preserveSpaces = WhiteSpace.Value is Whitespace.Pre or Whitespace.PreWrap;
            var respectNewLines = preserveSpaces || WhiteSpace.Value == Whitespace.PreLine || IsBrElement;

            // Only ever null for text set after CssBidiParagraphResolver.AssignBidiLevels already ran
            // (e.g. DomParser.CorrectLineBreaksBlocks' synthetic "\n" for a standalone <br>) - one level
            // for the whole box, from its own resolved Direction, is the correct behavior for that text
            // anyway (a lone line-break/space has nothing to bidi-split).
            var fallbackLevel = Direction.Value == DirectionMode.Rtl ? (byte)1 : (byte)0;

            while (startIdx < text.Length)
            {
                while (startIdx < text.Length && text[startIdx] == '\r')
                    startIdx++;

                if (startIdx < text.Length)
                {
                    var endIdx = startIdx;
                    while (endIdx < text.Length && HtmlUtils.IsCollapsibleWhitespace(text[endIdx]) && text[endIdx] != '\n')
                        endIdx++;

                    if (endIdx > startIdx)
                    {
                        if (preserveSpaces)
                        {
                            Words.Add(new CssRectWord(this, text.Substring(startIdx, endIdx - startIdx), false, false)
                            {
                                BidiLevel = BidiLevels is { } wsLevels ? wsLevels[startIdx] : fallbackLevel
                            });
                        }
                    }
                    else
                    {
                        // A soft hyphen (U+00AD) is an extra break opportunity honored for hyphens:
                        // manual/auto (the default is manual - see TextArea.Hyphens). Unlike a
                        // literal '-' it's never part of the rendered word text; unlike the old
                        // behavior, it no longer eagerly splits the word here either - at this
                        // pre-layout stage there's no way to know whether a line break will actually
                        // land at this exact position, so eagerly splitting could only ever show the
                        // hyphen glyph always or never, both wrong. Its position (and, for hyphens:auto
                        // with a known document language, HyphenationEngine's own suggested positions)
                        // is instead recorded as a candidate on the whole word and consulted only when
                        // CssLayoutEngine.FlowBox actually needs to break the line - see AddWord.
                        var honorSoftHyphen = Hyphens.Value != PeachPDF.CSS.Hyphens.None;

                        // Scan by whole codepoint (Rune), not UTF-16 code unit, so an astral character (an
                        // emoji, a CJK Extension-B ideograph, etc.) is never split across its surrogate pair -
                        // its two halves would otherwise each be treated as a separate per-character Asian
                        // word break and emitted as two invalid lone-surrogate words.
                        endIdx = startIdx;
                        while (endIdx < text.Length)
                        {
                            Rune.DecodeFromUtf16(text.AsSpan(endIdx), out var rune, out var runeLength);
                            if (HtmlUtils.IsCollapsibleWhitespace(text[endIdx]) || text[endIdx] == '-'
                                || WordBreak.Value == PeachPDF.CSS.WordBreak.BreakAll || CommonUtils.IsAsianCharacter(rune))
                                break;
                            endIdx += runeLength;
                        }

                        if (endIdx < text.Length)
                        {
                            Rune.DecodeFromUtf16(text.AsSpan(endIdx), out var rune, out var runeLength);
                            if (text[endIdx] == '-' || WordBreak.Value == PeachPDF.CSS.WordBreak.BreakAll || CommonUtils.IsAsianCharacter(rune))
                                endIdx += runeLength;
                        }

                        // An extra break opportunity at a UAX#9 embedding-level boundary - on top of the
                        // whitespace/hyphen/CJK ones above - so e.g. a digit run embedded directly in RTL
                        // text with no adjacent whitespace (a level change with nothing else marking a
                        // boundary) still ends up as its own word, which CssLayoutEngine's per-line bidi
                        // reorder step needs (it treats each word as one homogeneous-level UAX#9 unit).
                        // Only ever shrinks endIdx (never extends past what the rules above decided).
                        if (BidiLevels is { } levels && endIdx > startIdx)
                        {
                            var boundaryLevel = levels[startIdx];
                            var boundary = startIdx + 1;
                            while (boundary < endIdx && levels[boundary] == boundaryLevel)
                                boundary++;
                            endIdx = boundary;
                        }

                        if (endIdx > startIdx)
                        {
                            var wordBidiLevel = BidiLevels is { } wordLevels ? wordLevels[startIdx] : fallbackLevel;
                            var hasSpaceBefore = !preserveSpaces && (startIdx > 0 && Words.Count == 0 && HtmlUtils.IsCollapsibleWhitespace(text[startIdx - 1]));
                            var hasSpaceAfter = !preserveSpaces && (endIdx < text.Length && HtmlUtils.IsCollapsibleWhitespace(text[endIdx]));
                            var rawWord = text.Substring(startIdx, endIdx - startIdx);
                            // TextTransform is applied character-by-character and is always
                            // length-preserving (see ApplyTextTransform), so the same start/end indices
                            // slice out the pre-transform equivalent of rawWord from the original text -
                            // kept alongside so a ::first-line rule's own text-transform (if different
                            // from this box's) can be re-derived later without the information a transform
                            // like uppercase would otherwise destroy. See CssRect.OriginalText.
                            var rawOriginalWord = _text!.Substring(startIdx, endIdx - startIdx);

                            List<int>? hyphenationCandidates = null;
                            string cleanWord;
                            string cleanOriginalWord;

                            if (honorSoftHyphen && rawWord.IndexOf('­') >= 0)
                            {
                                (cleanWord, hyphenationCandidates) = StripSoftHyphens(rawWord);
                                (cleanOriginalWord, _) = StripSoftHyphens(rawOriginalWord);
                            }
                            else
                            {
                                cleanWord = rawWord;
                                cleanOriginalWord = rawOriginalWord;

                                if (Hyphens.Value == PeachPDF.CSS.Hyphens.Auto)
                                {
                                    var language = HtmlContainer?.DocumentLanguage;
                                    if (!string.IsNullOrEmpty(language))
                                    {
                                        var autoPoints = PeachPDF.Text.HyphenationEngine.FindHyphenationPoints(cleanWord, language);
                                        if (autoPoints.Count > 0)
                                            hyphenationCandidates = new List<int>(autoPoints);
                                    }
                                }
                            }

                            // AddWord may itself split cleanWord further (small-caps case-runs,
                            // per-codepoint font fragments) - every fragment it produces from this one
                            // call stays within this already level-homogeneous span, so they all share
                            // the same bidi level.
                            var wordsBefore = Words.Count;
                            AddWord(cleanWord, hasSpaceBefore, hasSpaceAfter, hyphenationCandidates, cleanOriginalWord);
                            for (var wi = wordsBefore; wi < Words.Count; wi++)
                                Words[wi].BidiLevel = wordBidiLevel;
                        }
                    }

                    // create new-line word so it will effect the layout
                    if (endIdx < text.Length && text[endIdx] == '\n')
                    {
                        var newlineBidiLevel = BidiLevels is { } newlineLevels && endIdx < newlineLevels.Length
                            ? newlineLevels[endIdx]
                            : fallbackLevel;
                        endIdx++;
                        if (respectNewLines)
                            Words.Add(new CssRectWord(this, "\n", false, false) { BidiLevel = newlineBidiLevel });
                    }

                    startIdx = endIdx;
                }
            }
        }

        /// <summary>
        /// Adds one word to <see cref="Words"/> — or, when <see cref="FontVariantCaps"/> is
        /// <c>small-caps</c>/<c>all-small-caps</c> and the resolved font lacks real GSUB support for it
        /// (see <see cref="DerivedStyle.ActualFontVariantCaps"/>), splits it into consecutive
        /// lowercase/non-lowercase case-run fragments instead. Each lowercase run is upper-cased and
        /// marked (<see cref="CssRect.FontSizeScale"/>) to be measured/painted smaller than the rest of
        /// the word (see <see cref="DerivedStyle.ActualSmallCapsFont"/>) - under all-small-caps, an
        /// already-uppercase run is shrunk too, without a case-flip. When the resolved font *does* have
        /// real <c>smcp</c>/<c>c2sc</c>/etc. GSUB data, none of this synthesis happens at all - the word
        /// is kept whole and the caps feature is instead threaded into shaping (see
        /// <see cref="ActualTextShapingFeatures"/>). Every fragment after the first is marked
        /// <see cref="CssRect.SuppressWrapBefore"/> so a synthetic split never introduces a new
        /// line-break opportunity in the middle of what was one word. <paramref name="hyphenationCandidates"/>
        /// (see <see cref="CssRect.HyphenationCandidates"/>) is only attached when the word is kept
        /// whole — small-caps splitting and hyphenation are a separate, non-composing pair of features.
        /// </summary>
        private void AddWord(string text, bool hasSpaceBefore, bool hasSpaceAfter, List<int>? hyphenationCandidates = null, string? originalText = null)
        {
            // The small-caps split path below re-slices by run position, which only lines up against
            // originalText when the two strings are the same length (true for the vast majority of real
            // content - see the ParseToWords call site comment - but not guaranteed if HTML-entity
            // decoding happened to produce a different length for the two). Fall back to treating text
            // itself as its own original in that rare case rather than slicing out of bounds.
            if (originalText is null || originalText.Length != text.Length)
                originalText = text;

            // Whether this word needs per-codepoint font selection (an @font-face unicode-range applies, or
            // the box's own font can't render some character and a later family in the stack can). The vast
            // majority of words don't - they take the single-word fast path unchanged.
            var needsPerCodepoint = NeedsPerCodepointFont(text);

            // Synthesis (uppercase lowercase runs + shrink) only ever applies to small-caps/
            // all-small-caps, and only when real GSUB substitution isn't already handling it -
            // DerivedStyle.ActualFontVariantCaps resolves to None whenever the resolved font actually
            // supports the requested feature, in which case the word is left untouched here and real
            // substitution happens transparently at measure/paint time. The other 4 caps keywords
            // never synthesize at all (real substitution or a silent no-op, never an approximation).
            var isSmallCapsFamily = FontVariantCaps is CssConstants.SmallCaps or CssConstants.AllSmallCaps;
            var isAllSmallCaps = FontVariantCaps == CssConstants.AllSmallCaps;
            var needsSynthesis = isSmallCapsFamily && ActualFontVariantCaps == FontVariantCapsFeature.None;
            var synthesisApplies = needsSynthesis && (ContainsLowerLetter(text) || (isAllSmallCaps && ContainsUpperLetter(text)));

            if (!synthesisApplies)
            {
                if (!needsPerCodepoint)
                {
                    Words.Add(new CssRectWord(this, text, hasSpaceBefore, hasSpaceAfter, originalText)
                    {
                        HyphenationCandidates = hyphenationCandidates
                    });
                    return;
                }

                EmitPerCodepointFragments(text, originalText, hasSpaceBefore, hasSpaceAfter, fontSizeScale: 1.0, alwaysSuppressWrap: false);
                return;
            }

            var runs = new List<(int Start, int Length, bool IsLower)>();
            var runStart = 0;

            while (runStart < text.Length)
            {
                var isLower = char.IsLower(text[runStart]);
                var runEnd = runStart + 1;
                while (runEnd < text.Length && char.IsLower(text[runEnd]) == isLower)
                    runEnd++;

                runs.Add((runStart, runEnd - runStart, isLower));
                runStart = runEnd;
            }

            for (var i = 0; i < runs.Count; i++)
            {
                var (start, length, isLower) = runs[i];
                var runText = text.Substring(start, length);
                var runOriginalText = originalText.Substring(start, length);
                var displayText = isLower ? runText.ToUpperInvariant() : runText;
                // Under all-small-caps, a run that was already uppercase is shrunk too (approximating
                // c2sc - no case-flip needed, since it's already upper) but only when it actually
                // contains an uppercase letter, not a pure digit/punctuation run (c2sc never touches
                // those either).
                var scale = isLower || (isAllSmallCaps && ContainsUpperLetter(runText)) ? SmallCapsFontScale : 1.0;
                var runSpaceBefore = i == 0 && hasSpaceBefore;
                var runSpaceAfter = i == runs.Count - 1 && hasSpaceAfter;

                if (!needsPerCodepoint)
                {
                    Words.Add(new CssRectWord(this, displayText, runSpaceBefore, runSpaceAfter, runOriginalText)
                    {
                        FontSizeScale = scale,
                        SuppressWrapBefore = i > 0
                    });
                }
                else
                {
                    // Per-codepoint splitting composes inside each small-caps case-run. Every fragment
                    // after the very first of the whole word suppresses wrap: run i>0 is never first, and
                    // within run 0 only its own first fragment is.
                    EmitPerCodepointFragments(displayText, runOriginalText, runSpaceBefore, runSpaceAfter, scale, alwaysSuppressWrap: i > 0);
                }
            }
        }

        /// <summary>
        /// Splits <paramref name="text"/> into maximal runs of consecutive codepoints that resolve to the
        /// same face (via <see cref="DerivedStyle.ActualFontForCodepoint"/>) and adds one
        /// <see cref="CssRectWord"/> per run, each marked <see cref="CssRect.UsesPerCodepointFont"/>. The
        /// split is glued back together for line-breaking (<see cref="CssRect.SuppressWrapBefore"/> on every
        /// fragment after the first) and only the boundary fragments carry the surrounding whitespace flags,
        /// exactly like the small-caps split it composes with.
        /// </summary>
        private void EmitPerCodepointFragments(string text, string originalText, bool hasSpaceBefore, bool hasSpaceAfter, double fontSizeScale, bool alwaysSuppressWrap)
        {
            if (originalText.Length != text.Length)
                originalText = text;

            var index = 0;
            var first = true;

            while (index < text.Length)
            {
                Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
                var faceKey = ActualFontForCodepoint(rune, fontSizeScale).FaceKey;
                var start = index;
                index += consumed;

                while (index < text.Length)
                {
                    Rune.DecodeFromUtf16(text.AsSpan(index), out var next, out var nextConsumed);
                    if (ActualFontForCodepoint(next, fontSizeScale).FaceKey != faceKey)
                        break;
                    index += nextConsumed;
                }

                Words.Add(new CssRectWord(this, text.Substring(start, index - start), first && hasSpaceBefore, index >= text.Length && hasSpaceAfter, originalText.Substring(start, index - start))
                {
                    FontSizeScale = fontSizeScale,
                    SuppressWrapBefore = !first || alwaysSuppressWrap,
                    UsesPerCodepointFont = true
                });

                first = false;
            }
        }

        /// <summary>
        /// Whether <paramref name="text"/> must be resolved per-codepoint: an <c>@font-face</c>
        /// <c>unicode-range</c> applies to one of this box's candidate families (so a covered character must
        /// come from that face even if the default face has the glyph), or the box's own font lacks a glyph
        /// for some character (so a later family in the <c>font-family</c> stack should supply it). Ordinary
        /// fully-covered text with no ranged faces returns false - the single-word fast path.
        /// </summary>
        private bool NeedsPerCodepointFont(string text)
        {
            if (HtmlContainer is null)
                return false;

            var adapter = HtmlContainer.Adapter;

            foreach (var family in (FontFamilyList ?? FontFamily ?? string.Empty).Split(','))
            {
                var name = family.Trim().TrimStart('"', '\'').TrimEnd('"', '\'');
                if (adapter.FamilyHasExplicitUnicodeRanges(name))
                    return true;
            }

            var font = ActualFont;
            foreach (var rune in text.EnumerateRunes())
            {
                if (!font.HasGlyph(rune))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The font a word/fragment is measured and painted with: its per-codepoint face (resolved from its
        /// first <see cref="Rune"/>) when <see cref="CssRect.UsesPerCodepointFont"/>, otherwise the box's
        /// own <see cref="DerivedStyle.ActualFont"/> (or <see cref="DerivedStyle.ActualSmallCapsFont"/>
        /// for a synthesized small-caps run). <paramref name="styleSource"/> is the box whose font applies -
        /// the owner box, or a <c>::first-line</c> shadow box for a word on the first formatted line.
        /// Shared by measurement and by <see cref="FragmentPainter"/>, so the two can never disagree
        /// about which face a word is drawn in.
        /// </summary>
        /// <remarks>
        /// Reads the representative codepoint from <see cref="CssRect.OriginalText"/>, not
        /// <see cref="CssRect.Text"/>: an RTL run's words are reordered/mirrored in place after
        /// measurement (<c>CssLayoutEngine.PlaceBidiRunWord</c> calls <see cref="CssRectWord.ReplaceText"/>,
        /// which only ever changes <c>Text</c>), and <see cref="DerivedStyle.ActualFontForCodepoint"/>'s
        /// cache is keyed by the literal codepoint value, not the resolved face - reading a *different*
        /// character's codepoint post-mirror than was read at measurement time returns a different cache
        /// entry, whose <see cref="RFont.Ascent"/>/<see cref="RFont.Height"/> were never populated (still
        /// their uninitialized sentinel), corrupting the baseline alignment <c>FragmentPainter.Text.cs</c>
        /// derives from them - even though every codepoint in one per-codepoint fragment resolves to the
        /// same face by construction (<see cref="EmitPerCodepointFragments"/>), so which one is read does
        /// not change *which font* is selected. <c>OriginalText</c> is never touched by mirroring, only by
        /// <see cref="TextTransform"/> (itself always length-and-position-preserving), so it names the same
        /// representative character at measurement and at paint alike.
        /// </remarks>
        internal static RFont ResolveWordFont(CssRect word, CssBox styleSource)
        {
            if (word.UsesPerCodepointFont && (word.OriginalText ?? word.Text) is { Length: > 0 } text)
            {
                Rune.DecodeFromUtf16(text, out var rune, out _);
                return styleSource.ActualFontForCodepoint(rune, word.FontSizeScale);
            }

            return word.FontSizeScale == 1.0 ? styleSource.ActualFont : styleSource.ActualSmallCapsFont;
        }

        private static bool ContainsLowerLetter(string text)
        {
            foreach (var c in text)
            {
                if (char.IsLower(c)) return true;
            }
            return false;
        }

        private static bool ContainsUpperLetter(string text)
        {
            foreach (var c in text)
            {
                if (char.IsUpper(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// Removes every soft hyphen (U+00AD) from <paramref name="rawWord"/> — decoding HTML entities
        /// segment-by-segment around each removed character so candidate indices stay correct against
        /// the final, decoded, hyphen-free text — and returns the candidate break index for each one
        /// removed (the position, in the resulting clean text, where a "-" may be inserted if
        /// <see cref="CssLayoutEngine.FlowBox"/> later decides to break the word there).
        /// </summary>
        private static (string CleanText, List<int> Candidates) StripSoftHyphens(string rawWord)
        {
            var segments = rawWord.Split('­');
            var sb = new StringBuilder();
            var candidates = new List<int>(segments.Length - 1);

            for (var i = 0; i < segments.Length; i++)
            {
                if (i > 0) candidates.Add(sb.Length);
                sb.Append(segments[i]);
            }

            return (sb.ToString(), candidates);
        }

        /// <summary>
        /// Applies the box's <see cref="TextTransform"/> to <paramref name="text"/>.
        /// Operates character-by-character (not via <see cref="string.ToUpperInvariant()"/>/
        /// <see cref="string.ToLowerInvariant()"/>) so the result is always the same length as the
        /// input - callers rely on word/whitespace boundary indices computed against the transformed
        /// text remaining valid.
        /// </summary>
        private static string ApplyTextTransform(string text, CssProperty<TextTransform> transform)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            switch (transform.Value)
            {
                case PeachPDF.CSS.TextTransform.Uppercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToUpperInvariant(chars[i]);
                    return new string(chars);
                }
                case PeachPDF.CSS.TextTransform.Lowercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    return new string(chars);
                }
                case PeachPDF.CSS.TextTransform.Capitalize:
                {
                    var chars = text.ToCharArray();
                    var atWordStart = true;
                    for (var i = 0; i < chars.Length; i++)
                    {
                        if (char.IsWhiteSpace(chars[i]))
                        {
                            atWordStart = true;
                        }
                        else if (atWordStart && char.IsLetter(chars[i]))
                        {
                            chars[i] = char.ToUpperInvariant(chars[i]);
                            atWordStart = false;
                        }
                    }
                    return new string(chars);
                }
                case PeachPDF.CSS.TextTransform.FullWidth:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = ToFullWidth(chars[i]);
                    return new string(chars);
                }
                default:
                    return text;
            }
        }

        /// <summary>
        /// Maps a single character to its fullwidth compatibility form per Unicode's &lt;wide&gt;
        /// decomposition mapping (used by CSS Text Module Level 3's <c>text-transform: full-width</c>).
        /// ASCII 0x21-0x7E map to U+FF01-FF5E (offset by U+FEE0), space maps to the ideographic space
        /// U+3000, and a handful of Latin-1 currency/symbol characters map to their own fullwidth forms
        /// in the U+FFE0-FFE6 range. Characters with no fullwidth form are returned unchanged. Does not
        /// implement the spec's &lt;narrow&gt;-tagged half (halfwidth katakana/Hangul jamo/symbol forms
        /// converting the other direction) - see
        /// .claude/accepted-gaps/text-transform-full-width-halfwidth-cjk-forms.md.
        /// </summary>
        private static char ToFullWidth(char c)
        {
            switch (c)
            {
                case ' ':
                    return '　';
                case >= '!' and <= '~':
                    return (char)(c + 0xfee0);
                case '¢':
                    return '￠';
                case '£':
                    return '￡';
                case '¬':
                    return '￢';
                case '¯':
                    return '￣';
                case '¦':
                    return '￤';
                case '¥':
                    return '￥';
                case '₩':
                    return '￦';
                default:
                    return c;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (BackgroundImages != null)
                foreach (var image in BackgroundImages)
                    image.Dispose();

            ListStyleImage?.Dispose();
            ContentImage?.Dispose();

            foreach (var childBox in Boxes)
            {
                childBox.Dispose();
            }
        }


        /// <summary>
        /// Re-entrancy guard for the keep-with-next first-line retry in <see cref="PerformLayoutImp"/> -
        /// prevents the retried layout pass from scheduling yet another retry.
        /// </summary>
        private bool _keepWithNextRetried;

        /// <summary>
        /// Whether a forced break falls before this box, resolved by <see cref="PerformLayoutPrologue"/>
        /// and read by the placement code. A field rather than a local because the two now run in
        /// separate methods — and, once a box can be laid out across several fragmentainer passes, in
        /// separate passes: the prologue runs only on the pass that first enters the box.
        /// </summary>
        private bool _isForcedBreak;

        /// <summary>
        /// Whether the break point before this box carries a forced break value at all, whether or not
        /// <i>this</i> box is the one that takes it. Wider than <see cref="_isForcedBreak"/> by exactly the
        /// §3.1 propagation case: a first in-flow child's own <c>break-before</c> is taken by the container
        /// it begins, so the child does not take one — but the author did declare a break at that point in
        /// the flow, so
        /// <see href="https://www.w3.org/TR/css-break-3/#break-margins">§5.2</see>'s truncation of margins
        /// adjoining an <i>unforced</i> break still must not reach this box's margin.
        /// </summary>
        private bool _adjoinsForcedBreakPoint;

        /// <summary>
        /// Whether this box registers a named-page entry, resolved by
        /// <see cref="PerformLayoutPrologue"/> and read by both registration sites.
        /// </summary>
        /// <remarks>
        /// This must be carried rather than re-derived: the early registration mutates
        /// <see cref="HtmlContainerInt.ActivePageName"/>, so a fresh
        /// <c>UsedPageName != ActivePageName</c> comparison would read false for an already-registered
        /// box and silently skip its Y-drift re-sync.
        /// </remarks>
        private bool _shouldRegisterPage;

        /// <summary>
        /// The side css-break-3 §3.1 requires the page after this box's forced break to fall on, or
        /// <see cref="PageSide.Any"/>. Resolved by <see cref="PerformLayoutPrologue"/> and acted on when
        /// the box is placed, once its preserved top margin is known.
        /// </summary>
        /// <remarks>
        /// Style, not geometry: the side is settled by the two break values at this box's own break point
        /// and by nothing that can move underneath it, which is why it stays latched in the prologue while
        /// the break's <i>target</i> does not (<see cref="ForcedBreakTopFor"/>).
        /// </remarks>
        private PageSide _forcedBreakSide;

        /// <summary>
        /// Whether this box's position was set by a forced break (css-break-3 §3.1) — and so, equally,
        /// whether the break before it has already been taken in this layout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A forced break is a hard positional constraint, not a margin, so such a box anchors what
        /// follows it even when it is otherwise self-collapsing: <see cref="FoldMarginsPrecedingChild"/>'s
        /// walk-back must stop here rather than resolving the next box against an earlier sibling and
        /// undoing the break. The canonical case is an empty <c>&lt;div class="page-break"&gt;</c>
        /// marker, which has nothing in it to collapse but everything to say about where the next
        /// section starts.
        /// </para>
        /// <para>
        /// <b>It is also the latch that stops the break being taken twice.</b> The target is re-derived at
        /// every placement rather than latched once (<see cref="ForcedBreakTopFor"/>), so this is what
        /// tells a second placement of the same box that its break is already spent — and, because
        /// <see cref="PerformLayoutPrologue"/> retracts it, what tells a re-decided break that it is not.
        /// Those two facts are the same fact, which is why there is one field for them rather than two.
        /// </para>
        /// </remarks>
        internal bool PlacedByForcedBreak { get; private set; }

        /// <summary>
        /// Where this box stopped, when it could not finish inside the fragmentainer the current pass is
        /// filling. Read by the parent's child loop, which wraps it in a link of its own and returns in
        /// turn, so the record unwinds to the fragmentation-context root.
        /// </summary>
        internal BreakToken? PendingBreakToken { get; private set; }

        /// <summary>
        /// Takes this box's resumption record, clearing it — how an engine driving fragmentainers of its
        /// own reads back where a column stopped before opening the next one.
        /// </summary>
        /// <remarks>
        /// The ordinary path never needs this: a parent's child loop reads a <i>child's</i> record and
        /// wraps it in a link of its own. A columns engine is reading its own, because it is standing in
        /// for the driver rather than for a parent.
        /// </remarks>
        internal BreakToken? TakePendingBreakToken()
        {
            var token = PendingBreakToken;
            PendingBreakToken = null;
            return token;
        }

        /// <summary>
        /// Records that this box could not finish the fragmentainer it is filling, for an engine that
        /// drives its own and has to hand the remainder back to the page driver.
        /// </summary>
        internal void SetPendingBreakToken(BreakToken? token) => PendingBreakToken = token;

        /// <summary>
        /// Lets this box's prologue run again, for a caller that is about to lay it out from scratch
        /// rather than continue it.
        /// </summary>
        /// <remarks>
        /// The prologue is once-per-box-per-layout and owns <c>RectanglesReset</c> plus word measurement,
        /// so a second real layout of the same box needs it back. Deliberately narrow: it does <b>not</b>
        /// touch the resumption record or the §4.3 latch, which belong to the pass rather than to the box
        /// being re-laid-out. Same reopening the keep-with-next retry performs on itself.
        /// </remarks>
        internal void ResetForRefill()
        {
            // A caller that resets a whole remaining child range on every page/retry (PassRewind.RollBackTo)
            // re-asks this for children the page in hand will never reach, over and over, across every later
            // page too - and a box already sitting in exactly the state this method produces, untouched since,
            // has nothing left to redo. Skipping needs no extra bookkeeping to stay correct: _awaitingRefill
            // clears the moment this box's own pass genuinely starts again (BeginBlockPass), which is the
            // only event that could make a repeat of this method do something different (see #573).
            if (_awaitingRefill) return;
            _awaitingRefill = true;

            // The columns engine calls this before the real fill lays the same boxes out again from scratch,
            // which on a resumed pass includes boxes a frozen fragmentainer already holds.
            NotifyGeometryChanged(OwnGeometryTop(), 0);

            _prologueDone = false;

            AwaitPlacement();

            // This box's whole subtree is about to be laid out from scratch, so every descendant's own
            // layout runs again regardless of its _prologueDone - but a descendant whose own prologue does
            // not re-run (because ResetForRefill was called at this box's level, not its) skips the one
            // line of that prologue that lets a forced break fire twice: PlacedByForcedBreak = false. A
            // break-before below a box a pass re-entry replays is then read as already taken and silently
            // never retaken. A full recursive _prologueDone reset would fix it too, but at the cost of
            // re-measuring every word and re-running every string-set/named-page registration in the whole
            // subtree - clearing only the one-shot latch that guards retaking a forced break is the
            // narrower fix, and safe on its own: _isForcedBreak/_forcedBreakSide/_adjoinsForcedBreakPoint
            // are settled from style alone and do not go stale between passes.
            foreach (var child in Boxes)
            {
                child.AllowDescendantForcedBreaksToBeRetaken();
            }
        }

        /// <summary>
        /// Clears <see cref="PlacedByForcedBreak"/> on this box and every box in its subtree, without
        /// touching anything else the prologue owns.
        /// </summary>
        /// <remarks>
        /// The recursive half of <see cref="ResetForRefill"/> - see its remarks for why this is narrower
        /// than a full prologue reset and why that is safe.
        /// </remarks>
        private void AllowDescendantForcedBreaksToBeRetaken()
        {
            PlacedByForcedBreak = false;

            foreach (var child in Boxes)
            {
                child.AllowDescendantForcedBreaksToBeRetaken();
            }
        }

        /// <summary>
        /// Marks every word in this subtree as belonging to the next fragmentainer, so that only the ones
        /// the layout about to run actually places can be claimed by it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A discarded attempt leaves its words where it put them, and the attempt that replaces it need not
        /// reach all of them again — a shorter column stops sooner. Cleared per word by being positioned
        /// (<see cref="CssRect.Top"/>'s setter), so what survives is exactly what this layout did not place:
        /// the same rule §4.1's own discarded line already follows, applied to a discarded fill.
        /// </para>
        /// <para>
        /// The other caller is the flow itself: a word a stopped flow never reached carries no position of
        /// its own either, and the one it carries instead — document Y 0 — lies inside the first slot's own
        /// band. So the block's inline flow says the same thing about itself before it starts, on the pass
        /// that opens it (<c>CssLayoutEngine.CreateLineBoxes</c>).
        /// </para>
        /// </remarks>
        internal void AwaitPlacement()
        {
            foreach (var word in Words)
            {
                word.AwaitsTheNextFragmentainer = true;
            }

            foreach (var childBox in Boxes)
            {
                childBox.AwaitPlacement();
            }
        }

        /// <summary>
        /// Drops the line boxes this box's inline flow produced from <paramref name="firstLine"/> on, for a
        /// fill attempt that is being abandoned and run again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The narrower half of <see cref="ResetForRefill"/>, and the only thing a <i>resumed</i> box can be
        /// given. Its prologue must not run — <see cref="RectanglesReset"/> would blank the fragment an
        /// earlier fragmentainer already holds — but the lines the abandoned attempt added must still go, or
        /// the retry hands them to <see cref="CssLineBox.AssignRectanglesToBoxes"/> a second time and the
        /// per-line rectangle they already carry throws. <c>CssLayoutEngine.CreateLineBoxes</c> finalizes
        /// from <c>InlineBreakToken.CompletedLineCount</c>, so that is the index to undo from and no new
        /// accounting is needed.
        /// </para>
        /// <para>
        /// The boxes a line assigned rectangles to are exactly its own <see cref="CssLineBox.Rectangles"/>
        /// keys, so the removal reaches every descendant the line reached — including the inline boxes whose
        /// rectangles a resumed flow deliberately does not reset.
        /// </para>
        /// <para>
        /// A word on a discarded line is left where the abandoned attempt put it, so it is marked as
        /// belonging to the next fragmentainer for the same reason §4.1's discarded line's words are: the
        /// position it carries describes nothing. Being positioned again by the retry clears it.
        /// </para>
        /// </remarks>
        internal void DiscardLineBoxesFrom(int firstLine)
        {
            var first = Math.Max(0, firstLine);

            for (var i = LineBoxes.Count - 1; i >= first; i--)
            {
                var line = LineBoxes[i];

                foreach (var hosted in line.Rectangles.Keys)
                {
                    hosted.Rectangles.Remove(line);
                }

                foreach (var word in line.Words)
                {
                    word.AwaitsTheNextFragmentainer = true;
                }

                LineBoxes.RemoveAt(i);
            }
        }

        /// <summary>
        /// One <c>widows</c> rewind per box per layout — not per pass. The rewound pass reaches this
        /// epilogue again, and asking a second time either finds the constraint satisfied or finds it
        /// unsatisfiable at a different line count; either way, re-deciding is how a box walks backwards
        /// through the document.
        /// </summary>
        private bool _widowsRewindTaken;

        /// <summary>
        /// Tries to satisfy <see href="https://www.w3.org/TR/css-break-3/#widows-orphans">§5.4</see>'s
        /// <c>widows</c> by keeping fewer lines in the fragment <i>before</i> the break, so the fragment
        /// after it reaches its minimum — the per-line correction the spec asks for, rather than moving the
        /// whole box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one break decision that has to reach <i>backwards</i>. The count of lines after a
        /// break is settled only once the box completes, which is a later pass than the one that placed its
        /// first fragment — so the fragment that has to give lines up has already been laid out and emitted.
        /// The driver re-runs that pass (<c>HtmlContainerInt.RequestWidowsRewind</c>); what this method owns
        /// is deciding whether a budget exists that satisfies both constraints at once.
        /// </para>
        /// <para>
        /// <b>Two fragments only.</b> The budget is a line count for the fragment before the break, so it is
        /// only meaningful when the lines before the break all sit in <i>one</i> fragment — otherwise a
        /// budget below the count an earlier fragment already completed would be asking a fragmentainer that
        /// is not being re-run to give lines up. A box spanning three fragmentainers therefore keeps the
        /// whole-box push, which is what it had before.
        /// </para>
        /// <para>
        /// <b>The budget must satisfy <c>orphans</c> too.</b> Giving up lines to feed <c>widows</c> can only
        /// go as far as leaving <c>orphans</c> lines behind; below that the two constraints cannot both hold
        /// and §4.3's ladder gives one of them up rather than trading one violation for another.
        /// </para>
        /// </remarks>
        private bool TryKeepFewerLinesForWidows(int linesBefore, int widows, int orphans)
        {
            if (_widowsRewindTaken || !OrphansAndWidowsMayMoveABreak
                || HtmlContainer is not { IsFragmenting: true })
            {
                return false;
            }

            // What the fragment before the break may keep so the one after it reaches `widows`.
            var budget = LineBoxes.Count - widows;

            if (budget < 1 || budget < orphans || budget >= linesBefore) return false;

            var firstSlot = HtmlContainer.PageIndexOf(LineBoxes[0].LineTop);

            if (HtmlContainer.PageIndexOf(LineBoxes[linesBefore - 1].LineTop) != firstSlot) return false;

            // The lines given up move into the box's later fragment, so the two pages have to share one
            // measure or they arrive wrapped for the page they left.
            if (!HtmlContainer.MeasureIsSharedBetween(
                    firstSlot, HtmlContainer.PageIndexOf(LineBoxes[^1].LineTop)))
            {
                return false;
            }

            if (!HtmlContainer.RequestWidowsRewind(this, budget)) return false;

            _widowsRewindTaken = true;
            return true;
        }

        /// <summary>
        /// The document Y this box asked to be placed at in a later fragmentainer, when the placement
        /// code decided the break falls <i>before</i> it. Distinct from
        /// <see cref="PendingBreakToken"/> because the box cannot name itself in a token — only its
        /// parent knows its index — so the parent converts this into a break-before link.
        /// </summary>
        internal double? RequestedBreakBeforeTop { get; private set; }

        /// <summary>The pagination slot <see cref="RequestedBreakBeforeTop"/> falls in.</summary>
        internal int RequestedBreakBeforeSlot { get; private set; }

        /// <summary>
        /// Whether the requested break is a forced <i>page</i> break raised while a nested fragmentainer
        /// was being filled, and so is not that fragmentainer's to satisfy
        /// (<see cref="BlockBreakToken.EscapesNestedFragmentainer"/>).
        /// </summary>
        internal bool RequestedBreakEscapesNestedFragmentainer { get; private set; }

        /// <summary>
        /// What an escaping forced break settled on the pass that raised it, waiting for the pass that
        /// actually places this box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The forced-break arm of <c>PlaceBlockChild</c> runs on the pass that <i>declines</i> to place
        /// the box; the pass that places it takes the resumed-target branch instead, which is not the
        /// branch that asserts either of these. And between the two, a nested engine re-opens this box's
        /// prologue (<see cref="PassRewind.RollBackTo"/>, called from <c>CssLayoutEngineColumns</c>'s own
        /// fill retry), which retracts both so a re-decided break can re-assert them — right for a break
        /// being decided again, wrong for one already decided and travelling in a record.
        /// </para>
        /// <para>
        /// Deliberately <b>not</b> cleared by the prologue, for that reason; cleared per layout in
        /// <see cref="BeginLayoutPass"/> like the other one-shot latches, and consumed on arrival.
        /// </para>
        /// <para>
        /// <b>Not replaceable by re-deriving the escape the way <see cref="ForcedBreakTopFor"/> re-derives
        /// its target</b>, and the per-<i>layout</i> clearing above is why. The record that carries the
        /// escaping break outlives the layout generation it was raised in — the driver re-feeds it to the
        /// box on the reflow layout that follows — but these do not, so a resume in the <i>second</i>
        /// generation deliberately asserts nothing at all. Anything derived afresh from the record or the
        /// box would assert on both, which reserves a page the first generation's own prologue had already
        /// retracted: measured turning
        /// <c>DirectionalBreakInsideMulticol_DegradesToAColumnBreak_KnownBoundary</c> from two pages into
        /// three with the middle one blank. Whether that asymmetry is right is
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/545">#545</see>'s question, not this
        /// field's.
        /// </para>
        /// </remarks>
        private bool _escapedForcedBreakPending;

        /// <summary>
        /// The slot a directional escaping break reserved to land on the side it names, or null when the
        /// break named no side or already landed on one.
        /// </summary>
        private int? _escapedForcedBreakBlankSlot;

        /// <summary>
        /// How this box resumes on the current pass, or null when it is being laid out from the start.
        /// </summary>
        private BreakToken? _incomingToken;

        /// <summary>
        /// The placement this box was granted when its parent broke before it — the already-computed
        /// target the margin-truncation and keep-with-next paths worked out, which must be used as-is
        /// rather than re-derived (re-deriving it would reach the same "does not fit" conclusion and
        /// break again, forever).
        /// </summary>
        private double? _resumeTopOverride;

        /// <summary>
        /// Whether <see cref="PerformLayoutPrologue"/> has already run for this box in the current
        /// layout. It runs once per box per layout, not once per fragmentainer pass.
        /// </summary>
        private bool _prologueDone;

        /// <summary>
        /// Whether <see cref="ResetForRefill"/> has already brought this box into the state a pass
        /// re-entry needs, with nothing having touched it since - so a second call before the box is
        /// genuinely laid out again would repeat exactly the same work for no new effect.
        /// </summary>
        /// <remarks>
        /// Set by <see cref="ResetForRefill"/> itself and cleared the moment this box's own pass
        /// genuinely starts again (<see cref="BeginBlockPass"/>, guarded the same way
        /// <see cref="_prologueDone"/> is - both flip together because a box's prologue re-running and a
        /// box being "used since its last reset" are the same event). A caller that resets a box already
        /// in this state (e.g. <see cref="Fragmentation.PassRewind.RollBackTo"/> re-resetting a
        /// container's whole remaining child list on every page/retry, most of which the page in hand
        /// never reaches) can skip the whole subtree walk rather than repeat it - see
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/573">#573</see>.
        /// </remarks>
        private protected bool _awaitingRefill;

        /// <summary>
        /// Where <see cref="PerformLayoutImp"/> is to re-place this box, set when an
        /// <see cref="EarlyBreak"/> is taken by re-laying the box out rather than by moving it.
        /// </summary>
        private double? _earlyBreakRetryTop;

        /// <summary>
        /// Whether this box has already taken an <see cref="EarlyBreak"/> on this fragmentainer pass.
        /// </summary>
        /// <remarks>
        /// Not a re-entrancy guard — a latch. The relocated box's own epilogue runs again and asks the
        /// same question again, and an unsatisfiable <c>avoid</c> is <i>relaxed</i> rather than skipped
        /// (§5.3), so the arm answers "still does not fit, move it" every time. One correction per box
        /// per pass is also what [§4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) sanctions:
        /// a bounded reconsideration, not an open-ended search.
        /// </remarks>
        private bool _earlyBreakTaken;

        /// <summary>
        /// Whether the break before this box has already been moved once for <c>orphans</c> in this layout.
        /// </summary>
        /// <remarks>
        /// Per <i>layout</i>, not per fragmentainer pass, and that is the point: the decision is taken by the
        /// parent's child loop, which runs again on every pass, and moving the box forward can perfectly well
        /// leave it in the same position relative to the next boundary — most easily when the keep-with-next
        /// run travels with it, since the box then lands the same distance below the fragmentainer top it did
        /// before. Repeated, that walks the box down the document one pass per page, and the driver's own cap
        /// is 100,000 passes (#332 measured exactly this shape). One correction, then the box takes whatever
        /// geometry gives it — which is <see cref="Fragmentation.BreakRelaxation"/>'s fourth tier.
        /// </remarks>
        private bool _orphansBreakTaken;

        /// <summary>
        /// A break decision a child discovered that falls before one of <i>this</i> box's earlier
        /// children — the keep-with-next run pull, which is the one correction a box cannot carry out
        /// for itself.
        /// </summary>
        /// <remarks>
        /// Read by <see cref="LayoutBlockChildren"/>, which is the only thing that can act on it, and
        /// deliberately separate from <see cref="PendingBreakToken"/>/<see cref="RequestedBreakBeforeTop"/>:
        /// those unwind to the driver and end the fragmentainer, while this is resolved inside the pass
        /// that raised it and never leaves the parent.
        /// </remarks>
        private EarlyBreak? _requestedChildRestart;

        /// <summary>
        /// Whether this box is currently inside <see cref="LayoutBlockChildren"/>, and so can act on a
        /// <see cref="_requestedChildRestart"/> raised by the child it is laying out.
        /// </summary>
        /// <remarks>
        /// Asked rather than assumed, because plenty of callers run a box's layout without being in a
        /// position to re-run its siblings — <see cref="LayoutOutOfFlowChildren"/>, the <c>::marker</c>
        /// call in the epilogue, and every layout engine. A request none of them would collect has to
        /// degrade to the translation instead of being silently dropped.
        /// </remarks>
        private bool _canRestartChildLoop;

        /// <summary>
        /// The <see cref="HtmlContainerInt.LayoutGeneration"/> this box last laid out in. Resumption
        /// state left behind by an earlier layout — the unrestricted-width double layout, the
        /// per-page-width reflow loop, <c>ShrinkToFit</c>'s re-layout — is recognised as stale by this
        /// and discarded, rather than being resumed into.
        /// </summary>
        private int _layoutGeneration;

        /// <summary>
        /// The pagination slot <see cref="Fragmentation.FragmentEmitter"/> last observed this box's whole
        /// subtree produce <i>nothing</i> in, or -1. Meaningless unless
        /// <see cref="_emittedNothingGeneration"/> is the current layout generation.
        /// </summary>
        private int _emittedNothingAtSlot = -1;

        /// <inheritdoc cref="_emittedNothingAtSlot"/>
        private int _emittedNothingGeneration = -1;

        /// <summary>
        /// How many reopening events (<c>FragmentEmitter.InvalidateFrom</c>) had been recorded when this
        /// observation was made — checked against <see cref="Fragmentation.InvalidationHistory"/> at read
        /// time so only a reopening that could actually have affected this box's own recorded slot
        /// retires the observation, rather than every reopening retiring every box's.
        /// </summary>
        private int _emittedNothingRecordedAt = -1;

        /// <summary>
        /// Records that this box's subtree contributed nothing to pagination slot
        /// <paramref name="slotIndex"/> — so the emitter may skip descending into it while filling later
        /// slots, until something clears the record again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is an <i>observation</i>, never a prediction: it is written only after the emitter has
        /// walked the whole subtree and found no rectangle, no word, no child fragment and no
        /// continuation shell, and only for a box that has already produced a fragment somewhere (so it
        /// is behind the layout frontier, not merely unreached). Both halves matter — see
        /// <c>FragmentEmitter.BuildDraft</c> for why the unreached case cannot be concluded from the
        /// same evidence.
        /// </para>
        /// <para>
        /// Deliberately not derived from <see cref="Location"/>/<see cref="ActualBottom"/>: several
        /// layout engines rewrite a box's extent well after its own layout pass has finished, and the
        /// multi-column engine keeps each column's geometry in a separate snapshot, so a box's live
        /// fields do not describe every fragment it has.
        /// </para>
        /// </remarks>
        internal void RecordEmittedNothingAt(int slotIndex, int invalidationCountNow)
        {
            _emittedNothingAtSlot = slotIndex;
            _emittedNothingGeneration = HtmlContainer?.LayoutGeneration ?? 0;
            _emittedNothingRecordedAt = invalidationCountNow;
        }

        /// <summary>
        /// Whether this box was observed to emit nothing at a slot at or before
        /// <paramref name="slotIndex"/>, and nothing has invalidated that observation since.
        /// </summary>
        internal bool EmittedNothingAtOrBefore(int slotIndex, Fragmentation.InvalidationHistory history) =>
            _emittedNothingGeneration == (HtmlContainer?.LayoutGeneration ?? 0)
            && _emittedNothingAtSlot >= 0
            && slotIndex >= _emittedNothingAtSlot
            && history.StillSafe(_emittedNothingRecordedAt, _emittedNothingAtSlot);

        /// <summary>
        /// Discards this box's <see cref="RecordEmittedNothingAt"/> observation and every ancestor's,
        /// because something has just given it, or may have given it, content it did not have when the
        /// observation was made.
        /// </summary>
        /// <remarks>
        /// Walks up <see cref="ParentBox"/> and stops at the first ancestor that holds no observation:
        /// an ancestor is only ever marked when its whole subtree was empty, so once one is clear
        /// everything above it is clear too. That makes this O(1) amortized on the hot paths that call
        /// it — during a layout pass almost every box is already clear.
        /// </remarks>
        internal void DiscardEmittedNothing()
        {
            var generation = HtmlContainer?.LayoutGeneration ?? 0;

            for (var box = this; box is not null; box = box.ParentBox)
            {
                var wasClear = box._emittedNothingGeneration == -1 && box._touchedGeneration == generation;

                box._emittedNothingGeneration = -1;
                box._emittedNothingAtSlot = -1;

                // Every caller of this is a write that gives a box content or moves it, so it is also
                // the signal layout has REACHED this box at all - which is what separates a subtree
                // that is finished from one that has not started. Recorded on the same walk because the
                // two questions have exactly the same answer set.
                box._touchedGeneration = generation;

                // Nothing above an already-clear, already-touched box can need either update: both are
                // only ever set walking up from below, so one clear ancestor means all of them are.
                if (wasClear) break;
            }
        }

        /// <summary>
        /// The layout generation in which anything wrote to this box's geometry — see
        /// <see cref="DiscardEmittedNothing"/>, which is every such write's common path.
        /// </summary>
        private int _touchedGeneration = -1;

        /// <summary>
        /// Whether layout has not yet reached this box at all in the current generation, so it holds no
        /// positioned content and cannot appear in <i>any</i> fragmentainer yet.
        /// </summary>
        /// <remarks>
        /// This is what lets the emitter skip the whole second half of a long document — the chapters
        /// layout has not started — rather than only the finished half behind it. It is sound in the one
        /// place the "observed empty" record is not: a box may be empty at a slot either because its
        /// content is behind us or because it is still ahead, and within one <c>EmitPass</c> range the
        /// emitter freezes slots the pass has <i>already</i> flowed content into, so emptiness alone
        /// cannot tell those apart. Never having been written to can: the first write that positions
        /// anything here discards the record, and that write necessarily happens before the pass which
        /// places it ends, hence before the slot that needs it is frozen.
        /// </remarks>
        internal bool NeverTouchedThisLayout => _touchedGeneration != (HtmlContainer?.LayoutGeneration ?? 0);

        /// <summary>
        /// <see cref="DiscardEmittedNothing"/> for this box, every ancestor, and every descendant — for a
        /// change that moves where a whole subtree <i>draws</i> without writing to any box in it (a
        /// fragment displacement, or a captured-geometry translation).
        /// </summary>
        internal void DiscardEmittedNothingIncludingDescendants()
        {
            DiscardEmittedNothing();

            foreach (var child in Boxes)
            {
                child.DiscardEmittedNothingIncludingDescendants();
            }
        }

        #region Private Methods

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.<br/>
        /// </summary>
        /// <param name="g">Device context to use</param>
        /// <summary>
        /// Lays out this box's out-of-flow (absolutely/fixed-positioned) direct children. The flex and table
        /// layout engines only place in-flow items and deliberately skip out-of-flow children (CSS Flexbox 1
        /// §4 / CSS2.1 §9.7: an absolutely-positioned child of a flex/table container does not participate in
        /// flex/table layout), so — unlike the generic block-children loop, which lays out every child — those
        /// children would otherwise never get a <see cref="PerformLayout"/> call. Running it here, after the
        /// engine has sized this container, lets each such child resolve its own <c>width</c>/<c>height</c>
        /// (e.g. <c>width: 100%</c>) and <c>left</c>/<c>top</c> against this now-sized containing block, exactly
        /// as the block path already does. Recurses naturally: each child's own <see cref="PerformLayoutImp"/>
        /// runs this again for its out-of-flow descendants.
        /// </summary>
        private async ValueTask LayoutOutOfFlowChildren(RGraphics g)
        {
            foreach (var childBox in Boxes)
            {
                if (childBox.IsOutOfFlow && childBox.DerivedStyle.ActualDisplay != CssConstants.None)
                {
                    await LayoutBlockChild(g, childBox);
                }
            }
        }

        /// <summary>
        /// One layout pass of this box, as <paramref name="frame"/> drives it — the seam a box kind that
        /// replaces the generic block pass overrides.
        /// </summary>
        /// <param name="g">the device context</param>
        /// <param name="frame">
        /// the frame driving this pass, which is this box's parent (or the box itself, for the root, which
        /// has no frame above it to stand in for it). Handed <i>down</i> rather than looked up, because the
        /// caller is what decides whether this box is a block-flow child at all — see
        /// <see cref="LayoutBlockChild"/>.
        /// </param>
        /// <param name="framePlacesChild">whether <paramref name="frame"/> assigns this box a position</param>
        /// <remarks>
        /// The base implementation is the generic block pass, driven in phases by the frame
        /// (<see cref="DriveBlockChildPass"/>). Three box kinds override it with a pass of their own — the
        /// horizontal rule (its size is a formula of its own, and it runs no prologue at all), an outside
        /// <c>::marker</c> (positioned beside its item rather than in any flow) and a repeated row group's
        /// proxy (its content was laid out elsewhere and is only translated here) — none of which has a
        /// prologue, a placement and a content phase that could be separated. The rule still asks the frame
        /// for what is genuinely the frame's, through <see cref="PlaceAsBlockChild"/>.
        /// </remarks>
        protected virtual ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild) =>
            frame.DriveBlockChildPass(g, this, framePlacesChild);

        /// <summary>
        /// Lays out one of this frame's block-level children: this frame decides where the child goes, has
        /// the child resolve its inline size against that, commits the offset, and only then hands the
        /// child its own content to lay out.
        /// </summary>
        /// <param name="g">the device context</param>
        /// <param name="child">the child to place and lay out</param>
        /// <param name="framePlacesChild">
        /// whether this frame assigns the child's position at all. False for a child a layout engine has
        /// already placed (<c>ItemContentCommit</c>'s commit pass): every earlier item layout in such an
        /// engine is a <i>measurement</i>, moved into place by translation afterwards, so it is harmless
        /// for this frame's block-flow arithmetic to run during those — but the commit pass is the item's
        /// real, final content layout, with nothing after it to correct a wrong position back. Nor is the
        /// child's own inline size safe to resolve again there: its <c>Words.Count &gt; 0</c> branch
        /// measures the leftover words of the layout this call is about to replace rather than the pinned
        /// <c>Width</c> the engine set, and for an inline-content box the two can disagree, corrupting the
        /// very re-wrap that pass exists to get right.
        /// </param>
        /// <remarks>
        /// <b>This is where a block-level box's layout is entered</b>: at the frame, not at the box. The
        /// frame's own child loop calls this for each child it owns, and every other caller reaches it
        /// through <see cref="PerformLayout"/>, which names the frame on the box's behalf. Error handling
        /// is <see cref="PerformLayout"/>'s, so a loop that drives its children through here reports a
        /// layout failure exactly as one calling <see cref="PerformLayout"/> on each of them did.
        /// </remarks>
        internal async ValueTask LayoutBlockChild(RGraphics g, CssBox child, bool framePlacesChild = true)
        {
            try
            {
                await child.PerformLayoutImp(g, this, framePlacesChild);
            }
            catch (Exception ex)
            {
                if (child.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Layout, "Exception in box layout", ex);
            }
        }

        /// <summary>
        /// Drives one layout pass of <paramref name="child"/> from this frame, in the three phases the
        /// pass is made of: the child opens it, this frame places the child, and the child then lays its
        /// own content out inside the position it was given.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The phases are in this order because each needs the one before it. Placement reads what the
        /// child's prologue settles — whether a forced break falls before it, which side that break names,
        /// the used page name the commit registers — and the child's inline size is resolved from the page
        /// the offset lands on, so the words the prologue measures have to exist by then
        /// (<see cref="PlaceAndSizeBlockChild"/>). Content comes last because everything laid out inside
        /// the child reads the width and origin the first two phases settled.
        /// </para>
        /// <para>
        /// A frame that declines to place the child at all — §5.2 concluding the break falls before it —
        /// skips the content phase entirely, which is what makes a break before a box produce no fragment
        /// in the fragmentainer it is leaving.
        /// </para>
        /// </remarks>
        private async ValueTask DriveBlockChildPass(RGraphics g, CssBox child, bool framePlacesChild)
        {
#if DEBUG
            Console.WriteLine($"layout start: {child}");
#endif

            var resume = await child.BeginBlockPass(g);

            // Bounded by _earlyBreakTaken: the epilogue may conclude, once, that this box has to start
            // somewhere else, and the only honest way to act on that is to lay it out again there.
            while (true)
            {
                var placed = true;

                if (child.PlacesItselfAsBlockBox && framePlacesChild)
                {
                    if (resume is null)
                    {
                        placed = await PlaceAndSizeBlockChild(g, child);
                    }
                    else
                    {
                        child.ResumeInTheNextFragmentainer();
                    }
                }

                if (await child.LayoutPassContents(g, resume, placed) is not { } retryTop) return;

                // The same one-shot channel a break-before uses, for the same reason: the target has
                // already been worked out and must not be re-derived here.
                child._resumeTopOverride = retryTop;

                // A retry re-places this box; it does not continue where a previous fragmentainer left
                // off. The prologue deliberately does not run again — everything it settles is either
                // already consumed or overridden by the target above, and re-running it would register
                // this box's named strings and named page a second time.
                resume = null;
            }
        }

        /// <summary>
        /// Lays this box's own content out at the position a layout engine has already assigned it, without
        /// the frame above deciding a position of its own.
        /// </summary>
        /// <remarks>
        /// The commit pass of the flex and grid engines is the caller (<c>ItemContentCommit</c>). Which
        /// children a frame positions is the frame's own question, asked once where the pass is driven from,
        /// so an engine-positioned item is simply an item nothing calls
        /// <see cref="ResolveBlockChildOffset"/>/<see cref="CommitBlockChildOffset"/> for — rather than one
        /// that is placed and then has to notice, from inside its own layout, that it should not have been.
        /// </remarks>
        internal ValueTask LayoutContentAtItsAssignedPosition(RGraphics g) =>
            (ParentBox ?? this).LayoutBlockChild(g, this, framePlacesChild: false);

        /// <summary>
        /// Opens this box's layout pass: picks up the resumption record left for it, and runs its
        /// once-per-layout prologue if no earlier pass has.
        /// </summary>
        private async ValueTask<BreakToken?> BeginBlockPass(RGraphics g)
        {
            var resume = BeginLayoutPass();

            // Once per box per layout, never once per fragmentainer pass - see the method's own remarks
            // for what re-running it would destroy.
            if (!_prologueDone)
            {
                _prologueDone = true;

                // This box's pass is genuinely starting again - the one event that can make a later
                // ResetForRefill() need to do real work rather than skip as already-reset (see its remarks).
                _awaitingRefill = false;

                await PerformLayoutPrologue(g);
            }

            return resume;
        }

        /// <summary>
        /// Lays this box's own content out inside the position its frame has just given it, and closes the
        /// pass.
        /// </summary>
        /// <param name="g">the device context</param>
        /// <param name="resume">this pass's resumption record, or null when the box is laid out afresh</param>
        /// <param name="placed">
        /// whether the frame placed this box at all. False when it declined — §5.2's margin truncation
        /// concluding the break falls before the box — in which case the box contributes nothing to the
        /// fragmentainer being filled and its content waits for the next pass.
        /// </param>
        /// <returns>
        /// where this box has to be laid out again, when its epilogue concluded it must start somewhere
        /// else; null when the pass is done with it.
        /// </returns>
        private async ValueTask<double?> LayoutPassContents(RGraphics g, BreakToken? resume, bool placed)
        {
            if (placed)
            {
                await LayoutContents(g, resume);
            }

            // Positioned here rather than from the epilogue, so that a box which does not finish in
            // this fragmentainer still has its marker - see the method's own remarks for why that is
            // the pass which places the item, and only that one.
            if (MarkerBelongsToTheFragmentainerBeingFilled(resume))
            {
                await LayoutOutsideMarker(g);
            }

            if (PendingBreakToken is not null || RequestedBreakBeforeTop is not null)
            {
                TakeBackTheMarkerOfAnItemThisPassKeptNothingOf();

                // This box did not finish in this fragmentainer. Its epilogue judges a *complete*
                // box, so it waits for the pass that completes it; the record unwinds from here.
                PublishBreakToTheContextRoot();
                return null;
            }

            await PerformLayoutEpilogue(g);

            if (_earlyBreakRetryTop is not { } retryTop) return null;

            _earlyBreakRetryTop = null;
            return retryTop;
        }

        /// <summary>
        /// Whether the pass now running is the one whose fragmentainer this list item's <c>outside</c>
        /// <c>::marker</c> belongs to — the pass that must position it.
        /// </summary>
        /// <param name="resume">
        /// this pass's resumption record, or null when it is the pass that <i>places</i> the item.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>The marker belongs to the fragmentainer its item begins in</b> (CSS 2.1 §12.5.1 / CSS Lists
        /// Level 3 §3.1: beside the item's <i>first</i> line box), and that is settled the moment the item is
        /// placed — it is positioned against the item's own border box rather than against its content, so
        /// neither the item's height nor how much of it fits here is an input. So the pass that places the
        /// item is the pass that positions the marker, and a pass that <i>resumes</i> it must not: whatever it
        /// does to the item's own <see cref="CssBox.Location"/>, the marker's place in the document
        /// was decided a fragmentainer ago.
        /// </para>
        /// <para>
        /// <b>Both halves cost a measured defect.</b> Positioned from <see cref="PerformLayoutEpilogue"/>,
        /// which runs only on the pass that <i>completes</i> the item, a straddling item's marker got its
        /// coordinates after the slot they fall in had been frozen, and was claimed by no fragment at all and
        /// painted on no page (<see href="https://github.com/jhaygood86/PeachPDF/issues/444">#444</see>).
        /// Positioned again on a resumed pass, it moved with the item: inside a column that means
        /// <see cref="ResumeInTheNextFragmentainer"/>'s new inline position, so the bullet appeared beside the
        /// continuation in the later column instead of beside the item's own first line, and the earlier
        /// column's <see cref="Fragments.BoxGeometrySnapshot"/> and the live box then held two origins for one
        /// word (<see href="https://github.com/jhaygood86/PeachPDF/issues/468">#468</see>). Leaving it where
        /// the placing pass put it needs nothing else: a column's snapshot already records word origins of
        /// its own, and <c>FragmentEmitter</c>'s region test rejects a marker sitting in a neighbouring
        /// column's inline span.
        /// </para>
        /// <para>
        /// A pass that requested a break <i>before</i> this box declined to place it at all, so there is no
        /// position for the marker to sit against; the pass that does place it lays it out afresh, with no
        /// resumption record, and answers true here.
        /// </para>
        /// </remarks>
        private bool MarkerBelongsToTheFragmentainerBeingFilled(BreakToken? resume) =>
            RequestedBreakBeforeTop is null
            && (resume is null || OutsideMarkerAwaitsPlacement());

        /// <summary>
        /// Whether this list item's <c>outside</c> <c>::marker</c> is still waiting to be positioned by this
        /// layout — the state <see cref="AwaitPlacement"/> puts every word into and being positioned takes it
        /// out of (<c>CssRect.Top</c>'s setter).
        /// </summary>
        /// <remarks>
        /// It is the marker's own record of "no pass has placed me yet", so it is what lets a resumed pass
        /// answer <see cref="MarkerBelongsToTheFragmentainerBeingFilled"/> true without any second bookkeeping
        /// channel: the pass that placed the item took its own positioning back
        /// (<see cref="TakeBackTheMarkerOfAnItemThisPassKeptNothingOf"/>), or an abandoned multi-column fill
        /// attempt did (<see cref="ResetForRefill"/>), and the marker is still owed a fragmentainer.
        /// </remarks>
        private bool OutsideMarkerAwaitsPlacement() =>
            OutsideMarkerChild is { Words.Count: > 0 } marker && marker.Words[0].AwaitsTheNextFragmentainer;

        /// <summary>
        /// Un-positions this list item's <c>outside</c> <c>::marker</c> when the pass that placed the item
        /// went on to keep none of its content, so that the pass which does keep some positions it instead.
        /// </summary>
        /// <remarks>
        /// A pass places a box before it discovers how much of it fits, and the answer can be "none": the
        /// break then falls <i>before</i> the item (css-break-3 §3.1 propagation, §5.4's orphans floor, or a
        /// column's own overflow arm), and the fill drops the item from that fragmentainer's geometry
        /// altogether. The marker positioned against that placement would be the only thing left of the item
        /// there — beside nothing, in a fragmentainer whose captured geometry no longer holds its item, so
        /// claimed by nothing and painted on no page, which is
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/444">#444</see>'s symptom reached from the
        /// other direction. Measured on a 660-document multi-column sweep: 9 markers, every one of them
        /// <c>column-fill: balance</c>.
        /// <para>
        /// "Kept nothing" is asked of the item's own content rather than of the break record, because the
        /// decision that drops it is the <i>parent's</i> and is not made until this box's layout has
        /// returned. Content placed by an earlier pass counts as kept: the item does have a fragment
        /// somewhere, and taking the marker back again would be how it goes missing.
        /// </para>
        /// </remarks>
        private void TakeBackTheMarkerOfAnItemThisPassKeptNothingOf()
        {
            if (DerivedStyle.ActualDisplay != CssConstants.ListItem) return;

            if (OutsideMarkerChild is not { } marker) return;

            if (!HasPlacedContent(this, marker)) marker.AwaitPlacement();
        }

        /// <summary>
        /// This box's <c>outside</c> <c>::marker</c> child, or null when it has none — the single scan every
        /// caller shares, so the three that need it cannot drift apart.
        /// </summary>
        private CssBox? OutsideMarkerChild
        {
            get
            {
                foreach (var childBox in Boxes)
                {
                    if (IsOutsideMarker(childBox)) return childBox;
                }

                return null;
            }
        }

        /// <summary>
        /// Whether any content of <paramref name="box"/>'s subtree has been placed by some pass of this
        /// layout, skipping the subtree rooted at <paramref name="excluded"/>.
        /// </summary>
        /// <remarks>
        /// <b>Two kinds of content, because a word alone does not answer it.</b> A positioned word says so
        /// directly (<see cref="AwaitPlacement"/> marks them all as owed a fragmentainer and
        /// <c>CssRect.Top</c>'s setter clears each as it is placed). But an item can keep content carrying no
        /// words at all — a run of empty block children with heights of their own — and reading only words
        /// there reports "kept nothing" for an item that plainly did keep something, handing its marker to a
        /// later fragmentainer than the one its first line is in. So a placed in-flow block child counts too:
        /// one that has been given a height is one this pass found room for. A box with a pending record has
        /// <c>ActualBottom == Location.Y</c>, so a child that stopped without placing anything does not
        /// answer true through this arm.
        /// <para>
        /// <c>display: none</c> and out-of-flow subtrees are skipped: neither contributes to the fragment the
        /// question is about, and a hidden text node would otherwise make any item look like it kept
        /// something.
        /// </para>
        /// </remarks>
        private static bool HasPlacedContent(CssBox box, CssBox excluded)
        {
            foreach (var word in box.Words)
            {
                if (!word.AwaitsTheNextFragmentainer) return true;
            }

            foreach (var childBox in box.Boxes)
            {
                if (ReferenceEquals(childBox, excluded)) continue;
                if (childBox.DerivedStyle.ActualDisplay == CssConstants.None || childBox.IsOutOfFlow) continue;

                if (childBox.PlacesItselfAsBlockBox && childBox.ActualBottom > childBox.Location.Y) return true;

                if (HasPlacedContent(childBox, excluded)) return true;
            }

            return false;
        }

        /// <summary>
        /// Lays out this list item's <c>outside</c> <c>::marker</c> (the CSS default), which is deliberately
        /// excluded from the item's own inline flow (<c>CssLayoutEngine.FlowBox</c>) and from the generic
        /// block-children loop (<see cref="LayoutBlockChildren"/>) alike, so this is the one call that
        /// positions it. An <c>inside</c> marker is an ordinary flowed child that has already positioned
        /// itself, and no-ops here (<see cref="CssBoxMarker.PerformLayoutImp"/>'s own
        /// <c>ListStylePosition</c> check) — which is why this scans for any marker rather than through
        /// <see cref="OutsideMarkerChild"/>: that no-op still measures the marker's words on its way to
        /// returning, and narrowing the scan would take the measurement with it.
        /// </summary>
        /// <remarks>
        /// Called after <see cref="LayoutContents"/> rather than immediately after placement, and that
        /// ordering is load-bearing: an item whose content is inline opens its flow by saying it has placed
        /// none of its subtree's words yet (<see cref="AwaitPlacement"/>, reaching the marker even though the
        /// flow never visits it), so positioning the marker first would have that statement take it straight
        /// back. Being positioned is what clears the flag (<c>CssRect.Top</c>'s setter), so the marker has to
        /// be positioned last.
        /// </remarks>
        private async ValueTask LayoutOutsideMarker(RGraphics g)
        {
            if (DerivedStyle.ActualDisplay != CssConstants.ListItem) return;

            foreach (var childBox in Boxes)
            {
                if (childBox.IsMarkerPseudoElement)
                {
                    await childBox.PerformLayout(g);
                    return;
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> is an <c>outside</c> <c>::marker</c> — the CSS default, and the one
        /// box that belongs to neither of a list item's flows.
        /// </summary>
        /// <remarks>
        /// It is positioned beside the item's principal block box rather than inside it (CSS 2.1 §12.5.1 /
        /// CSS Lists Level 3 §3.1), by <see cref="LayoutOutsideMarker"/> alone. Three places have to agree on
        /// which box that is — the inline flow (<c>CssLayoutEngine.FlowBox</c>), the block-children loop
        /// (<see cref="LayoutBlockChildren"/>), and the parser pass that gathers inline runs into anonymous
        /// blocks (<c>DomParser.JoinsTheInlineRun</c>) — so they ask here rather than each restating it. An
        /// <c>inside</c> marker is an ordinary flowed inline and answers false.
        /// </remarks>
        internal static bool IsOutsideMarker(CssBox box) =>
            box is { IsMarkerPseudoElement: true, ListStylePosition: not CssConstants.Inside };

        /// <summary>
        /// Picks up this box's resumption state for the pass that is starting, discarding anything left
        /// over from an earlier layout.
        /// </summary>
        private protected BreakToken? BeginLayoutPass()
        {
            var generation = HtmlContainer?.LayoutGeneration ?? 0;

            if (_layoutGeneration != generation)
            {
                _layoutGeneration = generation;
                _prologueDone = false;
                _incomingToken = null;
                _resumeTopOverride = null;
                _escapedForcedBreakPending = false;
                _escapedForcedBreakBlankSlot = null;
                _orphansBreakTaken = false;
                _widowsRewindTaken = false;

                // A box can end one layout generation sitting in "reset, not yet re-entered" state (e.g.
                // the last child a container's own RollBackTo touched, never revisited before the
                // generation ended) - carrying that flag into a brand new generation would let the first
                // ResetForRefill() call of the new one skip as a no-op, when nothing about this generation
                // has touched this box yet.
                _awaitingRefill = false;
            }

            var resume = _incomingToken;
            _incomingToken = null;
            PendingBreakToken = null;
            RequestedBreakBeforeTop = null;
            RequestedBreakEscapesNestedFragmentainer = false;

            // One §4.3 correction per box per fragmentainer pass. A resumed pass is a fresh chance to
            // make one, at coordinates the previous pass had not settled.
            _earlyBreakTaken = false;
            _earlyBreakRetryTop = null;

            return resume;
        }

        /// <summary>
        /// Hands the resumption record to the fragmentation context once it has unwound all the way to
        /// the context root, which is where the driver reads it.
        /// </summary>
        private void PublishBreakToTheContextRoot()
        {
            if (HtmlContainer?.CurrentFragmentainer is not { } context || !ReferenceEquals(this, context.ContextRoot))
                return;

            // The root itself has no parent to wrap a break-before request, so it stands in as one -
            // carrying every part of the request, the escape mark included, since nothing above it will
            // get another chance to.
            context.RecordBreak(PendingBreakToken
                                ?? new BlockBreakToken(this, RequestedBreakBeforeSlot, 0, null,
                                    IsBreakBefore: true, RequestedBreakBeforeTop,
                                    RequestedBreakEscapesNestedFragmentainer));
        }

        /// <summary>
        /// Records that the break falls before this box: it produces no fragment in the fragmentainer it
        /// is leaving, and resumes at <paramref name="top"/> in the next one
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §4.4</see>).
        /// </summary>
        private void RequestBreakBefore(double top, bool escapesNestedFragmentainer = false)
        {
            RequestedBreakBeforeTop = top;
            RequestedBreakBeforeSlot = HtmlContainer!.SlotStartingAt(top);
            RequestedBreakEscapesNestedFragmentainer = escapesNestedFragmentainer;
        }

        /// <summary>
        /// Seeds this box's resumption state for the pass about to run. Called by the parent's child
        /// loop immediately before it re-enters the child it stopped at.
        /// </summary>
        internal void ResumeAt(BreakToken? incomingToken, double? resumeTopOverride)
        {
            _incomingToken = incomingToken;
            _resumeTopOverride = resumeTopOverride;
        }

        /// <summary>
        /// Whether an engine has already decided this box's final <see cref="CssBox.Location"/>
        /// for the pass about to run, so the corrections that would move it afterwards must not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set by <c>ItemContentCommit</c> immediately before re-laying a flex or grid item's content out
        /// at the position <see cref="CssLayoutEngineFlex.AssignLocations"/>/line relocation already
        /// assigned it — every earlier item layout in those engines is a *measurement*, moved into place by
        /// translation afterward; this one is the item's real, final content layout, with nothing after it
        /// to correct a wrong position back.
        /// </para>
        /// <para>
        /// <b>It no longer decides whether the box is placed.</b> That is now the frame's own question,
        /// asked once where the pass is driven from (<see cref="LayoutBlockChild"/>'s
        /// <c>framePlacesChild</c>): a child an engine positions is simply a child the frame's loop does not
        /// call <see cref="ResolveBlockChildOffset"/>/<see cref="CommitBlockChildOffset"/> for. What is left
        /// here is the other half — the <see cref="PerformLayoutEpilogue"/> movers (the keep-with-next
        /// first-line retry, §4.3's <c>avoid</c>/monolithic relocation, §5.4's widows push), which run after
        /// the box is complete and would re-derive a position the engine owns.
        /// </para>
        /// </remarks>
        internal bool PositionAssignedByEngine { get; set; }

        /// <summary>
        /// Everything that must happen exactly once for this box, before any of its content is placed:
        /// measuring its words, applying <c>string-set</c>, resolving its used page name, and taking any
        /// forced break that falls before it.
        /// </summary>
        /// <remarks>
        /// Split out because it is precisely the part a <i>resumed</i> layout pass must not repeat —
        /// <see cref="RectanglesReset"/> would discard geometry already emitted into an earlier
        /// fragmentainer, <see cref="MeasureWordsSize"/> is expensive and resolves images, applying
        /// <c>string-set</c> is not idempotent, and a forced break must not fire a second time.
        /// </remarks>
        private async ValueTask PerformLayoutPrologue(RGraphics g)
        {
            if (DerivedStyle.ActualDisplay != CssConstants.None)
            {
                RectanglesReset();
                await MeasureWordsSize(g);
            }

            // Both registrations below append, and this prologue can run more than once inside a
            // single LayoutDocument invocation (a break decision taken against a finished box
            // re-lays it out at its new position - see PerformLayoutEpilogue). So withdraw what an
            // earlier run of *this* prologue registered before registering again; otherwise the box
            // accumulates one entry per re-entry, each recording a position it no longer occupies.
            // Across invocations PerformLayout's own ClearNamedStrings/ClearNamedPageElements still
            // does the wholesale job, which is why this went unnoticed.
            if (NamedStrings.Count > 0)
            {
                HtmlContainer?.UnregisterNamedStrings(NamedStrings.Values);
                NamedStrings.Clear();
            }

            // Apply named strings if string-set property is present
            if (!string.IsNullOrEmpty(StringSet) && StringSet != CssConstants.None)
            {
                CssNamedStringEngine.ApplyStringSet(this);
            }

            if (RegisteredNamedPageElement is { } staleRegistration)
            {
                HtmlContainer?.UnregisterNamedPageElement(staleRegistration);
            }

            // Whether or not the registry still held it, this pass's registration logic (early for
            // block containers below, tail sync/fallback at the end of PerformLayoutEpilogue) starts
            // clean rather than silently "syncing" an element it no longer owns.
            RegisteredNamedPageElement = null;

            // Spec (css-break §3.1): a forced break occurs at a class A break point if
            // the earlier sibling's break-after OR the later sibling's break-before has a
            // forced break value — at least one is sufficient.
            // Forced values include: page, always.
            //
            // Separately, CSS Paged Media Level 3 §3 (and CSS2.1 §13.2): a page break is also forced
            // whenever a box's *used* `page` value differs from the named page currently "in effect"
            // (the most recently registered name so far - see HtmlContainerInt.ActivePageName),
            // regardless of break-before/break-after. The used value is tree-based, not flow-based:
            // it is this box's own explicit `page` unless empty/"auto", in which case it is the parent
            // box's used value (root's "auto" -> empty). So a chapter body's ordinary paragraphs (and
            // any other descendants of the named element) inherit the same used name and don't each
            // force a break; but a *following sibling* of the named element - whose used value comes
            // from a common, un-named ancestor - correctly reverts, registering that reversion and
            // forcing a break back onto the reverted page.
            var previousSiblingForBreak = DomUtils.GetPreviousSibling(this, false);
            var hasExplicitPageName = !string.IsNullOrEmpty(PageName) && PageName != CssConstants.Auto;
            UsedPageName = hasExplicitPageName ? PageName : ParentBox?.UsedPageName ?? string.Empty;
            // A page break is forced only on a used-name *transition* (this includes a reversion, whose
            // used name comes from an un-named ancestor and differs from the active name). Registration
            // (below) is broader - it also re-registers a box carrying its own explicit name that
            // merely equals the active one, so a same-named element relocated by a layout engine still
            // has a registration entry the tail can re-sync; that registration is redundant for name
            // resolution but harmless (it resolves to the same name).
            var pageNameChanged = HtmlContainer is not null && UsedPageName != HtmlContainer.ActivePageName;
            _shouldRegisterPage = HtmlContainer is not null && (hasExplicitPageName || pageNameChanged);

            // css-break-3 §3.1 combination and propagation. A break-before on a container's first in-flow
            // child, and a break-after on its last, are values at the break point before or after the
            // *container*, so both sides of this break point are read through the chains of boxes they
            // begin and end - and a box whose own value travels outward that way does not take the break
            // itself, because the container it began does, and carries it along.
            // Asked in the page context, because this is the page vehicle: everything below realizes the
            // break by *placement*, putting the box at the next page's content top and leaving the
            // emitter's slot walk to cover what was stepped over. §3.1's `column` value cannot be carried
            // that way at all - every column of a container shares one block-axis band, so no coordinate
            // means "the next column" - and it is raised as a break decision by the parent's child loop
            // instead (CssBox.ForcedColumnBreakFallsBefore). A page break needs nothing there: it forces a
            // column break too, and reaching the next page ends every column on this one.
            var propagatesOutward = BreakPropagation.PropagatesBreakBeforeOutward(this);
            var ownForcedBefore = BreakPropagation.ForcedBreakBeforeAt(this, FragmentationContext.Page);
            var forcedBefore = propagatesOutward ? null : ownForcedBefore;
            var forcedAfter = previousSiblingForBreak is null
                ? null
                : BreakPropagation.ForcedBreakAfterAt(previousSiblingForBreak, FragmentationContext.Page);

            _isForcedBreak = forcedBefore is not null || forcedAfter is not null || pageNameChanged;

            // The value still governs this break point even where the container is what acts on it, so §5.2
            // leaves this box's margin alone either way. Without this, hoisting the break changed a stated
            // choice as a side effect: a box carrying a break that cannot be taken at all - because nothing
            // precedes the container in the flow - kept its margin before propagation and lost it after.
            _adjoinsForcedBreakPoint = _isForcedBreak || (propagatesOutward && ownForcedBefore is not null);

            // Every run of this prologue re-decides from scratch: the keep-with-next retry at the end of
            // PerformLayoutEpilogue clears _prologueDone and re-enters layout for this box at a new
            // position, where the break can legitimately land somewhere else. So this box's forced-break
            // state is retracted here and only re-asserted below (the side) or by the frame that places it
            // (that the break was taken, and the blank slot a directional value stepped over).
            _forcedBreakSide = PageSide.Any;
            PlacedByForcedBreak = false;
            HtmlContainer?.SetBlankSlotReservation(this, null);

            if (_isForcedBreak)
            {
                // Which side the content after the break has to begin on (css-break-3 §3.1's
                // left/right/recto/verso, which force one *or two* page breaks). Resolved here because it
                // is settled by the two break values at this break point and by nothing else, and acted on
                // in PlaceBlockChild: only that knows this box's preserved top margin, which can itself
                // carry the box past the slot the break landed in, and only boxes that reach it take the
                // break at all - a display:none or out-of-flow box runs this prologue but is never placed,
                // so reserving a blank page here would manufacture one for a break that is never taken.
                // The side comes from the two values resolved at *this* break point above - this box's
                // own break-before read through the chain it begins, and its immediate predecessor's
                // break-after read through the chain that one ends. Never the break anchor's: a
                // break-after states something about the break point after that box, and for a first
                // child the anchor's break point is several levels out. RequiredSide already accepts a
                // null second value, and resolves a conflict the way §3.1 does - to the value on the
                // latest element in flow.
                _forcedBreakSide = BreakValues.RequiredSide(forcedBefore, forcedAfter);
            }
        }

        /// <summary>
        /// Whether the frame above this box assigns it a block-level position at all
        /// (<see cref="PlaceAndSizeBlockChild"/>).
        /// </summary>
        /// <remarks>
        /// Everything else falls into <see cref="LayoutContents"/>'s else branch, which copies the
        /// <i>previous sibling's</i>
        /// <see cref="CssBox.Location"/> and <see cref="CssBox.ActualBottom"/> — a
        /// <c>display: none</c> box, a <c>table-row</c>, a bare inline. So any later code that measures this
        /// box's own height, or moves it, has to ask this first: for those boxes the coordinates belong to
        /// something else and both the measurement and the move are meaningless.
        /// </remarks>
        internal bool PlacesItselfAsBlockBox =>
            IsBlock
            || DerivedStyle.ActualDisplay is CssConstants.ListItem or CssConstants.Table or CssConstants.InlineTable
                       or CssConstants.TableCell or CssConstants.Flex or CssConstants.InlineFlex
                       or CssConstants.Grid or CssConstants.InlineGrid;

        /// <summary>
        /// Lays out this box's content, inside the position its frame has already given it — the part of
        /// layout a resumed pass re-enters, picking up where the previous fragmentainer stopped rather
        /// than starting over.
        /// </summary>
        private async ValueTask LayoutContents(RGraphics g, BreakToken? resume)
        {
            if (PlacesItselfAsBlockBox)
            {
                // The engines MonolithicContent.RunsAnEngineOfItsOwn names, in the same order; this branch
                // needs to know *which* one, which is why it cannot ask the combined predicate.
                if (DerivedStyle.ActualDisplay is CssConstants.Flex or CssConstants.InlineFlex)
                {
                    // The record travels into the engine so a resumed pass can re-enter exactly the items
                    // that did not finish their own content last time (CssLayoutEngineFlex's commit pass),
                    // rather than re-measuring and re-positioning the whole container from scratch.
                    await LayoutEngineContent(g, CssLayoutEngineFlex.PerformLayout, resume);

                    if (PendingBreakToken is not null) return;
                }
                else if (DerivedStyle.ActualDisplay is CssConstants.Grid or CssConstants.InlineGrid)
                {
                    // The record travels into the engine so a resumed pass can re-enter exactly the row
                    // (and that row's items) that did not finish their own content last time
                    // (CssLayoutEngineGrid's commit pass), rather than re-measuring and re-placing the
                    // whole container from scratch.
                    await LayoutEngineContent(g, CssLayoutEngineGrid.PerformLayout, resume);

                    if (PendingBreakToken is not null) return;
                }
                else if (DerivedStyle.ActualDisplay is CssConstants.Table or CssConstants.InlineTable)
                {
                    // The record travels into the engine so it can tell a resumed pass from a fresh layout
                    // of the same box - the once-per-table half of its work is destructive when repeated
                    // on top of rows another pass has already emitted (see TableSetup).
                    //
                    // Not behind a detached fragmentainer any more (issue #464). While it was, nothing
                    // inside a cell had a fragmentainer to run out of, so a cell could not stop, the row
                    // loop could not record where it stopped, and the whole of a table's pagination had to
                    // happen inside one pass by relocating words one at a time. The engine now fills one
                    // fragmentainer per pass like every other content, and publishes where it stopped as
                    // this box's own PendingBreakToken.
                    await LayoutEngineContent(g, CssLayoutEngineTable.PerformLayout, resume);

                    // The row loop stopped, so the rows after it belong to the next fragmentainer and this
                    // box's epilogue - which judges a complete table - waits for the pass that completes it.
                    if (PendingBreakToken is not null) return;
                }
                else
                {
                    //If there's just inline boxes, create LineBoxes
                    if (DomUtils.ContainsInlinesOnly(this))
                    {
                        if (resume is null) ActualBottom = Location.Y;

                        //This will automatically set the bottom of this block
                        var stopped = await CssLayoutEngine.CreateLineBoxes(g, this, resume as InlineBreakToken);

                        if (stopped is not null)
                        {
                            // This block's remaining lines belong to the next fragmentainer.
                            PendingBreakToken = stopped;
                            return;
                        }

#if DEBUG
                        foreach (var lineBox in LineBoxes)
                        {
                            Console.WriteLine($"layout linebox: {lineBox} [h: {lineBox.LineBottom}]");
                        }
#endif

                    }
                    else if (EstablishesMultiColumnContext && Boxes.Count > 0)
                    {
                        // Not monolithic any more: a column is a fragmentainer, and this engine drives its
                        // own. It is handed the resumption record so a container continuing on a later page
                        // picks up where its last column stopped instead of starting over.
                        await CssLayoutEngineColumns.PerformLayout(g, this, resume);

                        if (PendingBreakToken is not null) return;
                    }
                    else if (Boxes.Count > 0)
                    {
                        if (await LayoutBlockChildren(g, resume)) return;

                        ActualRight = CalculateActualRight();

                        if (Boxes.Any(b => !b.IsOutOfFlow))
                        {
                            ActualBottom = MarginBottomCollapse();
                        }
                    }
                }
            }
            else
            {
                var prevSibling = DomUtils.GetPreviousSibling(this, false);
                if (prevSibling != null)
                {
                    if (Location == RPoint.Empty)
                        Location = prevSibling.Location;
                    ActualBottom = prevSibling.ActualBottom;
                }
            }
        }

        /// <summary>
        /// Whether this box is <see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see>
        /// monolithic content that the epilogue's page-context mover may move at all. Whether there is
        /// somewhere to move it <i>to</i> is a separate question, asked at the call site against the
        /// destination band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two exclusions, each for its own reason. An out-of-flow box is not in the flow this mover shifts
        /// — a fixed box is emitted in every fragmentainer at identical coordinates, so "the next page"
        /// names nothing for it. And a box that does not place itself
        /// (<see cref="PlacesItselfAsBlockBox"/>) holds its <i>previous sibling's</i> coordinates rather
        /// than its own, so both the measurement and the move would be about the wrong box: a
        /// <c>display: none</c> panel with <c>overflow: hidden</c> — an ordinary hidden modal or accordion
        /// body — was relocated on its neighbour's geometry, inflating the document by a page.
        /// </para>
        /// <para>
        /// And the whole question is gated on <see cref="HtmlContainerInt.IsFragmenting"/>, which is what
        /// keeps this out of subtrees whose placement an engine owns
        /// (<see cref="MonolithicContent.PaginatesItsOwnContent"/>) and out of measurement passes at
        /// provisional positions. A scroll container inside a table cell is placed by the table engine
        /// against its own row grid; shifting it against the <i>page</i> grid from here moves it out from
        /// under its row — which is exactly what the showcase diff caught. Inside those engines a
        /// monolithic box degrades to being split, the same boundary the directional-break parity step and
        /// the rest of the break machinery already have (#166/#308).
        /// </para>
        /// <para>
        /// The <c>break-inside: avoid</c> arm beside this one is deliberately <b>not</b> gated the same
        /// way: it predates the fragmentation context and its behaviour inside those engines is
        /// long-standing. This arm opts into the gate from the start rather than inheriting the problem.
        /// </para>
        /// </remarks>
        private bool IsMonolithicBoxThisMoverMayMove() =>
            !IsOutOfFlow
            && PlacesItselfAsBlockBox
            && HtmlContainer is { IsFragmenting: true }
            && MonolithicContent.IsMonolithic(this);

        /// <summary>
        /// Whether this box paginates its own content but recorded no break inside itself on this pass,
        /// so it did not fragment and the §4.3 mover beside this one applies to it as it does to content
        /// that <see cref="MonolithicContent.IsMonolithic">may not be broken at all</see>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Only a table asserts this, and it asserts it as a fact rather than as a property.</b> A
        /// table's own break points are between its rows, and whether one was taken is settled by the
        /// engine and recorded in <see cref="PageBreakBottoms"/> — so unlike §2's set, which is decided
        /// from style, this is a question that can only be answered once the box has finished laying out.
        /// That is exactly the epilogue's own position, and it is why the correction belongs here rather
        /// than at the end of <c>CssLayoutEngineTable.LayoutCells</c>, where it used to sit: the engine
        /// runs with the fragmentainer detached, so a decision taken there can only ever be a
        /// translation, and the whole point of stating a decision is to be able to lay the box out again
        /// at its destination.
        /// </para>
        /// <para>
        /// The engine's row-height estimate is what makes this necessary. Its own pre-checks decide from
        /// <c>EstimateRowHeight</c>, a one-line-of-text heuristic that can grossly undershoot a row whose
        /// cells hold tall block content, and when it misses the table straddles a boundary it was
        /// predicted to clear. A table that <i>did</i> break internally is not a candidate: it fragmented,
        /// so the boundary it crosses is one it chose.
        /// </para>
        /// </remarks>
        private bool PaginatedItsOwnContentWithoutBreaking() =>
            !IsOutOfFlow
            && DerivedStyle.ActualDisplay is CssConstants.Table or CssConstants.InlineTable
            && HtmlContainer is { IsFragmenting: true }
            && PageBreakBottoms is not { Count: > 0 };

        /// <summary>
        /// Whether this box, with the decorations §6.2 makes each fragment re-open and close with, fits
        /// inside <paramref name="destination"/>'s band.
        /// </summary>
        private bool FitsInFragmentainer(BlockConstraint destination)
        {
            var (clonedStart, clonedEnd) = MonolithicContent.ClonedBlockInsets(this, HtmlContainer!);

            return MonolithicContent.FitsInBand(
                ActualBottom - Location.Y, clonedStart, clonedEnd, destination.NextBandHeight);
        }

        /// <summary>
        /// Runs a layout engine that positions its own children, leaving breaking live for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Flex, grid and — since issue #464 — table. Their items are still <i>measured</i> with breaking
        /// suppressed — every measurement lays an item out at the container's content origin, and a break
        /// decided there names a position the item is about to be translated away from — but the engine
        /// itself needs to know whether breaking is live at all, so that the pass it runs once its items
        /// are finally placed can tell "this container is being paginated" from "this container is inside
        /// something that is measuring it".
        /// </para>
        /// <para>
        /// <see cref="LayoutOutOfFlowChildren"/> keeps a suppressed scope of its own, and that is not
        /// incidental: it <b>discards</b> any resumption record a child leaves behind, and it is the only
        /// way an absolutely-positioned child of one of these containers is laid out at all. Dropping a
        /// token there drops the content it names. It used to be safe because its caller suppressed;
        /// making that explicit is what keeps it safe now that the caller does not.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="engine">the engine to run over this box</param>
        /// <param name="resume">
        /// how this engine resumes on the current fragmentainer pass, or null when it is laying the box out
        /// from the start. The record is what lets it tell a <i>continuation</i> — earlier fragments already
        /// emitted — from a fresh layout of the same box, which is a distinction only the engine can act on.
        /// The table engine always reads one; grid reads one for the row its own commit pass stopped in;
        /// flex reads one for the line (row/row-reverse, any count) or the lines (column/column-reverse)
        /// its own commit pass stopped in. All three otherwise still pass null for a container/pass their
        /// own commit pass declined to run for (see each engine's remarks).
        /// </param>
        private async ValueTask LayoutEngineContent(
            RGraphics g, Func<RGraphics, CssBox, BreakToken?, ValueTask> engine, BreakToken? resume)
        {
            await engine(g, this, resume);

            var previous = HtmlContainer?.DetachFragmentainer();

            try
            {
                await LayoutOutOfFlowChildren(g);
            }
            finally
            {
                HtmlContainer?.RestoreFragmentainer(previous);
            }
        }

        /// <summary>
        /// Lays this box's block-level children into the fragmentainer the current pass is filling,
        /// resuming at the child the previous pass stopped at.
        /// </summary>
        /// <returns>
        /// true when a child could not finish, in which case this box has recorded where to pick up and
        /// stops. The children after that point are not laid out at all on this pass — which is what
        /// makes a break before a box produce no fragment for it in the fragmentainer it is leaving
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §4.4</see>).
        /// </returns>
        /// <summary>
        /// Whether <paramref name="token"/> says the fragment being left holds fewer line boxes than
        /// <paramref name="box"/>'s <c>orphans</c> minimum — in which case the break belongs <i>before</i>
        /// the box rather than inside it, so those lines travel with the rest
        /// (<see href="https://www.w3.org/TR/css-break-3/#widows-orphans">§5.4</see>,
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§4.4</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is <c>orphans</c> decided at the break point, which is the only place it can be decided
        /// forward.</b> How many lines fall <i>before</i> a break is known the moment the break is taken;
        /// how many fall after it is not, since the rest of the content has yet to be flowed. So the
        /// epilogue's retroactive whole-box push stays for <c>widows</c>, and the orphans half becomes a
        /// break decision like any other — which also brings the case that push deliberately skips into
        /// scope: a block taller than a band cannot be helped by moving it whole, but the break before it
        /// can perfectly well fall earlier.
        /// </para>
        /// <para>
        /// Keeping <i>no</i> line is the degenerate case (any <c>orphans</c> value is at least 1), and it
        /// is §4.4's own rule rather than §5.4's: a box with no fragment here should not be left as an
        /// empty stub. It is a column that makes that visible rather than a column that makes it true — on
        /// the page grid an empty box at the foot of a page is easy to miss, while a column is sized to its
        /// content, so the same box is a hole at the foot of one column with its text at the head of the
        /// next.
        /// </para>
        /// </remarks>
        private static bool KeepsFewerLinesThanOrphans(BreakToken token, CssBox box) =>
            token is InlineBreakToken inline && inline.LinesKeptHere < OrphansOf(box);

        /// <summary>
        /// <paramref name="box"/>'s <c>orphans</c> minimum, never below 1 — a block that keeps no line at
        /// all has no fragment here whatever the property says.
        /// </summary>
        private static int OrphansOf(CssBox box) =>
            int.TryParse(box.Orphans, out var orphans) && orphans > 1 ? orphans : 1;

        /// <summary>
        /// Whether anything precedes <paramref name="box"/> inside the fragmentainer being filled — the
        /// question "would starting it in the next one give it any more room than it has here?".
        /// </summary>
        private bool HasRoomAboveInThisFragmentainer(CssBox box) =>
            HtmlContainer?.CurrentFragmentainer is { } context
            && box.Location.Y > context.BandTop + HtmlContainerInt.PageBoundaryEpsilon;

        /// <summary>
        /// Whether a break may be moved for <c>orphans</c> or <c>widows</c> yet — false while per-page
        /// horizontal reflow is still settling which page each box is on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A document with per-page left/right <c>@page</c> margins is laid out several times over
        /// (<c>HtmlContainerInt.PerformLayout</c>'s bounded reflow loop): a box's width is resolved before its
        /// position is known, so each pass re-wraps every box against the page it turned out to land on, and
        /// the loop runs until the box→page assignment stops changing. A box's page is therefore
        /// <b>provisional</b> during that loop, and a break moved from a provisional assignment feeds back
        /// into the very thing the loop is trying to settle — observed as a document that no longer converges
        /// within the loop's cap, leaving a paragraph wrapped to a neighbouring page's measure, which is far
        /// more visible than the orphan it was avoiding.
        /// </para>
        /// <para>
        /// So the decisions are taken in <b>one final layout</b>, entered once the loop has settled
        /// (<see cref="HtmlContainerInt.PageWidthsSettled"/>). There is no feedback there: every width in
        /// that layout comes from the settled assignment, and nothing runs afterwards to be disturbed by what
        /// the corrections move. A document without per-page horizontal margins has no such loop and nothing
        /// provisional, so both minimums apply from its first pass, as they always have.
        /// </para>
        /// <para>
        /// The same reasoning as the <see cref="HtmlContainerInt.IsFragmenting"/> gates: a decision taken
        /// against coordinates the box does not end up at is not a decision. <c>widows</c>' <b>per-line</b>
        /// correction is gated for exactly the same reason — it re-runs a pass, so it moves content across a
        /// boundary the loop has yet to settle. The whole-box push is unaffected: it runs after the box is
        /// complete and does not change any box's width.
        /// </para>
        /// </remarks>
        private bool OrphansAndWidowsMayMoveABreak => HtmlContainer is { PageWidthsSettled: true };

        /// <summary>
        /// Lays this box's out-of-flow children out again, for an engine that narrowed its own inline
        /// extent while filling fragmentainers and so resolved them against the wrong containing block.
        /// </summary>
        internal ValueTask LayoutOutOfFlowChildrenAgain(RGraphics g) => LayoutOutOfFlowChildren(g);

        /// <summary>
        /// Runs the block-children loop for a layout engine that drives fragmentainers of its own, so it
        /// fills each one through the same path ordinary block flow does rather than a parallel copy.
        /// </summary>
        /// <remarks>
        /// The multi-column engine is the caller: a column is a fragmentainer
        /// (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>), and filling one is
        /// exactly "lay out children until one does not fit, then record where to pick up". Everything
        /// that makes that work — the resumption record, the keep-with-next restart, a child's own break
        /// before it — is this loop's, and duplicating it is how the two would drift apart.
        /// </remarks>
        internal ValueTask<bool> FillFragmentainerWithBlockChildren(RGraphics g, BreakToken? resume) =>
            LayoutBlockChildren(g, resume);

        private async ValueTask<bool> LayoutBlockChildren(RGraphics g, BreakToken? resume)
        {
            var resumeAt = resume as BlockBreakToken;
            var start = resumeAt?.ResumeChildIndex ?? 0;

            // An outside ::marker is not one of this box's block children: it is positioned beside the item's
            // principal block box rather than in its flow (CSS 2.1 §12.5.1), by the one call
            // CssBox.LayoutOutsideMarker makes. It reaches this loop only for a list item whose content is
            // block-level, where nothing wraps it into an anonymous block (DomParser.JoinsTheInlineRun); the
            // inline flow, which is where it sits for every other item, skips it for the same reason
            // (CssLayoutEngine.FlowBox). Stepping `start` over it as well as the loop keeps the resumption
            // record with the first child this loop actually lays out - the marker is Boxes[0], so a record
            // naming index 0 would otherwise be consumed by a child that is passed over and lost.
            while (start < Boxes.Count && IsOutsideMarker(Boxes[start])) start++;

            // One restart per run head per loop, so a run whose members keep reaching the same
            // conclusion cannot cycle.
            HashSet<int>? restartedHeads = null;

            // The pass's own resumption record is consumed the first time the child it names is laid out.
            // A restart at that same index re-*places* that child, and applying the record again would tell
            // it to continue a flow it is about to lay out afresh: it would keep the line boxes an earlier
            // fragmentainer produced and re-finalize them from the resumed index, which is the duplicate-key
            // failure CssLineBox.AssignRectanglesToBoxes reports. Reachable whenever the restart head is the
            // resumed child itself - which §3.1 propagation makes ordinary, since the container that travels
            // is the very box the pass resumed into.
            var resumeConsumed = false;

            _canRestartChildLoop = true;

            try
            {
                for (var i = start; i < Boxes.Count; i++)
                {
                    var childBox = Boxes[i];

                    if (IsOutsideMarker(childBox)) continue;

                    // Only the child the previous pass stopped at resumes; everything after it is laid out
                    // from the start, having never been reached.
                    if (i == start && resumeAt is not null && !resumeConsumed)
                    {
                        resumeConsumed = true;
                        childBox.ResumeAt(
                            resumeAt.ChildToken,
                            resumeAt.ResumeTopOverride ?? ColumnTopForTheChildThisFillBeginsAt(resumeAt, childBox));
                    }

                    // This frame places the child and then hands it its own content — the offset is
                    // appended by the loop rather than assigned by the child (see LayoutBlockChild).
                    await LayoutBlockChild(g, childBox);

                    if (_requestedChildRestart is { } restart)
                    {
                        _requestedChildRestart = null;

                        if (TryRestartAt(restart, start, i, ref restartedHeads, out var resumeFrom))
                        {
                            i = resumeFrom - 1;
                            continue;
                        }

                        // Nothing could be re-run, so the decision has to be carried out the other way.
                        TranslateForEarlyBreak(restart);
                    }

                    // A forced page break raised inside a nested fragmentainer escapes it (§3.1). Asked
                    // before every column question below, because it is the one break the container being
                    // filled may not answer: its own columns are all on the page the break is leaving, so
                    // converting it into a column break - which is what each of those arms would do - is
                    // honouring half of a stated choice. Every link the record passes through while still
                    // inside a nested fragmentainer carries the mark, so a container reads it whatever
                    // depth below itself the break was raised at, and a nested container hands it to the
                    // one enclosing it.
                    //
                    // Asked only while such a fragmentainer is being filled. Once the record is out of the
                    // last of them the mark names nothing - no page-grid site reads it - and intercepting
                    // here would skip the two arms below that a page-context loop does have answers for.
                    //
                    // Gated on IsFragmenting as well as HasOwnBand: a column context nested inside a scope
                    // that cannot record a break at all - the no-progress recovery's suppressed pass being
                    // the case that found it (#423) - would otherwise still intercept this and hand back a
                    // token nothing above it ever reads, silently dropping whatever content it named.
                    if (HtmlContainer is { IsFragmenting: true }
                        && HtmlContainer.CurrentFragmentainer is { HasOwnBand: true }
                        && EscapingBreakBefore(childBox, i) is { } escaping)
                    {
                        PendingBreakToken = escaping;
                        return true;
                    }

                    // §3.1's `column` forced break, and §3.2's `avoid-column`. Both are questions about the
                    // fragmentainer being filled, so both are asked here, where a column is what that is -
                    // and both are answered the same way, by breaking *before* the child, because a column
                    // has no coordinate of its own to place a box at.
                    //
                    // The `i > start` guard is what keeps both decisions terminating, and it is exactly
                    // the guard the column-overflow arm below already uses: a child moved to the next
                    // column becomes the child that column's fill *starts* at, so the question is not put
                    // to it a second time. A box that asks not to be broken and does not fit a whole
                    // column is therefore split rather than walked from column to column - §4.3's fourth
                    // tier, where the constraint is given up rather than acted on pointlessly.
                    // css-multicol-1 §3's column-span: all - a direct child of the multi-column container
                    // that establishes the very column context being filled, so a break falls before it
                    // exactly as a forced column break would, but for CssLayoutEngineColumns to read
                    // differently: not "open the next column of this run", but "this run ends here; lay the
                    // spanning box out at the container's own width, then start a fresh run after it". A
                    // deeper descendant marked column-span:all is deliberately not recognized here - only
                    // the box whose own loop is directly inside the fill (this == ContextRoot) asks the
                    // question, matching this engine's existing atomic-per-top-level-child model. Gated on
                    // EstablishesMultiColumnContext too: column-span has no effect outside a multi-column
                    // container (css-multicol-1 §3), and HasOwnBand alone does not say which kind of
                    // fragmentainer this is - a table row context could otherwise misread it.
                    //
                    // Checked before the ordinary forced-column-break/avoid-column arm below, not after:
                    // a column-span:all child that also happens to carry break-before:column or
                    // break-inside:avoid-column must still end this run rather than being pushed into
                    // "the next column of this run", which is the answer that arm would otherwise give it.
                    if (i > start
                        && EstablishesMultiColumnContext
                        && HtmlContainer is { IsFragmenting: true }
                        && HtmlContainer.CurrentFragmentainer is { HasOwnBand: true } spanContext
                        && ReferenceEquals(this, spanContext.ContextRoot)
                        && childBox.ColumnSpan.Value == ColumnSpanMode.All)
                    {
                        PendingBreakToken = new BlockBreakToken(
                            this, spanContext.SlotIndex, i, null, IsBreakBefore: true, null,
                            IsColumnSpanHandoff: true);
                        return true;
                    }

                    if (i > start
                        && HtmlContainer is { IsFragmenting: true }
                        && HtmlContainer.CurrentFragmentainer is { HasOwnBand: true } columnContext
                        && (ForcedColumnBreakFallsBefore(childBox) || AvoidsBreakingAcrossThisColumn(childBox)))
                    {
                        // No target: every column of a container begins at the same block-axis coordinate,
                        // and the columns engine restates the record in the next column's terms
                        // (CssLayoutEngineColumns.ResumeInTheNextColumn).
                        PendingBreakToken = new BlockBreakToken(
                            this, columnContext.SlotIndex, ColumnBreakFallsBefore(i, start, childBox, columnContext),
                            null, IsBreakBefore: true, null);
                        return true;
                    }

                    // The mirror case: childBox itself is the column-span:all box, laid out here because
                    // it is what this fill actually began at (i == start) - a leading span, or one this
                    // container is resuming straight into. The check above never fires for it (i == start,
                    // not i > start), so nothing else stops this loop from walking straight past it into
                    // whatever ordinary content follows, laid out at its full width rather than the
                    // multi-column run's own. Ends the flow immediately after it instead, so the caller
                    // opens a fresh run - at the resolved column geometry - for what comes next.
                    //
                    // Gated on childBox.PendingBreakToken being null: a span whose own content does not
                    // fit this fragmentainer already has a pending record of its own, naming where *it*
                    // continues - overwriting that here instead of falling through to the ordinary
                    // propagation arm below would silently drop everything from that point on, since
                    // nothing else carries the discarded record forward.
                    if (i == start
                        && i + 1 < Boxes.Count
                        && EstablishesMultiColumnContext
                        && childBox.ColumnSpan.Value == ColumnSpanMode.All
                        && childBox.PendingBreakToken is null
                        && HtmlContainer is { IsFragmenting: true }
                        && HtmlContainer.CurrentFragmentainer is { HasOwnBand: true } afterSpanContext
                        && ReferenceEquals(this, afterSpanContext.ContextRoot))
                    {
                        PendingBreakToken = new BlockBreakToken(
                            this, afterSpanContext.SlotIndex, i + 1, null, IsBreakBefore: true, null,
                            IsColumnSpanHandoff: true);
                        return true;
                    }

                    if (childBox.PendingBreakToken is { } childToken)
                    {
                        // css-break-3 §3.1 propagation: a break before a container's own first in-flow
                        // child is the break point before the container, so the container travels with it
                        // instead of being left spanning the boundary with an empty stub of its chrome on
                        // the page its content just left.
                        //
                        // This is also the only place the keep-with-next run for such a break can be
                        // collected: the run's members are siblings of the container, so they are in *this*
                        // box's Boxes and nowhere reachable from the box that actually broke.
                        if (i > start && EarlyBreak.NamesAPropagatingBreakBefore(childToken, childBox))
                        {
                            var propagatedTop = ((BlockBreakToken)childToken).ResumeTopOverride;

                            if (propagatedTop is { } runTarget
                                && HtmlContainer?.CurrentFragmentainer is not { HasOwnBand: true }
                                && EarlyBreak.Discover(childBox, runTarget, EarlyBreakReason.KeepWithNext)
                                    is { KeepWithNextRun.Count: > 0 } pull
                                && TryRestartAt(pull, start, i, ref restartedHeads, out var pulledFrom))
                            {
                                i = pulledFrom - 1;
                                continue;
                            }

                            PendingBreakToken = new BlockBreakToken(
                                this, childToken.ResumeSlotIndex, i, null, IsBreakBefore: true, propagatedTop);
                            return true;
                        }

                        // A child that kept too few lines here to satisfy its own orphans minimum - none
                        // at all being the degenerate case - has the break fall *before* it rather than
                        // inside it (§5.4, §4.4), so those lines travel with the rest of its content
                        // instead of being stranded at the foot of the fragmentainer being left.
                        // Nothing above it in this fragmentainer means moving it cannot help: it would keep
                        // the same too-few lines at the top of the next one, and ask again. That is the
                        // ladder's fourth tier (see BreakRelaxation) - the constraint is given up rather than
                        // acted on pointlessly - and it is what keeps a band too small for `orphans` lines
                        // from walking the box down the document.
                        if (KeepsFewerLinesThanOrphans(childToken, childBox) && i > start
                            && OrphansAndWidowsMayMoveABreak
                            && HtmlContainer!.MeasureIsSharedBetween(
                                HtmlContainer.SlotStartingAt(childBox.Location.Y),
                                childToken.ResumeSlotIndex)
                            && !childBox._orphansBreakTaken
                            && HasRoomAboveInThisFragmentainer(childBox))
                        {
                            childBox._orphansBreakTaken = true;

                            // The target has to travel with the break. A break-before whose top is left to
                            // be re-derived places the child at its natural position again - which for a box
                            // that kept a line or two is still inside the fragmentainer being left, so the
                            // resumed pass reaches the same conclusion and the driver's no-progress backstop
                            // is what ends it. Flush at the destination's content top, per §5.2: the margin
                            // adjoining an unforced break is truncated - which is what the band the record
                            // names begins at.
                            var orphanTarget = BlockConstraint
                                .AtSlot(HtmlContainer!, this, childToken.ResumeSlotIndex)
                                .AbsoluteBandTop;

                            // And a run chained to it by `avoid` travels too, exactly as it does when the
                            // epilogue's own orphans/widows mover relocates the box: the reason the break
                            // moved is different, but "do not strand the heading above it" is the same
                            // requirement (§3.1), and this is the only level the run's members are siblings
                            // at.
                            if (HtmlContainer.CurrentFragmentainer is not { HasOwnBand: true }
                                && EarlyBreak.Discover(childBox, orphanTarget, EarlyBreakReason.OrphansWidows)
                                    is { KeepWithNextRun.Count: > 0 } orphanPull
                                && TryRestartAt(orphanPull, start, i, ref restartedHeads, out var orphanFrom))
                            {
                                i = orphanFrom - 1;
                                continue;
                            }

                            PendingBreakToken = new BlockBreakToken(
                                this, childToken.ResumeSlotIndex, i, null, IsBreakBefore: true, orphanTarget);
                            return true;
                        }

                        PendingBreakToken = new BlockBreakToken(
                            this, childToken.ResumeSlotIndex, i, childToken, IsBreakBefore: false, null);
                        return true;
                    }

                    // Filling a fragmentainer of its own - a column - means a child that does not fit
                    // starts the next one. On the page grid nothing asks this: a block whose content is
                    // inline stops at the line that does not fit and records its own token, and one that
                    // does not (an explicit height, a replaced element) simply overflows the page. A
                    // column cannot afford that second answer; not flowing on is the whole point of it.
                    //
                    // Only a child with something above it in this column may move: one that overflows a
                    // column it already starts has nowhere better to be, and breaking before it would ask
                    // the same question of every column in turn.
                    if (i > start
                        && childBox.PlacesItselfAsBlockBox
                        && !childBox.IsOutOfFlow
                        && childBox.DerivedStyle.ActualDisplay != CssConstants.None
                        && HtmlContainer is { IsFragmenting: true }
                        && HtmlContainer.CurrentFragmentainer is { HasOwnBand: true } columnBand
                        && childBox.ActualBottom > columnBand.BandBottom)
                    {
                        PendingBreakToken = new BlockBreakToken(
                            this, columnBand.SlotIndex, ColumnBreakFallsBefore(i, start, childBox, columnBand),
                            null, IsBreakBefore: true, null);
                        return true;
                    }

                    if (childBox.RequestedBreakBeforeTop is { } childTop)
                    {
                        // The child cannot name itself in a token - only this box knows its index - so it
                        // asked, and this is where the ask becomes a link in the chain. The slot travels up
                        // unchanged: every link in a chain resumes in the same fragmentainer.
                        PendingBreakToken = new BlockBreakToken(
                            this, childBox.RequestedBreakBeforeSlot, i, null, IsBreakBefore: true, childTop);
                        return true;
                    }

                    // On the page grid, unlike a column (above), a child that has genuinely finished -
                    // no break requested at any level above - but whose own bottom still lands past the
                    // fragmentainer the pass opened with (css-break-3 §2 monolithic content with nowhere
                    // better to be; an explicit height; or simply enough accumulated content, table rows,
                    // or line boxes that it was never asked a single crossing question anywhere on the
                    // way) is left exactly where it is. But the siblings the loop places after it now flow
                    // into whatever band follows, with no break ever recording that crossing - #435's
                    // shape one level up from a word's own unbreakable overflow.
                    //
                    // Safe once (not before) the straddle predicate itself asks the fragmentainer being
                    // filled rather than the page grid (#435 stage 2): before that landed, stepping here
                    // for every child - rather than only genuinely monolithic ones - corrupted which slot
                    // the emitter attributed the whole pass to for a child whose position was merely
                    // *inherited* from an already-exempt ancestor (the document root's own margin, which
                    // collapses through html/body/div with no crossing ever decided for any of them) -
                    // regressing a margin-truncation fixture from one fragmentainer to six. Once the
                    // predicate agrees with the cursor, that inherited position is exactly as real as any
                    // other, and the narrower monolithic-only gate instead left an ordinary tall filler
                    // (no break of its own to take, but a bottom past the page it started on) leaving the
                    // cursor stale for whatever follows it.
                    if (!childBox.IsOutOfFlow
                        && childBox.DerivedStyle.ActualDisplay != CssConstants.None
                        && HtmlContainer?.CurrentFragmentainer is { HasOwnBand: false })
                    {
                        HtmlContainer.CurrentFragmentainer.StepOverTo(HtmlContainer.SlotEndingAt(childBox.ActualBottom));
                    }
                }
            }
            finally
            {
                _canRestartChildLoop = false;
                _requestedChildRestart = null;
            }

            return false;
        }

        /// <summary>
        /// Where <paramref name="childBox"/> goes when it is the child a <i>column's</i> fill begins at and
        /// the record that named it carries no target of its own — the content top of the column being
        /// filled, per <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The arms that raise a column break deliberately record no target: every column of a container
        /// begins at the same block-axis coordinate, so the columns engine states that coordinate on the
        /// record it hands to the next column
        /// (<c>CssLayoutEngineColumns.ResumeInTheNextColumn</c>). It can only state it on the record's
        /// <b>outermost</b> link, though, because that is the only one it holds — so a break raised inside a
        /// block nested below the container's own child arrives at that block's loop with nothing, and
        /// <see cref="PlaceBlockChild"/> falls back to deriving the child's top from its previous sibling.
        /// That sibling is still in the column just left, so the continuation was laid out at the foot of a
        /// column it is not in, straddling the boundary it was moved to avoid.
        /// </para>
        /// <para>
        /// Only for a child laid out here <i>afresh</i>. One that carries a record of its own is
        /// <i>continuing</i>, and moves itself in both axes
        /// (<see cref="ResumeInTheNextFragmentainer"/>) — writing a target for it would place it twice.
        /// </para>
        /// <para>
        /// The destination is <see cref="ContentTopOfTheContainingBlockIn"/>'s, which is also what
        /// <see cref="ResumeInTheNextFragmentainer"/> moves a <i>continuing</i> box to — the same question
        /// asked of the same containing block, so the two arms of one column's first placement cannot
        /// disagree.
        /// </para>
        /// </remarks>
        private double? ColumnTopForTheChildThisFillBeginsAt(BlockBreakToken resumeAt, CssBox childBox)
        {
            if (resumeAt.ChildToken is not null) return null;

            if (HtmlContainer?.CurrentFragmentainer is not { HasOwnBand: true } column) return null;

            return ContentTopOfTheContainingBlockIn(column, childBox.ContainingBlock);
        }

        /// <summary>
        /// Where <paramref name="containingBlock"/>'s content begins in <paramref name="column"/> — §2's
        /// fragmentainer content edge, plus only what
        /// <see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see> says is re-inserted
        /// there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The fragmentainer's own content edge is the whole of the base answer.</b> Nothing is added to
        /// it for the multi-column container itself, which is not fragmented by its own columns — its border
        /// and padding wrap every column at once, and
        /// <see cref="Fragmentation.FragmentainerContext.ResumeContentTop"/> is already inside them.
        /// </para>
        /// <para>
        /// <b>The maximum against <see cref="CssBox.ClientTop"/> this replaced is gone for two
        /// different reasons, depending on which box is asking</b>, and neither is "it was equivalent". For
        /// the container itself that coordinate is its position on the page it <i>began</i>, at or above the
        /// edge of the fragmentainer now being filled, so the maximum never chose it. For any box below the
        /// container it is the one thing that must <i>not</i> be chosen: it folds in exactly the block-start
        /// border and padding §6.2 declines to re-insert under <c>slice</c>, which is the defect. So this is
        /// a correction on the deep path and a simplification on the shallow one; removing it changes no
        /// result across the suite.
        /// </para>
        /// <para>
        /// <b>Every box below it that the fill reaches here is a continuation</b> — the record that brought
        /// this pass in descends through it — so the column boundary falls inside it and §6.2 decides
        /// whether its block-start border and padding are re-inserted at that edge. Under <c>slice</c> they
        /// are not, and reading its content edge unconditionally opened every continuation column with that
        /// much blank space and no border drawn in it, because paint has always got this right
        /// (<c>FragmentEmitter.ResumesAnEarlierFragment</c> clears the fragment's top edge). Under
        /// <c>clone</c> they are, for the box and for every cloning ancestor the break falls inside at once,
        /// which is exactly <see cref="DomUtils.ClonedBlockStart(CssBox?, CssBox?)"/>. This is the same
        /// arithmetic <c>CssLayoutEngine.CreateLineBoxes</c> writes for a resumed flow's first line; the
        /// block axis consulted the property nowhere at all, so <c>slice</c> and <c>clone</c> were
        /// indistinguishable here. Note the inline path is the <i>shape</i> to copy and not a working
        /// precedent at a column boundary — a cloning box's own decorations are measurably not re-opened
        /// there either; see the accepted-gap note on clone decorations at a multicol boundary.
        /// </para>
        /// <para>
        /// <b>What separates a re-opened box from one that is not is the fragmentation context, and it is
        /// stated once — as the walk's bound</b>
        /// (<see cref="DomUtils.ClonedBlockStart(CssBox?, CssBox?)"/>'s <c>stopAt</c>). Stopping at
        /// <see cref="Fragmentation.FragmentainerContext.ContextRoot"/> says exactly "sum the boxes this
        /// boundary falls inside", which is what §6.2 turns on, and it is what makes the container
        /// contribute nothing without a branch of its own. Left unbounded the walk runs past the container
        /// to the document root, so a container that itself sets <c>clone</c> added its own block-start
        /// border and padding to content inside it — spacing the fragmentainer's content edge already
        /// accounts for. Measured at 14pt of spurious indent with <c>padding-top: 9pt; border-top: 5pt</c>
        /// on the container.
        /// </para>
        /// <para>
        /// The near-miss is to ask instead whether the containing block is the box whose child loop is
        /// running. Because a child's <see cref="ParentBox"/> always <i>is</i> that box, such a test is false
        /// only when the loop's box is not a block container by <see cref="ContainingBlock"/>'s walk — and
        /// the containing block is then an ancestor sitting <i>higher in the same continuing chain</i>, so
        /// "it did not resume here" does not follow from it and that arm would re-insert the ancestor's
        /// decorations.
        /// </para>
        /// <para>
        /// Both sites that begin a column's content ask this — the child laid out afresh
        /// (<see cref="ColumnTopForTheChildThisFillBeginsAt"/>) and the box that continues into it
        /// (<see cref="ResumeInTheNextFragmentainer"/>). Fixing only the first left a continuation two or
        /// more levels deep with its own fragment rectangle 16pt <i>below</i> the content it holds, which is
        /// a worse failure than the blank strip: its background and border paint outside their own content.
        /// </para>
        /// </remarks>
        private static double ContentTopOfTheContainingBlockIn(FragmentainerContext column, CssBox containingBlock) =>
            column.ResumeContentTop + (containingBlock.HtmlContainer is { HasCloneDecorations: true }
                ? DomUtils.ClonedBlockStart(containingBlock, stopAt: column.ContextRoot)
                : 0);

        /// <summary>
        /// Whether <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>'s <c>column</c>
        /// forced break falls at the break point immediately before <paramref name="childBox"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both sides of the break point are read through the chains they begin and end, by the same
        /// <see cref="BreakPropagation"/> the page vehicle uses — only the context differs, which is the
        /// whole of what makes <c>column</c> visible here and invisible in
        /// <see cref="PerformLayoutPrologue"/>. A value travelling outward is not acted on here either:
        /// the break point before a container's first in-flow child is the container's, so the container
        /// is the box the break falls before.
        /// </para>
        /// <para>
        /// A value that forces a <i>page</i> break is left alone. It is the page vehicle's, which has
        /// already placed the box at the next page's content top; converting it into a column break here
        /// would honour half of a stated choice.
        /// </para>
        /// </remarks>
        private static bool ForcedColumnBreakFallsBefore(CssBox childBox)
        {
            if (BreakPropagation.PropagatesBreakBeforeOutward(childBox)) return false;

            var before = BreakPropagation.ForcedBreakBeforeAt(childBox, FragmentationContext.Column);

            if (before is not null && !BreakValues.IsForcedPageBreak(before)) return true;

            var previous = DomUtils.GetPreviousSibling(childBox, false);
            var after = previous is null
                ? null
                : BreakPropagation.ForcedBreakAfterAt(previous, FragmentationContext.Column);

            return after is not null && !BreakValues.IsForcedPageBreak(after);
        }

        /// <summary>
        /// Whether <paramref name="childBox"/> has begun splitting across the column boundary being filled
        /// while asking not to be broken by one
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-within">§3.2</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pending record <i>is</i> the statement that the child did not finish in this fragmentainer, so
        /// it is what the question is asked of: the box is about to be broken, and the answer is to break
        /// before it instead so it is laid out whole in the next column.
        /// </para>
        /// <para>
        /// Nothing was needed for a child that does not fragment internally — one that simply overflows
        /// the column is already moved whole by the arm below, which asks no break value at all — so this
        /// is only reachable at all because a child can genuinely split across a column, and it is why
        /// <c>avoid-column</c> was a no-op to implement before that was true.
        /// </para>
        /// </remarks>
        private static bool AvoidsBreakingAcrossThisColumn(CssBox childBox) =>
            childBox.PendingBreakToken is not null
            && BreakValues.AvoidsBreak(childBox.BreakInside, FragmentationContext.Column);

        /// <summary>
        /// The link of the chain to record when <paramref name="childBox"/> raised — or is passing on — a
        /// forced page break that escapes the nested fragmentainer being filled
        /// (<see cref="BlockBreakToken.EscapesNestedFragmentainer"/>), or null when it did neither.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two shapes, because the break can be raised at any depth: the child asked for it itself, or the
        /// child is a container whose own record already carries the mark. In both the target and the slot
        /// travel unchanged — they name the page grid, which is exactly what nothing between here and the
        /// driver may restate.
        /// </para>
        /// <para>
        /// Asked before §3.1 propagation, and safe to be: a forced break on a container's first in-flow
        /// child propagates outward at the prologue, so the box that requests the escape is already the
        /// outermost container the break begins. A record that names a first child which propagates
        /// outward therefore never carries this mark.
        /// </para>
        /// <para>
        /// The carried shape is what a nested multi-column container needs: its own engine hands the record
        /// up marked, and the column of the container enclosing it may not answer the break either. It is
        /// not reachable through an ordinary wrapper, because a forced break below a container's own child
        /// is lost before it is ever raised — the measurement pass that sizes the fill spends the one-shot
        /// target, since only the container's own children have their prologue re-opened
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/395">#395</see>, characterized by
        /// <c>AForcedPageBreak_BelowTheContainersOwnChild_IsLostEntirely_KnownBoundary</c>).
        /// </para>
        /// </remarks>
        private BlockBreakToken? EscapingBreakBefore(CssBox childBox, int childIndex)
        {
            if (childBox.RequestedBreakEscapesNestedFragmentainer && childBox.RequestedBreakBeforeTop is { } top)
            {
                return new BlockBreakToken(
                    this, childBox.RequestedBreakBeforeSlot, childIndex, null, IsBreakBefore: true, top,
                    EscapesNestedFragmentainer: true);
            }

            if (childBox.PendingBreakToken is BlockBreakToken { EscapesNestedFragmentainer: true } carried)
            {
                return new BlockBreakToken(
                    this, carried.ResumeSlotIndex, childIndex, carried, IsBreakBefore: false, null,
                    EscapesNestedFragmentainer: true);
            }

            return null;
        }

        /// <summary>
        /// The child a break at this column boundary really falls before: <paramref name="childIndex"/>,
        /// unless a run of preceding siblings is chained to that child by break avoidance
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>), in which case the
        /// head of that run — an <c>h2 { break-after: avoid }</c> heading must not be left at the foot of
        /// the column whose content has just moved into the next one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep-with-next is a question about a run of <i>preceding siblings</i>, and every page-context
        /// site answers it by <b>moving</b> them to a lower coordinate. A column has none to move them to
        /// — its columns all begin at the same block-axis coordinate — so the answer here is the one
        /// <c>avoid-column</c> already takes: state the break before the head instead, and let the next
        /// column's fill lay the whole run out there. Nothing is translated, and no group offset is
        /// computed.
        /// </para>
        /// <para>
        /// <b>Which column a run member is in is an index question here, not a geometric one.</b> Every
        /// column of a container shares one block-axis band, so a member of an earlier column has a
        /// document Y indistinguishable from this one's — the "does the run start in the fragmentainer
        /// being left" guard the page sites answer with coordinates says nothing at all here, and the
        /// index this column's fill began at is the only thing that does.
        /// </para>
        /// <para>
        /// §4.3's ladder otherwise applies as it does on the page grid: a run that cannot fit a whole
        /// column with the content it is chained to is trimmed from its <i>front</i> — the members nearest
        /// the breaking box are what the chain is about — and where no member can travel, the content
        /// moves alone. So does a box whose height is not yet known, which is every box raising the
        /// <c>avoid-column</c> arm: see the guard below.
        /// </para>
        /// </remarks>
        private int ColumnBreakFallsBefore(int childIndex, int start, CssBox childBox, FragmentainerContext column)
        {
            var run = DomUtils.GetPrecedingKeepWithNextRun(childBox, FragmentationContext.Column);

            if (run.Count == 0) return childIndex;

            // A box that has not finished cannot be measured, so the fit test below cannot be asked about
            // it: a pending record *is* the statement that its epilogue has not run, and its ActualBottom
            // is still its own top. Answering anyway reads as "the run always fits", which is how a run
            // gets moved into a column of its own while the box it is chained to breaks again in the next
            // one - the very outcome the test exists to prevent. §4.3's ladder gives a constraint up
            // rather than acting on it speculatively, so the content moves alone.
            if (childBox.PendingBreakToken is not null) return childIndex;

            // Only a box that positions itself owns its ActualBottom; anything else holds its previous
            // sibling's, so measuring it would ask the fit question about the wrong box.
            var extent = childBox.PlacesItselfAsBlockBox
                ? childBox.ActualBottom - childBox.Location.Y
                : 0;

            for (var head = 0; head < run.Count; head++)
            {
                var headIndex = Boxes.IndexOf(run[head]);

                // Soundness rather than the deciding test. A head below the index this column's fill began
                // at names a child of a column already filled, and breaking before it would state a record
                // whose resume index is behind this column's own start - the next column would lay that
                // content out a second time. One *at* it is the child this column began with, so breaking
                // before it moves nothing and puts the identical question to the next column forever.
                // In practice the fit test below reaches those conclusions first, because it is exact for
                // a whole box: a head it accepts really does fit the destination, so the destination does
                // not overflow and is not asked again. This does not depend on that remaining true.
                if (headIndex <= start) continue;

                var extraAbove = childBox.Location.Y - run[head].Location.Y;

                if (extraAbove <= 0) continue;
                if (extraAbove + extent > column.BandHeight) continue;

                return headIndex;
            }

            return childIndex;
        }

        /// <summary>
        /// Re-runs this box's children from the head of a keep-with-next run, so the run and everything
        /// after it is laid out at its final position rather than moved there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The guard that makes this safe is structural rather than geometric: the head's index must be
        /// at or after <paramref name="start"/>, the index this pass began at. Every child from there on
        /// was laid out by <i>this</i> pass, and every child below it belongs to a fragmentainer that is
        /// already filled and whose geometry nothing here may touch. It replaces the "does the run start
        /// on the same page" test the translation used, which asked the same question of coordinates.
        /// </para>
        /// <para>
        /// Only the head is told where to go — via <see cref="ResumeAt"/>'s target, the same channel an
        /// ordinary fragmentainer resumption hands a continuing child. Every member from the head on is
        /// simply <b>re-appended</b>: the rewound loop calls <see cref="PerformLayout"/> on each of them
        /// again, in order, exactly as it would for a child it had never reached yet, and each one
        /// re-derives its position from the sibling above it — which, for the head, is
        /// <see cref="PlaceBlockChild"/> reading the resumed target rather than deriving one, and for
        /// every member after it, the ordinary derivation now reads a sibling already re-placed. Nothing
        /// here has to clear a break latch of its own first: <see cref="BeginLayoutPass"/> resets
        /// <c>_earlyBreakTaken</c> for every box on every entry to <see cref="PerformLayoutImp"/>, which a
        /// re-appended child gets from the same call the ordinary walk already makes.
        /// </para>
        /// </remarks>
        private bool TryRestartAt(EarlyBreak restart, int start, int raisedAt, ref HashSet<int>? restartedHeads, out int resumeFrom)
        {
            resumeFrom = Boxes.IndexOf(restart.BeforeBox);
            if (resumeFrom < 0 || resumeFrom > raisedAt) return false;

            // Below the index this pass began at, the head belongs to a fragmentainer the driver has
            // already filled — nothing this loop can re-run, but something the driver can, by re-entering
            // the pass that filled it. Asked here rather than at the call site so that "this head cannot
            // be re-run from here" keeps one home, with every guard above it applying to both answers.
            // A granted request makes everything this pass produces moot, so the loop simply carries on:
            // resuming one past the box that raised the decision is what "carry on" is spelt as here.
            if (resumeFrom < start)
            {
                resumeFrom = raisedAt + 1;
                return HtmlContainer!.RequestPassRewind(restart.BeforeBox, restart.Top);
            }

            restartedHeads ??= [];

            if (!restartedHeads.Add(resumeFrom)) return false;

            Boxes[resumeFrom].ResumeAt(null, restart.Top);
            return true;
        }

        /// <summary>
        /// Carries out <paramref name="decision"/> by moving its subject and its run, for the callers that
        /// cannot re-run anything — see <see cref="CanBeLaidOutAgain"/> for when that applies.
        /// </summary>
        /// <remarks>
        /// Static, and reading the boxes to move off the decision rather than off a receiver, because the box
        /// that travels is not always the one that discovered the decision: §3.1 propagation can put it on a
        /// container the discovering box begins, and <see cref="OffsetTop(double)"/> is deep, so moving the container
        /// moves the box inside it exactly once.
        /// </remarks>
        internal static void TranslateForEarlyBreak(EarlyBreak decision)
        {
            // The run's head lands on the decision's target and everything below it keeps its distance,
            // so the spacing inside the group survives the move.
            var offset = decision.Top - decision.BeforeBox.Location.Y;

            foreach (var member in decision.KeepWithNextRun)
            {
                member.OffsetTop(offset);
            }

            decision.Subject.OffsetTop(offset);
        }

        /// <summary>
        /// Moves this box to the origin of the fragmentainer a resumed pass is filling, where that
        /// fragmentainer is one of its own rather than a page — a multi-column column.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every fragmentainer of the page grid shares one inline position, which is why
        /// <see cref="PlaceAndSizeBlockChild"/> deliberately does not run again on a resumed pass: CSS
        /// Fragmentation Level 3 §2 gives a box one inline size across all of its fragments, and re-deriving its top from
        /// the previous sibling would read the end of the whole flow. A column differs from its neighbours in
        /// exactly the axis that rule holds constant, so a box continuing into one has to be moved there —
        /// otherwise its continuation is laid out over the fragment it just left.
        /// </para>
        /// <para>
        /// The block axis moves too, to where its containing block's content begins in this fragmentainer
        /// (<see cref="ContentTopOfTheContainingBlockIn"/>) — the same coordinate
        /// <see cref="ColumnTopForTheChildThisFillBeginsAt"/> hands the box that begins the column having
        /// been laid out afresh, since both are the first thing inside that containing block here. This box
        /// begins the fragmentainer, so it has no predecessor to resolve against, and §5.2 truncates the
        /// margin adjoining the unforced break that put it here.
        /// </para>
        /// <para>
        /// Only this box moves, not its subtree. Its already-placed descendants belong to the fragmentainer
        /// being left and keep the geometry that one's own fragment was built from
        /// (<c>FragmentEmitter.RecordNestedFragmentainer</c>); the content this pass places derives from the
        /// new <see cref="CssBox.ClientLeft"/> as it flows. The inline <i>size</i> is preserved —
        /// every column is the same width, so the box is translated rather than re-measured, which keeps §2's
        /// one-inline-size rule intact.
        /// </para>
        /// </remarks>
        private void ResumeInTheNextFragmentainer()
        {
            if (HtmlContainer?.CurrentFragmentainer is not { HasOwnBand: true } fragmentainer) return;
            if (DerivedStyle.ActualDisplay is CssConstants.TableCell || Position.Value is not (PositionMode.Static or PositionMode.Relative or PositionMode.Sticky))
                return;

            // Moving Location is the whole translation: ActualRight and ActualBottom are derived from it
            // (Location plus the box-sizing extent), so the inline size this box was measured at on the pass
            // that placed it survives untouched - which is precisely §2's one-inline-size rule.
            var top = ContentTopOfTheContainingBlockIn(fragmentainer, ContainingBlock);

            Location = new RPoint(ContainingBlock.ClientLeft + ActualMarginLeft, top);
            ActualBottom = Location.Y;
        }

        /// <summary>
        /// Decides where <paramref name="child"/> goes in this frame, has it resolve its own inline size
        /// against the page that offset lands on, and commits the offset.
        /// </summary>
        /// <returns>
        /// false when this frame declined to place the child here at all, so it contributes no fragment to
        /// the fragmentainer being filled.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>This is the seam a block-level box's position is written at, and it is the parent's.</b> The
        /// two halves are different questions: how wide the child is, is the child's own to answer from its
        /// style and its containing block; <i>where</i> it goes is a question about the break point between
        /// it and whatever this frame placed before it — which only this frame knows, because the answer is
        /// margin collapsing against a previous sibling, the fragmentainer that sibling ended in, and the
        /// run chained to it by break avoidance. So the frame's own child loop calls this, before the child
        /// lays out any content of its own; the child never reaches back out for it.
        /// </para>
        /// <para>
        /// <b>The offset is decided first, because the size depends on it and not the other way round.</b>
        /// <see href="https://www.w3.org/TR/css-page-3/#page-model">css-page-3 §5.1</see> makes each page's
        /// own page area the containing block for the layout that occurs between page breaks, so a box's
        /// measure comes from the page it <i>lands on</i>. Resolving the size first meant
        /// <c>CssLayoutEngine.GetBoxWidth</c> had nothing to read but <see cref="CssBox.Location"/>, which at
        /// that moment still held the position some earlier layout generation gave the box — page 0's
        /// measure on the first one — and only <see cref="HtmlContainerInt.PerformLayout"/>'s reflow loop
        /// could iterate that back to the truth. Nothing in this frame's block-flow arithmetic reads the
        /// child's inline size, so the dependency only runs one way and the order can simply be the right
        /// one. (The reflow loop stays: it also settles the width→height→page-assignment feedback of the
        /// boxes <i>after</i> this one, and the constrained-block nesting of issues #199-#201.)
        /// </para>
        /// <para>
        /// The root has no frame above it, so it stands in for its own. Nothing is lost by that:
        /// <see cref="PreviousInFlowSibling"/> reports null for a box the frame does not own, which is what
        /// <c>DomUtils.GetPreviousSibling</c> already answered for a box with no parent.
        /// </para>
        /// <para>
        /// Runs on the pass that first places the child and never again: a resumed pass continues the
        /// child's <i>content</i>, and re-deriving its top from the previous sibling would now read the
        /// end of the whole flow. Skipping it is also what keeps a box that spans a fragmentainer
        /// boundary on one inline size across its fragments, per CSS Fragmentation Level 3 §2.
        /// </para>
        /// </remarks>
        private async ValueTask<bool> PlaceAndSizeBlockChild(RGraphics g, CssBox child)
        {
            // The frame declined to place this box here at all (§5.2 concluded the break falls before it),
            // so there is no landing page to measure against and nothing to commit.
            if (ResolveBlockChildOffset(child) is not { } offset) return false;

            var measuredAt = offset.Top;

            await child.ResolveOwnInlineSize(g, measuredAt);
            CommitBlockChildOffset(child, offset);

            // Committing the offset can still move the box in the block axis: CssLayoutEngine.FloatBox
            // displaces a float past the ones it intersects, and `clear` pushes it below them. A box carried
            // into a band of a different measure that way has been measured for a page it is not on, so it
            // is measured again where it landed and re-committed from the same resolved offset — the float
            // scan restarts from the offset rather than from wherever the previous round left the box, so
            // each round is a fresh attempt rather than a cumulative slide. Bounded rather than a plain
            // `if`, and for the same reason StepPastSlotsOnTheWrongSide is: one round settles every case a
            // single displacement can produce, and the small cap keeps two measures that displace the box
            // into each other from spinning. Never entered by a document with one measure, which is every
            // document without per-page left/right margins.
            //
            // Measured, and worth knowing before deleting it: it does fire (a `clear: both` block displaced
            // past a float onto a narrower page enters it once, on every layout generation), but no fixture
            // found makes it change the *final* geometry - a displacement big enough to change the measure
            // has by definition crossed a page boundary, and in a fragmenting layout that is also a break
            // decision, so the box is placed again at the resumed target and measured correctly there
            // anyway. What it buys is that the first placement is self-consistent on its own terms rather
            // than by way of a later mechanism: the descendants laid out inside this box immediately
            // afterwards read its width, and a width belonging to a page the box is not on is the exact
            // defect this whole reorder removes.
            for (var guard = 0; guard < 4 && offset.PositionedInBlockFlow
                                          && child.InlineSizeCameFromAnotherPagesMeasure(measuredAt); guard++)
            {
                measuredAt = child.StaticTop;

                await child.ResolveOwnInlineSize(g, measuredAt);
                CommitBlockChildOffset(child, offset);
            }

            return true;
        }

        /// <summary>
        /// Whether this box's border-box top now sits on a page whose measure differs from the one at
        /// <paramref name="measuredAt"/>, which its inline size was resolved against.
        /// </summary>
        /// <remarks>
        /// Asked about the <i>measure</i> rather than about the slot index, and with
        /// <see cref="HtmlContainerInt.MeasureIsSharedBetween"/>'s own tolerance: two pages that differ only
        /// in where their content begins (mirrored inner/outer margins) wrap identically, so a box moving
        /// between them has nothing to re-resolve.
        /// </remarks>
        private bool InlineSizeCameFromAnotherPagesMeasure(double measuredAt) =>
            HtmlContainer is { UseVariablePageWidth: true } container
            && Math.Abs(container.PageContentRightOf(StaticTop)
                        - container.PageContentRightOf(measuredAt)) >= 0.01;

        /// <summary>
        /// Has the frame above this box assign its position.
        /// </summary>
        /// <remarks>
        /// The one dispatcher into <see cref="PlaceBlockChild"/>, so that a box reached by a path of its
        /// own — <see cref="CssBoxHr"/>, which resolves its own size and has no children to lay out — is
        /// placed by the same code as everything else rather than by a copy of it.
        /// </remarks>
        internal void PlaceAsBlockChild() => (ParentBox ?? this).PlaceBlockChild(this);

        /// <summary>
        /// Resolves this box's own inline size — the half of placing a block-level box that is the box's
        /// own to answer — against the page <paramref name="blockTop"/> falls on.
        /// </summary>
        /// <param name="g">the device context</param>
        /// <param name="blockTop">
        /// the border-box top the frame above has decided this box will occupy. Passed rather than read off
        /// <see cref="CssBox.Location"/>, which has not been written yet on the pass that places the box —
        /// see <see cref="PlaceAndSizeBlockChild"/> for why that read was the whole defect.
        /// </param>
        /// <remarks>
        /// Written as an extent rather than a width: <see cref="CssBox.ActualRight"/>'s setter
        /// stores it as a size against the current <see cref="CssBox.Location"/>, so the frame
        /// above is free to move the box afterwards and take the size with it.
        /// </remarks>
        private async ValueTask ResolveOwnInlineSize(RGraphics g, double blockTop)
        {
            // Because their width and height are set by CssTable, CssLayoutEngineFlex or CssLayoutEngineGrid
            if (DerivedStyle.ActualDisplay != CssConstants.TableCell && DerivedStyle.ActualDisplay != CssConstants.Table && DerivedStyle.ActualDisplay != CssConstants.Flex && DerivedStyle.ActualDisplay != CssConstants.InlineFlex && DerivedStyle.ActualDisplay != CssConstants.Grid && DerivedStyle.ActualDisplay != CssConstants.InlineGrid)
            {
                var width = await CssLayoutEngine.GetBoxWidth(g, this, blockTop);
                ActualRight = Location.X + width + ActualBoxSizeIncludedWidth;
            }

        }

        /// <summary>
        /// Where a forced break (css-break-3 §3.1) before <paramref name="child"/> puts it: the content top
        /// of the slot the break lands in. Null when no forced break falls before it, or when nothing
        /// precedes it in the flow for a break to fall after.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Re-derived at every placement, not latched once.</b> This is the frame's question, not the
        /// child's: it is resolved against the predecessor <i>this frame</i> placed, whose bottom edge is
        /// exactly the thing that moves between one placement and the next. Settling it in
        /// <see cref="PerformLayoutPrologue"/> instead — which runs once per box per layout — meant the
        /// answer had to survive every mechanism that retracts a pass's work, and it did not: the pass that
        /// <i>declines</i> to place an escaping break is not the pass that places it, and a prologue an
        /// enclosing engine re-opened in between retracted what the first had settled. Asking again costs
        /// one sibling walk and cannot go stale.
        /// </para>
        /// <para>
        /// The break is expressed by placing this box at the target rather than by inflating the
        /// <i>predecessor's</i> <c>ActualBottom</c> to reach it: that setter alters <c>Size.Height</c>, so a
        /// predecessor with a background or border would paint down to the page bottom — and, for a
        /// directional break, straight across the blank page, which would also make that slot look printable
        /// and so defeat its reservation. The predecessor's geometry is not the break's to change.
        /// </para>
        /// <para>
        /// Reaching the root without finding a predecessor means the child begins the flow, where a forced
        /// break has nothing to break from. Returning null there is what stops a <c>break-before</c> on the
        /// first element of a document — or on a heading whose <c>page</c> name merely starts the first
        /// named page — from manufacturing a blank page in front of it, which
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§4.4</see> asks user agents not to
        /// do. Only the <i>target</i> is resolved by climbing: the break is still taken by the child, so the
        /// containers it begins keep their own position and span the boundary; moving them too is §3.1
        /// propagation proper, which is a separate question.
        /// </para>
        /// <para>
        /// Whether the break has already been taken is <see cref="PlacedByForcedBreak"/>'s to say, not this
        /// method's — it answers where the break lands, every time it is asked.
        /// </para>
        /// </remarks>
        internal double? ForcedBreakTopFor(CssBox child)
        {
            if (!child._isForcedBreak || child.HtmlContainer is not { } container) return null;

            // The break falls between this box and whatever precedes it in the flow. For a container's
            // *first* in-flow child that is not a sibling of its own: §3.1's break point before it is the
            // same break point as the one before its container, so the predecessor to resolve the target
            // against is found by climbing the chain of containers this box begins. A climb that reaches
            // the root means nothing precedes this box in the flow at all — there is nothing to break from,
            // and taking a break anyway would manufacture a blank page in front of the first content in the
            // document.
            if (DomUtils.PrecedingBoxAcrossFirstChildChain(child) is not { } breakAnchor) return null;

            // HtmlContainer.PageSize.Height is already margin-free (PdfGenerator.SetContent subtracts both
            // page margins up front) - a page's real content band is the "shifted grid"
            // [k·PageSize.Height + MarginTop, (k+1)·PageSize.Height + MarginTop), not raw multiples of
            // PageSize.Height from document Y=0. PageIndexOf/PageTopOf are the single, unambiguous
            // definition of that grid (matching what the painter's own per-page clip and the fragment
            // builder's slot walk already use) - computing this via raw modulo arithmetic against
            // PageSize.Height alone (as this used to) silently lands a marginTop-wide band, right at the end
            // of every raw page, one whole page short.
            //
            // The epsilon implements css-break-3 §4.4's "no empty fragmentainer for a single forced break at
            // a boundary": a sibling whose content ENDS flush on a slot boundary (e.g. a full-bleed cover
            // sized exactly to its page's band) already satisfies the break - the target is that boundary
            // itself, not the slot after it (which manufactured a blank page). A zero-height sibling sitting
            // AT the boundary (the consecutive-forced-breaks case - it was itself relocated there by its own
            // preceding break) occupies the LATER slot, so the break between it and this box still pushes
            // past it, preserving the intentional blank page.
            // StaticBottom, and the previous sibling's static top, throughout: a relative offset moves a box
            // visually without affecting the layout of anything around it (CSS 2.1 §9.4.3), so it must not
            // decide which slot the break lands in either.
            var prevBottom = breakAnchor.StaticBottom;
            var prevTop = breakAnchor.Location.Y - breakAnchor.RelativeOffsetY;
            var slot = container.SlotEndingAt(prevBottom) + 1;

            if (prevTop >= container.PageTopOf(slot) - HtmlContainerInt.PageBoundaryEpsilon)
            {
                slot = container.SlotStartingAt(prevTop) + 1;
            }

            return container.PageTopOf(slot);
        }

        /// <summary>
        /// Steps <paramref name="child"/> past any slot on the wrong side for its directional forced break
        /// (css-break-3 §3.1's <c>left</c>/<c>right</c>/<c>recto</c>/<c>verso</c>, which force one <i>or
        /// two</i> page breaks), reserving each slot stepped over as a deliberately-blank page.
        /// </summary>
        /// <param name="child">the box taking the break</param>
        /// <param name="top">where the break has put it so far, its preserved top margin included</param>
        /// <param name="margin">
        /// that same preserved top margin, re-applied at every slot the box is stepped over to
        /// </param>
        /// <returns>where it lands, and the last slot reserved on the way — null if none was</returns>
        /// <remarks>
        /// <para>
        /// The content after the break has to <i>begin</i> on a page of the requested side, so the side is
        /// checked against where the box actually lands — which the preserved margin can carry past the slot
        /// the break itself reached. <paramref name="margin"/> travels with the box across every step, so it
        /// is preserved on whichever page the box ends up opening. Bounded rather than a plain <c>if</c>
        /// because a margin taller than a band can carry the box past the slot the step just chose; two
        /// rounds settle every case a single alternation can produce, and the small cap keeps a degenerate
        /// band from spinning.
        /// </para>
        /// <para>
        /// Gated on <see cref="HtmlContainerInt.IsFragmenting"/> because inside monolithic content
        /// (multicol's virtual single-column first pass, and the flex/grid/table engines) and during a
        /// measurement pass at a provisional position, the child's coordinates are not where it ends up — a
        /// reservation made from them would materialize a blank page nowhere near the real content. A
        /// directional break degrades to a plain page break there, the same engine-independence boundary the
        /// other break machinery already has. Asked while filling a column too, now that the break genuinely
        /// reaches the page it names: the coordinates it steps through are the page grid's either way, so a
        /// directional break inside a multi-column container reserves its blank page exactly as it does
        /// outside one. It used to be excluded, because reserving a page while the box merely moved to the
        /// next column honoured half of a decision.
        /// </para>
        /// </remarks>
        private static (double Top, int? ReservedBlankSlot) StepPastSlotsOnTheWrongSide(
            CssBox child, double top, double margin)
        {
            int? reservedBlankSlot = null;

            for (var guard = 0;
                 child._forcedBreakSide is not PageSide.Any
                 && child.HtmlContainer!.IsFragmenting
                 && guard < 4;
                 guard++)
            {
                var landing = child.HtmlContainer.SlotStartingAt(top);

                if (BreakValues.SlotIsOn(landing, child._forcedBreakSide))
                    break;

                child.HtmlContainer.SetBlankSlotReservation(child, landing);
                reservedBlankSlot = landing;
                top = child.HtmlContainer.PageTopOf(landing + 1) + margin;
            }

            return (top, reservedBlankSlot);
        }

        /// <summary>
        /// Assigns <paramref name="child"/>'s position in this frame, and registers the used page name it
        /// lands on.
        /// </summary>
        /// <remarks>
        /// The two halves together, for a caller that has nothing to do between them —
        /// <see cref="PlaceAsBlockChild"/>'s own callers, which have already resolved their size. A box
        /// whose size is still to be resolved goes through <see cref="PlaceAndSizeBlockChild"/> instead, which is
        /// exactly the caller that needs the offset before the size.
        /// </remarks>
        private void PlaceBlockChild(CssBox child)
        {
            if (ResolveBlockChildOffset(child) is { } offset)
            {
                CommitBlockChildOffset(child, offset);
            }
        }

        /// <summary>
        /// Where a frame has decided to put a block-level child, decided <i>before</i> that child's inline
        /// size is resolved so the size can be resolved against the page the offset lands on.
        /// </summary>
        /// <param name="Left">
        /// the containing block's content left edge. The child's own left margin is added to it at commit
        /// rather than here, because <c>margin: auto</c> resolves against the very inline size this offset
        /// is settled ahead of (CSS 2.1 §10.3.3).
        /// </param>
        /// <param name="Top">
        /// the child's border-box top — the coordinate whose page decides the child's measure, per
        /// <see href="https://www.w3.org/TR/css-page-3/#page-model">css-page-3 §5.1</see>.
        /// </param>
        /// <param name="PositionedInBlockFlow">
        /// whether this frame's block-flow arithmetic produced <see cref="Top"/> at all. False for a table
        /// cell (its engine positions it) and for an absolutely or fixed positioned box (positioned from
        /// its containing block by <see cref="CommitBlockChildOffset"/>, and out of flow, so no page break
        /// falls before it): for those, <see cref="Top"/> only reports where the box already sits.
        /// </param>
        private readonly record struct BlockChildOffset(double Left, double Top, bool PositionedInBlockFlow);

        /// <summary>
        /// Decides where <paramref name="child"/> goes in this frame, without writing it.
        /// </summary>
        /// <returns>
        /// the offset to commit, or null when the child declines to be placed here at all: <c>§5.2</c>'s
        /// margin truncation can conclude that the break falls <i>before</i> it, in which case this records
        /// the request and returns without writing a position or a registration, and the child contributes
        /// no fragment to the fragmentainer being filled.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Everything here is resolved against boxes only this frame can see — the previous in-flow sibling
        /// this frame placed, the keep-with-next run chained to it, and the fragmentainer that run started
        /// in. That is why it is the parent's and not the child's, even though every field it writes belongs
        /// to the child: a break point is between two children, so no child can answer it alone.
        /// </para>
        /// <para>
        /// Synchronous, deliberately. Deciding an offset consults nothing that has to be fetched or
        /// measured — and, in particular, nothing about the child's own inline size, which is what lets the
        /// size be resolved against the answer instead of the other way round.
        /// </para>
        /// </remarks>
        private BlockChildOffset? ResolveBlockChildOffset(CssBox child)
        {
            if (child.DerivedStyle.ActualDisplay != CssConstants.TableCell)
            {
                if (child.Position.Value is PositionMode.Static or PositionMode.Relative or PositionMode.Sticky)
                {
                    var prevSibling = PreviousInFlowSibling(child);

                    var left = child.ContainingBlock.ClientLeft;
                    // prevSibling.ActualBottom is already the outer border-box edge (CssBox.
                    // ActualBottom = Location.Y + content height + padding + border, per its own
                    // getter/ApplyHeight/MarginBottomCollapse - all three fold border-bottom in
                    // exactly once) - adding prevSibling.ActualBorderBottomWidth again here double-
                    // counted it, pushing every box that follows a bordered sibling an extra
                    // border-bottom-width too far down. CollapsedMarginBefore's own internal bookkeeping
                    // (anchor.ActualBottom + anchor.ActualBorderBottomWidth, then subtracting
                    // prevSibling's own equivalent) is unaffected by this fix: those two terms already
                    // cancel out exactly when anchor == prevSibling (the common case), and a
                    // self-collapsing prevSibling always has zero border by definition
                    // (IsMarginCollapseThrough requires it), so the residual term vanishes there too.
                    // StaticBottom (not ActualBottom) so a relatively-positioned previous sibling's
                    // visual offset doesn't shift the child - CSS 2.1 §9.4.3, relative offsets never
                    // affect the layout of following content.
                    var baseTop = (prevSibling == null ? child.ContainingBlock.ClientTop : child.ParentBox == null ? child.Location.Y : 0) + (prevSibling?.StaticBottom ?? 0);
                    var top = baseTop + CollapsedMarginBefore(child, prevSibling);

                    // CSS Fragmentation Level 3 §5.2: "When an unforced break occurs before or
                    // after a block-level box, any margins adjoining the break are truncated to
                    // zero." A margin big enough to push the child across one or more page
                    // boundaries by itself (as opposed to actual content straddling a boundary,
                    // which BreakInside/orphans-widows handles separately, later in this frame) is
                    // exactly that case - real UAs (and Prince, which this mirrors) discard the
                    // whole margin and start the box flush at the very next page boundary rather
                    // than paginating through a wall of blank pages. Acid2's own
                    // "#top { margin-top: 100em }" is the canonical example: that margin alone
                    // spans several page heights with no real content in it at all. A negative
                    // collapsed margin can never trigger this (it only pulls top backward, never
                    // forward across a new boundary), and an ordinary margin that stays within the
                    // same page as prevSibling's bottom is completely unaffected. Per the same spec
                    // section, a margin AFTER a *forced* break is explicitly preserved, not
                    // truncated (only the margin BEFORE a forced break is - already handled above by
                    // bumping previousSiblingForBreak.ActualBottom to the next page's top) - so this
                    // only applies when the child's own placement isn't already forced-break-governed.
                    if (child._resumeTopOverride is { } resumedTop)
                    {
                        // The break before the child was taken on an earlier pass, which already worked
                        // out where it lands and already pulled any keep-with-next run along. This pass
                        // places it there. Re-deriving the decision instead would reach the same
                        // "does not fit" conclusion and break again, forever.
                        child._resumeTopOverride = null;
                        top = resumedTop;

                        // An escaping forced break is placed *here*, one pass after the arm below decided
                        // it, and that arm is not re-entered - so the two things it settles beyond the
                        // target are settled here instead, by asking the same questions again: that this
                        // box is placed by a forced break, which its *next* sibling reads through §5.2 and
                        // the margin walk-back, and the blank slot a directional break steps over to land
                        // on the side it names. Both were retracted in between by a prologue the engine
                        // re-opened (PassRewind.RollBackTo, from CssLayoutEngineColumns's own fill retry);
                        // losing the first put the following sibling ahead of the break, and losing the
                        // second landed the content on a page of the wrong side, which is precisely what
                        // the value asked about.
                        //
                        // Re-asserted rather than re-derived, unlike the target itself: see
                        // _escapedForcedBreakPending for the measurement that says the two are not the same
                        // thing here. `top` stays the record's either way - the break decision already
                        // worked that out and it must be used as-is (re-deriving it would reach the same
                        // "does not fit" conclusion and break again, forever).
                        if (child._escapedForcedBreakPending)
                        {
                            child._escapedForcedBreakPending = false;
                            child.PlacedByForcedBreak = true;

                            if (child._escapedForcedBreakBlankSlot is { } blankSlot)
                            {
                                child.HtmlContainer!.SetBlankSlotReservation(child, blankSlot);
                            }
                        }
                    }
                    else if (!child.PlacedByForcedBreak && ForcedBreakTopFor(child) is { } forcedTop)
                    {
                        // Which slot the forced break lands in, asked of this frame now rather than read
                        // off what the child's prologue latched a pass or more ago. Applied here, to the
                        // child, rather than by inflating the previous sibling's height to reach it: that
                        // predecessor's own geometry is not the break's to change.
                        //
                        // §5.2 truncates margins adjoining an *unforced* break only, so the margin on
                        // the new page's side of a forced break survives and opens that page. That is
                        // the child's own margin collapsed with its adjoining first-child chain - which
                        // is what CollapsedMarginBefore computes for a box with no previous sibling, and
                        // the break makes the child exactly that. The group computed against the real
                        // previous sibling is the wrong quantity: it also holds that sibling's
                        // margin-bottom, which belongs to the page being left.
                        child.PlacedByForcedBreak = true;

                        var forcedBreakMargin = CollapsedMarginBefore(child, null);
                        int? reservedBlankSlot;

                        (top, reservedBlankSlot) =
                            StepPastSlotsOnTheWrongSide(child, forcedTop + forcedBreakMargin, forcedBreakMargin);

                        // css-break-3 §4.4's blank-page idiom: a forced break can land a box that carries
                        // no printable content of its own alone on a slot (an empty break marker between
                        // two "break-after: always" siblings is the canonical case). Without an explicit
                        // reservation, CSS Paged Media 3 §3.2's content-empty-page skip
                        // (FragmentEmitter.Finish) would drop that slot from the output entirely, silently
                        // discarding the deliberate blank page - the layout-position half of this (which
                        // slot the box lands on) was already correct without it. Skipped when a directional
                        // step already reserved a slot for this box: the dictionary holds one slot per
                        // owner, and the stepped-over slot must keep it - the box's own landing slot is what
                        // a directional break exists to carry real content to.
                        if (reservedBlankSlot is null)
                        {
                            child.HtmlContainer!.SetBlankSlotReservation(
                                child, child.HtmlContainer.SlotStartingAt(top));
                        }

                        // §3.1: a forced *page* break is not a nested fragmentainer's to satisfy. The page
                        // vehicle is realized by placement, and placing the child at `top` inside a column
                        // puts it past that column's band - which the container's own overflow arm then
                        // reads as "start the next column", one column over instead of one page over. So
                        // the break is stated as a record instead, marked as escaping: the columns engine
                        // stops opening columns for it and hands it up to the page driver, which already
                        // knows how to open the page this target names.
                        //
                        // What this arm settled travels with the request, because the pass that places the
                        // box takes the resumed-target branch above and never re-enters this one - and the
                        // prologue the engine re-opens in between retracts both of them.
                        if (child.HtmlContainer!.IsFragmenting
                            && child.HtmlContainer.CurrentFragmentainer is { HasOwnBand: true })
                        {
                            child._escapedForcedBreakPending = true;
                            child._escapedForcedBreakBlankSlot = reservedBlankSlot;
                            child.RequestBreakBefore(top, escapesNestedFragmentainer: true);
                            return null;
                        }

                        // The pass has just stepped over one or more slots without ending, so the
                        // fragmentainer it is filling from here on is the one this child lands in, not
                        // the one the pass opened with. The emitter has always known this
                        // (FragmentEmitter.EmitPass takes a `throughSlot`); the cursor has to know it
                        // too, or every later question about "the fragmentainer being filled" answers
                        // about a band the pass has left behind. Measured: a box placed by this break
                        // whose first fragment cannot meet its own `orphans` minimum was read as having
                        // room above it in this fragmentainer - it sits at the very top of one - so the
                        // §5.4 mover pushed it one page further and left the page the break named blank.
                        child.HtmlContainer.CurrentFragmentainer?.StepOverTo(
                            child.HtmlContainer.SlotStartingAt(top));
                    }
                    // A previous sibling that a forced break placed and that contributes no height of
                    // its own - the empty "<div class='page-break'>" marker - puts the break
                    // immediately before the child, so the child's margin adjoins a *forced* break and
                    // §5.2 preserves it rather than truncating it. Without this the flush-boundary
                    // convention below reads the marker's position (exactly a slot top, so one epsilon
                    // earlier is the previous slot) as a boundary the child's margin crossed, and
                    // discards the margin.
                    //
                    // A *first* in-flow child has no previous sibling to resolve against, but the break
                    // point before it is a real one all the same: baseTop is already defined for it
                    // (the containing block's own content top, above) and the boundary test below reads
                    // nothing else from the sibling, so the arithmetic needs no predecessor. Only the
                    // root is excluded - it has nothing before it for a break to fall between, and a
                    // break-before published from the context root would have no parent link to travel
                    // up (see PublishBreakToTheContextRoot).
                    else if (!child._adjoinsForcedBreakPoint && child.ParentBox is not null
                             && !(prevSibling is { PlacedByForcedBreak: true } marker && marker.IsMarginCollapseThrough()))
                    {
                        // Same shifted grid the fragment builder/the forced-break logic above use (see
                        // HtmlContainer.PageIndexOf's own doc comment) - matching
                        // BreakInside_Avoid_PositionsAtTopOfNextPage's already-established convention, and
                        // mirroring the forced-break flush-fit rule above. The constraint is over the band
                        // baseTop *ends* in - baseTop is the predecessor's bottom edge - and the question
                        // asked of it is FallsPast, which carries the same bottom-edge convention. That is
                        // deliberate rather than a slip: a child landing flush ON the band's bottom has not
                        // crossed it, because it is the first thing in the next band and there is nothing
                        // for the margin to be truncated against. The top-edge convention (BlockConstraint.
                        // For, Straddles) would call that a crossing and truncate a margin that never
                        // spanned anything - which is why the two are separate members rather than one.
                        // An unpaginated pass has no band at all and so nothing to cross out of, which
                        // EndingAt answers by naming no fragmentainer.
                        var boundary = BlockConstraint.EndingAt(child.HtmlContainer!, child, baseTop);

                        if (boundary.FallsPast(top))
                        {
                            // The band's own bottom, which is the next one's top: bands are contiguous,
                            // so "start at the next slot" and "start where this band ends" are the same
                            // coordinate, and the second is the one the question above was asked in.
                            var newTop = boundary.AbsoluteBandBottom;

                            // css-break §3.1 keep-with-next: the child is about to relocate to the next
                            // page's content top, which would otherwise strand a preceding
                            // break-after/break-before: avoid run (e.g. the UA default
                            // `h1-h6 { break-after: avoid }`) alone at the bottom of the page it's
                            // leaving - see CssLayoutEngineTable's identical whole-table pre-check
                            // (LayoutCells) and OffsetTopWithKeepWithNextRun, which this mirrors. Pull
                            // the run along when it starts on this same page and its own height still
                            // fits the destination page's band; an unsatisfiable avoid is relaxed per
                            // spec and the child moves alone, exactly as before. Unlike those two
                            // siblings' guards, this one doesn't also require the child's own
                            // (not-yet-laid-out) content to fit alongside the run: a
                            // break-inside:avoid/orphans-widows box must land whole or the move is
                            // pointless, but the child is free to fragment across further pages on its
                            // own afterward (a table re-applies its per-row break logic, an ordinary
                            // block just keeps flowing) - only the run needs a page to itself.
                            var keepWithNextRun = DomUtils.GetPrecedingKeepWithNextRun(child, FragmentationContext.Page);
                            if (keepWithNextRun.Count > 0)
                            {
                                var runTop = keepWithNextRun[0].Location.Y;
                                var extraAbove = top - runTop;
                                // Asked the same way as the boundary above, so the two are comparable -
                                // a run head flush on the boundary belongs to the band the constraint
                                // names, not the one after it.
                                var runStartsOnSamePage =
                                    child.HtmlContainer!.SlotEndingAt(runTop) == boundary.Fragmentainer!.SlotIndex;

                                if (extraAbove > 0 && runStartsOnSamePage
                                    && extraAbove <= boundary.AtNextSlot().NextBandHeight)
                                {
                                    var groupOffset = newTop - runTop;

                                    foreach (var member in keepWithNextRun)
                                    {
                                        member.OffsetTop(groupOffset);
                                    }

                                    newTop += extraAbove;
                                }
                            }

                            // The margin pushed the child out of the fragmentainer being filled, so the
                            // break falls *before* it: it produces no fragment here at all, and resumes
                            // at newTop in the next one (css-break-3 §4.4). Where breaking is not live -
                            // a measurement pass, or monolithic content - the box is simply placed at
                            // that target, exactly as it was before this became a break decision.
                            if (child.HtmlContainer!.IsFragmenting)
                            {
                                child.RequestBreakBefore(newTop);
                                return null;
                            }

                            // Breaking is not live here (a measurement pass, or monolithic content), so
                            // the child is simply placed at the target a break would have named - flush
                            // at the next band's top, without a pass boundary to carry the cursor there
                            // on its own. Any further content this same (possibly suppressed-but-real,
                            // see LayoutTheRemainderMonolithically) pass places has to see a truthful
                            // "band being filled" - see #435.
                            child.HtmlContainer.CurrentFragmentainer?.StepOverTo(
                                child.HtmlContainer.SlotStartingAt(newTop));
                            top = newTop;
                        }
                    }

                    return new BlockChildOffset(left, top, PositionedInBlockFlow: true);
                }
            }

            // Nothing in this frame's block flow to decide: a table cell is positioned by the table engine,
            // and an out-of-flow box by its containing block at commit. Reporting where the box already
            // sits keeps its measure resolved against the same coordinate it always was.
            return new BlockChildOffset(0, child.Location.Y, PositionedInBlockFlow: false);
        }

        /// <summary>
        /// CSS 2.1 §9.4.3's near/far offset resolution for one axis: the near offset (<c>left</c>/<c>top</c>)
        /// wins when set; if it's <c>auto</c> and the far offset (<c>right</c>/<c>bottom</c>) isn't, the far
        /// offset applies with its sign flipped; if both are <c>auto</c>, the offset is 0.
        /// </summary>
        private static double ResolveNearFarOffset(
            CssProperty<CssKeywordOrValue<AutoKeyword, LengthOrCalc>> near,
            CssProperty<CssKeywordOrValue<AutoKeyword, LengthOrCalc>> far,
            double basis, CssBox box)
        {
            var nearValue = near.Value;
            var farValue = far.Value;

            if (nearValue.IsValue || !farValue.IsValue)
            {
                return nearValue.Value is { } n ? CssValueParser.ParseLength(n, basis, box) : 0;
            }

            return -CssValueParser.ParseLength(farValue.Value!.Value, basis, box);
        }

        /// <summary>
        /// Resolves a <c>left</c>/<c>top</c>/<c>right</c>/<c>bottom</c> offset for the absolute/fixed
        /// positioning branches below, where the counterpart edge is never consulted (unlike the
        /// relative-positioning near/far resolution in <see cref="ResolveNearFarOffset"/>) - an <c>auto</c>
        /// offset simply contributes 0.
        /// </summary>
        private static double ResolveOffsetOrZero(
            CssProperty<CssKeywordOrValue<AutoKeyword, LengthOrCalc>> offset, double basis, CssBox box) =>
            offset.Value.Value is { } value ? CssValueParser.ParseLength(value, basis, box) : 0;

        /// <summary>
        /// Writes the offset <see cref="ResolveBlockChildOffset"/> decided on, positions
        /// <paramref name="child"/> under whichever positioning scheme it uses, and registers the used page
        /// name it lands on.
        /// </summary>
        /// <remarks>
        /// Split from the decision so the child's inline size can be resolved in between: every line here
        /// that reads a size — the left margin (which <c>margin: auto</c> centres against the used width),
        /// a percentage relative offset, and <c>CssLayoutEngine.FloatBox</c>'s displacement scan — needs the
        /// size the box actually has, and the decision above needs none of it.
        /// </remarks>
        private void CommitBlockChildOffset(CssBox child, BlockChildOffset offset)
        {
            if (child.DerivedStyle.ActualDisplay != CssConstants.TableCell)
            {
                if (offset.PositionedInBlockFlow)
                {
                    var top = offset.Top;

                    child.Location = new RPoint(offset.Left + child.ActualMarginLeft, top);
                    child.ActualBottom = top;

                    // The root places itself (PlaceAsBlockChild's (ParentBox ?? this) receiver), and §5.2's
                    // whole crossing question above is never asked of it - "only the root is excluded - it
                    // has nothing before it for a break to fall between." A descendant's margin can still
                    // collapse all the way up to the root (margin-collapse-through), landing it far down
                    // the document with no crossing ever decided anywhere - the one placement every other
                    // site's StepOverTo call is scoped to skip, and so the one that needs its own - #435.
                    if (child.ParentBox is null)
                    {
                        child.HtmlContainer?.CurrentFragmentainer?.StepOverTo(
                            child.HtmlContainer!.SlotStartingAt(top));
                    }

                    CssLayoutEngine.FloatBox(child);
                }

                if (child.Position.Value is PositionMode.Relative)
                {
                    // CSS 2.1 §9.4.3: for each axis, the "near" offset (left/top) wins when set; if
                    // it's auto and the "far" offset (right/bottom) isn't, the far offset applies
                    // with its sign flipped (moving the box the opposite direction from that edge);
                    // if both are auto, the offset is 0. Previously only left/top were ever read, so
                    // e.g. "bottom: -1em" with left/top both auto/unset was a silent no-op.
                    //
                    // The offsets are recorded (not just applied) because, per the same section,
                    // relative positioning is purely visual: following siblings and the parent's own
                    // content-driven height must lay out against the box's STATIC position, which
                    // StaticBottom recovers by backing RelativeOffsetY out again. Acid2's
                    // ".smile div { position: relative; bottom: -1em }" is exactly this: the mouth
                    // bar paints 1em lower, but ".chin"'s position must not move with it.
                    var offsetX = ResolveNearFarOffset(child.Left, child.Right, child.ActualWidth, child);
                    var offsetY = ResolveNearFarOffset(child.Top, child.Bottom, child.ActualHeight, child);

                    child.RelativeOffsetX = offsetX;
                    child.RelativeOffsetY = offsetY;
                    child.Location = new RPoint(child.Location.X + offsetX, child.Location.Y + offsetY);
                    child.ActualBottom = child.Location.Y;
                }

                if (child.Position.Value is PositionMode.Absolute)
                {
                    var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(child);

                    // CSS 2.1 §10.3.7: `left`/`top` on an absolutely positioned box are measured
                    // from the containing block's PADDING edge (ClientLeft/ClientTop - inside the
                    // border), not its border-box edge (Location.X/Y) - and, like every other
                    // positioning scheme, the box's own margin still applies on top of that offset
                    // (previously dropped entirely here, unlike the static/relative branch above
                    // which already adds ActualMarginLeft). Acid2's own
                    // "[class~=one].first.one { position:absolute; margin: 36px 0 0 60px; }" inside
                    // ".picture" (which has a 1em border) exercises both of these: the missing
                    // margin alone lands the box ~36px/60px off, on top of the next sibling.
                    var left = nearestPositionedAncestor.ClientLeft + child.ActualMarginLeft +
                               ResolveOffsetOrZero(child.Left, nearestPositionedAncestor.ActualWidth, child);

                    var top = nearestPositionedAncestor.ClientTop + child.ActualMarginTop +
                              ResolveOffsetOrZero(child.Top, nearestPositionedAncestor.ActualHeight, child);

                    child.Location = new RPoint(left, top);
                }

                if (child.Position.Value is PositionMode.Fixed)
                {
                    // Like every other positioning scheme (see the Absolute branch above, fixed for
                    // the same omission), the box's own margin still applies on top of the left/top
                    // offset - previously dropped entirely here. Acid2's own
                    // ".picture p + table + p { margin-top: 3em; }" (which legitimately matches the
                    // fixture's second, HTML4-DTD-auto-closed <p> - see Acid2RegressionTests) relies
                    // on this to shift that fixed-position paragraph down from underneath the first
                    // one's own fixed black bar. Percentages resolve against the page/viewport size
                    // (CSS2.1 §10.1: the initial containing block), not ScrollOffset (a scroll
                    // position, not a size) - not exercised by this fixture (uses em, not %) but
                    // wrong regardless.
                    var left = child.ActualMarginLeft + ResolveOffsetOrZero(child.Left, child.HtmlContainer!.PageSize.Width, child);
                    var top = child.ActualMarginTop + ResolveOffsetOrZero(child.Top, child.HtmlContainer!.PageSize.Height, child);
                    child.Location = new RPoint(left, top);
                }
            }

            // Register the used page name BEFORE any child lays out: descendants' page-break
            // decisions consult the per-page geometry table, whose slot bands from the child's
            // page onward depend on this name being visible (PageRuleResolver.
            // ActiveNameAtSlotStart) - registering only after child layout (the epilogue's tail,
            // formerly the sole registration point) let a multi-page named element's own content
            // paginate against the PREVIOUS name's bands. We register the *used* name whenever this
            // box either carries its own explicit name or is a used-name transition (see
            // shouldRegisterPage) - crucially including a reversion whose UsedPageName is empty or
            // an outer named page, which is what stops a named page's margins/margin-boxes from
            // leaking onto later default pages. Movers that can still run after this point
            // (BreakInside: avoid, orphans/widows, the absolute bottom-edge fallback) all route
            // through OffsetTop, which keeps the registration in sync via MoveNamedPageElement;
            // engines that relocate this box directly (e.g. CssLayoutEngineTable's whole-table
            // pre-check) are re-synced by the tail check.
            if (child._shouldRegisterPage)
            {
                // Registration appends, and this box can be placed more than once inside one layout: a
                // break before it is taken on a later pass, or a column driver re-places it in the next
                // column. Withdraw what the previous placement registered rather than accumulating one
                // entry per position it has occupied - the same leak the prologue's own withdrawal
                // closes for the paths that do re-run it.
                if (child.RegisteredNamedPageElement is { } stale)
                {
                    child.HtmlContainer!.UnregisterNamedPageElement(stale);
                }

                child.RegisteredNamedPageElement = child.HtmlContainer!.RegisterNamedPageElement(child.UsedPageName, child.NamedPageRegistrationY());
            }
        }

        /// <summary>
        /// The in-flow sibling this frame placed immediately before <paramref name="child"/>, or null when
        /// nothing in this frame precedes it.
        /// </summary>
        /// <remarks>
        /// Null for a box this frame does not own, which is how the root — standing in for its own frame —
        /// gets the answer it has always had: a box with no parent has no previous sibling.
        /// </remarks>
        private CssBox? PreviousInFlowSibling(CssBox child) =>
            ReferenceEquals(child.ParentBox, this) ? DomUtils.GetPreviousSibling(child, false) : null;

        /// <summary>
        /// Everything that must happen exactly once, after this box's content is complete: resolving its
        /// height, and the corrections that can only be judged against a finished box — the
        /// keep-with-next first-line retry, <c>break-inside: avoid</c>, <c>orphans</c>/<c>widows</c>, the
        /// absolute right/bottom fallbacks, and the named-page/named-string bookkeeping.
        /// </summary>
        /// <remarks>
        /// A box that stopped part-way through a fragmentainer has none of this settled yet, so a
        /// resumed pass runs it only once the box actually completes.
        /// </remarks>
        private async ValueTask PerformLayoutEpilogue(RGraphics g)
        {
            CssLayoutEngine.ApplyHeight(this);
            CssLayoutEngine.ApplyParentHeight(this);

            // css-break keep-with-next at the word-flow fragmentation site: word flow relocates any
            // line that would straddle a page boundary to the next page (CssRect.BreakPage, called
            // from CssLayoutEngine.FlowBox). When that happens to this block's FIRST line, the break
            // effectively falls right before this box's content - so preceding siblings chained to it
            // by break-after/break-before: avoid (css-break §3.1, e.g. the UA default
            // `h1-h6 { break-after: avoid }`) must not be left behind on the old page. Move the
            // chained run to the top of the page the line landed on, then re-run this box's own layout:
            // its position re-derives from the moved run's new bottom and its lines re-flow without a
            // boundary in the middle (PerformLayoutImp double-execution is already an established
            // pattern - see HtmlContainerInt.PerformLayout's own double layout). Guarded to one retry.
            //
            // The run pull below has never fired, and the reason is structural rather than a missing
            // fixture. Measured twice - once by #538, once by #539, which went looking for a fixture that
            // would reach it: across the whole suite `firstLinePage > ownPage` is true 244 times and
            // `keepWithNextRun.Count > 0` on none of them, and every attempt to build a document that
            // reaches it with a run in hand was intercepted first. Whenever a run does precede a box whose
            // first line would land on the next page, an earlier mechanism has already pulled it - the
            // break-decision movers in LayoutBlockChildren (EarlyBreak.Discover + TryRestartAt, on the
            // §3.1-propagation and orphans arms) and PlaceBlockChild's own §5.2 pull, all of which run on
            // the pass that *declines* to place the box and therefore before this epilogue is reached at
            // all. What is left here is the case where none of them applied, which is the case where the
            // box has no run. Deliberately left as-is rather than converted or deleted: proving it dead is
            // not the same as proving it unnecessary, and #545 is where that is decided.
            if (!_keepWithNextRetried
                && Position.Value is PositionMode.Static or PositionMode.Relative or PositionMode.Sticky && !IsFloated
                && LineBoxes.Count > 0 && LineBoxes[0].Words.Count > 0
                && HtmlContainer!.PageSize.Height > 0
                && !PositionAssignedByEngine)
            {
                var firstWordTop = LineBoxes[0].Words.Min(w => w.Top);
                var ownPage = HtmlContainer.PageIndexOf(Location.Y);
                var firstLinePage = HtmlContainer.PageIndexOf(firstWordTop);

                if (firstLinePage > ownPage)
                {
                    var keepWithNextRun = DomUtils.GetPrecedingKeepWithNextRun(this, FragmentationContext.Page);

                    if (keepWithNextRun.Count > 0)
                    {
                        var runTop = keepWithNextRun[0].Location.Y;
                        var extraAbove = Location.Y - runTop;
                        var runStartsOnSamePage = HtmlContainer.PageIndexOf(runTop) == ownPage;
                        var pageStart = HtmlContainer.PageTopOf(firstLinePage);

                        if (extraAbove > 0 && runStartsOnSamePage
                            && extraAbove + ActualBottom - firstWordTop <= HtmlContainer.PageBandHeightOf(firstLinePage))
                        {
                            var runDelta = pageStart - runTop;

                            foreach (var member in keepWithNextRun)
                            {
                                member.OffsetTop(runDelta);
                            }

                            _keepWithNextRetried = true;

                            // The retry re-runs this box from scratch, prologue included: it is a fresh
                            // layout of the same box at a new position, not a continuation, so its
                            // per-line rectangles must be reset and its words re-measured. (A resumed
                            // fragmentainer pass is the opposite case and deliberately keeps them.)
                            _prologueDone = false;

                            try
                            {
                                // The same frame this pass was driven from — the retry re-places this box
                                // as well as re-flowing it, and this arm is unreachable for a box a layout
                                // engine positioned (PositionAssignedByEngine, guarded above).
                                await PerformLayoutImp(g, ParentBox ?? this, framePlacesChild: true);
                            }
                            finally
                            {
                                _keepWithNextRetried = false;
                            }

                            return;
                        }
                    }
                }
            }

            // avoid / avoid-page, but not avoid-column or avoid-region: this mover is a page-context
            // mover by construction (it measures against PageBandHeightOf and relocates to PageTopOf),
            // so a hint naming a different fragmentation context must not suppress a page break.
            //
            // Monolithic content (css-break-3 §2 - a replaced element, a scroll container) reaches the same
            // mover, because "may not be broken" and "asks not to be broken" want the same relocation. So
            // does a table that did not break between any two of its own rows: it did not fragment, which
            // is what the other two say about themselves in advance rather than after the fact.
            var avoidsBreak = BreakValues.AvoidsBreak(BreakInside, FragmentationContext.Page);
            var monolithic = IsMonolithicBoxThisMoverMayMove() || PaginatedItsOwnContentWithoutBreaking();

            // One correction per box per pass (_earlyBreakTaken). Where the box was laid out again
            // rather than moved, this epilogue is the relocated box's own, and it asks the same
            // question of the same geometry - an unsatisfiable `avoid` is relaxed rather than skipped
            // (§5.3), so without the latch the answer is "still does not fit" and the box walks down
            // the document one page per pass.
            //
            // A flex item's own break-inside:avoid/monolithic relocation is CssLayoutEngineFlex's
            // RelocateLinesAcrossFragmentainers, which already ran and already decided this - by the
            // commit pass this box's Location is not "wherever the previous phase happened to leave it",
            // it is the engine's own final answer, and this page-context mover (built for an ordinary
            // block sibling with siblings and a page grid of its own to relocate against) would ask the
            // same question again with none of that context and can disagree.
            if ((avoidsBreak || monolithic) && !_earlyBreakTaken && !PositionAssignedByEngine)
            {
                // The space this box's own top already sits in - BlockConstraint.For reproduces the same
                // shifted-grid convention (see HtmlContainer.PageIndexOf) the pre-BlockConstraint version
                // of this mover spelt out inline: distance from the start of the box's own page's real
                // content band, not a raw modulo of PageSize.Height (which ignored MarginTop and, for the
                // last MarginTop-wide sliver of every page, mis-detected which page a box's top belonged to).
                var constraint = BlockConstraint.For(this);

                // The two arms part company on a box that fits in no fragmentainer. An unsatisfiable
                // `avoid` is relaxed and the box still moves, maximizing what lands on one page (§4.3);
                // a monolithic box is left exactly where it is, because §2 would have it overflow and
                // overflowing discards every fragmentainer past the first - so PeachPDF keeps fragmenting
                // it instead (#350). The question is asked of the *destination* band, which per-page
                // @page margins can size differently from the current one and from PageSize.Height.
                if (constraint.Straddles(ActualBottom - Location.Y)
                    && (avoidsBreak || FitsInFragmentainer(constraint.AtNextSlot()))
                    && TakeEarlyBreak(EarlyBreak.Discover(
                        this,
                        constraint.AbsoluteBandBottom,
                        // The two reasons share a mover but not a rationale, and §4.3 relaxation will
                        // need to tell "may not be broken" from "asks not to be broken" apart.
                        monolithic ? EarlyBreakReason.Monolithic : EarlyBreakReason.AvoidBreakInside)))
                {
                    // Being laid out again, at a position nothing below this point has seen yet.
                    return;
                }
            }


            // widows (§5.4): a paragraph-like box (real line boxes, not multicol's atomic-child model -
            // which never splits a child, so this defect can't occur there in the first place) whose last
            // fragment keeps too few lines. `orphans` is settled at the break point and is decided there
            // (LayoutBlockChildren); `widows` is not, because how many lines fall *after* a break depends
            // on content that has not been flowed yet - so it is answered here, on the pass that completes
            // the box, in one of two ways: by moving the minimum number of lines the spec asks for, or,
            // where that cannot be arranged, by pushing the whole box to the next fragmentainer. A
            // paragraph taller than one page is not pushed: it would just recreate the violation there.
            if (DomUtils.ContainsInlinesOnly(this) && LineBoxes.Count > 1
                && !_earlyBreakTaken && !PositionAssignedByEngine
                && int.TryParse(Orphans, out var orphans) && int.TryParse(Widows, out var widows)
                && (orphans > 1 || widows > 1))
            {
                var owPageHeight = HtmlContainer!.PageSize.Height;

                if (owPageHeight > 0)
                {
                    // Same shifted-grid convention as the BreakInside:Avoid block above.
                    var constraint = BlockConstraint.For(this);

                    // Absolute Y of the first shifted-page boundary at or after this box's own top.
                    var boundaryY = constraint.AbsoluteBandBottom;

                    if (boundaryY > Location.Y && boundaryY < ActualBottom)
                    {
                        var linesBefore = LineBoxes.Count(l => l.LineBottom <= boundaryY);
                        var linesAfter = LineBoxes.Count - linesBefore;

                        // The per-line answer first: keep fewer lines in the fragment before the break so
                        // the one after it reaches its minimum. It needs no room in the destination, so it
                        // is not subject to the whole-box push's own fits-on-one-page test.
                        if (linesBefore > 0 && linesAfter > 0 && linesAfter < widows
                            && TryKeepFewerLinesForWidows(linesBefore, widows, orphans))
                        {
                            return;
                        }

                        // The whole-box push moves the box to the next page without re-wrapping it, so it
                        // is subject to the same measure question the per-line correction is: a box that
                        // arrives on a page of a different measure is wrapped for the page it left, which
                        // is worse than the line minimum it was serving. Asked against the destination's
                        // whole band, not the room remaining below this box's *current* offset - the push
                        // lands the box fresh at that band's own content top.
                        var ownPageIndex = constraint.Fragmentainer!.SlotIndex;
                        if (linesBefore > 0 && linesAfter > 0 && (linesBefore < orphans || linesAfter < widows)
                            && ActualBottom - Location.Y <= constraint.NextBandHeight
                            && HtmlContainer.MeasureIsSharedBetween(ownPageIndex, ownPageIndex + 1)
                            && TakeEarlyBreak(EarlyBreak.Discover(this, boundaryY, EarlyBreakReason.OrphansWidows)))
                        {
                            return;
                        }
                    }
                }
            }

            if (Position.Value is PositionMode.Absolute)
            {
                if (Left.Value.IsKeyword && Right.Value.IsValue)
                {
                    var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                    var right = CssValueParser.ParseLength(Right.Value.Value!.Value, nearestPositionedAncestor.ActualWidth, this);
                    var actualRight = nearestPositionedAncestor.ClientRight + nearestPositionedAncestor.ActualPaddingRight - right;

                    var delta = actualRight - ActualRight;

                    OffsetLeft(delta);
                }

                // Symmetric vertical-axis counterpart to the right-edge fallback just above: `top` was
                // already always honored when set (the primary Position-is-Absolute branch earlier in
                // this method), but `bottom` was never read anywhere, so a box relying on `bottom` with
                // `top: auto` silently stayed at the containing block's top edge instead of being placed
                // relative to its bottom edge.
                if (Top.Value.IsKeyword && Bottom.Value.IsValue)
                {
                    var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                    var bottom = CssValueParser.ParseLength(Bottom.Value.Value!.Value, nearestPositionedAncestor.ActualHeight, this);

                    // Unlike ActualRight/ActualWidth (resolved for every box, including this ancestor,
                    // before its children are laid out - see the GetBoxWidth call earlier in this
                    // method), a block-container ancestor's ActualBottom is only finalized by
                    // ApplyHeight/MarginBottomCollapse AFTER all of its children (including this box)
                    // have already run their own PerformLayoutImp - so ClientBottom here would still be
                    // reading a provisional, usually-wrong value. Resolve the ancestor's border-box
                    // height directly from its own declared CSS Height (independent of child layout
                    // order) when it has one; only fall back to its (possibly still-provisional)
                    // ActualBottom for an auto-height ancestor, where there is no better source yet.
                    var ancestorBorderBoxHeight = CssLayoutEngine.GetBoxHeight(nearestPositionedAncestor)
                        ?? nearestPositionedAncestor.ActualBottom - nearestPositionedAncestor.Location.Y;
                    var ancestorPaddingBoxBottom = nearestPositionedAncestor.Location.Y + ancestorBorderBoxHeight
                        - nearestPositionedAncestor.ActualBorderBottomWidth;

                    var actualBottom = ancestorPaddingBoxBottom - bottom;

                    var delta = actualBottom - ActualBottom;

                    OffsetTop(delta);
                }
            }

            // Named-page registration tail: block containers already registered before child layout
            // (see the early registration above the layout-engine dispatch); everything else (e.g. a
            // box that never entered the block branch) registers here, after every branch above that
            // can still move this box's own Location. For an already-registered box this is a re-sync
            // for movers that bypass OffsetTop (CssLayoutEngineTable's whole-table pre-check assigns
            // Location directly). A *later* reposition by an ancestor's layout engine after this
            // box's own PerformLayoutImp has returned (e.g. CssLayoutEngineColumns re-banding a
            // column child via OffsetTop) is handled by retaining the registered element on
            // RegisteredNamedPageElement, which OffsetTop keeps in sync.
            // Reuse the shouldRegisterPage boolean computed near the top of this method - it must NOT
            // be re-derived here: the early registration above mutates HtmlContainer.ActivePageName,
            // so a fresh UsedPageName != ActivePageName comparison would now read false for an
            // already-registered box and skip its Y-drift re-sync.
            if (_shouldRegisterPage)
            {
                var registrationY = NamedPageRegistrationY();
                if (RegisteredNamedPageElement is null)
                {
                    RegisteredNamedPageElement = HtmlContainer!.RegisterNamedPageElement(UsedPageName, registrationY);
                }
                else if (Math.Abs(RegisteredNamedPageElement.Y - registrationY) > HtmlContainerInt.PageBoundaryEpsilon)
                {
                    HtmlContainer!.MoveNamedPageElement(RegisteredNamedPageElement, registrationY);
                }
            }

            // Correct the Y captured too early by ApplyStringSet (called near the top of this method,
            // before Location was known) now that it's final. NamedStrings holds the exact same object
            // references already registered in HtmlContainer's document-level list (ApplyStringSet
            // stores one shared instance in both places), so mutating Y here updates both — no need to
            // touch the document-level list's API, and safe regardless of when other boxes read the
            // document-level list's *value*, since nothing but paint-time margin-box resolution ever
            // reads Y.
            if (NamedStrings.Count > 0)
            {
                foreach (var namedString in NamedStrings.Values)
                {
                    namedString.Y = Location.Y;
                }
            }

#if DEBUG
            Console.WriteLine($"layout finish: {ToString()} [x: {Location.X}, y: {Location.Y}, b: {ActualBottom}, r: {ActualRight}, h: {Size.Height}, w: {Size.Width}]");
#endif
            if (IsFixed) return;

            var actualWidth = Math.Max(GetMinimumWidth() + GetWidthMarginDeep(this), Size.Width < 90999 ? ActualRight - HtmlContainer!.Root!.Location.X : 0);
            HtmlContainer!.ActualSize = CommonUtils.Max(HtmlContainer.ActualSize, new RSize(actualWidth, ActualBottom - HtmlContainer!.Root!.Location.Y));
        }

        /// <summary>
        /// Loads this box's own `background-image`/`list-style-image` layers (NOT `ContentImage` -
        /// CSS generated-content images stay in the base <see cref="MeasureWordsSize"/> flow, since
        /// they also need the phantom-image-word logic right after). Extracted so a replaced element
        /// (<see cref="CssBoxImage"/>, <see cref="CssBoxObject"/> once resolved) can still load its OWN
        /// CSS background - those two override <see cref="MeasureWordsSize"/> and short-circuit before
        /// ever reaching the base implementation once they know they're replaced content, which
        /// silently skipped this box's own `background-image` entirely (its `Image` stayed null
        /// forever, so `CssImagePainter.Paint`'s `urlImage.Image != null` guard always failed at paint
        /// time). Acid2's own "#eyes-a object object object" - a resolved, replaced &lt;object&gt; with
        /// its own `background: url(...) fixed 1px 0` checkerboard tile - is exactly this: the tile
        /// silently never painted at all, leaving ".eyes"'s own red background fully exposed instead of
        /// interlocking into solid yellow with "#eyes-b"'s matching tile.
        /// </summary>
        internal async ValueTask EnsureAuxiliaryImagesLoadedAsync()
        {
            if (BackgroundImages is { Count: > 0 })
                foreach (var image in BackgroundImages)
                    await image.EnsureLoadedAsync(HtmlContainer!);

            if (ListStyleImage != null)
                await ListStyleImage.EnsureLoadedAsync(HtmlContainer!);
        }

        /// <summary>
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g"></param>
        internal virtual async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (_wordsSizeMeasured) return;

            await EnsureAuxiliaryImagesLoadedAsync();

            if (ContentImage != null)
            {
                await ContentImage.EnsureLoadedAsync(HtmlContainer!);
                // Add a phantom image word so this box claims space in inline layout
                if (Words.Count == 0)
                {
                    var w = CssValueParser.IsValidLength(Width)
                        ? CssValueParser.ParseLength(Width, ContainingBlock?.Size.Width ?? 0, this) : 20;
                    var h = CssValueParser.IsValidLength(Height)
                        ? CssValueParser.ParseLength(Height, ContainingBlock?.Size.Height ?? 0, this) : w;
                    Words.Add(new CssRectImage(this) { Width = w, Height = h });
                }
            }

            MeasureWordSpacing(g);
            MeasureLetterSpacing();

            if (Words.Count > 0)
            {
                foreach (var boxWord in Words)
                {
                    if (boxWord.IsImage) continue;
                    var font = ResolveWordFont(boxWord, this);
                    boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text!, font, ActualTextShapingFeatures).Width : 0;
                    // Letter-spacing adds space after every glyph shown including the last (N gaps for
                    // an N-glyph word) - matching both the PDF Tc operator's actual per-glyph behavior
                    // (PaintWords/RealizeFont) and CSS Text 3 §7.2, which only exempts the start/end of a
                    // *line*, not the end of a word. Reserving only N-1 gaps here (an old CSS1/2.1-era
                    // assumption) undersized the word's own box, so its Tc-driven paint spilled one
                    // letter-spacing unit into the next word's gap - collapsing adjacent words together
                    // once letter-spacing reached the gap's width. The gap count is the *shaped glyph*
                    // count, not the character count - a GSUB ligature merges several characters into
                    // one glyph, so Tc (applied once per glyph shown) fires fewer times than Text.Length
                    // would suggest.
                    if (boxWord.Text != "\n" && ActualLetterSpacing != 0)
                        boxWord.Width += g.CountShapedGlyphs(boxWord.Text!, font, ActualTextShapingFeatures) * ActualLetterSpacing;
                    boxWord.Height = ActualFont.Height;
                }
            }

            _wordsSizeMeasured = true;
        }

        /// <summary>
        /// Re-measures every word in this box using <paramref name="firstLineStyle"/>'s font/letter-
        /// spacing instead of this box's own, and marks each with <see cref="CssRect.FirstLineStyle"/>
        /// so paint time (and <see cref="RemeasureWordsTail"/>, if this box's content later turns out
        /// to straddle the line-1/2 boundary) can find their way back to it. Called from
        /// <see cref="CssLayoutEngine.FlowBox"/> right after the ordinary (one-time, cached)
        /// <see cref="MeasureWordsSize"/> pass, only while still on the target's first line - unlike
        /// that method, this always re-runs (no "already measured" guard), since which words actually
        /// end up using first-line style can change (see <see cref="RemeasureWordsTail"/>).
        /// </summary>
        internal void ApplyFirstLineStyleOverride(RGraphics g, CssBox firstLineStyle)
        {
            firstLineStyle.MeasureWordSpacing(g);
            firstLineStyle.MeasureLetterSpacing();

            foreach (var boxWord in Words)
            {
                if (boxWord.IsImage) continue;

                boxWord.FirstLineStyle = firstLineStyle;

                // A ::first-line rule's own text-transform (if it declares one different from this
                // box's own) must be re-derived from OriginalText rather than Text - Text may already be
                // case-transformed by this box's own TextTransform, which for a value like uppercase has
                // irreversibly destroyed the casing information capitalize/lowercase would need.
                boxWord.FirstLineText = firstLineStyle.TextTransform != TextTransform && boxWord.Text != "\n"
                    ? ApplyTextTransform(boxWord.OriginalText ?? boxWord.Text!, firstLineStyle.TextTransform)
                    : null;
                var effectiveText = boxWord.FirstLineText ?? boxWord.Text;

                var font = ResolveWordFont(boxWord, firstLineStyle);
                boxWord.Width = effectiveText != "\n" ? g.MeasureString(effectiveText!, font, firstLineStyle.ActualTextShapingFeatures).Width : 0;
                // See MeasureWordsSize's identical fix/comment - N gaps for an N-glyph word, not N-1,
                // and the shaped glyph count rather than the character count.
                if (effectiveText != "\n" && firstLineStyle.ActualLetterSpacing != 0)
                    boxWord.Width += g.CountShapedGlyphs(effectiveText!, font, firstLineStyle.ActualTextShapingFeatures) * firstLineStyle.ActualLetterSpacing;
                boxWord.Height = font.Height;
            }
        }

        /// <summary>
        /// Reverts words from <paramref name="fromWordIndex"/> onward back to this box's own (non-
        /// first-line) font/letter-spacing, clearing their <see cref="CssRect.FirstLineStyle"/>. Called
        /// by <see cref="CssLayoutEngine.FlowBox"/> at the exact moment a box's content is found to
        /// straddle the line-1/2 boundary: words up to <paramref name="fromWordIndex"/> already
        /// committed to line 1 (and genuinely render with first-line style - CSS2.1 first-line
        /// applies to whatever ends up on the first formatted line, which these words did), but
        /// <paramref name="fromWordIndex"/> onward are wrapping to a later line and are no longer
        /// first-line content, so their width (measured using the first-line font/spacing, which may
        /// differ from this box's own) needs correcting before line-2+ placement continues. This is
        /// the piece that makes width-affecting ::first-line properties (font-size, letter-spacing,
        /// word-spacing) fully correct even when a single inline element's content spans the boundary,
        /// rather than only approximately so.
        /// </summary>
        internal void RemeasureWordsTail(RGraphics g, int fromWordIndex)
        {
            for (var i = fromWordIndex; i < Words.Count; i++)
            {
                var boxWord = Words[i];
                if (boxWord.IsImage) continue;

                boxWord.FirstLineStyle = null;
                boxWord.FirstLineText = null;

                var font = ResolveWordFont(boxWord, this);
                boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text!, font, ActualTextShapingFeatures).Width : 0;
                // See MeasureWordsSize's identical fix/comment - N gaps for an N-glyph word, not N-1,
                // and the shaped glyph count rather than the character count.
                if (boxWord.Text != "\n" && ActualLetterSpacing != 0)
                    boxWord.Width += g.CountShapedGlyphs(boxWord.Text!, font, ActualTextShapingFeatures) * ActualLetterSpacing;
                boxWord.Height = font.Height;
            }
        }

        /// <summary>
        /// Searches for the first word occurrence inside the box, on the specified linebox
        /// </summary>
        /// <param name="b"></param>
        /// <param name="line"> </param>
        /// <returns></returns>
        internal static CssRect? FirstWordOccurence(CssBox b, CssLineBox line)
        {
            switch (b.Words.Count)
            {
                case 0 when b.Boxes.Count == 0:
                    return null;
                case > 0:
                    {
                        foreach (CssRect word in b.Words)
                        {
                            if (line.Words.Contains(word))
                            {
                                return word;
                            }
                        }
                        return null;
                    }
                default:
                    {
                        foreach (CssBox bb in b.Boxes)
                        {
                            CssRect? w = FirstWordOccurence(bb, line);

                            if (w != null)
                            {
                                return w;
                            }
                        }

                        return null;
                    }
            }
        }

        /// <summary>
        /// Gets the specified Attribute, returns string.Empty if no attribute specified
        /// </summary>
        /// <param name="attribute">Attribute to retrieve</param>
        /// <returns>Attribute value or string.Empty if no attribute specified</returns>
        internal string GetAttribute(string attribute)
        {
            return GetAttribute(attribute, string.Empty);
        }

        /// <summary>
        /// Gets the value of the specified attribute of the source HTML tag.
        /// </summary>
        /// <param name="attribute">Attribute to retrieve</param>
        /// <param name="defaultValue">Value to return if attribute is not specified</param>
        /// <returns>Attribute value or defaultValue if no attribute specified</returns>
        [return: NotNullIfNotNull(nameof(defaultValue))]
        internal string? GetAttribute(string attribute, string? defaultValue)
        {
            return HtmlTag != null ? HtmlTag.TryGetAttribute(attribute, defaultValue) : defaultValue;
        }

        #region ICssDomNode

        // The HTML box tree is the primary ICssDomNode implementation the selector engine matches
        // against; these members are thin views over the box's existing state. HTML matches element/
        // attribute names ASCII case-insensitively (unlike SVG's XML case-sensitivity), so NameComparison
        // reports InvariantCultureIgnoreCase - the value the matcher previously hardcoded, keeping HTML
        // matching byte-identical. Implemented explicitly where the natural name collides with an existing
        // member (GetAttribute, the CustomProperties field).
        string? ICssDomNode.TagName => HtmlTag?.Name;

        string? ICssDomNode.GetAttribute(string name) => GetAttribute(name, null);

        StringComparison ICssDomNode.NameComparison => StringComparison.InvariantCultureIgnoreCase;

        ICssDomNode? ICssDomNode.Parent => ParentBox;

        IReadOnlyList<ICssDomNode> ICssDomNode.Children => Boxes;

        bool ICssDomNode.IsRoot => IsRoot;

        bool ICssDomNode.IsEmpty => IsEmptyElement(this);

        /// <summary>
        /// Selectors 4 §9.5's <c>:empty</c> test over the box tree: an element child, or a text child
        /// carrying anything other than collapsible white space, makes the element non-empty. Comments
        /// never reach the box tree at all (<c>HtmlParser</c> drops the token), so they need no handling.
        /// Shared with the SVG subsystem's <c>ICssDomNode</c> view of the same boxes.
        /// </summary>
        /// <remarks>
        /// Two box kinds are not source children and so cannot decide the answer. A generated
        /// <c>::before</c>/<c>::after</c>/<c>::marker</c> box is skipped, because <c>:empty</c> is defined
        /// over the source tree and one may already have been synthesized as a match side effect of an
        /// earlier rule in the very same cascade pass - letting it count would make <c>div:empty</c> depend
        /// on whether a <c>div::before</c> rule happened to be matched first. An anonymous box (no source
        /// element of its own) is transparent instead: the test descends into it, since the restructuring
        /// passes wrap real element and text children in one. A <c>::first-letter</c> box is deliberately
        /// NOT skipped - unlike the other pseudo-elements it holds real source text, split out of a text
        /// box that keeps only the remainder.
        /// </remarks>
        internal static bool IsEmptyElement(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.IsBeforePseudoElement || child.IsAfterPseudoElement || child.IsMarkerPseudoElement)
                    continue;

                if (child.HtmlTag is not null) return false;
                if (!HtmlUtils.IsNullOrCollapsibleWhitespace(child.Text)) return false;
                if (!IsEmptyElement(child)) return false;
            }

            return true;
        }

        Dictionary<string, string>? ICssDomNode.CustomProperties
        {
            get => CustomProperties;
            set => CustomProperties = value;
        }

        #endregion

        /// <summary>
        /// Gets the minimum width that the box can be.<br/>
        /// The box can be as thin as the longest word plus padding.<br/>
        /// The check is deep thru box tree.<br/>
        /// </summary>
        /// <returns>the min width of the box</returns>
        internal double GetMinimumWidth()
        {
            double maxWidth = 0;
            CssRect? maxWidthWord = null;
            GetMinimumWidth_LongestWord(this, ref maxWidth, ref maxWidthWord);

            double padding = 0f;
            if (maxWidthWord != null)
            {
                var box = maxWidthWord.OwnerBox;
                while (box != null)
                {
                    padding += box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualBorderLeftWidth + box.ActualPaddingLeft;
                    box = box != this ? box.ParentBox : null;
                }
            }

            return maxWidth + padding;
        }

        /// <summary>
        /// Gets the longest word (in width) inside the box, deeply.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="maxWidth"> </param>
        /// <param name="maxWidthWord"> </param>
        /// <returns></returns>
        private static void GetMinimumWidth_LongestWord(CssBox box, ref double maxWidth, ref CssRect? maxWidthWord)
        {
            if (box.Words.Count > 0)
            {
                foreach (CssRect cssRect in box.Words)
                {
                    if (cssRect.Width > maxWidth)
                    {
                        maxWidth = cssRect.Width;
                        maxWidthWord = cssRect;
                    }
                }
            }
            else
            {
                foreach (CssBox childBox in box.Boxes)
                {
                    if (childBox.DerivedStyle.ActualDisplay == CssConstants.None) continue;
                    GetMinimumWidth_LongestWord(childBox, ref maxWidth, ref maxWidthWord);
                }
            }
        }

        /// <summary>
        /// Get the total margin value (left and right) from the given box to the given end box.<br/>
        /// </summary>
        /// <param name="box">the box to start calculation from.</param>
        /// <returns>the total margin</returns>
        private static double GetWidthMarginDeep(CssBox? box)
        {
            double sum = 0f;

            if (box is not null && (box.Size.Width > 90999 || box.ParentBox is { Size.Width: > 90999 }))
            {
                while (box != null)
                {
                    sum += box.ActualMarginLeft + box.ActualMarginRight;
                    box = box.ParentBox;
                }
            }
            return sum;
        }

        /// <summary>
        /// Gets the maximum bottom of the boxes inside the startBox
        /// </summary>
        /// <param name="startBox"></param>
        /// <param name="currentMaxBottom"></param>
        /// <returns></returns>
        internal static double GetMaximumBottom(CssBox startBox, double currentMaxBottom)
        {
            foreach (var line in startBox.Rectangles.Keys)
            {
                currentMaxBottom = Math.Max(currentMaxBottom, startBox.Rectangles[line].Bottom);
            }

            foreach (var b in startBox.Boxes)
            {
                currentMaxBottom = Math.Max(currentMaxBottom, GetMaximumBottom(b, currentMaxBottom));
            }

            if (startBox.Height is not CssConstants.Auto)
            {
                currentMaxBottom = Math.Max(currentMaxBottom, startBox.ActualBottom);
            }

            return currentMaxBottom;
        }

        /// <summary>
        /// Get the <paramref name="minWidth"/> and <paramref name="maxWidth"/> width of the box content.<br/>
        /// </summary>
        /// <param name="minWidth">The minimum width the content must be so it won't overflow (largest word + padding).</param>
        /// <param name="maxWidth">The total width the content can take without line wrapping (with padding).</param>
        internal void GetMinMaxWidth(out double minWidth, out double maxWidth)
        {
            double min = 0f;
            double maxSum = 0f;
            double paddingSum = 0f;
            double marginSum = 0f;

            GetMinMaxSumWords(this, ref min, ref maxSum, ref paddingSum, ref marginSum);

            maxWidth = paddingSum + maxSum;
            minWidth = paddingSum + (min < 90999 ? min : 0);
        }

        /// <summary>
        /// Get the <paramref name="min"/> and <paramref name="maxSum"/> of the box words content and <paramref name="paddingSum"/>.<br/>
        /// </summary>
        /// <param name="box">the box to calculate for</param>
        /// <param name="min">the width that allows for each word to fit (width of the longest word)</param>
        /// <param name="maxSum">the max width a single line of words can take without wrapping</param>
        /// <param name="paddingSum">the total amount of padding the content has </param>
        /// <param name="marginSum"></param>
        /// <returns></returns>
        private static void GetMinMaxSumWords(CssBox box, ref double min, ref double maxSum, ref double paddingSum, ref double marginSum)
        {
            double? oldSum = null;
            // paddingSum must be scoped per "line" the same way maxSum is (see the oldSum save/restore
            // below) - it represents the border/padding belonging to the WIDEST line found so far, not a
            // running total across every sibling's own unrelated line. Without oldPaddingSum, a block
            // box's own border/padding (and every descendant's, recursively) permanently accumulated
            // into paddingSum and was never reset between siblings - e.g. Acid2's "#eyes-a" (contributing
            // real intrinsic word/image width) followed by sibling "#eyes-b"/"#eyes-c" (contributing 0
            // words but their own borders) summed all three siblings' unrelated border/padding into one
            // box's shrink-to-fit width instead of using only the widest line's own padding, inflating
            // position:absolute ".eyes"'s auto width well past its actual content.
            double? oldPaddingSum = null;

            // not inline (block) boxes start a new line so we need to reset the max sum
            if (box.DerivedStyle.ActualDisplay != CssConstants.Inline && box.DerivedStyle.ActualDisplay != CssConstants.TableCell && box.WhiteSpace.Value != Whitespace.NoWrap)
            {
                oldSum = maxSum;
                maxSum = marginSum;
                oldPaddingSum = paddingSum;
                paddingSum = 0;
            }

            // add the padding
            paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualPaddingLeft;


            // for tables the padding also contains the spacing between cells
            if (box.DerivedStyle.ActualDisplay == CssConstants.Table)
                paddingSum += CssLayoutEngineTable.GetTableSpacing(box);

            if (box.Words.Count > 0)
            {
                // calculate the min and max sum for all the words in the box
                foreach (var word in box.Words)
                {
                    maxSum += word.FullWidth + (word.HasSpaceBefore ? word.OwnerBox.ActualWordSpacing : 0);
                    min = Math.Max(min, word.Width);
                }

                // remove the last word padding
                if (box.Words.Count > 0 && !box.Words[^1].HasSpaceAfter)
                    maxSum -= box.Words[^1].ActualWordSpacing;
            }
            else
            {
                // recursively on all the child boxes
                foreach (var childBox in box.Boxes)
                {
                    if (childBox.DerivedStyle.ActualDisplay == CssConstants.None) continue;

                    marginSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;

                    //maxSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;
                    var maxSumBeforeChild = maxSum;
                    GetMinMaxSumWords(childBox, ref min, ref maxSum, ref paddingSum, ref marginSum);

                    // This walk otherwise never consults a box's own explicit CSS `width` at all - only
                    // literal word/text content. That's usually fine (explicit width constrains layout
                    // AFTER content is measured, not the content's own intrinsic size) but breaks down
                    // for a child whose only real sizing signal IS an explicit width with no word
                    // content to measure (e.g. a solid-color box, or - Acid2's own case - an anonymous
                    // table-cell (CSS2.1 17.2.1) wrapping a nested "display:table"/"display:list-item"
                    // "<li>" that has "width:1em" but no text): the recursive content sum alone finds
                    // nothing, so the anonymous cell sized itself to 0 instead of its child's real 1em,
                    // clipping/overlapping the nested content. A plain absolute length (not a percentage
                    // - resolving that here would read this box's own not-yet-final ActualWidth,
                    // circular in exactly the way GetBoxWidth's shrink-to-fit callers already guard
                    // against) is folded in as an explicit floor for this line's running total.
                    //
                    // Excludes a non-replaced inline box (Display:Inline with no Words of its own - a
                    // replaced inline element, e.g. an image or resolved <object>, is already measured
                    // via the Words.Count>0 branch elsewhere in this function and never reaches this
                    // check in a way that would be wrongly excluded here): per CSS2.1 10.3.3, `width`
                    // has NO EFFECT on a non-replaced inline-level box. Acid2's own
                    // "#eyes-a object[type] { width: 7.5em; }" is exactly this - the middle <object
                    // type="text/html"> in the fallback chain, which falls back to display:inline and
                    // is deliberately meant to have this width ignored (Round 6 verified this is a
                    // real no-op at layout time via CssBox.PerformLayoutImp's IsBlock gate).
                    //
                    // A child that starts its OWN new "line" (same condition as the block-reset check
                    // at the top of this function) must have its explicit width combined via Math.Max,
                    // NOT added to maxSumBeforeChild - maxSumBeforeChild already reflects whatever an
                    // EARLIER, unrelated block-level sibling contributed (each such sibling resets to
                    // its own line via the oldSum mechanism and is meant to compete for "widest line
                    // wins", not accumulate). The very first version of this fix always added
                    // maxSumBeforeChild + explicitContentWidth unconditionally, which was fine for a
                    // lone child (maxSumBeforeChild was 0) but wrongly summed multiple separate
                    // block-level siblings' explicit widths together - Acid2's own ".eyes" with three
                    // block-level children ("#eyes-a" ~128 intrinsic, "#eyes-b"/"#eyes-c" each
                    // explicit 10em/90pt) summed to 308 (128+90+90) instead of correctly taking the
                    // widest single line (~128).
                    if (CssValueParser.IsValidLength(childBox.Width) && !childBox.Width.EndsWith('%')
                        && !(childBox.DerivedStyle.ActualDisplay == CssConstants.Inline && childBox.Words.Count == 0))
                    {
                        var explicitContentWidth = CssValueParser.ParseLength(childBox.Width, 0, childBox);
                        var childStartsNewLine = childBox.DerivedStyle.ActualDisplay != CssConstants.Inline
                            && childBox.DerivedStyle.ActualDisplay != CssConstants.TableCell && childBox.WhiteSpace.Value != Whitespace.NoWrap;
                        maxSum = childStartsNewLine
                            ? Math.Max(maxSum, explicitContentWidth)
                            : Math.Max(maxSum, maxSumBeforeChild + explicitContentWidth);
                        min = Math.Max(min, explicitContentWidth);
                    }

                    marginSum -= childBox.ActualMarginLeft + childBox.ActualMarginRight;
                }
            }

            // max sum (and its matching padding contribution) is the max of all the lines in the box
            if (oldSum.HasValue)
            {
                maxSum = Math.Max(maxSum, oldSum.Value);
                paddingSum = Math.Max(paddingSum, oldPaddingSum!.Value);
            }
        }

        /// <summary>
        /// Gets the rectangles where inline box will be drawn. See Remarks for more info.
        /// </summary>
        /// <returns>Rectangles where content should be placed</returns>
        /// <remarks>
        /// Inline boxes can be split across different LineBoxes, that's why this method
        /// Delivers a rectangle for each LineBox related to this box, if inline.
        /// </remarks>
        /// <summary>
        /// Set by an ancestor's lookahead in <see cref="FoldOwnAdjoiningTopMargins"/> when this box is a
        /// non-anchor member of a shared chain of adjoining first-in-flow-child margins: always 0,
        /// because the anchor member (the outermost box in the chain, wherever the chain's resolution
        /// began) already received the group's FULL collapsed value as its own return value, and this
        /// box's position is computed relative to its immediate parent's already-correctly-positioned
        /// ClientTop - adding the group value again here would double (or triple, ...) count it. See the
        /// lookahead loop below for why this box must not resolve its own top margin independently.
        /// </summary>
        private double? _groupTopMarginOverride;

        /// <summary>
        /// The collapsed margin between whatever this frame placed before <paramref name="child"/> and
        /// the child itself — CSS 2.1 §8.3.1's adjoining-margin set, resolved for one break point.
        /// </summary>
        /// <param name="child">the box being placed in this frame</param>
        /// <param name="prevSibling">the box this frame placed immediately before it, or null</param>
        /// <returns>what the caller adds to <paramref name="prevSibling"/>'s placed bottom</returns>
        /// <remarks>
        /// <b>The set spans two frames, which is why the frame resolves it and the child does not.</b> Half
        /// of it is what precedes the child — a predecessor's bottom margin and, through a run of
        /// self-collapsing predecessors, everything those fold in — which only this frame can see
        /// (<see cref="FoldMarginsPrecedingChild"/>). The other half is the child's own top margin and the
        /// chain of first-in-flow-child margins adjoining it, which is a walk into the child's own subtree
        /// and stays there (<see cref="FoldOwnAdjoiningTopMargins"/>). §8.3.1 collapses the <i>whole</i> set
        /// at once, so the two halves fold into one <see cref="AdjoiningMarginSet"/> rather than being
        /// resolved separately and combined afterwards.
        /// </remarks>
        private double CollapsedMarginBefore(CssBox child, CssBox? prevSibling)
        {
            // Per CSS2.1 8.3.1, floats (and absolutely/fixed-positioned boxes, which never reach this
            // call site - see the Position guard at the call site in CssLayoutEngine) never COLLAPSE
            // their own margin with anything (they're out-of-flow, so their margin never "adjoins"
            // another box's) - but the preceding sibling's own trailing margin still occupies real
            // physical space the float must be positioned after; only the MERGING (taking whichever
            // margin is larger/more-negative instead of summing both) is skipped, not the sibling's
            // margin itself. Acid2's own ".forehead" (margin-bottom: 4em) immediately followed by
            // ".nose" (float:left, margin: -2em ...) is exactly this: the correct gap between them is
            // forehead's 4em margin-bottom PLUS nose's own -2em margin-top (summed, net +2em), not
            // just nose's raw -2em with forehead's entire margin-bottom silently dropped - which
            // previously pulled ".nose" a full margin-bottom's worth too far up, overlapping ".eyes"
            // far more than the fixture intends and hiding the nose diamond behind it entirely.
            if (child.IsFloated)
            {
                var floatValue = child.ActualMarginTop + (prevSibling?.GetEffectiveBottomMargin() ?? 0);
                child.CollapsedMarginTop = floatValue;
                return floatValue;
            }

            // An ancestor's own lookahead already folded the child into a shared chain of adjoining
            // first-in-flow-child margins and resolved the group's true, fully-collapsed value - use it
            // directly rather than resolving independently. The child's own isolated view (e.g. via the
            // escape formula below) could only ever "see" as far as its immediate parent, which is
            // exactly what caused a real bug: a 3+-level chain where the outermost box's position was
            // itself fixed by sibling-margin-collapse before a deeper descendant's larger margin was
            // known, silently adding on top instead of properly collapsing into one shared value.
            if (child.TryTakeGroupTopMarginOverride(out var overrideValue))
            {
                child.CollapsedMarginTop = overrideValue;
                return overrideValue;
            }

            // CSS2.1 §8.3.1: a set of adjoining margins collapses to the maximum of its positive
            // margins plus the most negative of its negative margins, computed over the WHOLE set at
            // once (see AdjoiningMarginSet). Acid2's ".forehead / .empty / .smile" run is exactly
            // such a mixed-sign set.
            var margins = new AdjoiningMarginSet();

            var anchor = FoldMarginsPrecedingChild(child, prevSibling, ref margins);

            child.FoldOwnAdjoiningTopMargins(ref margins);

            var groupValue = margins.CollapsedValue;

            // fix for hr tag
            if (groupValue < 0.1 && child.HtmlTag is { Name: "hr" })
            {
                groupValue = child.GetEmHeight() * 1.1f;
            }

            child.CollapsedMarginTop = groupValue;

            if (prevSibling == null)
            {
                return groupValue;
            }

            // Every preceding sibling back to the start of this frame's children is self-collapsing
            // (no real anchor found) - approximate the anchor as this frame's own content-top, same
            // as if the child were the frame's first child (a rare compound edge case).
            var anchorY = anchor != null
                ? anchor.StaticBottom + anchor.ActualBorderBottomWidth
                : child.ContainingBlock.ClientTop;

            // The call site unconditionally adds prevSibling.StaticBottom + its bottom border on top
            // of whatever this method returns - back that out so the final sum lands at the true,
            // fully-resolved anchorY + groupValue regardless of how partial prevSibling's own
            // (already-finalized, possibly stale) position turned out to be. StaticBottom on both
            // sides (anchor and back-out) so a relatively-positioned sibling's visual offset never
            // leaks into following flow (CSS 2.1 §9.4.3).
            return anchorY + groupValue - prevSibling.StaticBottom - prevSibling.ActualBorderBottomWidth;
        }

        /// <summary>
        /// Folds into <paramref name="margins"/> everything this frame placed before
        /// <paramref name="child"/> that adjoins it, and returns the box the child's position is
        /// ultimately measured from.
        /// </summary>
        /// <returns>
        /// The nearest non-self-collapsing predecessor — the real position anchor — or null when every
        /// preceding sibling collapses through.
        /// </returns>
        /// <remarks>
        /// This is the half of §8.3.1's set that is the frame's to see: it walks this frame's own child
        /// list backwards, which no box can do from inside itself.
        /// </remarks>
        private CssBox? FoldMarginsPrecedingChild(CssBox child, CssBox? prevSibling, ref AdjoiningMarginSet margins)
        {
            if (prevSibling is null) return null;

            // A self-collapsing previous sibling (and any run of self-collapsing siblings
            // immediately before it) contributes no height of its own, so every margin adjoining
            // through it (its own top+bottom plus, recursively, its in-flow descendants' - see
            // FoldSelfCollapsingMargins) joins the group, and the group keeps adjoining further
            // back rather than acting as a break in the chain (CSS2.1 8.3.1, self-collapsing
            // empty boxes). Walk back to find the nearest NON-self-collapsing predecessor - that one
            // (not prevSibling itself, when prevSibling is self-collapsing) is the real position
            // anchor, because a self-collapsing box's own Location only reflected a partial view of
            // the group's margin at the time IT was positioned (the child may be the one that finally
            // reveals the group's true, larger collapsed value). Bounded defensively (real documents
            // never have this many consecutive self-collapsing siblings) so any unexpected sibling-
            // chain quirk degrades to "stop walking back" instead of spinning forever.
            // A box whose own position was set by a forced break anchors what follows it even when
            // it is self-collapsing: the break is a positional constraint rather than a margin, so
            // walking back past it would resolve the child against an earlier sibling and undo the
            // break outright. An empty "<div class='page-break'>" marker is exactly that box.
            CssBox? anchor = null;

            if (prevSibling.IsMarginCollapseThrough() && !prevSibling.PlacedByForcedBreak)
            {
                prevSibling.FoldSelfCollapsingMargins(ref margins);
            }
            else
            {
                anchor = prevSibling;
                margins.Fold(prevSibling.ActualMarginBottom);
            }

            var walker = prevSibling;
            var walkBackSteps = 0;
            while (walker.IsMarginCollapseThrough() && !walker.PlacedByForcedBreak && walkBackSteps++ < 1000)
            {
                var earlierSibling = PreviousInFlowSibling(walker);
                if (earlierSibling == null || earlierSibling == walker) break;
                if (earlierSibling.IsMarginCollapseThrough() && !earlierSibling.PlacedByForcedBreak)
                {
                    earlierSibling.FoldSelfCollapsingMargins(ref margins);
                }
                else
                {
                    margins.Fold(earlierSibling.ActualMarginBottom);
                }
                walker = earlierSibling;
                if (!walker.IsMarginCollapseThrough() || walker.PlacedByForcedBreak) anchor = walker;
            }

            return anchor;
        }

        /// <summary>
        /// Folds into <paramref name="margins"/> this box's own top margin and every first-in-flow-child
        /// margin adjoining it — the half of §8.3.1's set that lives inside this box's own subtree.
        /// </summary>
        private void FoldOwnAdjoiningTopMargins(ref AdjoiningMarginSet margins)
        {
            // Only this box's own TOP margin joins its own position group - even when this box is
            // itself self-collapsing. Per CSS2.1 §8.3.1 a collapsed-through box's top border edge
            // sits where it would "if the element had a non-zero bottom border", i.e. its own bottom
            // margin positions only what FOLLOWS it (folded there via FoldSelfCollapsingMargins in
            // the following sibling's walk-back above), never the box itself. (When there is no
            // prevSibling at all, this is also the whole group: reaching that case means the parent
            // couldn't fold this box into its own lookahead - see the override in
            // CollapsedMarginBefore - so this box's top margin is genuinely isolated from anything
            // above it.)
            margins.Fold(ActualMarginTop);

            // Lookahead: does this box have a first-in-flow child whose own top margin is ALSO adjoining
            // (no border/padding/overflow of this box's own blocking it, no clearance on the child) -
            // and, transitively, that child's first-in-flow child, and so on? CSS2.1 8.3.1 requires the
            // WHOLE such chain to resolve to one single collapsed value; resolving it top-down without
            // this lookahead would let each level "lock in" a value before a deeper level's possibly
            // larger margin is even known. Walk the chain now (all the CSS-value-derived properties
            // involved - ActualMarginTop, border/padding widths - are independent of Y-position layout,
            // so reading them before these descendants are positioned is safe) and fold every member's
            // own top margin into the same running set - folding into the SET (rather than into the
            // final position-corrected return value, as an earlier version did) keeps a chain member's
            // small margin from displacing the group's already-larger collapsed value. THIS box (the
            // anchor, wherever the chain's resolution began) ends up with the group's full collapsed
            // value as the frame's return value. Every deeper chain member instead gets a 0 override:
            // since nothing separates them from their own immediate parent (that parent is either the
            // anchor itself or another 0 member), their position is already exactly right as soon as
            // it's computed relative to that parent's own (already-correct) ClientTop - giving them the
            // full group value AGAIN would double/triple/... count it at each level.
            var chainMembers = new List<CssBox>();
            var current = this;
            // Capped defensively (real documents never nest this deep) so a malformed/cyclic box tree
            // degrades to "stop extending the group" instead of hanging or overflowing the stack.
            while (chainMembers.Count < 1000 && current.Overflow.Value == PeachPDF.CSS.Overflow.Visible &&
                   current.ActualBorderTopWidth < 0.1 && current.ActualPaddingTop < 0.1)
            {
                var firstInFlowChild = current.Boxes.FirstOrDefault(b => !b.IsOutOfFlow && b.DerivedStyle.ActualDisplay != CssConstants.None);
                if (firstInFlowChild == null || firstInFlowChild.Clear.Value != ClearMode.None || firstInFlowChild == current) break;

                margins.Fold(firstInFlowChild.ActualMarginTop);
                chainMembers.Add(firstInFlowChild);
                current = firstInFlowChild;
            }

            foreach (var member in chainMembers)
            {
                member._groupTopMarginOverride = 0;
            }
        }

        /// <summary>
        /// Takes the group value an ancestor's lookahead already resolved for this box, if it left one.
        /// </summary>
        /// <remarks>
        /// One-shot: the override is consumed, because it describes the chain resolved on the pass that
        /// set it and must not survive into a later one.
        /// </remarks>
        private bool TryTakeGroupTopMarginOverride(out double value)
        {
            if (_groupTopMarginOverride is not { } resolved)
            {
                value = 0;
                return false;
            }

            _groupTopMarginOverride = null;
            value = resolved;
            return true;
        }

        /// <summary>
        /// A set of adjoining vertical margins being collapsed per CSS2.1 §8.3.1: the collapsed value
        /// of the whole set is the maximum of its positive margins plus the most negative of its
        /// negative margins, each defaulting to zero when absent. Kept as a running (max, min) pair
        /// rather than reduced pairwise because pairwise reduction
        /// loses information whenever signs mix across steps - e.g. collapsing {6.25em, -6em} first
        /// (0.25em) and then folding a 4em margin in gives 4em, but the true set value is still
        /// 0.25em because the 6.25em maximum keeps dominating the 4em.
        /// </summary>
        private struct AdjoiningMarginSet
        {
            private double _maxPositive;
            private double _minNegative;

            public void Fold(double margin)
            {
                _maxPositive = Math.Max(_maxPositive, margin);
                _minNegative = Math.Min(_minNegative, margin);
            }

            public readonly double CollapsedValue => _maxPositive + _minNegative;
        }

        /// <summary>
        /// This box's bottom-margin contribution when it precedes another box: its own bottom margin,
        /// unless it is a self-collapsing empty box (<see cref="IsMarginCollapseThrough"/>), in which
        /// case every margin adjoining through it first collapses into one pass-through value
        /// (CSS2.1 8.3.1) - see <see cref="FoldSelfCollapsingMargins"/>.
        /// </summary>
        private double GetEffectiveBottomMargin()
        {
            if (!IsMarginCollapseThrough()) return ActualMarginBottom;

            var margins = new AdjoiningMarginSet();
            FoldSelfCollapsingMargins(ref margins);
            return margins.CollapsedValue;
        }

        /// <summary>
        /// Folds every margin adjoining through this self-collapsing box (<see
        /// cref="IsMarginCollapseThrough"/>) into the running collapse set: its own top and bottom
        /// margins plus, recursively, those of its in-flow children - which are all themselves
        /// self-collapsing by definition, so ALL of their margins are part of one adjoining set per
        /// CSS2.1 §8.3.1. A self-collapsing box's pass-through contribution is the collapse of this
        /// whole set, not just its own two margins - Acid2's ".empty" (margin: 6.25em) with a child
        /// whose margin-bottom is -6em passes 0.25em through, not 6.25em, and that difference is
        /// what puts the following ".smile"'s hypothetical position back above the ".nose" float so
        /// clear:both actually triggers.
        /// </summary>
        private void FoldSelfCollapsingMargins(ref AdjoiningMarginSet margins, int depth = 0)
        {
            // Capped defensively (real documents never nest this deep) so a malformed/cyclic box tree
            // degrades to "stop folding" instead of a stack overflow.
            if (depth > 500) return;

            margins.Fold(ActualMarginTop);
            margins.Fold(ActualMarginBottom);

            foreach (var childBox in Boxes)
            {
                if (childBox.IsOutOfFlow || childBox.DerivedStyle.ActualDisplay == CssConstants.None) continue;
                childBox.FoldSelfCollapsingMargins(ref margins, depth + 1);
            }
        }

        /// <summary>
        /// Whether this box's own top and bottom margins are adjoining to each other (CSS2.1 8.3.1): the
        /// box has no top/bottom border or padding, resolves to zero/auto height and min-height, doesn't
        /// establish a new block formatting context, is in-flow, and either has no in-flow children or
        /// all of its in-flow children are themselves margin-collapse-through. Such a box contributes no
        /// height of its own and its margins pass through to whatever adjoins it.
        /// </summary>
        private bool IsMarginCollapseThrough(int depth = 0)
        {
            // Capped defensively (real documents never nest this deep) so a malformed/cyclic box tree
            // degrades to "not self-collapsing" instead of a stack overflow.
            if (depth > 500) return false;
            if (DerivedStyle.ActualDisplay == CssConstants.None) return false;
            if (IsOutOfFlow) return false;
            // A percentage height against an indefinite (not-yet-height-calculated) containing block
            // resolves to auto (CSS2.1 §10.5, the same rule ApplyHeight already applies) - Acid2's own
            // ".empty { margin: 6.25em; height: 10%; }" is written to exercise exactly this: its own
            // comment notes "computes to auto which makes it empty per 8.3.1:7 (own margins)".
            var heightIsAuto = Height == CssConstants.Auto ||
                (Height.EndsWith('%') && !ContainingBlock.IsHeightCalculated);
            if (!heightIsAuto) return false;
            if (Overflow.Value != PeachPDF.CSS.Overflow.Visible) return false;
            if (!(ActualPaddingTop < 0.1) || !(ActualPaddingBottom < 0.1)) return false;
            if (!(ActualBorderTopWidth < 0.1) || !(ActualBorderBottomWidth < 0.1)) return false;
            // A box with real text content (e.g. an anonymous text-node box) is not empty even when it
            // has zero nested CssBox children - it still has real line-box height from its own words.
            if (Words.Count > 0) return false;

            var minHeightZero = MinHeight == CssConstants.Auto ||
                (CssValueParser.IsValidLength(MinHeight) &&
                 CssValueParser.ParseLength(MinHeight, ContainingBlock.Size.Height, this) <= 0);
            if (!minHeightZero) return false;

            var inFlowChildren = Boxes.Where(b => !b.IsOutOfFlow && b.DerivedStyle.ActualDisplay != CssConstants.None && b != this).ToList();
            return inFlowChildren.Count == 0 || inFlowChildren.All(b => b.IsMarginCollapseThrough(depth + 1));
        }

        public virtual bool BreakPage()
        {
            var container = HtmlContainer;

            if (Size.Height >= container!.PageSize.Height)
                return false;

            // Given the height guard above, the box straddles a slot boundary exactly when its top
            // and bottom land in different slots. The epsilons make a flush fit a NON-break: a box
            // ending exactly ON a boundary is wholly inside the earlier slot (css-break-3 - no
            // spurious relocation for exact-fit content), where the historical modulo formulation
            // relocated it by a page.
            if (container.SlotStartingAt(Location.Y)
                >= container.SlotEndingAt(ActualBottom))
                return false;

            Location = Location with { Y = container.NextPageTopOf(Location.Y) };

            return true;
        }

        /// <summary>
        /// Calculate the actual right of the box by the actual right of the child boxes if this box actual right is not set.
        /// </summary>
        /// <returns>the calculated actual right value</returns>
        internal double CalculateActualRight()
        {
            if (!(ActualRight > 90999)) return ActualRight;

            var maxRight = 0d;

            double additionalMarginRight;

            foreach (var box in Boxes)
            {
                additionalMarginRight = box.BoxSizing.Value switch
                {
                    BoxSizingMode.ContentBox => 0,
                    BoxSizingMode.BorderBox => box.ActualMarginRight,
                    _ => throw new HtmlRenderException("Unknown BoxSizing", HtmlRenderErrorType.Layout)
                };

                // RelativeOffsetX backed out for the same reason MarginBottomCollapse uses
                // StaticBottom: a relatively-positioned child's visual offset must not widen the
                // parent (CSS 2.1 §9.4.3).
                maxRight = Math.Max(maxRight, box.ActualRight - box.RelativeOffsetX + additionalMarginRight);
            }

            additionalMarginRight = BoxSizing.Value switch
            {
                BoxSizingMode.ContentBox => 0,
                BoxSizingMode.BorderBox => ActualMarginRight,
                _ => throw new HtmlRenderException("Unknown BoxSizing", HtmlRenderErrorType.Layout)
            };

            return maxRight + ActualPaddingRight + additionalMarginRight + ActualBorderRightWidth;

        }

        /// <summary>
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <returns>Resulting bottom margin</returns>
        internal double MarginBottomCollapse()
        {
            var lastNonFloatingBox = Boxes.Last(b => !b.IsOutOfFlow);

            double margin = 0;
            // Per CSS 2.1 §8.3.1, a box's own bottom margin can only collapse with (i.e. be folded
            // into) its last in-flow child's bottom margin when there is nothing of this box's own
            // separating the two - non-zero bottom padding or a bottom border on THIS box blocks it,
            // just like it blocks parent/child collapsing on the top side, and so does this box
            // establishing a new block formatting context (e.g. via `overflow`).
            //
            // The "is this box its own parent's last child" condition below is NOT an unrelated/
            // incidental restriction - it is load-bearing. When this box folds its own bottom margin
            // into its own ActualBottom, that inflated ActualBottom is what a FOLLOWING SIBLING's own
            // CollapsedMarginBefore call adds on top of (via the ordinary adjoining-sibling-margin path,
            // which separately reads this box's raw ActualMarginBottom too) - if this box has a
            // following sibling, the same margin value gets counted twice: once baked into
            // ActualBottom here, and again via the sibling's own fold of prevSibling.
            // ActualMarginBottom into its adjoining set. Removing this gate (an earlier attempt at this fix did
            // exactly that) reproduces precisely that double-count - confirmed via a real regression
            // where a heading's own 60pt bottom margin was added once into the heading's own height and
            // a second time into the following paragraph's top offset, an easy 60pt to trace back to
            // the heading's own declared margin. Only when this box has NO following sibling (is the
            // last child) is folding the margin into ActualBottom safe: nothing else will ever
            // separately collapse against this box's own ActualMarginBottom, so propagating the fold via
            // ActualBottom (which return value the box's PARENT then treats as this box's true bottom
            // edge, letting a further collapse continue outward through as many blocked-only-by-
            // border/padding ancestors as apply) is the only place left for it to go.
            // lastNonFloatingBox.StaticBottom (not ActualBottom) throughout: a relatively-positioned
            // last child's visual offset must not grow this box's own content-driven height
            // (CSS 2.1 §9.4.3) - Acid2's ".smile div { position: relative; bottom: -1em }" otherwise
            // inflates ".smile" by 1em and pushes ".chin" that much too far down.
            if (ParentBox == null || ParentBox.Boxes.IndexOf(this) != ParentBox.Boxes.Count - 1 ||
                !(_parentBox!.ActualMarginBottom < 0.1) ||
                !(ActualPaddingBottom < 0.1) || !(ActualBorderBottomWidth < 0.1) ||
                Overflow.Value != PeachPDF.CSS.Overflow.Visible)
                return Math.Max(ActualBottom,
                    lastNonFloatingBox.StaticBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);

            // Set-based accumulation (AdjoiningMarginSet, not pairwise CollapseMargins) here too: the
            // last child's contribution can itself be a whole adjoining set when it is self-collapsing
            // (its {+10px, -3px} collapses to 7px, but folding this box's own 8px against that
            // PRE-collapsed 7px pairwise gives 8px when the true set {10, -3, 8} is still 7px).
            if (Height == "auto")
            {
                var margins = new AdjoiningMarginSet();
                margins.Fold(ActualMarginBottom);
                if (lastNonFloatingBox.IsMarginCollapseThrough())
                {
                    lastNonFloatingBox.FoldSelfCollapsingMargins(ref margins);
                }
                else
                {
                    margins.Fold(lastNonFloatingBox.ActualMarginBottom);
                }
                margin = margins.CollapsedValue;
            }
            else
            {
                margin = lastNonFloatingBox.GetEffectiveBottomMargin();
            }
            return Math.Max(ActualBottom, lastNonFloatingBox.StaticBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);
        }

        /// <summary>
        /// The document Y to attribute this box's named-page registration to: the top of the
        /// pagination slot its page starts on. After a named-page forced break the box itself sits
        /// its preserved margin-top below the slot top (css-break-3 §5.2 - margins after a forced
        /// break are kept), and the document's first box sits below the content origin by its own
        /// margins - but per css-page-3 the PAGE the box starts on carries its name, so the geometry
        /// table's slot-start attribution (<c>PageRuleResolver.ActiveNameAtSlotStart</c>) must see
        /// the registration at the slot top itself. Outside real pagination (no page band, or the
        /// <c>double.MaxValue</c> measurement sentinel) the raw location is used unchanged.
        /// </summary>
        private double NamedPageRegistrationY()
        {
            var container = HtmlContainer!;
            return container.HasRealPageGrid
                ? container.PageTopOf(container.SlotStartingAt(Location.Y))
                : Location.Y;
        }

        /// <summary>
        /// Deeply offsets the top of the box and its contents
        /// </summary>
        /// <param name="amount"></param>
        internal void OffsetTop(double amount) => OffsetTop(amount, translationRoot: this);

        /// <remarks>
        /// <paramref name="translationRoot"/> is the box the caller originally asked to move — fixed for
        /// the whole recursive walk, not re-derived per frame — so that a descendant's containing block
        /// (<see cref="DomUtils.GetNearestPositionedAncestor"/>) can be asked "is this inside the subtree
        /// being moved at all", not merely "is this inside the box recursing into it right now". See
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/437">#437</see>: an out-of-flow
        /// descendant whose containing block sits outside <paramref name="translationRoot"/> was
        /// positioned against something that is not moving, so translating it here would double-move it
        /// relative to where CSS 2.1 §10.1 actually places it. A <c>position:relative</c> mover's own
        /// genuinely-contained absolutely-positioned descendants are unaffected — their containing block is
        /// inside the subtree being moved, so the walk still reaches them.
        /// </remarks>
        private void OffsetTop(double amount, CssBox translationRoot)
        {
            // A relocation that reaches back into a fragmentainer an earlier pass already froze has to
            // un-freeze it, or the frozen copy keeps painting this box where it no longer is. Asked of this
            // box's own geometry rather than of Location alone, because an inline box's rectangles and words
            // move while its Location stays at a line-local value.
            NotifyGeometryChanged(OwnGeometryTop(), amount);

            List<CssLineBox> lines = [];
            foreach (var line in Rectangles.Keys)
                lines.Add(line);

            foreach (var line in lines)
            {
                var r = Rectangles[line];
                Rectangles[line] = new RRect(r.X, r.Y + amount, r.Width, r.Height);
            }

            foreach (var word in Words)
            {
                word.Top += amount;
            }

            // Keep this box's own registered string-set/named-page tracking in sync with a reposition
            // that happens after this box's own PerformLayoutImp already returned (e.g. a later ancestor's
            // layout engine re-banding this box, like CssLayoutEngineColumns's Phase 2) - the one-time
            // absolute correction in PerformLayoutImp can't see this, since it already ran and returned.
            foreach (var namedString in NamedStrings.Values)
            {
                namedString.Y += amount;
            }

            if (RegisteredNamedPageElement is not null)
            {
                // Routed through the container so the per-page geometry table can invalidate every
                // slot either the old or new position could have influenced.
                HtmlContainer!.MoveNamedPageElement(RegisteredNamedPageElement, RegisteredNamedPageElement.Y + amount);
            }

            foreach (var b in Boxes)
            {
                if (b.EscapesTranslationOf(translationRoot)) continue;

                b.OffsetTop(amount, translationRoot);
            }

            Location = Location with { Y = Location.Y + amount };
            OnTranslated(0, amount);
        }

        /// <summary>
        /// Whether this box's containing block lies outside <paramref name="translationRoot"/>, so a
        /// subtree translation rooted there must not move it. See the remarks on the private
        /// <see cref="OffsetTop(double, CssBox)"/> overload for why.
        /// </summary>
        private bool EscapesTranslationOf(CssBox translationRoot) =>
            Position.Value is PositionMode.Absolute or PositionMode.Fixed
            && !DomUtils.IsSelfOrDescendantOf(DomUtils.GetNearestPositionedAncestor(this), translationRoot);

        /// <summary>
        /// Called once a subtree translation (<see cref="OffsetTop(double)"/>/<see cref="OffsetLeft(double)"/>)
        /// has finished moving this box by <paramref name="dx"/>/<paramref name="dy"/>, after
        /// <see cref="CssBox.Location"/> has already been updated. A no-op here; overridden by a
        /// box that holds geometry the ordinary <see cref="Boxes"/>/<see cref="Rectangles"/>/<see cref="Words"/>
        /// walk cannot reach, such as <see cref="CssProxyBox"/>'s frozen source-subtree snapshot.
        /// </summary>
        protected virtual void OnTranslated(double dx, double dy) { }

        /// <summary>
        /// Acts on a break decision discovered against this box, and reports whether it was taken by
        /// re-laying the box out — in which case the caller must stop, because everything it has
        /// measured so far describes a position the box is about to leave.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The single place the §4.3 corrections — <c>break-inside: avoid</c>,
        /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see> monolithic content,
        /// <c>orphans</c>/<c>widows</c>, and the keep-with-next pull they share — turn a stated
        /// decision into geometry, so that how a break is <i>taken</i> is decided once rather than per
        /// mover.
        /// </para>
        /// <para>
        /// Re-laying the box out is what makes the decision honest: a box that had already begun
        /// flowing text across the boundary cannot simply be shifted, because its later lines were laid
        /// out against the next band's top and the shift carries that gap into the box as interior
        /// blank space. Where the box cannot be laid out again — see <see cref="CanBeLaidOutAgain"/> —
        /// the move degrades to the translation this used to always do.
        /// </para>
        /// </remarks>
        private bool TakeEarlyBreak(EarlyBreak decision)
        {
            // A box only reaches its epilogue on the pass that *completes* it, which for a box spanning
            // several fragmentainers is later than the pass that placed it - so this decision can relocate
            // content out of a fragmentainer already frozen. Un-freeze it here rather than at the (three
            // different) places the move is finally carried out.
            HtmlContainer?.InvalidateEmittedFragmentsFor(
                decision.Subject,
                Math.Min(decision.Top, Math.Min(decision.BeforeBox.Location.Y, decision.Subject.Location.Y)));

            if (decision.BeforeBox == this && CanBeLaidOutAgain(decision))
            {
                _earlyBreakRetryTop = decision.Top;
                _earlyBreakTaken = true;
                return true;
            }

            // The break falls before an earlier sibling, so this box cannot carry it out: only the
            // parent's child loop can re-run something placed before the box it is currently laying
            // out. Hand it over, and stop - what follows would measure a position about to change.
            // Whether the boxes a restart would re-run can survive it is the parent's question, not
            // this one's: only the child loop knows which indices it is about to replay.
            //
            // The loop to hand it to is the one that owns the box the break falls before, which is this
            // box's own parent for a plain sibling run and an ancestor's parent where §3.1 propagation
            // moved the decision onto a container this box begins. Handing it to the immediate parent
            // regardless would name a box that parent's Boxes does not contain, and the restart would be
            // refused and degrade to translating the wrong box.
            if (decision.BeforeBox != this
                && decision.BeforeBox.ParentBox is { _canRestartChildLoop: true } owner
                && HtmlContainer is { IsFragmenting: true })
            {
                owner._requestedChildRestart = decision;
                _earlyBreakTaken = true;
                return true;
            }

            TranslateForEarlyBreak(decision);
            _earlyBreakTaken = true;
            return false;
        }

        /// <summary>
        /// Whether this box can take <paramref name="decision"/> by being laid out again at its new
        /// position, rather than by being translated to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three things have to hold. The box must <see cref="PlacesItselfAsBlockBox">place itself</see>,
        /// since the target is delivered through <c>PlaceAndSizeBlockChild</c> and any other box would simply
        /// ignore it and never move at all. It must not already have taken a break on this pass
        /// (<see cref="_earlyBreakTaken"/>). And breaking must be live: inside the flex, grid, table and
        /// multi-column engines a box's coordinates are provisional — a flex item is laid out at the
        /// container's content origin purely to be measured, and is translated into place afterwards —
        /// so re-flowing it there would change the very measurement the engine is in the middle of
        /// taking. That is the #166 boundary the rest of the break machinery already has, and inside it
        /// the translation remains exactly what it was.
        /// </para>
        /// <para>
        /// The box must also actually <i>fit</i> where it is going. An <c>avoid</c> that cannot be
        /// satisfied is relaxed by moving the box anyway, so that as much of it as possible lands on
        /// one page (§5.3) — which is a sound thing to do to a box being <i>translated</i> and a
        /// runaway when the box is laid out again: it still does not fit, so it fragments from its new
        /// top, that fragmentation opens another fragmentainer pass, and the pass re-asks the same
        /// question. Verified to walk a box down 100,000 pages before the driver's own cap stopped it.
        /// Relaxation therefore keeps the translation, which is exactly what it did before.
        /// </para>
        /// </remarks>
        private bool CanBeLaidOutAgain(EarlyBreak decision) =>
            PlacesItselfAsBlockBox
            && HtmlContainer is { IsFragmenting: true } container
            && FitsInFragmentainer(BlockConstraint.AtSlot(container, this, decision.Slot));

        /// <summary>
        /// Deeply offsets the left of the box and its contents
        /// </summary>
        /// <param name="amount"></param>
        internal void OffsetLeft(double amount) => OffsetLeft(amount, translationRoot: this);

        /// <remarks>
        /// See the remarks on the private <see cref="OffsetTop(double, CssBox)"/> overload — the same
        /// containing-block-aware skip applies to the horizontal axis for the same reason (#437).
        /// </remarks>
        private void OffsetLeft(double amount, CssBox translationRoot)
        {
            List<CssLineBox> lines = [];
            foreach (var line in Rectangles.Keys)
                lines.Add(line);

            foreach (var line in lines)
            {
                var r = Rectangles[line];
                Rectangles[line] = new RRect(r.X + amount, r.Y, r.Width, r.Height);
            }

            foreach (var word in Words)
            {
                word.Left += amount;
            }

            foreach (var b in Boxes)
            {
                if (b.EscapesTranslationOf(translationRoot)) continue;

                b.OffsetLeft(amount, translationRoot);
            }

            Location = Location with { X = Location.X + amount };
            OnTranslated(amount, 0);
        }

        /// <summary>
        /// Resets the <see cref="Rectangles"/> array
        /// </summary>
        internal void RectanglesReset()
        {
            // Discarding the rectangles is how a re-layout of this box begins, so anything already emitted
            // from them describes geometry that is about to be replaced.
            if (Rectangles.Count > 0) NotifyGeometryChanged(OwnGeometryTop(), 0);

            Rectangles.Clear();
        }

        /// <summary>
        /// Un-freezes any fragmentainer already emitted for this box, because its geometry from
        /// <paramref name="fromY"/> down is about to change by <paramref name="amount"/>.
        /// </summary>
        /// <remarks>
        /// The emitter ignores this for a box it has not frozen anywhere, which is what keeps ordinary
        /// forward layout - where every box is placed for the first time - from re-emitting anything.
        /// </remarks>
        private void NotifyGeometryChanged(double fromY, double amount)
        {
            // Unconditional, unlike the emitted-fragment call below: that one may skip a box no frozen
            // fragmentainer holds, but an "emitted nothing here" observation exists precisely for boxes
            // that hold no fragment in the slot being filled.
            DiscardEmittedNothing();

            NotifyEmittedFragmentsChanged(fromY, amount);
        }

        private void NotifyEmittedFragmentsChanged(double fromY, double amount) =>
            HtmlContainer?.InvalidateEmittedFragmentsFor(this, Math.Min(fromY, fromY + amount));

        /// <summary>
        /// The topmost document Y this box's own geometry occupies. Its per-line rectangles and words where
        /// it has them, since an inline box's own <see cref="CssBox.Location"/> stays at a
        /// line-local value layout never updates.
        /// </summary>
        private double OwnGeometryTop()
        {
            if (Rectangles.Count == 0 && Words.Count == 0) return Location.Y;

            var top = double.MaxValue;

            foreach (var rect in Rectangles.Values) top = Math.Min(top, rect.Top);
            foreach (var word in Words) top = Math.Min(top, word.Top);

            return top;
        }

        private void OnBlockAxisRelocated(double fromY, double toY) =>
            NotifyGeometryChanged(Math.Min(fromY, toY), 0);

        internal RFont? GetCachedFont(string fontFamily, double fsize, RFontStyle st, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return FontFamilyResolver.Resolve(HtmlContainer!.Adapter, fontFamily, fsize, st, weight, stretch, obliqueSkewSinus);
        }

        internal RFont? GetCachedFontForCodepoint(string fontFamily, double fsize, RFontStyle st, System.Text.Rune codepoint, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return FontFamilyResolver.Resolve(HtmlContainer!.Adapter, fontFamily, fsize, st, codepoint, weight, stretch, obliqueSkewSinus);
        }

        internal RColor GetActualColor(string colorStr)
        {
            return HtmlContainer!.CssParser.ParseColor(colorStr);
        }




        /// <summary>
        /// ToString override.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var tag = HtmlTag != null ? $"<{HtmlTag.Name}#{Id}>" : $"anon#{Id}";

            if (HtmlTag?.Attributes?.ContainsKey("class") ?? false)
            {
                tag = $"{tag}, Class: {HtmlTag.Attributes["class"]}";
            }

            if (HtmlTag?.Attributes?.ContainsKey("id") ?? false)
            {
                tag = $"{tag}, Id: {HtmlTag.Attributes["id"]}";
            }

            if (HtmlTag?.Attributes?.ContainsKey("src") ?? false)
            {
                tag = $"{tag}, Src: {HtmlTag.Attributes["src"]}";
            }

            if (Text is not null)
            {
                tag = $"{tag} Text: {Text}";
            }

            if (IsBlock)
            {
                return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} Block {FontSize}, Children:{Boxes.Count}";
            }
            else if (DerivedStyle.ActualDisplay == CssConstants.None)
            {
                return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} None";
            }
            else
            {
                return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} {DerivedStyle.ActualDisplay}: {Text}";
            }
        }

        #endregion
    }
}