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
    internal class CssBox : CssBoxProperties, IDisposable
    {
        #region Fields and Consts

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
        private string? _listItemMarkerText;
        private string? _listItemMarkerShape;
        private RPoint _listItemMarkerPosition;
        private RSize _listItemMarkerSize;
        internal double _pendingListItemMarkerReservedWidth;
        private const double ListItemMarkerGap = 5;
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

        public bool IsPseudoElement => IsBeforePseudoElement || IsAfterPseudoElement;

        /// <summary>
        /// is the box "Display" is "Inline", is this is an inline box and not block.
        /// </summary>
        public bool IsInline => Display is CssConstants.Inline or CssConstants.InlineBlock or CssConstants.InlineTable or CssConstants.InlineFlex;

        /// <summary>
        /// is the box "Display" is "Block", is this is a block box and not inline.
        /// </summary>
        public bool IsBlock => Display == CssConstants.Block;

        public bool IsFloated => Float is CssConstants.Left or CssConstants.Right;

        public bool IsOutOfFlow => IsFloated || Position is CssConstants.Absolute or CssConstants.Fixed;

        /// <summary>
        /// Is the css box clickable (by default only "a" element is clickable)
        /// </summary>
        public virtual bool IsClickable => HtmlTag is { Name: HtmlConstants.A } && !HtmlTag.HasAttribute("id") && !HtmlTag.HasAttribute("name");

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
        /// Tells if the box is empty or contains just blank spaces
        /// </summary>
        public bool IsSpaceOrEmpty
        {
            get
            {
                if ((Words.Count != 0 || Boxes.Count != 0) && (Words.Count != 1 || !Words[0].IsSpaces))
                {
                    foreach (CssRect word in Words)
                    {
                        if (!word.IsSpaces)
                        {
                            return false;
                        }
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
                    var rect = ContainingBlock.ClientRectangle;
                    rect.X -= 2;
                    rect.Width += 2;
                    if (!IsFixed)
                    {
                        //rect.Offset(new RPoint(-HtmlContainer.Location.X, -HtmlContainer.Location.Y));
                        rect.Offset(HtmlContainer!.ScrollOffset);
                    }
                    clip.Intersect(rect);

                    if (clip != RRect.Empty)
                        visible = true;
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

                    visible = clip != RRect.Empty;
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

                    if (IsOpaque)
                    {
                        await PaintImp(g);
                    }
                    else
                    {
                        await PaintWithOpacity(g);
                    }

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
                    while (endIdx < text.Length && char.IsWhiteSpace(text[endIdx]) && text[endIdx] != '\n')
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

                        endIdx = startIdx;
                        while (endIdx < text.Length && !char.IsWhiteSpace(text[endIdx]) && text[endIdx] != '-'
                               && WordBreak != CssConstants.BreakAll && !CommonUtils.IsAsianCharacter(text[endIdx]))
                            endIdx++;

                        if (endIdx < text.Length && (text[endIdx] == '-' || WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharacter(text[endIdx])))
                            endIdx++;

                        if (endIdx > startIdx)
                        {
                            var hasSpaceBefore = !preserveSpaces && (startIdx > 0 && Words.Count == 0 && char.IsWhiteSpace(text[startIdx - 1]));
                            var hasSpaceAfter = !preserveSpaces && (endIdx < text.Length && char.IsWhiteSpace(text[endIdx]));
                            var rawWord = text.Substring(startIdx, endIdx - startIdx);

                            List<int>? hyphenationCandidates = null;
                            string cleanWord;

                            if (honorSoftHyphen && rawWord.IndexOf('­') >= 0)
                            {
                                (cleanWord, hyphenationCandidates) = StripSoftHyphens(rawWord);
                            }
                            else
                            {
                                cleanWord = HtmlUtils.DecodeHtml(rawWord);

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

                            AddWord(cleanWord, hasSpaceBefore, hasSpaceAfter, hyphenationCandidates);
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
        private void AddWord(string text, bool hasSpaceBefore, bool hasSpaceAfter, List<int>? hyphenationCandidates = null)
        {
            if (FontVariant != CssConstants.SmallCaps || !ContainsLowerLetter(text))
            {
                Words.Add(new CssRectWord(this, text, hasSpaceBefore, hasSpaceAfter)
                {
                    HyphenationCandidates = hyphenationCandidates
                });
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

                Words.Add(new CssRectWord(
                    this,
                    isLower ? runText.ToUpperInvariant() : runText,
                    hasSpaceBefore: i == 0 && hasSpaceBefore,
                    hasSpaceAfter: i == runs.Count - 1 && hasSpaceAfter)
                {
                    FontSizeScale = isLower ? CssBoxProperties.SmallCapsFontScale : 1.0,
                    SuppressWrapBefore = i > 0
                });
            }
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


        #region Private Methods

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.<br/>
        /// </summary>
        /// <param name="g">Device context to use</param>
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

            // Spec (css-break §3.1): a forced break occurs at a class A break point if
            // the earlier sibling's break-after OR the later sibling's break-before has a
            // forced break value — at least one is sufficient.
            // Forced values include: page, always.
            var previousSiblingForBreak = DomUtils.GetPreviousSibling(this, false);
            if (IsForcedBreakValue(BreakBefore) || IsForcedBreakValue(previousSiblingForBreak?.BreakAfter))
            {
                if (previousSiblingForBreak is not null)
                {
                    var bottomRelativeToCurrentPage = previousSiblingForBreak.ActualBottom;
                    var pageHeight = HtmlContainer!.PageSize.Height;

                    while (bottomRelativeToCurrentPage > pageHeight)
                    {
                        bottomRelativeToCurrentPage -= pageHeight;
                    }

                    var pixelsToNextPage = pageHeight - bottomRelativeToCurrentPage;
                    previousSiblingForBreak.ActualBottom += pixelsToNextPage + HtmlContainer.MarginTop;
                }
            }

            // Must run before this box (or a descendant reached via the recursive layout below) builds
            // its first line box, so an "inside" marker's reserved width is already in place for it.
            MeasureListItemMarker(g);

            if (IsBlock || Display == CssConstants.ListItem || Display == CssConstants.Table || Display == CssConstants.InlineTable || Display == CssConstants.TableCell || Display == CssConstants.Flex || Display == CssConstants.InlineFlex)
            {
                // Because their width and height are set by CssTable or CssLayoutEngineFlex
                if (Display != CssConstants.TableCell && Display != CssConstants.Table && Display != CssConstants.Flex && Display != CssConstants.InlineFlex)
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
                        var top = (prevSibling == null ? ContainingBlock.ClientTop : ParentBox == null ? Location.Y : 0) + MarginTopCollapse(prevSibling) + (prevSibling != null ? prevSibling.ActualBottom + prevSibling.ActualBorderBottomWidth : 0);

                        Location = new RPoint(left + ActualMarginLeft, top);
                        ActualBottom = top;


                        CssLayoutEngine.FloatBox(this);
                    }

                    if (Position is CssConstants.Relative)
                    {
                        var left = Location.X + CssValueParser.ParseLength(Left, ActualWidth, this);
                        var top = Location.Y + CssValueParser.ParseLength(Top, ActualHeight, this);

                        Location = new RPoint(left, top);
                        ActualBottom = top;
                    }

                    if (Position is CssConstants.Absolute)
                    {
                        var nearestPositionedAncestor = DomUtils.GetNearestPositionedAncestor(this);

                        var left = nearestPositionedAncestor.Location.X +
                                   CssValueParser.ParseLength(Left, nearestPositionedAncestor.ActualWidth, this);

                        var top = nearestPositionedAncestor.Location.Y +
                                  CssValueParser.ParseLength(Top, nearestPositionedAncestor.ActualHeight, this);

                        Location = new RPoint(left, top);
                    }

                    if (Position is CssConstants.Fixed)
                    {
                        var left = CssValueParser.ParseLength(Left, HtmlContainer!.ScrollOffset.X, this);
                        var top = CssValueParser.ParseLength(Top, HtmlContainer!.ScrollOffset.Y, this);
                        Location = new RPoint(left, top);
                    }
                }

                if (Display is CssConstants.Flex or CssConstants.InlineFlex)
                {
                    await CssLayoutEngineFlex.PerformLayout(g, this);
                }
                else if (Display is CssConstants.Table or CssConstants.InlineTable)
                {
                    await CssLayoutEngineTable.PerformLayout(g, this);
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

            PositionListItemMarker();

            if (BreakInside is CssConstants.Avoid)
            {
                var pageHeight = HtmlContainer!.PageSize.Height;

                var topRelativeToCurrentPage = Location.Y;

                while (topRelativeToCurrentPage > pageHeight)
                {
                    topRelativeToCurrentPage -= pageHeight;
                }

                var bottomRelativeToCurrentPage = topRelativeToCurrentPage + ActualBottom - Location.Y;

                if (bottomRelativeToCurrentPage > pageHeight)
                {
                    var offset = pageHeight - topRelativeToCurrentPage + HtmlContainer.MarginTop;
                    OffsetTop(offset);
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

                if (owPageHeight > 0 && ActualBottom - Location.Y <= owPageHeight)
                {
                    var ownTopRelativeToPage = Location.Y;
                    while (ownTopRelativeToPage > owPageHeight)
                        ownTopRelativeToPage -= owPageHeight;

                    // Absolute Y of the first page boundary at or after this box's own top.
                    var boundaryY = Location.Y - ownTopRelativeToPage + owPageHeight;

                    if (boundaryY > Location.Y && boundaryY < ActualBottom)
                    {
                        var linesBefore = LineBoxes.Count(l => l.LineBottom <= boundaryY);
                        var linesAfter = LineBoxes.Count - linesBefore;

                        if (linesBefore > 0 && linesAfter > 0 && (linesBefore < orphans || linesAfter < widows))
                        {
                            var offset = boundaryY - Location.Y + HtmlContainer.MarginTop;
                            OffsetTop(offset);
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
            }

            // Register named page element if page property is set. Must run here, after every branch
            // above that can still move this box's own Location (Position: static/relative/absolute/
            // fixed, the BreakInside: avoid OffsetTop nudge, and the absolute right-edge OffsetLeft
            // adjustment) — registering earlier (e.g. at the top of this method) would capture
            // Location's default (0, 0), since it isn't assigned until those branches run. A *later*
            // reposition by an ancestor's layout engine after this box's own PerformLayoutImp has returned
            // (e.g. CssLayoutEngineColumns re-banding a column child via OffsetTop) is handled by retaining
            // the registered element on RegisteredNamedPageElement, which OffsetTop keeps in sync.
            if (!string.IsNullOrEmpty(PageName) && PageName != "auto")
            {
                RegisteredNamedPageElement = HtmlContainer?.RegisterNamedPageElement(PageName, Location.Y);
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
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g"></param>
        internal virtual async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (_wordsSizeMeasured) return;

            if (BackgroundImages is { Count: > 0 })
                foreach (var image in BackgroundImages)
                    await image.EnsureLoadedAsync(HtmlContainer!);

            if (ListStyleImage != null)
                await ListStyleImage.EnsureLoadedAsync(HtmlContainer!);

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

            if (Words.Count > 0)
            {
                foreach (var boxWord in Words)
                {
                    if (boxWord.IsImage) continue;
                    var font = boxWord.FontSizeScale == 1.0 ? ActualFont : ActualSmallCapsFont;
                    boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text!, font).Width : 0;
                    boxWord.Height = ActualFont.Height;
                }
            }

            _wordsSizeMeasured = true;
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
        /// Gets the index of the box to be used on a (ordered) list
        /// </summary>
        /// <returns></returns>
        private int GetIndexForList()
        {
            bool reversed = !string.IsNullOrEmpty(ParentBox!.GetAttribute("reversed"));
            if (!int.TryParse(ParentBox.GetAttribute("start"), out var index))
            {
                if (reversed)
                {
                    index = 0;
                    foreach (CssBox b in ParentBox.Boxes)
                    {
                        if (b.Display == CssConstants.ListItem)
                            index++;
                    }
                }
                else
                {
                    index = 1;
                }
            }

            foreach (CssBox b in ParentBox.Boxes)
            {
                if (b.Equals(this))
                    return index;

                if (b.Display == CssConstants.ListItem)
                    index += reversed ? -1 : 1;
            }

            return index;
        }

        /// <summary>
        /// Computes the marker's shape/text/size for a list-item box (independent of <c>Location</c>,
        /// so it's safe to call before line-box layout). When <c>ListStylePosition</c> is "inside",
        /// also pushes the reserved marker width onto whichever descendant box will host the first line.
        /// </summary>
        /// <param name="g"></param>
        private void MeasureListItemMarker(RGraphics g)
        {
            _listItemMarkerShape = null;
            _listItemMarkerText = null;

            if (Display != CssConstants.ListItem) return;

            if (ListStyleImage != null)
            {
                // Any CSS image (URL or gradient) is painted by PaintListStyleImageMarker, sized the
                // same square as the text/shape markers so line-1 reservation stays consistent.
                _listItemMarkerSize = new RSize(ActualFont.Height, ActualFont.Height);
            }
            else if (ListStyleType.Equals(CssConstants.Disc, StringComparison.InvariantCultureIgnoreCase) ||
                     ListStyleType.Equals(CssConstants.Circle, StringComparison.InvariantCultureIgnoreCase) ||
                     ListStyleType.Equals(CssConstants.Square, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemMarkerShape = ListStyleType.ToLowerInvariant();
                var shapeSize = ActualFont.Height * 0.35;
                _listItemMarkerSize = new RSize(shapeSize, shapeSize);
            }
            else if (ListStyleType != CssConstants.None)
            {
                if (ListStyleType.Equals(CssConstants.Decimal, StringComparison.InvariantCultureIgnoreCase))
                {
                    _listItemMarkerText = GetIndexForList().ToString(CultureInfo.InvariantCulture) + ".";
                }
                else if (ListStyleType.Equals(CssConstants.DecimalLeadingZero, StringComparison.InvariantCultureIgnoreCase))
                {
                    _listItemMarkerText = GetIndexForList().ToString("00", CultureInfo.InvariantCulture) + ".";
                }
                else
                {
                    _listItemMarkerText = CommonUtils.ConvertToAlphaNumber(GetIndexForList(), ListStyleType) + ".";
                }

                _listItemMarkerSize = g.MeasureString(_listItemMarkerText, ActualFont);
            }
            else
            {
                return; // list-style-type: none, no image - no marker at all
            }

            if (ListStylePosition == CssConstants.Inside)
            {
                var hostBox = FindListItemMarkerHostBox();
                if (hostBox != null)
                    hostBox._pendingListItemMarkerReservedWidth = _listItemMarkerSize.Width + ListItemMarkerGap;
            }
        }

        /// <summary>
        /// Walks down from this list-item box to the descendant that will actually build the first
        /// line box (skipping anonymous/block wrappers), so an "inside" marker can reserve room on
        /// that box's first line even when the &lt;li&gt; itself contains block-level children.
        /// </summary>
        private CssBox? FindListItemMarkerHostBox()
        {
            var box = this;
            while (!DomUtils.ContainsInlinesOnly(box))
            {
                var firstInFlowChild = box.Boxes.FirstOrDefault(b => !b.IsOutOfFlow);
                if (firstInFlowChild == null) return null;
                box = firstInFlowChild;
            }
            return box;
        }

        /// <summary>
        /// Computes the marker's paint position from its already-measured size and this box's
        /// <c>Location</c>. Must run after <c>Location</c> is assigned.
        /// </summary>
        private void PositionListItemMarker()
        {
            if (Display != CssConstants.ListItem) return;
            if (ListStyleImage == null && _listItemMarkerShape == null && _listItemMarkerText == null) return;

            double markerTop;
            if (_listItemMarkerShape != null)
            {
                // Text is drawn top-aligned at ActualPaddingTop; center the (much smaller) shape
                // within the font's line box instead of also top-aligning it, so it sits level
                // with the middle of the adjacent text rather than hugging the top of the line.
                markerTop = Location.Y + ActualPaddingTop + (ActualFont.Height - _listItemMarkerSize.Height) / 2;
            }
            else
            {
                markerTop = Location.Y + ActualPaddingTop;
            }

            var markerLeft = ListStylePosition == CssConstants.Inside
                ? ClientLeft
                : ClientLeft - _listItemMarkerSize.Width - ListItemMarkerGap;

            _listItemMarkerPosition = new RPoint(markerLeft, markerTop);
        }

        private void PaintListStyleShapeMarker(RGraphics g)
        {
            if (Display != CssConstants.ListItem || _listItemMarkerShape == null) return;

            var offset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
            var rect = new RRect(
                _listItemMarkerPosition.X + offset.X,
                _listItemMarkerPosition.Y + offset.Y,
                _listItemMarkerSize.Width,
                _listItemMarkerSize.Height);

            if (_listItemMarkerShape == CssConstants.Square)
            {
                using var brush = g.GetSolidBrush(ActualColor);
                g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                return;
            }

            var rx = rect.Width / 2;
            var ry = rect.Height / 2;
            using var path = RenderUtils.GetRoundRect(g, rect, rx, ry, rx, ry, rx, ry, rx, ry);

            if (_listItemMarkerShape == CssConstants.Disc)
            {
                using var brush = g.GetSolidBrush(ActualColor);
                g.DrawPath(brush, path);
            }
            else // circle: hollow ring
            {
                var pen = g.GetPen(ActualColor);
                g.DrawPath(pen, path);
            }
        }

        private void PaintListStyleTextMarker(RGraphics g)
        {
            if (Display != CssConstants.ListItem || string.IsNullOrEmpty(_listItemMarkerText)) return;

            var offset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
            var point = new RPoint(_listItemMarkerPosition.X + offset.X, _listItemMarkerPosition.Y + offset.Y);
            g.DrawString(_listItemMarkerText, ActualFont, ActualColor, point, _listItemMarkerSize, Direction == CssConstants.Rtl);
        }

        private void PaintListStyleImageMarker(RGraphics g)
        {
            if (Display != CssConstants.ListItem || ListStyleImage == null) return;
            var offset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
            var markerRect = new RRect(
                _listItemMarkerPosition.X + offset.X,
                _listItemMarkerPosition.Y + offset.Y,
                _listItemMarkerSize.Width,
                _listItemMarkerSize.Height);
            CssImagePainter.Paint(g, ListStyleImage, layerIndex: 0, isFirst: true,
                originRect: markerRect, clipRect: markerRect, roundedClipPath: null,
                positionList: "center", sizeList: CssConstants.Auto, repeatList: "no-repeat", box: this,
                drawBrush: brush =>
                {
                    g.DrawRectangle(brush, markerRect.X, markerRect.Y, markerRect.Width, markerRect.Height);
                    brush.Dispose();
                });
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
                    positionList: "0% 0%", sizeList: CssConstants.Auto, repeatList: "no-repeat", box: this,
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

            // not inline (block) boxes start a new line so we need to reset the max sum
            if (box.Display != CssConstants.Inline && box.Display != CssConstants.TableCell && box.WhiteSpace != CssConstants.NoWrap)
            {
                oldSum = maxSum;
                maxSum = marginSum;
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
                    GetMinMaxSumWords(childBox, ref min, ref maxSum, ref paddingSum, ref marginSum);

                    marginSum -= childBox.ActualMarginLeft + childBox.ActualMarginRight;
                }
            }

            // max sum is max of all the lines in the box
            if (oldSum.HasValue)
            {
                maxSum = Math.Max(maxSum, oldSum.Value);
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
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <param name="prevSibling">the previous box under the same parent</param>
        /// <returns>Resulting top margin</returns>
        protected double MarginTopCollapse(CssBox? prevSibling)
        {
            double value;
            if (prevSibling != null)
            {
                value = Math.Max(prevSibling.ActualMarginBottom, ActualMarginTop);
                CollapsedMarginTop = value;
            }
            else if (_parentBox != null && ActualPaddingTop < 0.1 && ActualPaddingBottom < 0.1 && _parentBox.ActualPaddingTop < 0.1 && _parentBox.ActualPaddingBottom < 0.1)
            {
                value = Math.Max(0, ActualMarginTop - Math.Max(_parentBox.ActualMarginTop, _parentBox.CollapsedMarginTop));
            }
            else
            {
                value = ActualMarginTop;
            }

            // fix for hr tag
            if (value < 0.1 && HtmlTag is { Name: "hr" })
            {
                value = GetEmHeight() * 1.1f;
            }

            return value;
        }

        public virtual bool BreakPage()
        {
            var container = HtmlContainer;

            if (Size.Height >= container!.PageSize.Height)
                return false;

            var remTop = (Location.Y - container.MarginTop) % container.PageSize.Height;
            var remBottom = (ActualBottom - container.MarginTop) % container.PageSize.Height;

            if (!(remTop > remBottom)) return false;

            var diff = container.PageSize.Height - remTop;
            Location = Location with { Y = Location.Y + diff + 1 };

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

                maxRight = Math.Max(maxRight, box.ActualRight + additionalMarginRight);
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
            if (ParentBox == null || ParentBox.Boxes.IndexOf(this) != ParentBox.Boxes.Count - 1 ||
                !(_parentBox!.ActualMarginBottom < 0.1))
                return Math.Max(ActualBottom,
                    lastNonFloatingBox.ActualBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);

            var lastChildBottomMargin = lastNonFloatingBox.ActualMarginBottom;
            margin = Height == "auto" ? Math.Max(ActualMarginBottom, lastChildBottomMargin) : lastChildBottomMargin;
            return Math.Max(ActualBottom, lastNonFloatingBox.ActualBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);
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
                RegisteredNamedPageElement.Y += amount;
            }

            foreach (var b in Boxes)
            {
                b.OffsetTop(amount);
            }

            _listItemMarkerPosition = _listItemMarkerPosition with { Y = _listItemMarkerPosition.Y + amount };

            Location = Location with { Y = Location.Y + amount };
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

            _listItemMarkerPosition = _listItemMarkerPosition with { X = _listItemMarkerPosition.X + amount };

            Location = Location with { X = Location.X + amount };
        }

        private bool _hasPainted;

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected virtual async ValueTask PaintImp(RGraphics g)
        {
            if (_hasPainted)
            {
                return;
            }

            if (Display == CssConstants.None ||
                (Display == CssConstants.TableCell && EmptyCells == CssConstants.Hide && IsSpaceOrEmpty)) return;

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            var areas = Rectangles.Count == 0 ? new List<RRect>([Bounds]) : new List<RRect>(Rectangles.Values);
            var clip = g.GetClip();
            var rects = areas.ToArray();
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

                PaintBackground(g, actualRect, i == 0);

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
                        var currentPageIndex = (int)(-offset.Y / pageHeight + 0.001);
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
                    PaintDecoration(g, actualRect, i == 0, i == rects.Length - 1);
                }
            }

            var stackingContextBoxes = DomUtils.FlattenStackingContext(this);

            foreach (var layerBoxes in DomUtils.GetBoxesByLayers(stackingContextBoxes))
            {
                // split paint to handle z-order
                foreach (var b in layerBoxes)
                {
                    if (b.Position != CssConstants.Absolute && b is { IsFixed: false, IsFloated: false })
                        await b.Paint(g);
                }

                foreach (var b in layerBoxes)
                {
                    if (b.IsFloated)
                        await b.Paint(g);
                }

                foreach (var b in layerBoxes)
                {
                    if (b.Position == CssConstants.Absolute)
                        await b.Paint(g);
                }

                foreach (var b in layerBoxes)
                {
                    if (b.IsFixed)
                        await b.Paint(g);
                }
            }

            if (clipped)
                g.PopClip();

            PaintListStyleShapeMarker(g);
            PaintListStyleTextMarker(g);
            PaintListStyleImageMarker(g);
            PaintContentImage(g);

            _hasPainted = true;
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

            return clip != RRect.Empty;
        }

        /// <summary>
        /// Paints the background of the box
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rect">the bounding rectangle to draw in</param>
        /// <param name="isFirst">is it the first rectangle of the element</param>
        protected void PaintBackground(RGraphics g, RRect rect, bool isFirst)
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

                RBrush? solidBrush = RenderUtils.IsColorVisible(ActualBackgroundColor)
                    ? g.GetSolidBrush(ActualBackgroundColor)
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
                        roundedClipPath, BackgroundPosition, BackgroundSize, BackgroundRepeat, this, DrawBrush);

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

                if (clip == RRect.Empty) continue;

                // A synthesized small-caps run is drawn with a smaller font than the rest of its line
                // (see ActualSmallCapsFont) — both are drawn top-anchored at the same word.Top (the
                // shared line box's top), so without correction the smaller glyphs' baseline would sit
                // higher than their full-size neighbors'. Shift down by the ascent difference so every
                // fragment's baseline lines up regardless of its font size.
                var font = word.FontSizeScale == 1.0 ? ActualFont : ActualSmallCapsFont;
                var baselineAdjust = word.FontSizeScale == 1.0 ? 0 : ActualFont.Ascent - font.Ascent;
                var wordPoint = new RPoint(word.Left + offset.X, word.Top + offset.Y + baselineAdjust);
                g.DrawString(word.Text!, font, ActualColor, wordPoint, new RSize(word.Width, word.Height), isRtl);
            }
        }

        /// <summary>
        /// Paints the text decoration (underline/strike-through/over-line)
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rectangle"> </param>
        /// <param name="isFirst"> </param>
        /// <param name="isLast"> </param>
        protected void PaintDecoration(RGraphics g, RRect rectangle, bool isFirst, bool isLast)
        {
            var textDecorationLine = TextDecorationLine;
            var textDecorationStyle = TextDecorationStyle;
            var textDecorationColor = TextDecorationColor;

            var textDecorationParts = TextDecoration?.Split(' ') ?? [];


            if (textDecorationParts.Length > 0)
            {
                HashSet<string> lineValues =
                    [
                    CssConstants.Underline, CssConstants.Overline, CssConstants.LineThrough, CssConstants.Blink];

                HashSet<string> styleValues =
                [
                    CssConstants.Solid, CssConstants.Double, CssConstants.Dotted, CssConstants.Dashed, CssConstants.Wavy
                ];

                foreach (var textDecorationPart in textDecorationParts)
                {
                    if (string.IsNullOrEmpty(textDecorationLine) && lineValues.Contains(textDecorationPart))
                    {
                        textDecorationLine = textDecorationPart;
                    }

                    if (string.IsNullOrEmpty(textDecorationStyle) && styleValues.Contains(textDecorationPart))
                    {
                        textDecorationStyle = textDecorationPart;
                    }

                    if (string.IsNullOrEmpty(textDecorationColor) && _htmlContainer!.CssParser.IsColorValid(textDecorationPart))
                    {
                        textDecorationColor = textDecorationPart;
                    }
                }
            }

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

            var textDecorationActualColor = string.IsNullOrEmpty(textDecorationColor) ? ActualColor : HtmlContainer!.CssParser.ParseColor(textDecorationColor);

            double y = textDecorationLine switch
            {
                CssConstants.Underline => Math.Round(rectangle.Top + ActualFont.UnderlineOffset),
                CssConstants.LineThrough => rectangle.Top + rectangle.Height / 2f,
                CssConstants.Overline => rectangle.Top,
                _ => 0f
            };

            y -= ActualPaddingBottom - ActualBorderBottomWidth;

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
            g.DrawLine(pen, x1, y, x2, y);
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

        protected override RFont? GetCachedFont(string fontFamily, double fsize, RFontStyle st)
        {
            return FontFamilyResolver.Resolve(HtmlContainer!.Adapter, fontFamily, fsize, st);
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