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
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    internal class CssBox : CssBoxProperties, IDisposable, ICssDomNode
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

        /// <inheritdoc/>
        protected override IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? FontPaletteValuesRegistry
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
        /// (if any), retained so a later ancestor reposition (<see cref="OffsetTop"/>) can keep it in sync -
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
        /// <c>PdfGenerator.ResolveCanvasBackground</c> per CSS2.1 §14.2: that box's background has been
        /// "promoted" to fill the whole page canvas on every page (see
        /// <see cref="PaintCanvasBackground"/>), so this box's own normal <see cref="PaintBackground"/>
        /// call must no-op instead of painting the same background a second time at its own (possibly
        /// much smaller than the page) laid-out rect.
        /// </summary>
        internal bool SuppressOwnBackgroundPaint { get; set; }

        /// <summary>
        /// Paints this box's own background (see <see cref="PaintBackground"/>) at <paramref name="rect"/>
        /// instead of this box's own laid-out rect - used by <c>PdfGenerator.AddPdfPages</c> to fill the
        /// whole page canvas with the <c>&lt;body&gt;</c>/<c>&lt;html&gt;</c> background per CSS2.1 §14.2,
        /// reusing the exact same background-image/-position/-size/-repeat/-origin/-clip resolution the
        /// box's own normal paint path uses so canvas-fill behavior matches per-box behavior exactly.
        /// </summary>
        internal void PaintCanvasBackground(RGraphics g, RRect rect) => PaintBackground(g, rect, isFirst: true);

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
        public bool IsInline => Display is CssConstants.Inline or CssConstants.InlineBlock or CssConstants.InlineTable or CssConstants.InlineFlex or CssConstants.InlineGrid;

        /// <summary>
        /// is the box "Display" is "Block", is this is a block box and not inline.
        /// </summary>
        public bool IsBlock => Display == CssConstants.Block;

        public bool IsFloated => Float is CssConstants.Left or CssConstants.Right;

        public bool IsOutOfFlow => IsFloated || Position is CssConstants.Absolute or CssConstants.Fixed;

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
                if (Position == CssConstants.Fixed)
                    return true;

                if (this.ParentBox == null)
                    return false;

                CssBox parent = this;

                while (!(parent.ParentBox == null || parent == parent.ParentBox))
                {
                    parent = parent.ParentBox;

                    if (parent.Position == CssConstants.Fixed)
                        return true;
                }

                return false;
            }
        }

        public virtual bool IsTableRowGroupBox => Display is CssConstants.TableRowGroup or CssConstants.TableHeaderGroup or CssConstants.TableFooterGroup;

        /// <summary>
        /// Maps page number → last row bottom Y on that page. Set by CssLayoutEngineTable when rows break across pages.
        /// Used during paint to clip the table box border to the actual content height on each page.
        /// </summary>
        internal Dictionary<int, double>? PageBreakBottoms { get; set; }

        /// <summary>
        /// The vertical line segments (in absolute document coordinates) to draw between adjacent
        /// columns of a multi-column container — one segment per gap per page-row actually used.
        /// Set by <see cref="CssLayoutEngineColumns"/>, painted by <see cref="PaintImp"/>.
        /// </summary>
        internal List<(double X, double Top, double Bottom)>? ColumnRuleSegments { get; set; }

        public virtual bool IsTableCell => Display is CssConstants.TableCell;

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
                       box.Display != CssConstants.ListItem &&
                       box.Display != CssConstants.Table &&
                       box.Display != CssConstants.TableCell &&
                       box.Display != CssConstants.Flex &&
                       box.Display != CssConstants.InlineFlex &&
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

        public bool IsHeightCalculated { get; set; } = false;

        /// <summary>
        /// Gets the actual top's Margin
        /// </summary>
        public double ActualMarginTop => CssValueParser.ParseLength(MarginTop, ContainingBlock.Size.Width, this);

        /// <summary>
        /// Gets the actual Margin on the left
        /// </summary>
        public double ActualMarginLeft => CssLayoutEngine.GetActualMarginLeft(this);

        /// <summary>
        /// Gets the actual Margin of the bottom
        /// </summary>
        public double ActualMarginBottom => CssValueParser.ParseLength(MarginBottom, ContainingBlock.Size.Width, this);

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
                Display = CssConstants.Block
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
            newBox.Display = CssConstants.Block;
            return newBox;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.
        /// </summary>
        /// <param name="g">Device context to use</param>
        public async ValueTask PerformLayout(RGraphics g)
        {
            try
            {
                await PerformLayoutImp(g);
            }
            catch (Exception ex)
            {
                HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Exception in box layout", ex);
            }
        }

        internal void ResetPaint()
        {
            _hasPainted = false;

            foreach (var childBox in Boxes)
            {
                childBox.ResetPaint();
            }
        }

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">Device context to use</param>
        public async ValueTask Paint(RGraphics g)
        {
#if DEBUG
            Console.WriteLine($"paint: {ToString()}");
#endif

            try
            {
                if (Display == CssConstants.None || Visibility != CssConstants.Visible) return;

                // use initial clip to draw blocks with Position = fixed. I.e. ignore page margins
                if (Position == CssConstants.Fixed)
                {
                    g.SuspendClipping();
                }

                // don't call paint if the rectangle of the box is not in visible rectangle
                var visible = Rectangles.Count == 0;
                if (!visible)
                {
                    var clip = g.GetClip();

                    // Check this box's OWN rects individually via the same IsRectVisible helper
                    // PaintImpCore/PaintDecoration already use, not ContainingBlock.ClientRectangle as
                    // one combined region - an inline box (e.g. a replaced <object>/<img>) can paint
                    // well outside its containing block's client area whenever that containing block
                    // collapses to zero height by design (Acid2's own "#eyes-a { height: 0; line-height:
                    // 2em; }" trick, whose inline content is positioned via line-height/vertical-align
                    // alone) - using the containing block's rectangle here treated the box as invisible
                    // and skipped its entire paint (including nested replaced content and backgrounds),
                    // not just mis-measured its visible area. Checked per rect (rather than unioning
                    // them all into one bounding rect first) so a box fragmented across many pages -
                    // Rectangles isn't itself page-scoped - can't produce one giant union spanning pages
                    // it isn't actually on, which would defeat this check's purpose as a cheap early-out.
                    // IsRectVisible's own doc comment covers the degenerate-touch/epsilon handling
                    // (GitHub issue #113) this loop relies on.
                    foreach (var r in Rectangles.Values)
                    {
                        var rect = r;
                        if (!IsFixed)
                        {
                            //rect.Offset(new RPoint(-HtmlContainer.Location.X, -HtmlContainer.Location.Y));
                            rect.Offset(HtmlContainer!.ScrollOffset);
                        }

                        if (IsRectVisible(rect, clip))
                        {
                            visible = true;
                            break;
                        }
                    }
                }
                else if (HtmlContainer?.HasOutOfFlowBoxes ?? true)
                {
                    // Rectangles.Count == 0 boxes (most block-level containers) have historically always
                    // been treated as visible here regardless of the current clip, so every page walked
                    // every box in the whole document. That's only safe to tighten up when there's no
                    // out-of-flow content (float/absolute/fixed) anywhere in the document: an out-of-flow
                    // descendant's visual position can fall outside this box's own Bounds, and it's only
                    // discovered/painted via this box's own PaintImp -> FlattenStackingContext call, so
                    // skipping that call based on Bounds alone could silently drop it. With no such content
                    // anywhere (checked once for the whole document - see HtmlContainerInt.PerformLayout),
                    // every descendant is normal-flow and nested within this box's own Bounds, so it's safe
                    // to prune using them below instead.
                }
                else
                {
                    var clip = g.GetClip();
                    var rect = Bounds;
                    rect.X -= 2;
                    rect.Width += 2;
                    if (!IsFixed)
                    {
                        rect.Offset(HtmlContainer!.ScrollOffset);
                    }
                    clip.Intersect(rect);

                    // See the identical zero-area-intersection fix/comment in the Rectangles.Count > 0
                    // branch above (GitHub issue #113) - applies here too, for the Bounds-based cull.
                    visible = clip.Width > VisibilityClipEpsilon && clip.Height > VisibilityClipEpsilon;
                }

                if (visible)
                {
                    var transformed = IsTransformed;
                    var pageOffset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;

                    if (transformed)
                    {
                        // ActualTransformMatrix is cached treating the box's own top-left as local
                        // (0, 0) - painting draws in absolute page coordinates, so re-anchor the pivot
                        // to the box's actual page position (its Bounds, shifted by the current page's
                        // scroll offset, same as the visibility check above) right before pushing it.
                        var pageMatrix = ActualTransformMatrix.RebaseOrigin(
                            Bounds.X + pageOffset.X, Bounds.Y + pageOffset.Y);
                        g.PushTransform(pageMatrix);
                    }

                    // clip-path clips the entire element rendering (background, border, content, children).
                    // It is established inside the transform push so the clip and the content it clips are
                    // transformed together (CSS Masking 1: the clip is in the element's local coordinate
                    // system, which any `transform` then maps). The reference box is the border-box (the
                    // default), in absolute paint coordinates (Bounds shifted by the current page scroll).
                    var clipped = false;
                    if (ClipPath != CssConstants.None && !string.IsNullOrEmpty(ClipPath))
                    {
                        var referenceBox = new RRect(
                            Bounds.X + pageOffset.X, Bounds.Y + pageOffset.Y,
                            ActualBoxSizingWidth, ActualBoxSizingHeight);

                        if (CssClipPathResolver.TryBuildClipPath(g, ClipPath, referenceBox, this, out var clipGeometry, out _)
                            && clipGeometry is not null)
                        {
                            g.PushClip(clipGeometry);
                            clipped = true;
                        }
                    }

                    if (IsOpaque)
                    {
                        await PaintImp(g);
                    }
                    else
                    {
                        await PaintWithOpacity(g);
                    }

                    if (clipped)
                        g.PopClip();

                    if (transformed)
                        g.PopTransform();
                }

                // Restore clips
                if (Position == CssConstants.Fixed)
                {
                    g.ResumeClipping();
                }
            }
            catch (Exception ex)
            {
                HtmlContainer?.ReportError(HtmlRenderErrorType.Paint, "Exception in box paint", ex);
            }
        }

        /// <summary>
        /// Paints this box (and, via <see cref="PaintImp"/>, its whole subtree) into an offscreen tile
        /// sized to the current page's visible clip, then composites that tile onto <paramref name="g"/>
        /// as a single flattened result at <c>ActualOpacity</c> - the CSS <c>opacity</c> property
        /// is a group effect (it applies once to the element and everything painted inside it, not to
        /// each descendant's own paint calls independently), and this is what makes overlapping content
        /// within the box composite correctly instead of double-blending where it overlaps.
        /// </summary>
        /// <remarks>
        /// The tile is sized to the whole current page-visible rect (not a tight bounding box of this
        /// box's own content) so that <see cref="PaintImp"/> can keep painting at its normal absolute
        /// page coordinates unmodified - this box's own possible multiple <see cref="Rectangles"/> (line
        /// wraps) and any overflowing/absolutely-positioned descendants all land correctly inside the
        /// tile with no extra translation math, at the cost of a somewhat larger-than-necessary Form
        /// XObject. Any transform already pushed onto <paramref name="g"/> for this same box (see the
        /// caller in <see cref="Paint"/>) is left active and applies to the tile's placement automatically,
        /// since PDF's own <c>cm</c> operator concatenates - no separate transform-folding is needed here.
        /// </remarks>
        private async ValueTask PaintWithOpacity(RGraphics g)
        {
            var clip = g.GetClip();
            var tileRect = new RRect(0, 0, clip.Right, clip.Bottom);

            var tile = g.CreateTile(tileRect.Width, tileRect.Height);
            if (tile is not { } t)
            {
                // No page/document context to own a Form XObject in (e.g. a measure-only pass) -
                // opacity has no visual effect there anyway, so just paint directly.
                await PaintImp(g);
                return;
            }

            t.Graphics.PushClip(clip);
            await PaintImp(t.Graphics);
            t.Graphics.Dispose();

            g.DrawImageWithOpacity(t.Image, tileRect, ActualOpacity);
        }

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
            var preserveSpaces = WhiteSpace is CssConstants.Pre or CssConstants.PreWrap;
            var respectNewLines = preserveSpaces || WhiteSpace == CssConstants.PreLine || IsBrElement;

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
                            Words.Add(new CssRectWord(this, HtmlUtils.DecodeHtml(text.Substring(startIdx, endIdx - startIdx)), false, false));
                    }
                    else
                    {
                        // A soft hyphen (U+00AD) is an extra break opportunity honored for hyphens:
                        // manual/auto (the default is manual - see CssBoxProperties.Hyphens). Unlike a
                        // literal '-' it's never part of the rendered word text; unlike the old
                        // behavior, it no longer eagerly splits the word here either - at this
                        // pre-layout stage there's no way to know whether a line break will actually
                        // land at this exact position, so eagerly splitting could only ever show the
                        // hyphen glyph always or never, both wrong. Its position (and, for hyphens:auto
                        // with a known document language, HyphenationEngine's own suggested positions)
                        // is instead recorded as a candidate on the whole word and consulted only when
                        // CssLayoutEngine.FlowBox actually needs to break the line - see AddWord.
                        var honorSoftHyphen = Hyphens != CssConstants.None;

                        // Scan by whole codepoint (Rune), not UTF-16 code unit, so an astral character (an
                        // emoji, a CJK Extension-B ideograph, etc.) is never split across its surrogate pair -
                        // its two halves would otherwise each be treated as a separate per-character Asian
                        // word break and emitted as two invalid lone-surrogate words.
                        endIdx = startIdx;
                        while (endIdx < text.Length)
                        {
                            Rune.DecodeFromUtf16(text.AsSpan(endIdx), out var rune, out var runeLength);
                            if (HtmlUtils.IsCollapsibleWhitespace(text[endIdx]) || text[endIdx] == '-'
                                || WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharacter(rune))
                                break;
                            endIdx += runeLength;
                        }

                        if (endIdx < text.Length)
                        {
                            Rune.DecodeFromUtf16(text.AsSpan(endIdx), out var rune, out var runeLength);
                            if (text[endIdx] == '-' || WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharacter(rune))
                                endIdx += runeLength;
                        }

                        if (endIdx > startIdx)
                        {
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
                                cleanWord = HtmlUtils.DecodeHtml(rawWord);
                                cleanOriginalWord = HtmlUtils.DecodeHtml(rawOriginalWord);

                                if (Hyphens == CssConstants.Auto)
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

                            AddWord(cleanWord, hasSpaceBefore, hasSpaceAfter, hyphenationCandidates, cleanOriginalWord);
                        }
                    }

                    // create new-line word so it will effect the layout
                    if (endIdx < text.Length && text[endIdx] == '\n')
                    {
                        endIdx++;
                        if (respectNewLines)
                            Words.Add(new CssRectWord(this, "\n", false, false));
                    }

                    startIdx = endIdx;
                }
            }
        }

        /// <summary>
        /// Adds one word to <see cref="Words"/> — or, when <see cref="FontVariant"/> is
        /// <c>small-caps</c> and <paramref name="text"/> contains at least one lowercase letter, splits
        /// it into consecutive lowercase/non-lowercase case-run fragments instead. PeachPDF has no
        /// OpenType shaping engine to do real <c>smcp</c>/<c>c2sc</c> glyph substitution, so each
        /// lowercase run is upper-cased and marked (<see cref="CssRect.FontSizeScale"/>) to be
        /// measured/painted smaller than the rest of the word (see
        /// <see cref="CssBoxProperties.ActualSmallCapsFont"/>). Every fragment after the first is marked
        /// <see cref="CssRect.SuppressWrapBefore"/> so this split never introduces a new line-break
        /// opportunity in the middle of what was one word. <paramref name="hyphenationCandidates"/> (see
        /// <see cref="CssRect.HyphenationCandidates"/>) is only attached when the word is kept whole —
        /// small-caps splitting and hyphenation are a separate, non-composing pair of features.
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

            if (FontVariant != CssConstants.SmallCaps || !ContainsLowerLetter(text))
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
                var scale = isLower ? CssBoxProperties.SmallCapsFontScale : 1.0;
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
        /// same face (via <see cref="CssBoxProperties.ActualFontForCodepoint"/>) and adds one
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
        /// own <see cref="CssBoxProperties.ActualFont"/> (or <see cref="CssBoxProperties.ActualSmallCapsFont"/>
        /// for a synthesized small-caps run). <paramref name="styleSource"/> is the box whose font applies -
        /// the owner box, or a <c>::first-line</c> shadow box for a word on the first formatted line.
        /// </summary>
        private static RFont ResolveWordFont(CssRect word, CssBoxProperties styleSource)
        {
            if (word.UsesPerCodepointFont && word.Text is { Length: > 0 } text)
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
                sb.Append(HtmlUtils.DecodeHtml(segments[i]));
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
        private static string ApplyTextTransform(string text, string transform)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            switch (transform)
            {
                case CssConstants.Uppercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToUpperInvariant(chars[i]);
                    return new string(chars);
                }
                case CssConstants.Lowercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    return new string(chars);
                }
                case CssConstants.Capitalize:
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
                default:
                    return text;
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
                if (childBox.IsOutOfFlow && childBox.Display != CssConstants.None)
                {
                    await childBox.PerformLayout(g);
                }
            }
        }

        protected virtual async ValueTask PerformLayoutImp(RGraphics g)
        {
#if DEBUG
            Console.WriteLine($"layout start: {ToString()}");
#endif

            if (Display != CssConstants.None)
            {
                RectanglesReset();
                await MeasureWordsSize(g);
            }

            // Apply named strings if string-set property is present
            if (!string.IsNullOrEmpty(StringSet) && StringSet != CssConstants.None)
            {
                CssNamedStringEngine.ApplyStringSet(this);
            }

            // A previous layout pass's registration was orphaned wholesale by PerformLayout's
            // ClearNamedPageElements - drop the stale reference so this pass's registration logic
            // (early for block containers below, tail sync/fallback at the end of this method)
            // starts clean instead of silently "syncing" an element no longer in the registry.
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
            var shouldRegisterPage = HtmlContainer is not null && (hasExplicitPageName || pageNameChanged);
            var isForcedBreak = IsForcedBreakValue(BreakBefore) || IsForcedBreakValue(previousSiblingForBreak?.BreakAfter) || pageNameChanged;
            if (isForcedBreak)
            {
                if (previousSiblingForBreak is not null)
                {
                    // HtmlContainer.PageSize.Height is already margin-free (PdfGenerator.SetContent
                    // subtracts both page margins up front) - a page's real content band is the
                    // "shifted grid" [k·PageSize.Height + MarginTop, (k+1)·PageSize.Height + MarginTop),
                    // not raw multiples of PageSize.Height from document Y=0. PageIndexOf/PageTopOf are
                    // the single, unambiguous definition of that grid (matching what the painter's own
                    // per-page clip and GetPaginationSlots already use) - computing this via raw modulo
                    // arithmetic against PageSize.Height alone (as this used to) silently lands a
                    // marginTop-wide band, right at the end of every raw page, one whole page short.
                    //
                    // The epsilon implements css-break-3 §4.4's "no empty fragmentainer for a single
                    // forced break at a boundary": a sibling whose content ENDS flush on a slot
                    // boundary (e.g. a full-bleed cover sized exactly to its page's band) already
                    // satisfies the break - the target is that boundary itself, not the slot after
                    // it (which manufactured a blank page). A zero-height sibling sitting AT the
                    // boundary (the consecutive-forced-breaks case - it was itself relocated there
                    // by its own preceding break) occupies the LATER slot, so the break between it
                    // and this box still pushes past it, preserving the intentional blank page.
                    var container = HtmlContainer!;
                    var prevBottom = previousSiblingForBreak.ActualBottom;
                    var target = container.PageTopOf(
                        container.PageIndexOf(prevBottom - HtmlContainerInt.PageBoundaryEpsilon) + 1);
                    if (previousSiblingForBreak.Location.Y >= target - HtmlContainerInt.PageBoundaryEpsilon)
                    {
                        target = container.PageTopOf(
                            container.PageIndexOf(previousSiblingForBreak.Location.Y + HtmlContainerInt.PageBoundaryEpsilon) + 1);
                    }

                    previousSiblingForBreak.ActualBottom = target;
                }
            }

            if (IsBlock || Display == CssConstants.ListItem || Display == CssConstants.Table || Display == CssConstants.InlineTable || Display == CssConstants.TableCell || Display == CssConstants.Flex || Display == CssConstants.InlineFlex || Display == CssConstants.Grid || Display == CssConstants.InlineGrid)
            {
                // Because their width and height are set by CssTable, CssLayoutEngineFlex or CssLayoutEngineGrid
                if (Display != CssConstants.TableCell && Display != CssConstants.Table && Display != CssConstants.Flex && Display != CssConstants.InlineFlex && Display != CssConstants.Grid && Display != CssConstants.InlineGrid)
                {
                    var width = await CssLayoutEngine.GetBoxWidth(g, this);
                    ActualRight = Location.X + width + ActualBoxSizeIncludedWidth;
                }

                if (Display != CssConstants.TableCell)
                {
                    if (Position is CssConstants.Static or CssConstants.Relative)
                    {
                        var prevSibling = DomUtils.GetPreviousSibling(this, false);

                        var left = ContainingBlock.ClientLeft;
                        // prevSibling.ActualBottom is already the outer border-box edge (CssBoxProperties.
                        // ActualBottom = Location.Y + content height + padding + border, per its own
                        // getter/ApplyHeight/MarginBottomCollapse - all three fold border-bottom in
                        // exactly once) - adding prevSibling.ActualBorderBottomWidth again here double-
                        // counted it, pushing every box that follows a bordered sibling an extra
                        // border-bottom-width too far down. MarginTopCollapse's own internal bookkeeping
                        // (anchor.ActualBottom + anchor.ActualBorderBottomWidth, then subtracting
                        // prevSibling's own equivalent) is unaffected by this fix: those two terms already
                        // cancel out exactly when anchor == prevSibling (the common case), and a
                        // self-collapsing prevSibling always has zero border by definition
                        // (IsMarginCollapseThrough requires it), so the residual term vanishes there too.
                        // StaticBottom (not ActualBottom) so a relatively-positioned previous sibling's
                        // visual offset doesn't shift this box - CSS 2.1 §9.4.3, relative offsets never
                        // affect the layout of following content.
                        var baseTop = (prevSibling == null ? ContainingBlock.ClientTop : ParentBox == null ? Location.Y : 0) + (prevSibling?.StaticBottom ?? 0);
                        var top = baseTop + MarginTopCollapse(prevSibling);

                        // CSS Fragmentation Level 3 §5.2: "When an unforced break occurs before or
                        // after a block-level box, any margins adjoining the break are truncated to
                        // zero." A margin big enough to push this box across one or more page
                        // boundaries by itself (as opposed to actual content straddling a boundary,
                        // which BreakInside/orphans-widows handles separately, later in this method) is
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
                        // only applies when this box's own placement isn't already forced-break-governed.
                        if (prevSibling is not null && !isForcedBreak)
                        {
                            var pageHeight = HtmlContainer!.PageSize.Height;
                            if (pageHeight > 0)
                            {
                                // Same shifted grid GetPaginationSlots()/the forced-break logic above use
                                // (see HtmlContainer.PageIndexOf's own doc comment) - matching
                                // BreakInside_Avoid_PositionsAtTopOfNextPage's already-established
                                // convention. The epsilons attribute a value flush ON a boundary to
                                // the earlier slot (a sibling ending exactly at a slot boundary is
                                // wholly inside it), mirroring the forced-break flush-fit rule above.
                                var prevSlot = HtmlContainer.PageIndexOf(baseTop - HtmlContainerInt.PageBoundaryEpsilon);
                                var naturalSlot = HtmlContainer.PageIndexOf(top - HtmlContainerInt.PageBoundaryEpsilon);
                                if (naturalSlot > prevSlot)
                                {
                                    var newTop = HtmlContainer.PageTopOf(prevSlot + 1);

                                    // css-break §3.1 keep-with-next: this box is about to relocate to
                                    // the next page's content top, which would otherwise strand a
                                    // preceding break-after/break-before: avoid run (e.g. the UA default
                                    // `h1-h6 { page-break-after: avoid }`) alone at the bottom of the
                                    // page it's leaving - see CssLayoutEngineTable's identical whole-table
                                    // pre-check (LayoutCells) and OffsetTopWithKeepWithNextRun, which this
                                    // mirrors. Pull the run along when it starts on this same page and its
                                    // own height still fits the destination page's band; an unsatisfiable
                                    // avoid is relaxed per spec and this box moves alone, exactly as
                                    // before. Unlike those two siblings' guards, this one doesn't also
                                    // require this box's own (not-yet-laid-out) content to fit alongside
                                    // the run: a break-inside:avoid/orphans-widows box must land whole or
                                    // the move is pointless, but this box is free to fragment across
                                    // further pages on its own afterward (a table re-applies its per-row
                                    // break logic, an ordinary block just keeps flowing) - only the run
                                    // needs a page to itself.
                                    var keepWithNextRun = DomUtils.GetPrecedingKeepWithNextRun(this);
                                    if (keepWithNextRun.Count > 0)
                                    {
                                        var runTop = keepWithNextRun[0].Location.Y;
                                        var extraAbove = top - runTop;
                                        var runStartsOnSamePage =
                                            HtmlContainer.PageIndexOf(runTop - HtmlContainerInt.PageBoundaryEpsilon) == prevSlot;

                                        if (extraAbove > 0 && runStartsOnSamePage
                                            && extraAbove <= HtmlContainer.PageBandHeightOf(prevSlot + 1))
                                        {
                                            var groupOffset = newTop - runTop;

                                            foreach (var member in keepWithNextRun)
                                            {
                                                member.OffsetTop(groupOffset);
                                            }

                                            newTop += extraAbove;
                                        }
                                    }

                                    top = newTop;
                                }
                            }
                        }

                        Location = new RPoint(left + ActualMarginLeft, top);
                        ActualBottom = top;


                        CssLayoutEngine.FloatBox(this);
                    }

                    if (Position is CssConstants.Relative)
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
                        var offsetX = Left is not CssConstants.Auto || Right is CssConstants.Auto
                            ? CssValueParser.ParseLength(Left, ActualWidth, this)
                            : -CssValueParser.ParseLength(Right, ActualWidth, this);
                        var offsetY = Top is not CssConstants.Auto || Bottom is CssConstants.Auto
                            ? CssValueParser.ParseLength(Top, ActualHeight, this)
                            : -CssValueParser.ParseLength(Bottom, ActualHeight, this);

                        RelativeOffsetX = offsetX;
                        RelativeOffsetY = offsetY;
                        Location = new RPoint(Location.X + offsetX, Location.Y + offsetY);
                        ActualBottom = Location.Y;
                    }

                    if (Position is CssConstants.Absolute)
                    {
                        var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                        // CSS 2.1 §10.3.7: `left`/`top` on an absolutely positioned box are measured
                        // from the containing block's PADDING edge (ClientLeft/ClientTop - inside the
                        // border), not its border-box edge (Location.X/Y) - and, like every other
                        // positioning scheme, the box's own margin still applies on top of that offset
                        // (previously dropped entirely here, unlike the static/relative branch above
                        // which already adds ActualMarginLeft). Acid2's own
                        // "[class~=one].first.one { position:absolute; margin: 36px 0 0 60px; }" inside
                        // ".picture" (which has a 1em border) exercises both of these: the missing
                        // margin alone lands the box ~36px/60px off, on top of the next sibling.
                        var left = nearestPositionedAncestor.ClientLeft + ActualMarginLeft +
                                   CssValueParser.ParseLength(Left, nearestPositionedAncestor.ActualWidth, this);

                        var top = nearestPositionedAncestor.ClientTop + ActualMarginTop +
                                  CssValueParser.ParseLength(Top, nearestPositionedAncestor.ActualHeight, this);

                        Location = new RPoint(left, top);
                    }

                    if (Position is CssConstants.Fixed)
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
                        var left = ActualMarginLeft + CssValueParser.ParseLength(Left, HtmlContainer!.PageSize.Width, this);
                        var top = ActualMarginTop + CssValueParser.ParseLength(Top, HtmlContainer!.PageSize.Height, this);
                        Location = new RPoint(left, top);
                    }
                }

                // Register the used page name BEFORE any child lays out: descendants' page-break
                // decisions consult the per-page geometry table, whose slot bands from this box's
                // page onward depend on this name being visible (PageRuleResolver.
                // ActiveNameAtSlotStart) - registering only after child layout (this method's tail,
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
                if (shouldRegisterPage)
                {
                    RegisteredNamedPageElement = HtmlContainer!.RegisterNamedPageElement(UsedPageName, NamedPageRegistrationY());
                }

                if (Display is CssConstants.Flex or CssConstants.InlineFlex)
                {
                    await CssLayoutEngineFlex.PerformLayout(g, this);
                    await LayoutOutOfFlowChildren(g);
                }
                else if (Display is CssConstants.Grid or CssConstants.InlineGrid)
                {
                    await CssLayoutEngineGrid.PerformLayout(g, this);
                    await LayoutOutOfFlowChildren(g);
                }
                else if (Display is CssConstants.Table or CssConstants.InlineTable)
                {
                    await CssLayoutEngineTable.PerformLayout(g, this);
                    await LayoutOutOfFlowChildren(g);
                }
                else
                {
                    //If there's just inline boxes, create LineBoxes
                    if (DomUtils.ContainsInlinesOnly(this))
                    {
                        ActualBottom = Location.Y;
                        await CssLayoutEngine.CreateLineBoxes(g, this); //This will automatically set the bottom of this block

#if DEBUG
                        foreach (var lineBox in LineBoxes)
                        {
                            Console.WriteLine($"layout linebox: {lineBox} [h: {lineBox.LineBottom}]");
                        }
#endif

                    }
                    else if (EstablishesMultiColumnContext && Boxes.Count > 0)
                    {
                        await CssLayoutEngineColumns.PerformLayout(g, this);
                    }
                    else if (Boxes.Count > 0)
                    {
                        foreach (var childBox in Boxes)
                        {
                            await childBox.PerformLayout(g);
                        }

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

            CssLayoutEngine.ApplyHeight(this);
            CssLayoutEngine.ApplyParentHeight(this);

            // An "outside" ::marker (the CSS default) is deliberately excluded from this box's own
            // inline flow (CssLayoutEngine.FlowBox) and never gets a PerformLayoutImp call via the
            // generic block-children loop either (it's not a block child) - so it needs this one
            // explicit call, now that Location is final, to lay itself out (see
            // CssBoxMarker.PerformLayoutImp). An "inside" marker already laid itself out as an
            // ordinary flowed child above and no-ops here (ListStylePosition check).
            if (Display == CssConstants.ListItem)
            {
                var markerBox = Boxes.FirstOrDefault(b => b.IsMarkerPseudoElement);
                if (markerBox != null)
                {
                    await markerBox.PerformLayout(g);
                }
            }

            // css-break keep-with-next at the word-flow fragmentation site: word flow relocates any
            // line that would straddle a page boundary to the next page (CssRect.BreakPage, called
            // from CssLayoutEngine.FlowBox). When that happens to this block's FIRST line, the break
            // effectively falls right before this box's content - so preceding siblings chained to it
            // by break-after/break-before: avoid (css-break §3.1, e.g. the UA default
            // `h1-h6 { page-break-after: avoid }`) must not be left behind on the old page. Move the
            // chained run to the top of the page the line landed on, then re-run this box's own layout:
            // its position re-derives from the moved run's new bottom and its lines re-flow without a
            // boundary in the middle (PerformLayoutImp double-execution is already an established
            // pattern - see HtmlContainerInt.PerformLayout's own double layout). Guarded to one retry.
            if (!_keepWithNextRetried
                && Position is CssConstants.Static or CssConstants.Relative && !IsFloated
                && LineBoxes.Count > 0 && LineBoxes[0].Words.Count > 0
                && HtmlContainer!.PageSize.Height > 0)
            {
                var firstWordTop = LineBoxes[0].Words.Min(w => w.Top);
                var ownPage = HtmlContainer.PageIndexOf(Location.Y);
                var firstLinePage = HtmlContainer.PageIndexOf(firstWordTop);

                if (firstLinePage > ownPage)
                {
                    var keepWithNextRun = DomUtils.GetPrecedingKeepWithNextRun(this);
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
                            try
                            {
                                await PerformLayoutImp(g);
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

            if (BreakInside is CssConstants.Avoid)
            {
                // Shifted-grid convention (see HtmlContainer.PageIndexOf) - topRelativeToCurrentPage is
                // this box's distance from the start of its own page's real content band, not a raw
                // modulo of PageSize.Height (which ignored MarginTop and, for the last MarginTop-wide
                // sliver of every page, mis-detected which page a box's top actually belonged to).
                var currentPageIndex = HtmlContainer!.PageIndexOf(Location.Y);
                var topRelativeToCurrentPage = Location.Y - HtmlContainer.PageTopOf(currentPageIndex);

                var bottomRelativeToCurrentPage = topRelativeToCurrentPage + ActualBottom - Location.Y;

                if (bottomRelativeToCurrentPage > HtmlContainer.PageBandHeightOf(currentPageIndex))
                {
                    var offset = HtmlContainer.PageTopOf(currentPageIndex + 1) - Location.Y;
                    OffsetTopWithKeepWithNextRun(offset, topRelativeToCurrentPage);
                }
            }

            // orphans/widows: a paragraph-like box (real line boxes, not multicol's atomic-child model -
            // which never splits a child, so this defect can't occur there in the first place) whose
            // lines would otherwise straddle a page boundary with too few lines before/after it gets
            // nudged, as a whole, to the next page - the same OffsetTop mechanism BreakInside:avoid uses
            // just above. This is a coarser-than-spec approximation (a real UA pulls only the minimum
            // lines needed across the break; this moves the entire box) - accepted deliberately, since
            // real per-line fragmentation would need this engine's "whole child" layout model rewritten.
            // A paragraph taller than one page is left alone: pushing it whole can't help; it would just
            // recreate the same violation on the next page.
            if (DomUtils.ContainsInlinesOnly(this) && LineBoxes.Count > 1
                && int.TryParse(Orphans, out var orphans) && int.TryParse(Widows, out var widows)
                && (orphans > 1 || widows > 1))
            {
                var owPageHeight = HtmlContainer!.PageSize.Height;

                if (owPageHeight > 0
                    && ActualBottom - Location.Y <= HtmlContainer.PageBandHeightOf(HtmlContainer.PageIndexOf(Location.Y)))
                {
                    // Same shifted-grid convention as the BreakInside:Avoid block above.
                    var ownPageIndex = HtmlContainer.PageIndexOf(Location.Y);
                    var ownPageTop = HtmlContainer.PageTopOf(ownPageIndex);
                    var ownTopRelativeToPage = Location.Y - ownPageTop;

                    // Absolute Y of the first shifted-page boundary at or after this box's own top.
                    var boundaryY = HtmlContainer.PageTopOf(ownPageIndex + 1);

                    if (boundaryY > Location.Y && boundaryY < ActualBottom)
                    {
                        var linesBefore = LineBoxes.Count(l => l.LineBottom <= boundaryY);
                        var linesAfter = LineBoxes.Count - linesBefore;

                        if (linesBefore > 0 && linesAfter > 0 && (linesBefore < orphans || linesAfter < widows))
                        {
                            var offset = boundaryY - Location.Y;
                            OffsetTopWithKeepWithNextRun(offset, ownTopRelativeToPage);
                        }
                    }
                }
            }

            if (Position is CssConstants.Absolute)
            {
                if (Left is CssConstants.Auto && Right is not CssConstants.Auto)
                {
                    var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                    var right = CssValueParser.ParseLength(Right, nearestPositionedAncestor.ActualWidth, this);
                    var actualRight = nearestPositionedAncestor.ClientRight + nearestPositionedAncestor.ActualPaddingRight - right;

                    var delta = actualRight - ActualRight;

                    OffsetLeft(delta);
                }

                // Symmetric vertical-axis counterpart to the right-edge fallback just above: `top` was
                // already always honored when set (the primary Position-is-Absolute branch earlier in
                // this method), but `bottom` was never read anywhere, so a box relying on `bottom` with
                // `top: auto` silently stayed at the containing block's top edge instead of being placed
                // relative to its bottom edge.
                if (Top is CssConstants.Auto && Bottom is not CssConstants.Auto)
                {
                    var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                    var bottom = CssValueParser.ParseLength(Bottom, nearestPositionedAncestor.ActualHeight, this);

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
            if (shouldRegisterPage)
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
                    boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text!, font).Width : 0;
                    // Letter-spacing adds space after every character including the last (N gaps for an
                    // N-character word) - matching both the PDF Tc operator's actual per-glyph behavior
                    // (PaintWords/RealizeFont) and CSS Text 3 §7.2, which only exempts the start/end of a
                    // *line*, not the end of a word. Reserving only N-1 gaps here (an old CSS1/2.1-era
                    // assumption) undersized the word's own box, so its Tc-driven paint spilled one
                    // letter-spacing unit into the next word's gap - collapsing adjacent words together
                    // once letter-spacing reached the gap's width.
                    if (boxWord.Text != "\n" && ActualLetterSpacing != 0)
                        boxWord.Width += boxWord.Text!.Length * ActualLetterSpacing;
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
                boxWord.Width = effectiveText != "\n" ? g.MeasureString(effectiveText!, font).Width : 0;
                // See MeasureWordsSize's identical fix/comment - N gaps for an N-character word, not N-1.
                if (effectiveText != "\n" && firstLineStyle.ActualLetterSpacing != 0)
                    boxWord.Width += effectiveText!.Length * firstLineStyle.ActualLetterSpacing;
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
                boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text!, font).Width : 0;
                // See MeasureWordsSize's identical fix/comment - N gaps for an N-character word, not N-1.
                if (boxWord.Text != "\n" && ActualLetterSpacing != 0)
                    boxWord.Width += boxWord.Text!.Length * ActualLetterSpacing;
                boxWord.Height = font.Height;
            }
        }

        /// <summary>
        /// Get the parent of this css properties instance.
        /// </summary>
        /// <returns></returns>
        protected sealed override CssBoxProperties? GetParent()
        {
            return _parentBox;
        }

        private void PaintContentImage(RGraphics g)
        {
            if (ContentImage == null) return;
            var offset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
            var areas = Rectangles.Count == 0 ? [Bounds] : Rectangles.Values.ToArray();
            foreach (var area in areas)
            {
                var rect = area;
                rect.Offset(offset);
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                CssImagePainter.Paint(g, ContentImage, layerIndex: 0, isFirst: true,
                    originRect: rect, clipRect: rect, roundedClipPath: null,
                    positionList: "0% 0%", sizeList: CssConstants.Auto, repeatList: "no-repeat",
                    attachmentList: CssConstants.Scroll, viewportRect: rect, box: this,
                    drawBrush: brush =>
                    {
                        g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                        brush.Dispose();
                    });
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
        /// Returns true if the given break-before or break-after value is a forced page-break value
        /// per CSS Fragmentation §3.1 (page, always).
        /// </summary>
        private static bool IsForcedBreakValue(string? value) =>
            value is CssConstants.Page or CssConstants.Always;

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
                    if (childBox.Display == CssConstants.None) continue;
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
            if (box.Display != CssConstants.Inline && box.Display != CssConstants.TableCell && box.WhiteSpace != CssConstants.NoWrap)
            {
                oldSum = maxSum;
                maxSum = marginSum;
                oldPaddingSum = paddingSum;
                paddingSum = 0;
            }

            // add the padding
            paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualPaddingLeft;


            // for tables the padding also contains the spacing between cells
            if (box.Display == CssConstants.Table)
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
                    if (childBox.Display == CssConstants.None) continue;

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
                        && !(childBox.Display == CssConstants.Inline && childBox.Words.Count == 0))
                    {
                        var explicitContentWidth = CssValueParser.ParseLength(childBox.Width, 0, childBox);
                        var childStartsNewLine = childBox.Display != CssConstants.Inline
                            && childBox.Display != CssConstants.TableCell && childBox.WhiteSpace != CssConstants.NoWrap;
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
        /// Inherits inheritable values from parent.
        /// </summary>
        internal new void InheritStyle(CssBox? box = null, bool everything = false)
        {
            base.InheritStyle(box ?? ParentBox, everything);
        }

        /// <summary>
        /// Set by an ancestor's lookahead in <see cref="MarginTopCollapse"/> when this box is a
        /// non-anchor member of a shared chain of adjoining first-in-flow-child margins: always 0,
        /// because the anchor member (the outermost box in the chain, wherever the chain's resolution
        /// began) already received the group's FULL collapsed value as its own return value, and this
        /// box's position is computed relative to its immediate parent's already-correctly-positioned
        /// ClientTop - adding the group value again here would double (or triple, ...) count it. See the
        /// lookahead loop below for why this box must not resolve its own top margin independently.
        /// </summary>
        private double? _groupTopMarginOverride;

        /// <summary>
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <param name="prevSibling">the previous box under the same parent</param>
        /// <returns>Resulting top margin</returns>
        protected double MarginTopCollapse(CssBox? prevSibling)
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
            if (IsFloated)
            {
                var floatValue = ActualMarginTop + (prevSibling?.GetEffectiveBottomMargin() ?? 0);
                CollapsedMarginTop = floatValue;
                return floatValue;
            }

            // An ancestor's own MarginTopCollapse call already looked ahead into this box (as part of a
            // shared chain of adjoining first-in-flow-child margins) and resolved the group's true,
            // fully-collapsed value - use it directly rather than resolving independently. This box's own
            // isolated view (e.g. via the escape formula below) could only ever "see" as far as its
            // immediate parent, which is exactly what caused a real bug: a 3+-level chain where the
            // outermost box's position was itself fixed by sibling-margin-collapse before a deeper
            // descendant's larger margin was known, silently adding on top instead of properly collapsing
            // into one shared value.
            if (_groupTopMarginOverride is { } overrideValue)
            {
                _groupTopMarginOverride = null;
                CollapsedMarginTop = overrideValue;
                return overrideValue;
            }

            // CSS2.1 §8.3.1: a set of adjoining margins collapses to the maximum of its positive
            // margins plus the most negative of its negative margins, computed over the WHOLE set at
            // once (see AdjoiningMarginSet). Acid2's ".forehead / .empty / .smile" run is exactly
            // such a mixed-sign set.
            var margins = new AdjoiningMarginSet();

            CssBox? anchor = null;
            if (prevSibling != null)
            {
                // A self-collapsing previous sibling (and any run of self-collapsing siblings
                // immediately before it) contributes no height of its own, so every margin adjoining
                // through it (its own top+bottom plus, recursively, its in-flow descendants' - see
                // FoldSelfCollapsingMargins) joins the group, and the group keeps adjoining further
                // back rather than acting as a break in the chain (CSS2.1 8.3.1, self-collapsing
                // empty boxes). Walk back to find the nearest NON-self-collapsing predecessor - that one
                // (not prevSibling itself, when prevSibling is self-collapsing) is the real position
                // anchor, because a self-collapsing box's own Location only reflected a partial view of
                // the group's margin at the time IT was positioned (this box may be the one that finally
                // reveals the group's true, larger collapsed value). Bounded defensively (real documents
                // never have this many consecutive self-collapsing siblings) so any unexpected sibling-
                // chain quirk degrades to "stop walking back" instead of spinning forever.
                if (prevSibling.IsMarginCollapseThrough())
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
                while (walker.IsMarginCollapseThrough() && walkBackSteps++ < 1000)
                {
                    var earlierSibling = DomUtils.GetPreviousSibling(walker, false);
                    if (earlierSibling == null || earlierSibling == walker) break;
                    if (earlierSibling.IsMarginCollapseThrough())
                    {
                        earlierSibling.FoldSelfCollapsingMargins(ref margins);
                    }
                    else
                    {
                        margins.Fold(earlierSibling.ActualMarginBottom);
                    }
                    walker = earlierSibling;
                    if (!walker.IsMarginCollapseThrough()) anchor = walker;
                }
            }

            // Only this box's own TOP margin joins its own position group - even when this box is
            // itself self-collapsing. Per CSS2.1 §8.3.1 a collapsed-through box's top border edge
            // sits where it would "if the element had a non-zero bottom border", i.e. its own bottom
            // margin positions only what FOLLOWS it (folded there via FoldSelfCollapsingMargins in
            // the following sibling's walk-back above), never the box itself. (When there is no
            // prevSibling at all, this is also the whole group: reaching that case means the parent
            // couldn't fold this box into its own lookahead - see the override above - so this box's
            // top margin is genuinely isolated from anything above it.)
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
            // value as its own return value below. Every deeper chain member instead gets a 0 override:
            // since nothing separates them from their own immediate parent (that parent is either the
            // anchor itself or another 0 member), their position is already exactly right as soon as
            // it's computed relative to that parent's own (already-correct) ClientTop - giving them the
            // full group value AGAIN here would double/triple/... count it at each level.
            var chainMembers = new List<CssBox>();
            var current = this;
            // Capped defensively (real documents never nest this deep) so a malformed/cyclic box tree
            // degrades to "stop extending the group" instead of hanging or overflowing the stack.
            while (chainMembers.Count < 1000 && current.Overflow == CssConstants.Visible &&
                   current.ActualBorderTopWidth < 0.1 && current.ActualPaddingTop < 0.1)
            {
                var firstInFlowChild = current.Boxes.FirstOrDefault(b => !b.IsOutOfFlow && b.Display != CssConstants.None);
                if (firstInFlowChild == null || firstInFlowChild.Clear != CssConstants.None || firstInFlowChild == current) break;

                margins.Fold(firstInFlowChild.ActualMarginTop);
                chainMembers.Add(firstInFlowChild);
                current = firstInFlowChild;
            }

            foreach (var member in chainMembers)
            {
                member._groupTopMarginOverride = 0;
            }

            var groupValue = margins.CollapsedValue;

            // fix for hr tag
            if (groupValue < 0.1 && HtmlTag is { Name: "hr" })
            {
                groupValue = GetEmHeight() * 1.1f;
            }

            CollapsedMarginTop = groupValue;

            if (prevSibling == null)
            {
                return groupValue;
            }

            // Every preceding sibling back to the start of the parent's children is self-collapsing
            // (no real anchor found) - approximate the anchor as the parent's own content-top, same
            // as if this box were the parent's first child (a rare compound edge case).
            var anchorY = anchor != null
                ? anchor.StaticBottom + anchor.ActualBorderBottomWidth
                : ContainingBlock.ClientTop;

            // The call site unconditionally adds prevSibling.StaticBottom + its bottom border on top
            // of whatever this method returns - back that out so the final sum lands at the true,
            // fully-resolved anchorY + groupValue regardless of how partial prevSibling's own
            // (already-finalized, possibly stale) position turned out to be. StaticBottom on both
            // sides (anchor and back-out) so a relatively-positioned sibling's visual offset never
            // leaks into following flow (CSS 2.1 §9.4.3).
            return anchorY + groupValue - prevSibling.StaticBottom - prevSibling.ActualBorderBottomWidth;
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
                if (childBox.IsOutOfFlow || childBox.Display == CssConstants.None) continue;
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
            if (Display == CssConstants.None) return false;
            if (IsOutOfFlow) return false;
            // A percentage height against an indefinite (not-yet-height-calculated) containing block
            // resolves to auto (CSS2.1 §10.5, the same rule ApplyHeight already applies) - Acid2's own
            // ".empty { margin: 6.25em; height: 10%; }" is written to exercise exactly this: its own
            // comment notes "computes to auto which makes it empty per 8.3.1:7 (own margins)".
            var heightIsAuto = Height == CssConstants.Auto ||
                (Height.EndsWith('%') && !ContainingBlock.IsHeightCalculated);
            if (!heightIsAuto) return false;
            if (Overflow != CssConstants.Visible) return false;
            if (!(ActualPaddingTop < 0.1) || !(ActualPaddingBottom < 0.1)) return false;
            if (!(ActualBorderTopWidth < 0.1) || !(ActualBorderBottomWidth < 0.1)) return false;
            // A box with real text content (e.g. an anonymous text-node box) is not empty even when it
            // has zero nested CssBox children - it still has real line-box height from its own words.
            if (Words.Count > 0) return false;

            var minHeightZero = MinHeight == CssConstants.Auto ||
                (CssValueParser.IsValidLength(MinHeight) &&
                 CssValueParser.ParseLength(MinHeight, ContainingBlock.Size.Height, this) <= 0);
            if (!minHeightZero) return false;

            var inFlowChildren = Boxes.Where(b => !b.IsOutOfFlow && b.Display != CssConstants.None && b != this).ToList();
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
            if (container.PageIndexOf(Location.Y + HtmlContainerInt.PageBoundaryEpsilon)
                >= container.PageIndexOf(ActualBottom - HtmlContainerInt.PageBoundaryEpsilon))
                return false;

            Location = Location with { Y = container.NextPageTopOf(Location.Y) + 1 };

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
                additionalMarginRight = box.BoxSizing switch
                {
                    CssConstants.ContentBox => 0,
                    CssConstants.BorderBox => box.ActualMarginRight,
                    _ => throw new HtmlRenderException("Unknown BoxSizing", HtmlRenderErrorType.Layout)
                };

                // RelativeOffsetX backed out for the same reason MarginBottomCollapse uses
                // StaticBottom: a relatively-positioned child's visual offset must not widen the
                // parent (CSS 2.1 §9.4.3).
                maxRight = Math.Max(maxRight, box.ActualRight - box.RelativeOffsetX + additionalMarginRight);
            }

            additionalMarginRight = BoxSizing switch
            {
                CssConstants.ContentBox => 0,
                CssConstants.BorderBox => ActualMarginRight,
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
            // MarginTopCollapse call adds on top of (via the ordinary adjoining-sibling-margin path,
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
                Overflow != CssConstants.Visible)
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
            return container.PageSize.Height > 0 && container.PageSize.Height < double.MaxValue - 1
                ? container.PageTopOf(container.PageIndexOf(Location.Y + HtmlContainerInt.PageBoundaryEpsilon))
                : Location.Y;
        }

        /// <summary>
        /// Deeply offsets the top of the box and its contents
        /// </summary>
        /// <param name="amount"></param>
        internal void OffsetTop(double amount)
        {
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
                b.OffsetTop(amount);
            }

            Location = Location with { Y = Location.Y + amount };
        }

        /// <summary>
        /// Moves this box to the next page (like a plain <see cref="OffsetTop"/> by <paramref name="offset"/>),
        /// additionally pulling along the run of preceding siblings chained to it by
        /// break-after/break-before: avoid (css-break §3.1 keep-with-next, e.g. the UA default
        /// <c>h1-h6 { page-break-after: avoid }</c>) so a heading is not stranded at the bottom of the
        /// page its content just left. The run only comes along when it starts on the same page as this
        /// box and the combined run + box still fits on a single page; an unsatisfiable avoid is relaxed
        /// per spec and this box moves alone, exactly as before.
        /// </summary>
        /// <param name="offset">the offset that moves this box's top to the next page's content top</param>
        /// <param name="topRelativeToCurrentPage">this box's top, reduced to page-relative coordinates by the caller</param>
        internal void OffsetTopWithKeepWithNextRun(double offset, double topRelativeToCurrentPage)
        {
            // "Fits on a single page" is judged against the destination page's band (the page this
            // box's top lands on after the offset), not the page it is leaving.
            var targetPageBand = HtmlContainer!.PageBandHeightOf(HtmlContainer.PageIndexOf(Location.Y + offset));
            var keepWithNextRun = DomUtils.GetPrecedingKeepWithNextRun(this);

            if (keepWithNextRun.Count > 0)
            {
                var runTop = keepWithNextRun[0].Location.Y;
                var extraAbove = Location.Y - runTop;

                if (extraAbove > 0 && extraAbove < topRelativeToCurrentPage
                    && extraAbove + ActualBottom - Location.Y <= targetPageBand)
                {
                    // Shift the run and this box by one common offset, chosen so the run's top lands at
                    // the next page's content top - relative spacing inside the group is preserved.
                    var groupOffset = offset + extraAbove;

                    foreach (var member in keepWithNextRun)
                    {
                        member.OffsetTop(groupOffset);
                    }

                    OffsetTop(groupOffset);
                    return;
                }
            }

            OffsetTop(offset);
        }

        /// <summary>
        /// Deeply offsets the top of the box and its contents
        /// </summary>
        /// <param name="amount"></param>
        internal void OffsetLeft(double amount)
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
                b.OffsetLeft(amount);
            }

            Location = Location with { X = Location.X + amount };
        }

        private bool _hasPainted;

        /// <summary>
        /// Paints the fragment, wrapping <see cref="PaintImpCore(RGraphics)"/> (the actual per-box
        /// paint logic, overridden by replaced-element subclasses) with tagged-PDF structure-tree/
        /// marked-content bookkeeping when tagging is enabled
        /// (<c>HtmlContainer.StructureTagBuilder</c> is non-null). This is the single choke point
        /// all tagging flows through, mirroring how <see cref="Paint"/> already conditionally wraps
        /// <see cref="PaintImpCore(RGraphics)"/>/<see cref="PaintWithOpacity"/> for transform/opacity
        /// handling. When tagging is disabled this adds one null check and otherwise behaves exactly
        /// as calling <see cref="PaintImpCore(RGraphics)"/> directly would.
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected async ValueTask PaintImp(RGraphics g)
        {
            // Mirrors PaintImpCore's own early-out: skip classification/tagging entirely for a call
            // that will paint nothing this pass (see PaintImpCore's identical _hasPainted check).
            if (_hasPainted)
                return;

            var builder = HtmlContainer?.StructureTagBuilder;
            if (builder == null)
            {
                await PaintImpCore(g);
                return;
            }

            var classification = StructureTagMapper.Classify(this);
            switch (classification.Kind)
            {
                case StructureTagKind.Artifact:
                    using (builder.OpenArtifact(g))
                        await PaintImpCore(g);
                    break;

                case StructureTagKind.Grouping when classification.StructureType == Keywords.Li:
                    await PaintListItem(g, builder);
                    break;

                case StructureTagKind.Grouping:
                    using (builder.OpenGroupingElement(this, classification.StructureType!))
                        await PaintImpCore(g);
                    break;

                case StructureTagKind.Content:
                    using (builder.OpenContentElement(g, this, classification.StructureType!, classification.AltText))
                        await PaintImpCore(g);
                    break;

                default:
                    await PaintImpCore(g);
                    break;
            }
        }

        /// <summary>
        /// Paints a tagged &lt;li&gt;'s marker and body as separate sibling structure elements under
        /// "/LI" - "/Lbl" (the marker, via the synthesized <c>::marker</c> child from
        /// <see cref="IsMarkerPseudoElement"/>, defaulting to "Lbl" per the UA stylesheet's
        /// <c>li::marker</c> rule, or suppressed if an author set
        /// <c>li::marker { -peachpdf-pdf-tag-type: none }</c>) and "/LBody" (everything else - not
        /// itself a separate CSS signal, inferred as "whatever isn't the marker" per the design
        /// decision this implements). The struct element for "/Lbl" is opened, and its own content
        /// painted, before "/LBody"'s so the two land in that order under "/LI"'s "/K" (correct
        /// reading order) - the marker's own on-page paint position is unaffected by this call-order
        /// swap, since it's driven entirely by pre-computed layout coordinates, not paint order.
        /// </summary>
        private async ValueTask PaintListItem(RGraphics g, StructureTagBuilder builder)
        {
            using (builder.OpenGroupingElement(this, Keywords.Li))
            {
                var markerBox = Boxes.FirstOrDefault(b => b.IsMarkerPseudoElement);
                if (markerBox != null)
                {
                    var markerClassification = StructureTagMapper.Classify(markerBox);
                    if (markerClassification.Kind == StructureTagKind.None)
                    {
                        await markerBox.PaintImpCore(g);
                    }
                    else
                    {
                        using (builder.OpenContentElement(g, markerBox, markerClassification.StructureType ?? Keywords.Lbl))
                            await markerBox.PaintImpCore(g);
                    }
                }

                using (builder.OpenListItemBodyElement(this))
                    await PaintImpCore(g, paintMarkers: false);
            }
        }

        /// <summary>
        /// Paints the fragment. Renamed from the historical "PaintImp" - <see cref="PaintImp"/> is
        /// now a non-virtual wrapper around this method that adds tagged-PDF marked-content/struct-
        /// element bookkeeping around whatever this override actually paints, when tagging is
        /// enabled (see StructureTagMapper/StructureTagBuilder). Subclasses needing custom paint
        /// logic still override this method, exactly as they overrode "PaintImp" before.
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected virtual async ValueTask PaintImpCore(RGraphics g)
        {
            await PaintImpCore(g, paintMarkers: true);
        }

        /// <summary>
        /// The actual paint logic, with list-marker painting optionally excluded. Only the base
        /// <see cref="CssBox"/> needs this overload - it exists so the tagged-PDF &lt;li&gt; path in
        /// <see cref="PaintImp"/> (always a plain <see cref="CssBox"/>, never one of the replaced-
        /// element subclasses that override the virtual <see cref="PaintImpCore(RGraphics)"/>) can
        /// paint an &lt;li&gt;'s body content and its marker as two separately MCID-tagged, sibling
        /// structure elements ("/Lbl" and "/LBody") instead of one combined region.
        /// </summary>
        private async ValueTask PaintImpCore(RGraphics g, bool paintMarkers)
        {
            if (_hasPainted)
            {
                return;
            }

            if (Display == CssConstants.None ||
                (Display == CssConstants.TableCell && EmptyCells == CssConstants.Hide && IsSpaceOrEmpty)) return;

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            // Captured together (not as two separate Rectangles.Keys/.Values calls) so each rect's
            // associated line box - needed to resolve first-line style per rect below - can never
            // misalign with its rect by index.
            var rectEntries = Rectangles.Count == 0
                ? new (CssLineBox? Line, RRect Rect)[] { (null, Bounds) }
                : Rectangles.Select(kv => (Line: (CssLineBox?)kv.Key, Rect: kv.Value)).ToArray();
            var clip = g.GetClip();
            var rects = rectEntries.Select(e => e.Rect).ToArray();
            var offset = RPoint.Empty;

            if (!IsFixed)
            {
                offset = HtmlContainer!.ScrollOffset;
            }

            for (var i = 0; i < rects.Length; i++)
            {
                var actualRect = rects[i];
                actualRect.Offset(offset);

                if (!IsRectVisible(actualRect, clip)) continue;

                var hasBoxShadow = !string.IsNullOrEmpty(BoxShadow) &&
                                   !string.Equals(BoxShadow, CssConstants.None, StringComparison.OrdinalIgnoreCase);

                // Outset (drop) shadows paint BEFORE the background so they sit behind the box
                // (CSS Backgrounds & Borders 3 §5).
                if (hasBoxShadow)
                    PaintBoxShadows(g, actualRect, inset: false);

                // A box whose background was "promoted" to fill the whole page canvas (see
                // PdfGenerator.ResolveCanvasBackground / PaintCanvasBackground) already had it painted
                // for every page - skip this box's own normal paint pass so it isn't painted twice.
                if (!SuppressOwnBackgroundPaint)
                    PaintBackground(g, actualRect, i == 0, GetFirstLineStyleForRect(rectEntries[i].Line));

                // Inset shadows paint AFTER (over) the background, clipped to the padding box.
                if (hasBoxShadow)
                    PaintBoxShadows(g, actualRect, inset: true);

                // For multi-page tables, draw the outer bottom border at the page-break Y on
                // intermediate pages (instead of at actualRect.Bottom which is off-page).
                // The PerformPaint clip already constrains the side-border top to MarginTop.
                var rectForBorders = actualRect;
                if ((Display == CssConstants.Table || Display == CssConstants.InlineTable)
                    && PageBreakBottoms != null && HtmlContainer != null)
                {
                    var pageHeight = HtmlContainer.PageSize.Height;
                    if (pageHeight > 0)
                    {
                        // -offset.Y is the current slot's top in scrollOffset space (slotTop, no
                        // MarginTop shift), so re-add MarginTop to express it as a document-space
                        // content-band Y before asking the shared grid which slot it is; the epsilon
                        // absorbs the same float noise the old "+0.001" fudge did.
                        var currentPageIndex = HtmlContainer.PageIndexOf(
                            -offset.Y + HtmlContainer.MarginTop + HtmlContainerInt.PageBoundaryEpsilon);
                        if (PageBreakBottoms.TryGetValue(currentPageIndex, out var pageBreakBottom))
                        {
                            var pageBreakBottomVisual = pageBreakBottom + offset.Y;
                            if (pageBreakBottomVisual < actualRect.Bottom)
                            {
                                rectForBorders = new RRect(
                                    actualRect.Left,
                                    actualRect.Top,
                                    actualRect.Width,
                                    pageBreakBottomVisual - actualRect.Top);
                            }
                        }
                    }
                }

                BordersDrawHandler.DrawBoxBorders(g, this, rectForBorders, i == 0, i == rects.Length - 1);
            }

            if (ColumnRuleSegments is { Count: > 0 } && ActualColumnRuleWidth > 0)
            {
                PaintColumnRules(g, offset, clip);
            }

            PaintWords(g, offset);

            for (var i = 0; i < rects.Length; i++)
            {
                var actualRect = rects[i];
                actualRect.Offset(offset);

                if (IsRectVisible(actualRect, clip))
                {
                    PaintDecoration(g, actualRect, i == 0, i == rects.Length - 1, GetFirstLineStyleForRect(rectEntries[i].Line));
                }
            }

            var stackingContextBoxes = DomUtils.FlattenStackingContext(this);

            foreach (var layerBoxes in DomUtils.GetBoxesByLayers(stackingContextBoxes))
            {
                // Split paint to handle z-order, per CSS2.1 Appendix E's within-a-stacking-context
                // order: in-flow block-level descendants, then non-positioned floats, then in-flow
                // inline-level descendants (text and inline replaced content), then positioned
                // descendants. Block and inline normal-flow content used to share a single pass here
                // (painted in tree order with no float-relative ordering guarantee at all) - Acid2's own
                // ".eyes" trap (a block, a float, and an inline replaced <object> as siblings, each
                // required to paint in a different layer) depends on inline being its own later pass.
                //
                // A plain (non-inline-itself) box whose entire content is inline - e.g. Acid2's own
                // "#eyes-a" div, a block wrapper around nothing but its resolved inline <object> image -
                // is treated as belonging to the inline pass too, via ActsAsInline below. Its own
                // recursive Paint() call is what actually paints its inline child (through ITS OWN
                // nested stacking loop), so deferring that whole call to this stacking context's inline
                // pass is what makes the child paint after this context's float pass, matching Appendix
                // E, without needing to hoist the descendant out of its normal DOM position/ancestor at
                // all (unlike the out-of-flow float/absolute/fixed hoisting FlattenStackingContext
                // already does - that mechanism moves a box's paint call across ancestor boundaries
                // entirely, which isn't needed or wanted here since "#eyes-a" itself already belongs to
                // this stacking context's own direct children).
                foreach (var p in layerBoxes)
                {
                    if (!ActsAsInline(p.Box) && p.Box.Position != CssConstants.Absolute && p.Box is { IsFixed: false, IsFloated: false })
                        await PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.IsFloated)
                        await PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (ActsAsInline(p.Box) && p.Box.Position != CssConstants.Absolute && p.Box is { IsFixed: false, IsFloated: false })
                        await PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.Position == CssConstants.Absolute)
                        await PaintStackingParticipant(g, p);
                }

                foreach (var p in layerBoxes)
                {
                    if (p.Box.IsFixed)
                        await PaintStackingParticipant(g, p);
                }
            }

            if (clipped)
                g.PopClip();

            if (paintMarkers)
            {
                var markerBox = Boxes.FirstOrDefault(b => b.IsMarkerPseudoElement);
                if (markerBox != null)
                {
                    await markerBox.PaintImpCore(g);
                }
            }

            PaintContentImage(g);

            _hasPainted = true;
        }

        /// <summary>
        /// Whether <paramref name="box"/> belongs in the "inline" paint pass of the block/float/inline
        /// ordering in <see cref="PaintImpCore(RGraphics)"/>: either it's genuinely inline-level itself, or it's a
        /// plain block-level box whose entire content is inline (an "invisible" wrapper carrying only
        /// inline content, own box aside - e.g. Acid2's own "#eyes-a", a div around nothing but its
        /// resolved inline &lt;object&gt; image). <c>Boxes.Count > 0</c> guards against misclassifying a
        /// genuinely empty block box as inline (<see cref="System.Linq.Enumerable.All{T}"/> on an empty
        /// sequence is vacuously true).
        /// </summary>
        private static bool ActsAsInline(CssBox box) =>
            box.IsInline || (box.Boxes.Count > 0 && box.Boxes.All(b => b.IsInline));

        /// <summary>
        /// Paints one stacking-context participant discovered by <see cref="DomUtils.FlattenStackingContext"/>.
        /// A hoisted participant (non-empty <see cref="DomUtils.StackingParticipant.ClipAncestors"/>)
        /// paints via this box's own paint loop rather than the ordinary nested parent-to-child
        /// <see cref="Paint"/> cascade, so its ancestors' own <c>overflow: hidden</c> clipping - normally
        /// picked up "for free" from still being active on the graphics clip stack during natural nested
        /// painting - is never applied on its own. Re-apply it explicitly here instead, scoped to exactly
        /// this participant's own paint call. A no-op for a direct plain child (empty ClipAncestors).
        /// </summary>
        private static async ValueTask PaintStackingParticipant(RGraphics g, DomUtils.StackingParticipant participant)
        {
            var pushedClips = RenderUtils.PushAncestorOverflowClips(g, participant.Box, participant.ClipAncestors);

            await participant.Box.Paint(g);

            for (var i = 0; i < pushedClips; i++)
                g.PopClip();
        }

        /// <summary>
        /// Draws the vertical rule lines between columns of a multi-column container, one segment per
        /// gap per page-row (see <see cref="ColumnRuleSegments"/>).
        /// </summary>
        private void PaintColumnRules(RGraphics g, RPoint offset, RRect clip)
        {
            var pen = g.GetPen(ActualColumnRuleColor);
            pen.Width = ActualColumnRuleWidth;
            pen.DashStyle = ColumnRuleStyle switch
            {
                CssConstants.Dashed => RDashStyle.Dash,
                CssConstants.Dotted => RDashStyle.Dot,
                _ => RDashStyle.Solid,
            };

            foreach (var (x, top, bottom) in ColumnRuleSegments!)
            {
                var visualX = x + offset.X;
                var visualTop = top + offset.Y;
                var visualBottom = bottom + offset.Y;

                if (!IsRectVisible(new RRect(visualX - 1, visualTop, 2, visualBottom - visualTop), clip)) continue;

                g.DrawLine(pen, visualX, visualTop, visualX, visualBottom);
            }
        }

        private static bool IsRectVisible(RRect rect, RRect clip)
        {
            rect.X -= 2;
            rect.Width += 2;
            clip.Intersect(rect);

            // See the zero-area-intersection fix/comment on CssBox.Paint's visibility cull
            // (GitHub issue #113) - a rect merely touching the clip edge isn't actually visible.
            return clip.Width > VisibilityClipEpsilon && clip.Height > VisibilityClipEpsilon;
        }

        /// <summary>
        /// Resolves the <c>::first-line</c> style (if any) that applies to a rectangle painted for
        /// <paramref name="lineBox"/> - true exactly when the line's own owner (the block establishing
        /// that inline formatting context - see <see cref="CssLineBox.OwnerBox"/>) has a resolved
        /// first-line style and this is genuinely that owner's first line. Used to make
        /// <see cref="PaintBackground"/>/<see cref="PaintDecoration"/> first-line-aware without either
        /// method needing to know its own relationship to the block establishing its inline formatting
        /// context - the line box already knows.
        /// </summary>
        private static CssBox? GetFirstLineStyleForRect(CssLineBox? lineBox) =>
            lineBox is not null && lineBox.OwnerBox.ResolvedFirstLineStyle is not null && lineBox == lineBox.OwnerBox.LineBoxes.FirstOrDefault()
                ? lineBox.OwnerBox.ResolvedFirstLineStyle
                : null;

        /// <summary>
        /// Paints the background of the box
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rect">the bounding rectangle to draw in</param>
        /// <param name="isFirst">is it the first rectangle of the element</param>
        /// <param name="firstLineStyle">
        /// When set, this rect is on the target's first formatted line under a <c>::first-line</c>
        /// rule - its resolved <c>background-color</c> is used instead of this box's own. Only
        /// <c>background-color</c> is first-line-aware, not <c>background-image</c>/-position/-size/
        /// -repeat/-origin/-clip layers (a documented narrowing - see docs/html-css-support.md);
        /// those, like border/padding/border-radius (genuine box-model properties CSS2.1 never allows
        /// on <c>::first-line</c> at all), always come from this box's own resolved style.
        /// </param>
        protected void PaintBackground(RGraphics g, RRect rect, bool isFirst, CssBox? firstLineStyle = null)
        {
            if (rect is { Width: > 0, Height: > 0 })
            {
                RRect BoxModelRect(string value) => value switch
                {
                    CssConstants.BorderBox => rect,
                    CssConstants.ContentBox => new RRect(
                        rect.X + ActualBorderLeftWidth + ActualPaddingLeft,
                        rect.Y + ActualBorderTopWidth  + ActualPaddingTop,
                        rect.Width  - ActualBorderLeftWidth - ActualBorderRightWidth  - ActualPaddingLeft - ActualPaddingRight,
                        rect.Height - ActualBorderTopWidth  - ActualBorderBottomWidth - ActualPaddingTop  - ActualPaddingBottom),
                    _ => new RRect(
                        rect.X + ActualBorderLeftWidth,
                        rect.Y + ActualBorderTopWidth,
                        rect.Width  - ActualBorderLeftWidth - ActualBorderRightWidth,
                        rect.Height - ActualBorderTopWidth  - ActualBorderBottomWidth),
                };

                // background-origin/background-clip are themselves comma-list (per-layer) properties,
                // just like background-image/-position/-size - resolved per layer inside the loop below,
                // not once for the whole box.
                var originLayers = BackgroundLayerResolver.SplitLayers(BackgroundOrigin);
                var clipLayers   = BackgroundLayerResolver.SplitLayers(BackgroundClip);
                var viewportRect = HtmlContainer!.PageBoxRect;

                var actualBackgroundColor = firstLineStyle?.ActualBackgroundColor ?? ActualBackgroundColor;
                RBrush? solidBrush = RenderUtils.IsColorVisible(actualBackgroundColor)
                    ? g.GetSolidBrush(actualBackgroundColor)
                    : null;

                // background-color is always the bottom-most layer (CSS Backgrounds 3 §3.6/§3.8) -
                // painted BEFORE any background-image/gradient layer so those layers appear on top of
                // it, not hidden beneath an opaque color.
                if (solidBrush != null)
                {
                    // background-color is not itself a layered property: when background-clip has
                    // multiple values, the solid fill uses the LAST (bottom-most) one, independent of
                    // how many background-image layers exist - including zero.
                    var colorClipRect = BoxModelRect(clipLayers[^1]);

                    RGraphicsPath? colorRoundedClipPath = null;
                    if (IsRounded)
                    {
                        var radii = ComputeRadii(colorClipRect);
                        colorRoundedClipPath = RenderUtils.GetRoundRect(g, colorClipRect,
                            radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                            radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                    }

                    PaintClippedBrush(g, solidBrush, colorClipRect, colorRoundedClipPath);
                    colorRoundedClipPath?.Dispose();
                }

                // Then paint image/gradient layers back-to-front (last in the comma-list = bottom-most
                // image layer, but still on top of the solid color) so the first-declared layer ends up
                // visually on top.
                var layersToPaint = BackgroundImages != null
                    ? Enumerable.Range(0, BackgroundImages.Count).Reverse()
                    : Enumerable.Empty<int>();

                foreach (int layerIndex in layersToPaint)
                {
                    var originRect = BoxModelRect(BackgroundLayerResolver.LayerAt(originLayers, layerIndex));
                    var clipRect   = BoxModelRect(BackgroundLayerResolver.LayerAt(clipLayers, layerIndex));

                    RGraphicsPath? roundedClipPath = null;
                    if (IsRounded)
                    {
                        var radii = ComputeRadii(clipRect);
                        roundedClipPath = RenderUtils.GetRoundRect(g, clipRect,
                            radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                            radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                    }

                    void DrawBrush(RBrush brush) => PaintClippedBrush(g, brush, clipRect, roundedClipPath);

                    CssImagePainter.Paint(g, BackgroundImages![layerIndex], layerIndex, isFirst, originRect, clipRect,
                        roundedClipPath, BackgroundPosition, BackgroundSize, BackgroundRepeat, BackgroundAttachment,
                        viewportRect, this, DrawBrush);

                    roundedClipPath?.Dispose();
                }
            }
        }

        /// <summary>
        /// Fills <paramref name="brush"/> clipped to <paramref name="roundedClipPath"/> (border-radius
        /// case) or <paramref name="clipRect"/> (rectangular case), and disposes the brush afterward.
        /// Shared by every per-layer background-image/gradient draw and the final solid-color fill.
        /// </summary>
        private void PaintClippedBrush(RGraphics g, RBrush brush, RRect clipRect, RGraphicsPath? roundedClipPath)
        {
            // TODO:a handle it correctly (tables background)
            object? prevMode = null;
            if (HtmlContainer is { AvoidGeometryAntialias: false } && IsRounded)
                prevMode = g.SetAntiAliasSmoothingMode();

            if (roundedClipPath != null)
                g.DrawPath(brush, roundedClipPath);
            else
                g.DrawRectangle(brush, clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height);

            g.ReturnPreviousSmoothingMode(prevMode);
            brush.Dispose();
        }

        /// <summary>
        /// Paints this box's <c>box-shadow</c> layers of one kind (outset or inset) for a single border-box
        /// fragment (CSS Backgrounds &amp; Borders Level 3 §5). PDF has no native blur, so a blurred shadow is
        /// approximated with vector geometry: a stack of concentric, overlapping rounded-rect fills each at a
        /// small constant alpha, so their source-over accumulation ramps the shadow color's alpha from zero at
        /// the outer blur edge to the full color where every layer overlaps (the interior). Because the layers
        /// overlap rather than abut, there are no partial-alpha shared edges to double-blend into seam lines,
        /// and the corners round off automatically over the blur radius. Layers paint last-listed first so the
        /// first-declared shadow ends up on top.
        /// </summary>
        private void PaintBoxShadows(RGraphics g, RRect borderBox, bool inset)
        {
            var layers = BoxShadowGrammar.TryParse(CssValueParser.GetCssTokens(BoxShadow));
            if (layers is null || layers.Count == 0) return;

            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer.Inset != inset) continue;

                var dx = CssValueParser.ParseLength(layer.OffsetX, 0, this);
                var dy = CssValueParser.ParseLength(layer.OffsetY, 0, this);
                var blur = CssValueParser.ParseLength(layer.Blur, 0, this);
                var spread = CssValueParser.ParseLength(layer.Spread, 0, this);
                var color = ResolveShadowColor(layer.Color);

                if (inset)
                    PaintInsetShadow(g, borderBox, dx, dy, blur, spread, color);
                else
                    PaintOutsetShadow(g, borderBox, dx, dy, blur, spread, color);
            }
        }

        /// <summary>Resolves a shadow layer's authored color string to an <see cref="RColor"/>; a null/omitted
        /// or <c>currentColor</c> value uses the element's own text color (CSS Backgrounds 3 §5).</summary>
        private RColor ResolveShadowColor(string? color) =>
            string.IsNullOrEmpty(color) || color.Equals(CssConstants.CurrentColor, StringComparison.OrdinalIgnoreCase)
                ? ActualColor
                : GetActualColor(color);

        private void PaintOutsetShadow(RGraphics g, RRect borderBox, double dx, double dy, double blur, double spread, RColor color)
        {
            // The shadow shape is the border box, translated by the offset and expanded by spread on all
            // sides (a negative spread shrinks it).
            var shadowRect = new RRect(
                borderBox.X + dx - spread,
                borderBox.Y + dy - spread,
                borderBox.Width + 2 * spread,
                borderBox.Height + 2 * spread);

            if (shadowRect.Width <= 0 || shadowRect.Height <= 0) return;

            // The shadow shape's own corner radii: the box's border radius grown by the spread (a sharp,
            // zero-radius corner stays sharp).
            var baseRadii = ShadowCornerRadii(borderBox, spread);

            if (blur <= 0)
            {
                var brush = g.GetSolidBrush(color);
                if (baseRadii.IsRounded)
                {
                    var path = BuildLayerRoundRect(g, shadowRect, baseRadii, 0);
                    g.DrawPath(brush, path);
                    path.Dispose();
                }
                else
                {
                    g.DrawRectangle(brush, shadowRect.X, shadowRect.Y, shadowRect.Width, shadowRect.Height);
                }
                brush.Dispose();
                return;
            }

            var steps = BlurSteps(blur);
            var layerColors = ComputeBlurLayerColors(color, steps);
            if (layerColors.Length == 0) return;

            // Draw outermost (largest) to innermost. A point at signed outward distance x is covered by
            // layers 0..K(x); painting them in this order accumulates to the target alpha ramp for that point.
            for (var k = 0; k < steps; k++)
            {
                var d = blur - 2 * blur * k / (steps - 1); // +blur (outer) .. -blur (inner)
                var rect = new RRect(shadowRect.X - d, shadowRect.Y - d, shadowRect.Width + 2 * d, shadowRect.Height + 2 * d);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var brush = g.GetSolidBrush(layerColors[k]);
                var path = BuildLayerRoundRect(g, rect, baseRadii, d);
                g.DrawPath(brush, path);
                path.Dispose();
                brush.Dispose();
            }
        }

        private void PaintInsetShadow(RGraphics g, RRect borderBox, double dx, double dy, double blur, double spread, RColor color)
        {
            var paddingBox = new RRect(
                borderBox.X + ActualBorderLeftWidth,
                borderBox.Y + ActualBorderTopWidth,
                borderBox.Width - ActualBorderLeftWidth - ActualBorderRightWidth,
                borderBox.Height - ActualBorderTopWidth - ActualBorderBottomWidth);

            if (paddingBox.Width <= 0 || paddingBox.Height <= 0) return;

            // The lit "hole" = the padding box, translated by the offset and shrunk by spread. The shadow is
            // the inverse (the region between the padding box and this inner shape), clipped to the padding box.
            var inner = new RRect(
                paddingBox.X + dx + spread,
                paddingBox.Y + dy + spread,
                Math.Max(0, paddingBox.Width - 2 * spread),
                Math.Max(0, paddingBox.Height - 2 * spread));

            RGraphicsPath? clipPath = null;
            if (IsRounded)
            {
                clipPath = BuildLayerRoundRect(g, paddingBox, ShadowCornerRadii(borderBox, spread: 0), 0);
                g.PushClip(clipPath);
            }
            else
            {
                g.PushClip(paddingBox);
            }

            if (blur <= 0)
            {
                // Solid ring = padding box minus the inner hole.
                FillRingRects(g, paddingBox, inner, color);
            }
            else
            {
                var steps = BlurSteps(blur);
                var layerColors = ComputeBlurLayerColors(color, steps);

                // Each layer is the padding box with a rectangular hole punched out (via an even-odd fill,
                // since the PDF backend has no clip-subtract primitive). The hole grows from
                // inner-deflated-by-blur (drawn first) to inner-inflated-by-blur, so a point out toward the
                // padding edge is covered by every layer (full color) and one deep in the hole by none
                // (transparent) - the inset falloff, fading inward over the blur radius. The already-pushed
                // padding-box clip trims each ring to the (possibly rounded) padding box.
                for (var k = 0; k < layerColors.Length; k++)
                {
                    var d = -blur + 2 * blur * k / (steps - 1);
                    var hole = new RRect(inner.X - d, inner.Y - d, inner.Width + 2 * d, inner.Height + 2 * d);
                    var brush = g.GetSolidBrush(layerColors[k]);
                    var ring = BuildRingPath(g, paddingBox, hole);
                    g.DrawPath(brush, ring);
                    ring.Dispose();
                    brush.Dispose();
                }
            }

            g.PopClip();
            clipPath?.Dispose();
        }

        /// <summary>The shadow shape's per-corner radii: the box's <c>border-radius</c> grown by
        /// <paramref name="spread"/> where non-zero, with sharp corners staying sharp.</summary>
        private BorderRadii ShadowCornerRadii(RRect borderBox, double spread)
        {
            var r = ComputeRadii(borderBox);
            double Adj(double v) => v > 0 ? Math.Max(0, v + spread) : 0;
            return new BorderRadii(Adj(r.TLX), Adj(r.TLY), Adj(r.TRX), Adj(r.TRY),
                                   Adj(r.BRX), Adj(r.BRY), Adj(r.BLX), Adj(r.BLY));
        }

        /// <summary>Number of concentric fills used to approximate a blur of the given radius (in points).</summary>
        private static int BlurSteps(double blur) => Math.Clamp((int)Math.Round(blur * 2), 6, 40);

        /// <summary>
        /// Per-layer colors for the <paramref name="steps"/> concentric blur fills (index 0 = outermost,
        /// faintest). The alpha of each layer is chosen so that the running source-over accumulation of
        /// layers 0..k reaches a linear target ramp - <c>alpha[k] = 1 - (1 - T_k)/(1 - T_{k-1})</c> with
        /// <c>T_k = Amax·(k+1)/steps</c> - so the interior reaches the shadow color's <b>full</b> alpha even
        /// when it is fully opaque (a constant per-layer alpha would collapse an opaque color to a hard edge).
        /// Empty (paints nothing) for a fully-transparent shadow color.
        /// </summary>
        private static RColor[] ComputeBlurLayerColors(RColor color, int steps)
        {
            var amax = color.A / 255.0;
            if (amax <= 0) return [];

            var colors = new RColor[steps];
            var prevTarget = 0.0;

            for (var k = 0; k < steps; k++)
            {
                var target = amax * (k + 1) / steps;
                var a = 1 - (1 - target) / (1 - prevTarget);
                prevTarget = target;

                var alpha = Math.Clamp((int)Math.Round(a * 255), 1, 255);
                colors[k] = RColor.FromArgb(alpha, color.R, color.G, color.B);
            }

            return colors;
        }

        /// <summary>
        /// Builds a rounded-rect path for one concentric shadow layer at signed outward distance
        /// <paramref name="d"/> from the shadow's sharp edge. Each corner's radius is the base (sharp-shape)
        /// radius plus <paramref name="d"/>, clamped non-negative - so outer layers of even a square box round
        /// off over the blur radius, matching how a real blurred shadow's corners soften.
        /// </summary>
        private static RGraphicsPath BuildLayerRoundRect(RGraphics g, RRect rect, BorderRadii baseRadii, double d)
        {
            double R(double b) => Math.Max(0, b + d);
            return RenderUtils.GetRoundRect(g, rect,
                R(baseRadii.TLX), R(baseRadii.TLY), R(baseRadii.TRX), R(baseRadii.TRY),
                R(baseRadii.BRX), R(baseRadii.BRY), R(baseRadii.BLX), R(baseRadii.BLY));
        }

        /// <summary>Fills the region of <paramref name="outer"/> that lies outside <paramref name="hole"/>
        /// with a solid color, as four axis-aligned rectangles (the hole is clamped to the outer bounds).</summary>
        private static void FillRingRects(RGraphics g, RRect outer, RRect hole, RColor color)
        {
            var hl = Math.Max(outer.Left, hole.Left);
            var ht = Math.Max(outer.Top, hole.Top);
            var hr = Math.Min(outer.Right, hole.Right);
            var hb = Math.Min(outer.Bottom, hole.Bottom);
            if (hr < hl) hr = hl;
            if (hb < ht) hb = ht;

            var brush = g.GetSolidBrush(color);
            FillRect(g, brush, outer.Left, outer.Top, outer.Width, ht - outer.Top);          // top
            FillRect(g, brush, outer.Left, hb, outer.Width, outer.Bottom - hb);              // bottom
            FillRect(g, brush, outer.Left, ht, hl - outer.Left, hb - ht);                    // left
            FillRect(g, brush, hr, ht, outer.Right - hr, hb - ht);                           // right
            brush.Dispose();
        }

        private static void FillRect(RGraphics g, RBrush brush, double x, double y, double width, double height)
        {
            if (width > 0 && height > 0)
                g.DrawRectangle(brush, x, y, width, height);
        }

        /// <summary>Builds an even-odd fill path of <paramref name="outer"/> with a rectangular
        /// <paramref name="hole"/> punched out - a filled ring. Used for inset-shadow falloff layers, since the
        /// PDF backend has no clip-subtract primitive.</summary>
        private static RGraphicsPath BuildRingPath(RGraphics g, RRect outer, RRect hole)
        {
            var path = g.GetGraphicsPath();

            path.Start(outer.Left, outer.Top);
            path.LineTo(outer.Right, outer.Top);
            path.LineTo(outer.Right, outer.Bottom);
            path.LineTo(outer.Left, outer.Bottom);
            path.CloseFigure();

            path.AddMove(hole.Left, hole.Top);
            path.LineTo(hole.Right, hole.Top);
            path.LineTo(hole.Right, hole.Bottom);
            path.LineTo(hole.Left, hole.Bottom);
            path.CloseFigure();

            path.FillMode = RFillMode.EvenOdd;
            return path;
        }

        /// <summary>
        /// Paint all the words in the box.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="offset">the current scroll offset to offset the words</param>
        private void PaintWords(RGraphics g, RPoint offset)
        {
            if (Width is null or { Length: <= 0 }) return;

            var isRtl = Direction == CssConstants.Rtl;

            foreach (var word in Words)
            {
                if (word.IsLineBreak || word.IsImage) continue;
                var clip = g.GetClip();
                var wordRect = word.Rectangle;
                wordRect.Offset(offset);
                clip.Intersect(wordRect);

                // A word whose box was relocated to the next page's content top (keep-with-next,
                // break-inside:avoid, orphans/widows) sits exactly flush against the previous page's
                // clip bottom - RRect.Intersect can land a hair off exact zero in either direction
                // (floating-point rounding across the several arithmetic steps a relocated box's Y goes
                // through), so neither RRect.Empty nor a strict zero check reliably catches it; the
                // epsilon does. Without this, a fully-clipped (invisible on screen, but present in the
                // content stream and text-extraction layer) duplicate of the word painted on the page it
                // just left. See GitHub issue #113.
                if (clip.Width <= VisibilityClipEpsilon || clip.Height <= VisibilityClipEpsilon) continue;

                // A word on the target's first formatted line, under a ::first-line rule, uses that
                // resolved shadow box's font/color/letter-spacing instead of this box's own - it was
                // already measured against this same styleSource (see ApplyFirstLineStyleOverride), so
                // word.Top/Height are already consistent with it.
                var styleSource = word.FirstLineStyle ?? this;

                // A fragment drawn with a different font than the box's own ActualFont - a synthesized
                // small-caps run (smaller size) or a per-codepoint fallback face (different metrics) - is
                // top-anchored at the same word.Top (the shared line box's top), so without correction its
                // baseline would sit at a different height than its full-size neighbors'. Shift down by the
                // ascent difference so every fragment's baseline lines up. This is exactly 0 for an ordinary
                // word (font == ActualFont), so it is a no-op there.
                var font = ResolveWordFont(word, styleSource);
                var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
                var wordPoint = new RPoint(word.Left + offset.X, word.Top + offset.Y + baselineAdjust);
                var text = word.FirstLineText ?? word.Text!;
                g.DrawString(text, font, styleSource.ActualColor, wordPoint, new RSize(word.Width, word.Height), isRtl, styleSource.ActualLetterSpacing, styleSource.ActualFontPalette);
            }
        }

        /// <summary>
        /// Paints the text decoration (underline/strike-through/over-line)
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rectangle"> </param>
        /// <param name="isFirst"> </param>
        /// <param name="isLast"> </param>
        /// <param name="firstLineStyle">
        /// When set, this rectangle is on the target's first formatted line under a <c>::first-line</c>
        /// rule - its resolved text-decoration/color/font (for underline-offset) are used instead of
        /// this box's own.
        /// </param>
        protected void PaintDecoration(RGraphics g, RRect rectangle, bool isFirst, bool isLast, CssBox? firstLineStyle = null)
        {
            // The `text-decoration` shorthand is expanded into these longhands by the CSS-OM (Layer A) before
            // it ever reaches the box, so the painter reads the longhands directly. text-decoration-line may
            // carry several space-separated line keywords (e.g. "underline overline"); each is drawn.
            var styleSource = firstLineStyle ?? this;
            var textDecorationLine = styleSource.TextDecorationLine;
            var textDecorationStyle = styleSource.TextDecorationStyle;
            var textDecorationColor = styleSource.TextDecorationColor;

            if (string.IsNullOrEmpty(textDecorationLine) || textDecorationLine == CssConstants.None)
                return;

            if (string.IsNullOrEmpty(textDecorationStyle))
            {
                textDecorationStyle = CssConstants.Solid;
            }

            if (!string.IsNullOrEmpty(textDecorationColor) && !HtmlContainer!.CssParser.IsColorValid(textDecorationColor))
            {
                textDecorationColor = string.Empty;
            }

            var textDecorationActualColor = string.IsNullOrEmpty(textDecorationColor) ? styleSource.ActualColor : HtmlContainer!.CssParser.ParseColor(textDecorationColor);

            double x1 = rectangle.X;
            if (isFirst)
                x1 += ActualPaddingLeft + ActualBorderLeftWidth;

            double x2 = rectangle.Right;
            if (isLast)
                x2 -= ActualPaddingRight + ActualBorderRightWidth;

            var dashStyle = textDecorationStyle switch
            {
                CssConstants.Solid => RDashStyle.Solid,
                CssConstants.Double => RDashStyle.Solid,
                CssConstants.Dotted => RDashStyle.Dot,
                CssConstants.Dashed => RDashStyle.Dash,
                CssConstants.Wavy => RDashStyle.Solid,
                _ => RDashStyle.Solid
            };

            var pen = g.GetPen(textDecorationActualColor);
            pen.Width = 1;
            pen.DashStyle = dashStyle;

            // text-decoration-line may list several keywords (e.g. "underline overline"); draw each.
            var bottomInset = ActualPaddingBottom - ActualBorderBottomWidth;
            foreach (var line in textDecorationLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double y = line switch
                {
                    CssConstants.Underline => Math.Round(rectangle.Top + styleSource.ActualFont.UnderlineOffset),
                    CssConstants.LineThrough => rectangle.Top + rectangle.Height / 2f,
                    CssConstants.Overline => rectangle.Top,
                    _ => double.NaN
                };

                if (double.IsNaN(y)) continue;

                y -= bottomInset;
                g.DrawLine(pen, x1, y, x2, y);
            }
        }

        /// <summary>
        /// Offsets the rectangle of the specified linebox by the specified gap,
        /// and goes deep for rectangles of children in that linebox.
        /// </summary>
        /// <param name="lineBox"></param>
        /// <param name="gap"></param>
        internal void OffsetRectangle(CssLineBox lineBox, double gap)
        {
            if (!Rectangles.TryGetValue(lineBox, out var r)) return;
            Rectangles[lineBox] = new RRect(r.X, r.Y + gap, r.Width, r.Height);
        }

        /// <summary>
        /// Resets the <see cref="Rectangles"/> array
        /// </summary>
        internal void RectanglesReset()
        {
            Rectangles.Clear();
        }

        protected override RFont? GetCachedFont(string fontFamily, double fsize, RFontStyle st, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return FontFamilyResolver.Resolve(HtmlContainer!.Adapter, fontFamily, fsize, st, weight, stretch, obliqueSkewSinus);
        }

        protected override RFont? GetCachedFontForCodepoint(string fontFamily, double fsize, RFontStyle st, System.Text.Rune codepoint, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return FontFamilyResolver.Resolve(HtmlContainer!.Adapter, fontFamily, fsize, st, codepoint, weight, stretch, obliqueSkewSinus);
        }

        protected override RColor GetActualColor(string colorStr)
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
            else if (Display == CssConstants.None)
            {
                return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} None";
            }
            else
            {
                return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} {Display}: {Text}";
            }
        }

        #endregion
    }
}