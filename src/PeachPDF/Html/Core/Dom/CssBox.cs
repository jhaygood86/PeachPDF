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
        /// Set by <see cref="CssLayoutEngineColumns"/>, painted by <see cref="FragmentPainter"/>.
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
        /// Shared by measurement and by <see cref="FragmentPainter"/>, so the two can never disagree
        /// about which face a word is drawn in.
        /// </summary>
        internal static RFont ResolveWordFont(CssRect word, CssBoxProperties styleSource)
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
        /// Where a forced break (css-break-3 §3.1) puts this box: the content top of the slot it lands
        /// in, already stepped past a slot on the wrong side for a directional value. Resolved by
        /// <see cref="PerformLayoutPrologue"/> and consumed on arrival by <c>PlaceBlockBox</c>.
        /// </summary>
        /// <remarks>
        /// The break used to be expressed by inflating the <i>previous sibling's</i> <c>ActualBottom</c>
        /// to the target instead. That setter alters <c>Size.Height</c>, so a predecessor with a
        /// background or border painted down to the page bottom — and for a directional break it would
        /// have painted straight across the blank page, which would also have made that slot look
        /// printable and so defeated the reservation. Naming the target on the box that actually takes
        /// the break keeps the predecessor's geometry its own.
        /// </remarks>
        private double? _forcedBreakTop;

        /// <summary>
        /// The side css-break-3 §3.1 requires the page after this box's forced break to fall on, or
        /// <see cref="PageSide.Any"/>. Resolved by <see cref="PerformLayoutPrologue"/> and acted on when
        /// the box is placed, once its preserved top margin is known.
        /// </summary>
        private PageSide _forcedBreakSide;

        /// <summary>
        /// Whether this box's position was set by a forced break (css-break-3 §3.1).
        /// </summary>
        /// <remarks>
        /// A forced break is a hard positional constraint, not a margin, so such a box anchors what
        /// follows it even when it is otherwise self-collapsing: <see cref="MarginTopCollapse"/>'s
        /// walk-back must stop here rather than resolving the next box against an earlier sibling and
        /// undoing the break. The canonical case is an empty <c>&lt;div class="page-break"&gt;</c>
        /// marker, which has nothing in it to collapse but everything to say about where the next
        /// section starts.
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
        internal void ResetForRefill() => _prologueDone = false;

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

            var resume = BeginLayoutPass();

            // Once per box per layout, never once per fragmentainer pass - see the method's own remarks
            // for what re-running it would destroy.
            if (!_prologueDone)
            {
                _prologueDone = true;
                await PerformLayoutPrologue(g);
            }

            // Bounded by _earlyBreakTaken: the epilogue may conclude, once, that this box has to start
            // somewhere else, and the only honest way to act on that is to lay it out again there.
            while (true)
            {
                await LayoutContents(g, resume);

                if (PendingBreakToken is not null || RequestedBreakBeforeTop is not null)
                {
                    // This box did not finish in this fragmentainer. Its epilogue judges a *complete*
                    // box, so it waits for the pass that completes it; the record unwinds from here.
                    PublishBreakToTheContextRoot();
                    return;
                }

                await PerformLayoutEpilogue(g);

                if (_earlyBreakRetryTop is not { } retryTop) return;

                _earlyBreakRetryTop = null;

                // The same one-shot channel a break-before uses, for the same reason: the target has
                // already been worked out and must not be re-derived here.
                _resumeTopOverride = retryTop;

                // A retry re-places this box; it does not continue where a previous fragmentainer left
                // off. The prologue deliberately does not run again — everything it settles is either
                // already consumed or overridden by the target above, and re-running it would register
                // this box's named strings and named page a second time.
                resume = null;
            }
        }

        /// <summary>
        /// Picks up this box's resumption state for the pass that is starting, discarding anything left
        /// over from an earlier layout.
        /// </summary>
        private BreakToken? BeginLayoutPass()
        {
            var generation = HtmlContainer?.LayoutGeneration ?? 0;

            if (_layoutGeneration != generation)
            {
                _layoutGeneration = generation;
                _prologueDone = false;
                _incomingToken = null;
                _resumeTopOverride = null;
            }

            var resume = _incomingToken;
            _incomingToken = null;
            PendingBreakToken = null;
            RequestedBreakBeforeTop = null;

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

            // The root itself has no parent to wrap a break-before request, so it stands in as one.
            context.RecordBreak(PendingBreakToken
                                ?? new BlockBreakToken(this, RequestedBreakBeforeSlot, 0, null,
                                    IsBreakBefore: true, RequestedBreakBeforeTop));
        }

        /// <summary>
        /// Records that the break falls before this box: it produces no fragment in the fragmentainer it
        /// is leaving, and resumes at <paramref name="top"/> in the next one
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §4.4</see>).
        /// </summary>
        private void RequestBreakBefore(double top)
        {
            RequestedBreakBeforeTop = top;
            RequestedBreakBeforeSlot = HtmlContainer!.PageIndexOf(top + HtmlContainerInt.PageBoundaryEpsilon);
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
            if (Display != CssConstants.None)
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
            var propagatesOutward = BreakPropagation.PropagatesBreakBeforeOutward(this);
            var ownForcedBefore = BreakPropagation.ForcedBreakBeforeAt(this);
            var forcedBefore = propagatesOutward ? null : ownForcedBefore;
            var forcedAfter = previousSiblingForBreak is null
                ? null
                : BreakPropagation.ForcedBreakAfterAt(previousSiblingForBreak);

            _isForcedBreak = forcedBefore is not null || forcedAfter is not null || pageNameChanged;

            // The value still governs this break point even where the container is what acts on it, so §5.2
            // leaves this box's margin alone either way. Without this, hoisting the break changed a stated
            // choice as a side effect: a box carrying a break that cannot be taken at all - because nothing
            // precedes the container in the flow - kept its margin before propagation and lost it after.
            _adjoinsForcedBreakPoint = _isForcedBreak || (propagatesOutward && ownForcedBefore is not null);
            // Every run of this prologue re-decides from scratch: the keep-with-next retry at the end of
            // PerformLayoutEpilogue clears _prologueDone and re-enters layout for this box at a new
            // position, where the break can legitimately land somewhere else. So both the target and
            // this box's blank-slot reservation are retracted here and only re-asserted below.
            _forcedBreakTop = null;
            _forcedBreakSide = PageSide.Any;
            PlacedByForcedBreak = false;
            HtmlContainer?.SetBlankSlotReservation(this, null);

            if (_isForcedBreak)
            {
                // The break falls between this box and whatever precedes it in the flow. For a
                // container's *first* in-flow child that is not a sibling of its own: §3.1's break point
                // before it is the same break point as the one before its container, so the predecessor
                // to resolve the target against is found by climbing the chain of containers this box
                // begins. A climb that reaches the root means nothing precedes this box in the flow at
                // all — there is nothing to break from, and taking a break anyway would manufacture a
                // blank page in front of the first content in the document.
                var breakAnchor = previousSiblingForBreak ?? PredecessorOfEnclosingFirstChildChain();

                if (breakAnchor is not null)
                {
                    // HtmlContainer.PageSize.Height is already margin-free (PdfGenerator.SetContent
                    // subtracts both page margins up front) - a page's real content band is the
                    // "shifted grid" [k·PageSize.Height + MarginTop, (k+1)·PageSize.Height + MarginTop),
                    // not raw multiples of PageSize.Height from document Y=0. PageIndexOf/PageTopOf are
                    // the single, unambiguous definition of that grid (matching what the painter's own
                    // per-page clip and the fragment builder's slot walk already use) - computing this via raw modulo
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
                    // StaticBottom, and the previous sibling's static top, throughout: a relative offset
                    // moves a box visually without affecting the layout of anything around it
                    // (CSS 2.1 §9.4.3), so it must not decide which slot the break lands in either.
                    var container = HtmlContainer!;
                    var prevBottom = breakAnchor.StaticBottom;
                    var prevTop = breakAnchor.Location.Y - breakAnchor.RelativeOffsetY;
                    var slot = container.PageIndexOf(prevBottom - HtmlContainerInt.PageBoundaryEpsilon) + 1;
                    if (prevTop >= container.PageTopOf(slot) - HtmlContainerInt.PageBoundaryEpsilon)
                    {
                        slot = container.PageIndexOf(prevTop + HtmlContainerInt.PageBoundaryEpsilon) + 1;
                    }

                    _forcedBreakTop = container.PageTopOf(slot);

                    // Which side the content after the break has to begin on (css-break-3 §3.1's
                    // left/right/recto/verso, which force one *or two* page breaks). Resolved here but
                    // acted on in PlaceBlockBox: only that knows this box's preserved top margin, which
                    // can itself carry the box past the slot the break landed in, and only boxes that
                    // reach it take the break at all - a display:none or out-of-flow box runs this
                    // prologue but is never placed, so reserving a blank page here would manufacture
                    // one for a break that is never taken.
                    // The side comes from the two values resolved at *this* break point above - this box's
                    // own break-before read through the chain it begins, and its immediate predecessor's
                    // break-after read through the chain that one ends. Never the climbed anchor's: a
                    // break-after states something about the break point after that box, and for a first
                    // child the anchor's break point is several levels out. RequiredSide already accepts a
                    // null second value, and resolves a conflict the way §3.1 does - to the value on the
                    // latest element in flow.
                    _forcedBreakSide = BreakValues.RequiredSide(forcedBefore, forcedAfter);
                }
            }
        }

        /// <summary>
        /// The nearest preceding in-flow box a forced break before this box falls after, looking out
        /// through any containers this box begins. Null when nothing precedes it in the flow.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>'s break point before a
        /// container's first in-flow child is the <i>same</i> break point as the one before the container
        /// — the requirement propagates outward through every box this one begins — so the predecessor to
        /// resolve the target against is found by climbing that chain rather than invented.
        /// </para>
        /// <para>
        /// Reaching the root without finding one means this box begins the flow, where a forced break has
        /// nothing to break from. Returning null there is what stops a <c>break-before</c> on the first
        /// element of a document — or on a heading whose <c>page</c> name merely starts the first named
        /// page — from manufacturing a blank page in front of it, which
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§4.4</see> asks user agents not
        /// to do.
        /// </para>
        /// <para>
        /// Only the target is resolved this way. The break is still taken by <i>this</i> box, so the
        /// containers it begins keep their own position and span the boundary; moving them too is
        /// §3.1 propagation proper, which is a separate question.
        /// </para>
        /// </remarks>
        private CssBox? PredecessorOfEnclosingFirstChildChain() =>
            DomUtils.PrecedingBoxAcrossFirstChildChain(this);

        /// <summary>
        /// Places this box and lays out its content — the part of layout a resumed pass re-enters,
        /// picking up where the previous fragmentainer stopped rather than starting over.
        /// </summary>
        /// <summary>
        /// Whether <see cref="LayoutContents"/> positions this box itself, via <c>PlaceBlockBox</c>.
        /// </summary>
        /// <remarks>
        /// Everything else falls into that method's else branch, which copies the <i>previous sibling's</i>
        /// <see cref="CssBoxProperties.Location"/> and <see cref="CssBoxProperties.ActualBottom"/> — a
        /// <c>display: none</c> box, a <c>table-row</c>, a bare inline. So any later code that measures this
        /// box's own height, or moves it, has to ask this first: for those boxes the coordinates belong to
        /// something else and both the measurement and the move are meaningless.
        /// </remarks>
        private bool PlacesItselfAsBlockBox =>
            IsBlock
            || Display is CssConstants.ListItem or CssConstants.Table or CssConstants.InlineTable
                       or CssConstants.TableCell or CssConstants.Flex or CssConstants.InlineFlex
                       or CssConstants.Grid or CssConstants.InlineGrid;

        private async ValueTask LayoutContents(RGraphics g, BreakToken? resume)
        {
            if (PlacesItselfAsBlockBox)
            {
                if (resume is null)
                {
                    await PlaceBlockBox(g);

                    // Placement decided the break falls before this box, so it contributes nothing to
                    // the fragmentainer being filled and its content waits for the next pass.
                    if (RequestedBreakBeforeTop is not null) return;
                }

                // The engines MonolithicContent.RunsAnEngineOfItsOwn names, in the same order; this branch
                // needs to know *which* one, which is why it cannot ask the combined predicate.
                if (Display is CssConstants.Flex or CssConstants.InlineFlex)
                {
                    await LayoutEngineContent(g, CssLayoutEngineFlex.PerformLayout);
                }
                else if (Display is CssConstants.Grid or CssConstants.InlineGrid)
                {
                    await LayoutEngineContent(g, CssLayoutEngineGrid.PerformLayout);
                }
                else if (Display is CssConstants.Table or CssConstants.InlineTable)
                {
                    await LayoutMonolithicContent(g, CssLayoutEngineTable.PerformLayout);
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
        /// Whether this box, with the decorations §6.2 makes each fragment re-open and close with, fits
        /// inside pagination slot <paramref name="slotIndex"/>'s content band.
        /// </summary>
        private bool FitsInFragmentainer(int slotIndex)
        {
            var container = HtmlContainer!;
            var (clonedStart, clonedEnd) = MonolithicContent.ClonedBlockInsets(this, container);

            return MonolithicContent.FitsInBand(
                ActualBottom - Location.Y, clonedStart, clonedEnd, container.PageBandHeightOf(slotIndex));
        }

        /// <summary>
        /// Runs a layout engine that paginates its own content, with breaking suppressed for the
        /// duration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The set of boxes this covers is
        /// <see cref="MonolithicContent.PaginatesItsOwnContent"/>: flex, grid, table and multi-column
        /// containers. Each lays its subtree out in one go, inside whichever fragmentainer it starts in,
        /// and keeps the pagination it already does for itself — the table engine's per-row breaks and
        /// repeated header/footer proxies, the columns engine's re-banding. Suppressing breaks here also
        /// stops a provisional placement made during those engines' measurement passes from being mistaken
        /// for a real break decision, which is the hazard <c>SuppressWordPageBreaks</c> was introduced for.
        /// </para>
        /// <para>
        /// <b>This is an implementation constraint, not
        /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see>'s own set</b> — which is
        /// <see cref="MonolithicContent.IsMonolithic"/>, and which these boxes need not be members of.
        /// The two used to be one undifferentiated notion of "monolithic"; see that type.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Runs a layout engine that positions its own children, leaving breaking live for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Flex and grid, as against <see cref="LayoutMonolithicContent"/>'s table. Their items are still
        /// <i>measured</i> with breaking suppressed — every measurement lays an item out at the
        /// container's content origin, and a break decided there names a position the item is about to be
        /// translated away from — but the engine itself needs to know whether breaking is live at all, so
        /// that the pass it runs once its items are finally placed can tell "this container is being
        /// paginated" from "this container is inside something that is measuring it".
        /// </para>
        /// <para>
        /// <see cref="LayoutOutOfFlowChildren"/> keeps a suppressed scope of its own, and that is not
        /// incidental: it <b>discards</b> any resumption record a child leaves behind, and it is the only
        /// way an absolutely-positioned child of one of these containers is laid out at all. Dropping a
        /// token there drops the content it names. It used to be safe because its caller suppressed;
        /// making that explicit is what keeps it safe now that the caller does not.
        /// </para>
        /// </remarks>
        private async ValueTask LayoutEngineContent(RGraphics g, Func<RGraphics, CssBox, ValueTask> engine)
        {
            await engine(g, this);

            var context = HtmlContainer?.CurrentFragmentainer;
            var previous = context?.EnterMonolithic() ?? false;

            try
            {
                await LayoutOutOfFlowChildren(g);
            }
            finally
            {
                context?.ExitMonolithic(previous);
            }
        }

        private async ValueTask LayoutMonolithicContent(
            RGraphics g, Func<RGraphics, CssBox, ValueTask> engine, bool layoutOutOfFlowChildren = true)
        {
            var context = HtmlContainer?.CurrentFragmentainer;
            var previous = context?.EnterMonolithic() ?? false;

            try
            {
                await engine(g, this);

                // The multi-column engine lays out every child itself, including out-of-flow ones; the
                // flex/table/grid engines deliberately place only in-flow items.
                if (layoutOutOfFlowChildren) await LayoutOutOfFlowChildren(g);
            }
            finally
            {
                context?.ExitMonolithic(previous);
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
        /// Whether <paramref name="token"/> says the box it names produced nothing at all in the
        /// fragmentainer being left — an inline flow that kept no line.
        /// </summary>
        /// <remarks>
        /// It is a column that makes this visible rather than a column that makes it true. On the page
        /// grid an empty box left at the foot of a page is easy to miss; a column is sized to its content,
        /// so the same box is a hole at the foot of one column with its text at the head of the next —
        /// the shape multi-column layout exists to avoid. The rule is §4.4's either way, so it is not
        /// gated on being in a column, and the full suite is unchanged by it.
        /// </remarks>
        private static bool KeptNothingInThisFragmentainer(BreakToken token) =>
            token is InlineBreakToken { CompletedLineCount: 0 };

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

            // One restart per run head per loop, so a run whose members keep reaching the same
            // conclusion cannot cycle.
            HashSet<int>? restartedHeads = null;

            _canRestartChildLoop = true;

            try
            {
                for (var i = start; i < Boxes.Count; i++)
                {
                    var childBox = Boxes[i];

                    // Only the child the previous pass stopped at resumes; everything after it is laid out
                    // from the start, having never been reached.
                    if (i == start && resumeAt is not null)
                    {
                        childBox.ResumeAt(resumeAt.ChildToken, resumeAt.ResumeTopOverride);
                    }

                    await childBox.PerformLayout(g);

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

                        // A child that kept nothing here has no fragment in this fragmentainer, so the
                        // break falls *before* it rather than inside it (§4.4). Left as a break inside,
                        // it leaves an empty box behind - which on the page grid is invisible, and in a
                        // column is a hole the next column's content cannot fill.
                        if (KeptNothingInThisFragmentainer(childToken) && i > start)
                        {
                            PendingBreakToken = new BlockBreakToken(
                                this, childToken.ResumeSlotIndex, i, null, IsBreakBefore: true, null);
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
                        && childBox.Display != CssConstants.None
                        && HtmlContainer?.CurrentFragmentainer is { HasOwnBand: true } columnBand
                        && childBox.ActualBottom > columnBand.BandBottom)
                    {
                        PendingBreakToken = new BlockBreakToken(
                            this, columnBand.SlotIndex, i, null, IsBreakBefore: true, null);
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
        /// Only the head is told where to go; every other member re-derives its position from the
        /// sibling above it, which has just been re-placed. Their break latches are cleared so a member
        /// can still take a decision of its own at its new position — except the box that raised this
        /// one, which keeps its latch and so follows the run rather than deciding again.
        /// </para>
        /// </remarks>
        private bool TryRestartAt(EarlyBreak restart, int start, int raisedAt, ref HashSet<int>? restartedHeads, out int resumeFrom)
        {
            resumeFrom = Boxes.IndexOf(restart.BeforeBox);
            if (resumeFrom < start || resumeFrom > raisedAt) return false;

            // Asked of every index about to be replayed, not just of the box that raised this. The run
            // is found by walking siblings, which skips display:none, floated and out-of-flow ones, so
            // the range re-run is wider than the run itself — and a table in it that repeats a header
            // would not survive being laid out a second time.
            for (var j = resumeFrom; j <= raisedAt; j++)
            {
                if (ContainsARepeatingTable(Boxes[j])) return false;
            }

            restartedHeads ??= [];

            if (!restartedHeads.Add(resumeFrom)) return false;

            for (var j = resumeFrom; j < raisedAt; j++)
            {
                Boxes[j]._earlyBreakTaken = false;
            }

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
        /// container the discovering box begins, and <see cref="OffsetTop"/> is deep, so moving the container
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
        /// Resolves this box's own inline size and position, and registers its used page name.
        /// </summary>
        /// <remarks>
        /// Runs on the pass that first places the box and never again: a resumed pass continues the
        /// box's <i>content</i>, and re-deriving its top from the previous sibling would now read the
        /// end of the whole flow. Skipping it is also what keeps a box that spans a fragmentainer
        /// boundary on one inline size across its fragments, per CSS Fragmentation Level 3 §2.
        /// </remarks>
        private async ValueTask PlaceBlockBox(RGraphics g)
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
                    if (_resumeTopOverride is { } resumedTop)
                    {
                        // The break before this box was taken on an earlier pass, which already worked
                        // out where it lands and already pulled any keep-with-next run along. This pass
                        // places it there. Re-deriving the decision instead would reach the same
                        // "does not fit" conclusion and break again, forever.
                        _resumeTopOverride = null;
                        top = resumedTop;
                    }
                    else if (_forcedBreakTop is { } forcedTop)
                    {
                        // The prologue worked out which slot the forced break lands in. Applied here,
                        // to this box, rather than by inflating the previous sibling's height to reach
                        // it: that predecessor's own geometry is not the break's to change.
                        //
                        // §5.2 truncates margins adjoining an *unforced* break only, so the margin on
                        // the new page's side of a forced break survives and opens that page. That is
                        // this box's own margin collapsed with its adjoining first-child chain - which
                        // is what MarginTopCollapse computes for a box with no previous sibling, and
                        // the break makes this box exactly that. The group computed against the real
                        // previous sibling is the wrong quantity: it also holds that sibling's
                        // margin-bottom, which belongs to the page being left.
                        _forcedBreakTop = null;
                        PlacedByForcedBreak = true;

                        var forcedBreakMargin = MarginTopCollapse(null);
                        top = forcedTop + forcedBreakMargin;

                        // §3.1's "one or two page breaks": the content after the break has to *begin*
                        // on a page of the requested side, so the side is checked against where this
                        // box actually lands - which the preserved margin above can carry past the slot
                        // the break itself reached. The slot stepped over becomes a deliberately-blank
                        // page.
                        //
                        // Gated on IsFragmenting because inside monolithic content (multicol's virtual
                        // single-column first pass, and the flex/grid/table engines) and during a
                        // measurement pass at a provisional position, this box's coordinates are not
                        // where it ends up - a reservation made from them would materialize a blank
                        // page nowhere near the real content. A directional break degrades to a plain
                        // page break there, the same engine-independence boundary the other break
                        // machinery already has.
                        // The margin travels with the box across the step, so it is preserved on
                        // whichever page the box ends up opening. Bounded rather than a plain "if"
                        // because a margin taller than a band can carry the box past the slot the step
                        // just chose; two rounds settle every case a single alternation can produce,
                        // and the small cap keeps a degenerate band from spinning.
                        // Also excluded while filling a column, and for the same reason one step further
                        // in: the fragmentainer this box is being placed into is a column, so where it
                        // ends up is a question about columns, not about which side of the sheet a page
                        // falls on. Reserving a page from here manufactured a blank one while the box
                        // itself simply moved to the next column - honouring half of a decision. Inside a
                        // multi-column container a forced page break degrades to a column break.
                        for (var guard = 0;
                             _forcedBreakSide is not PageSide.Any
                             && HtmlContainer!.IsFragmenting
                             && HtmlContainer.CurrentFragmentainer is not { HasOwnBand: true }
                             && guard < 4;
                             guard++)
                        {
                            var landing = HtmlContainer.PageIndexOf(top + HtmlContainerInt.PageBoundaryEpsilon);

                            if (BreakValues.SlotIsOn(landing, _forcedBreakSide))
                                break;

                            HtmlContainer.SetBlankSlotReservation(this, landing);
                            top = HtmlContainer.PageTopOf(landing + 1) + forcedBreakMargin;
                        }
                    }
                    // A previous sibling that a forced break placed and that contributes no height of
                    // its own - the empty "<div class='page-break'>" marker - puts the break
                    // immediately before this box, so this box's margin adjoins a *forced* break and
                    // §5.2 preserves it rather than truncating it. Without this the flush-boundary
                    // convention below reads the marker's position (exactly a slot top, so one epsilon
                    // earlier is the previous slot) as a boundary this box's margin crossed, and
                    // discards the margin.
                    //
                    // A *first* in-flow child has no previous sibling to resolve against, but the break
                    // point before it is a real one all the same: baseTop is already defined for it
                    // (the containing block's own content top, above) and the boundary test below reads
                    // nothing else from the sibling, so the arithmetic needs no predecessor. Only the
                    // root is excluded - it has nothing before it for a break to fall between, and a
                    // break-before published from the context root would have no parent link to travel
                    // up (see PublishBreakToTheContextRoot).
                    else if (!_adjoinsForcedBreakPoint && ParentBox is not null
                             && !(prevSibling is { PlacedByForcedBreak: true } marker && marker.IsMarginCollapseThrough()))
                    {
                        var pageHeight = HtmlContainer!.PageSize.Height;
                        if (pageHeight > 0)
                        {
                            // Same shifted grid the fragment builder/the forced-break logic above use
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

                                // The margin pushed this box out of the fragmentainer being filled, so
                                // the break falls *before* it: it produces no fragment here at all, and
                                // resumes at newTop in the next one (css-break-3 §4.4). Where breaking
                                // is not live - a measurement pass, or monolithic content - the box is
                                // simply placed at that target, exactly as it was before this became a
                                // break decision.
                                if (HtmlContainer.IsFragmenting)
                                {
                                    RequestBreakBefore(newTop);
                                    return;
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
            if (_shouldRegisterPage)
            {
                // Registration appends, and this box can be placed more than once inside one layout: a
                // break before it is taken on a later pass, or a column driver re-places it in the next
                // column. Withdraw what the previous placement registered rather than accumulating one
                // entry per position it has occupied - the same leak the prologue's own withdrawal
                // closes for the paths that do re-run it.
                if (RegisteredNamedPageElement is { } stale)
                {
                    HtmlContainer!.UnregisterNamedPageElement(stale);
                }

                RegisteredNamedPageElement = HtmlContainer!.RegisterNamedPageElement(UsedPageName, NamedPageRegistrationY());
            }
        }

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

                            // The retry re-runs this box from scratch, prologue included: it is a fresh
                            // layout of the same box at a new position, not a continuation, so its
                            // per-line rectangles must be reset and its words re-measured. (A resumed
                            // fragmentainer pass is the opposite case and deliberately keeps them.)
                            _prologueDone = false;

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

            // avoid / avoid-page, but not avoid-column or avoid-region: this mover is a page-context
            // mover by construction (it measures against PageBandHeightOf and relocates to PageTopOf),
            // so a hint naming a different fragmentation context must not suppress a page break.
            //
            // Monolithic content (css-break-3 §2 - a replaced element, a scroll container) reaches the same
            // mover, because "may not be broken" and "asks not to be broken" want the same relocation.
            var avoidsBreak = BreakValues.AvoidsPageBreak(BreakInside);
            var monolithic = IsMonolithicBoxThisMoverMayMove();

            // One correction per box per pass (_earlyBreakTaken). Where the box was laid out again
            // rather than moved, this epilogue is the relocated box's own, and it asks the same
            // question of the same geometry - an unsatisfiable `avoid` is relaxed rather than skipped
            // (§5.3), so without the latch the answer is "still does not fit" and the box walks down
            // the document one page per pass.
            if ((avoidsBreak || monolithic) && !_earlyBreakTaken)
            {
                // Shifted-grid convention (see HtmlContainer.PageIndexOf) - topRelativeToCurrentPage is
                // this box's distance from the start of its own page's real content band, not a raw
                // modulo of PageSize.Height (which ignored MarginTop and, for the last MarginTop-wide
                // sliver of every page, mis-detected which page a box's top actually belonged to).
                var currentPageIndex = HtmlContainer!.PageIndexOf(Location.Y);
                var topRelativeToCurrentPage = Location.Y - HtmlContainer.PageTopOf(currentPageIndex);

                var bottomRelativeToCurrentPage = topRelativeToCurrentPage + ActualBottom - Location.Y;

                // The two arms part company on a box that fits in no fragmentainer. An unsatisfiable
                // `avoid` is relaxed and the box still moves, maximizing what lands on one page (§4.3);
                // a monolithic box is left exactly where it is, because §2 would have it overflow and
                // overflowing discards every fragmentainer past the first - so PeachPDF keeps fragmenting
                // it instead (#350). The question is asked of the *destination* band, which per-page
                // @page margins can size differently from the current one and from PageSize.Height.
                if (bottomRelativeToCurrentPage > HtmlContainer.PageBandHeightOf(currentPageIndex)
                    && (avoidsBreak || FitsInFragmentainer(currentPageIndex + 1))
                    && TakeEarlyBreak(EarlyBreak.Discover(
                        this,
                        HtmlContainer.PageTopOf(currentPageIndex + 1),
                        // The two reasons share a mover but not a rationale, and §4.3 relaxation will
                        // need to tell "may not be broken" from "asks not to be broken" apart.
                        monolithic ? EarlyBreakReason.Monolithic : EarlyBreakReason.AvoidBreakInside)))
                {
                    // Being laid out again, at a position nothing below this point has seen yet.
                    return;
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
                && !_earlyBreakTaken
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

                        if (linesBefore > 0 && linesAfter > 0 && (linesBefore < orphans || linesAfter < widows)
                            && TakeEarlyBreak(EarlyBreak.Discover(this, boundaryY, EarlyBreakReason.OrphansWidows)))
                        {
                            return;
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
                // A box whose own position was set by a forced break anchors what follows it even when
                // it is self-collapsing: the break is a positional constraint rather than a margin, so
                // walking back past it would resolve this box against an earlier sibling and undo the
                // break outright. An empty "<div class='page-break'>" marker is exactly that box.
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
                    var earlierSibling = DomUtils.GetPreviousSibling(walker, false);
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
            return container.HasRealPageGrid
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
        /// since the target is delivered through <c>PlaceBlockBox</c> and any other box would simply
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
        /// <para>
        /// The last exclusion is narrower and is a defect rather than a boundary: laying a table out a
        /// second time does not reproduce the first result. Its repeating <c>&lt;thead&gt;</c> is
        /// detached from the tree and replaced by one proxy per page, and a second run neither finds
        /// the header nor removes the proxies — so the header stops repeating and stale copies are left
        /// behind, or, once the structure is restored, the header's height is counted twice. Until that
        /// is fixed a subtree containing one keeps the translation.
        /// </para>
        /// </remarks>
        private bool CanBeLaidOutAgain(EarlyBreak decision) =>
            PlacesItselfAsBlockBox
            && HtmlContainer is { IsFragmenting: true }
            && FitsInFragmentainer(decision.Slot)
            && !ContainsARepeatingTable(this);

        /// <summary>
        /// Whether <paramref name="box"/>'s subtree contains a table that repeats a header or footer
        /// group — the structure a second layout of the same table does not reproduce.
        /// </summary>
        /// <remarks>
        /// Both spellings have to be looked for, because the first layout consumes the first one: before
        /// it, the repeating group is an ordinary child of the table; after it, the group has been
        /// detached and only the per-page <see cref="CssProxyBox"/> proxies remain.
        /// </remarks>
        private static bool ContainsARepeatingTable(CssBox box)
        {
            if (box.Display is CssConstants.Table or CssConstants.InlineTable
                && box.Boxes.Exists(child =>
                    child is CssProxyBox
                    || child.Display is CssConstants.TableHeaderGroup or CssConstants.TableFooterGroup))
            {
                return true;
            }

            return box.Boxes.Exists(ContainsARepeatingTable);
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