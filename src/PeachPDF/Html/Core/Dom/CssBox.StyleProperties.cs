using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using PeachPDF;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The cascaded (<see cref="ComputedStyle"/>) and calculated-from-cascade (<see cref="DerivedStyle"/>)
    /// halves of what used to be <c>CssBoxProperties</c>. Every property below keeps the exact name/type
    /// it always had, so the ~40 other files that read <c>box.Color</c>/<c>box.ActualFont</c>/etc. need no
    /// changes - only the storage moved. See <see cref="ComputedStyle"/> and <see cref="DerivedStyle"/> for
    /// what's cascaded vs. calculated, and why <see cref="SmallCapsFontScale"/>/<c>UsedPageName</c>/
    /// <c>SubgridContext</c> below are neither (a plain constant and two non-cascaded, layout-only fields
    /// respectively).
    /// <para>
    /// Every setter below follows the same two-level copy-on-write pattern: clone the small "area" record
    /// (<c>ComputedStyleAreas.cs</c>) the property lives in only if the property's own value actually
    /// changed, then swap that area onto <see cref="ComputedStyle"/> only if the area instance actually
    /// changed. A true no-op write (setting the value it already has) allocates nothing at either level.
    /// </para>
    /// </summary>
    internal partial class CssBox
    {
        private ComputedStyle _computedStyle = ComputedStyle.Default;

        /// <summary>The CSS properties cascaded onto this box. See <see cref="Dom.ComputedStyle"/>.</summary>
        internal ComputedStyle ComputedStyle => _computedStyle;

        private readonly DerivedStyle _derivedStyle;

        /// <summary>Values calculated from <see cref="ComputedStyle"/> for this box. See <see cref="Dom.DerivedStyle"/>.</summary>
        internal DerivedStyle DerivedStyle => _derivedStyle;

        /// <summary>
        /// Size ratio applied to an originally-lowercase run when synthesizing
        /// <c>font-variant: small-caps</c> (see <see cref="DerivedStyle.ActualSmallCapsFont"/>,
        /// <see cref="CssRect.FontSizeScale"/>). Not derived from any real OpenType metric (PeachPDF has no
        /// shaping engine to measure a font's actual <c>smcp</c> cap-height) - a representative
        /// approximation, tuned by eye.
        /// </summary>
        public const double SmallCapsFontScale = 0.72;

        #region Custom properties

        /// <summary>
        /// Specified (not var()-resolved) values of this box's CSS custom properties (--foo), keyed by
        /// their case-sensitive name. Null when no custom property has been declared or inherited.
        /// </summary>
        public Dictionary<string, string>? CustomProperties
        {
            get => _computedStyle.CustomProperties;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CustomProperties, value, static (s, v) => s with { CustomProperties = v });
        }

        #endregion

        #region Border widths, styles, colors, radii

        #endregion

        #region Transform, opacity, clip-path, aspect-ratio, box-shadow

        #endregion

        #region Counters, page, pdf-tag-type

        /// <summary>
        /// The <em>used</em> value of the CSS <c>page</c> property for this box (CSS Paged Media
        /// Level 3 §3): this box's own <see cref="PageName"/> unless it is empty/<c>auto</c>, in which
        /// case the parent box's used value (the root's <c>auto</c> resolves to <see cref="string.Empty"/>).
        /// Unlike <see cref="PageName"/> this propagates down the box tree, so a later sibling of a
        /// named-page element correctly reverts to its ancestors' used page rather than inheriting the
        /// named page in document flow order. Computed top-down in <c>CssBox.PerformLayoutImp</c>
        /// and recomputed every layout pass - not a cascaded property, so it is intentionally absent
        /// from <see cref="InheritStyle"/>.
        /// </summary>
        internal string UsedPageName { get; set; } = string.Empty;

        #endregion

        #region Margin, padding

        #endregion

        #region Break, box-decoration-break, orphans/widows

        // css-break-3 §6.2, honored at both the page breaks of a block box and the line-box breaks of an
        // inline one. Read by Html/Core/Paint/BoxDecorationGeometry, which owns the whole decision and
        // resolves it against the per-rectangle geometry the fragment tree carries
        // (Html/Core/Fragments/SliceGeometry) - deliberately not against BoxFragment's
        // IsFirstFragment/IsLastFragment, whose span includes descendants. Layout also reserves room for
        // cloned decorations, so a cloned border at a break pushes content rather than overlapping it.

        // Enforced in CssBox.PerformLayoutImp, for plain (non-multi-column) block flow only: a box whose
        // lines would straddle a page boundary with too few before/after it is pushed whole to the next
        // page. That is coarser than the spec, which pulls only the minimum lines across the break, and
        // it is skipped for a box taller than one page (pushing it whole would just recreate the same
        // violation). Inert inside multicol, whose whole-child fragmentation never splits a child in the
        // first place, so it cannot strand a line.

        #endregion

        #region Box position and size

        #endregion

        #region Background

        #endregion

        #region Color, content, display, direction, float, clear, position

        /// <summary>True for a positioned element: <c>position</c> of relative, absolute, fixed, or sticky.</summary>
        public bool IsPositioned => DerivedStyle.IsPositioned;

        #endregion

        #region Line-height, vertical-align, text-*

        /// <summary>Gets the line height. Recomputed fresh every call, not cached.</summary>
        public double ActualLineHeight => DerivedStyle.ActualLineHeight;

        /// <summary>Gets the text indentation of an indented line (see <see cref="ActualTextIndentHanging"/>/
        /// <see cref="ActualTextIndentEachLine"/> for which lines that is).</summary>
        public double ActualTextIndent => DerivedStyle.ActualTextIndent;

        /// <summary>Whether <c>text-indent</c>'s <c>hanging</c> keyword was specified - inverts which
        /// lines <see cref="ActualTextIndent"/> applies to (CSS Text 3 §3).</summary>
        public bool ActualTextIndentHanging => DerivedStyle.ActualTextIndentHanging;

        /// <summary>Whether <c>text-indent</c>'s <c>each-line</c> keyword was specified - also applies
        /// <see cref="ActualTextIndent"/> to the line after every forced break, not just the block's own
        /// first line (CSS Text 3 §3).</summary>
        public bool ActualTextIndentEachLine => DerivedStyle.ActualTextIndentEachLine;

        /// <summary>Gets the actual width of whitespace between words.</summary>
        public double ActualWordSpacing => DerivedStyle.ActualWordSpacing;

        /// <summary>Gets the actual extra space added between each pair of adjacent characters.</summary>
        public double ActualLetterSpacing => DerivedStyle.ActualLetterSpacing;

        /// <summary>Measures the width of whitespace between words (set <see cref="ActualWordSpacing"/>).</summary>
        protected void MeasureWordSpacing(RGraphics g) => DerivedStyle.MeasureWordSpacing(g);

        /// <summary>Measures the extra space added between each pair of adjacent characters (set <see cref="ActualLetterSpacing"/>).</summary>
        protected void MeasureLetterSpacing() => DerivedStyle.MeasureLetterSpacing();

        #endregion

        #region Font

        /// <summary>
        /// The full, unresolved <c>font-family</c> list as authored (e.g. <c>"Latin", "Symbols", serif</c>),
        /// as opposed to <see cref="FontFamily"/> which the cascade already collapses to the first family
        /// that exists. Retained so per-codepoint font matching can walk the whole stack. Inherited alongside <see cref="FontFamily"/>.
        /// </summary>
        public string? FontFamilyList
        {
            get => _computedStyle.Font.FontFamilyList;
            set
            {
                var area = _computedStyle.Font;
                var newArea = area.SetPropertyValue(area.FontFamilyList, value, static (a, v) => a with { FontFamilyList = v });
                _computedStyle = _computedStyle.AdoptArea(area, newArea, static (s, a) => s with { Font = a });
            }
        }

        #endregion

        #region Overflow, list-style

        #endregion

        #region Z-index

        #endregion

        #region Flex container/item

        #endregion

        #region Grid container/item

        /// <summary>Transient parent→child conduit set by <see cref="CssLayoutEngineGrid"/> immediately before
        /// it lays out a <c>subgrid</c> grid item, so the child adopts the parent's spanned tracks (CSS Grid
        /// Level 2 §9). Layout-only - not a cascaded property, cleared by the parent after the child lays out.</summary>
        internal GridSubgridContext? SubgridContext { get; set; }

        #endregion

        #region Multi-column

        /// <summary>
        /// Whether this box establishes a CSS multi-column formatting context, per spec: <c>column-width</c>
        /// is not <c>auto</c>, or <c>column-count</c> is not <c>auto</c>.
        /// </summary>
        public bool EstablishesMultiColumnContext => DerivedStyle.EstablishesMultiColumnContext;

        #endregion

        #region Actual* forwarding (calculated from ComputedStyle - see DerivedStyle)

        public double ActualBorderTopWidth => DerivedStyle.ActualBorderTopWidth;
        public double ActualBorderRightWidth => DerivedStyle.ActualBorderRightWidth;
        public double ActualBorderBottomWidth => DerivedStyle.ActualBorderBottomWidth;
        public double ActualBorderLeftWidth => DerivedStyle.ActualBorderLeftWidth;

        /// <summary>Gets the actual column-rule width (the line drawn between columns in a multi-column container).</summary>
        public double ActualColumnRuleWidth => DerivedStyle.ActualColumnRuleWidth;

        public RColor ActualBorderTopColor => DerivedStyle.ActualBorderTopColor;
        public RColor ActualBorderRightColor => DerivedStyle.ActualBorderRightColor;
        public RColor ActualBorderBottomColor => DerivedStyle.ActualBorderBottomColor;
        public RColor ActualBorderLeftColor => DerivedStyle.ActualBorderLeftColor;

        /// <summary>Gets the actual column-rule color (the line drawn between columns in a multi-column container).</summary>
        public RColor ActualColumnRuleColor => DerivedStyle.ActualColumnRuleColor;

        public double ActualBorderTopLeftRadiusX => DerivedStyle.ActualBorderTopLeftRadiusX;
        public double ActualBorderTopLeftRadiusY => DerivedStyle.ActualBorderTopLeftRadiusY;
        public double ActualBorderTopRightRadiusX => DerivedStyle.ActualBorderTopRightRadiusX;
        public double ActualBorderTopRightRadiusY => DerivedStyle.ActualBorderTopRightRadiusY;
        public double ActualBorderBottomRightRadiusX => DerivedStyle.ActualBorderBottomRightRadiusX;
        public double ActualBorderBottomRightRadiusY => DerivedStyle.ActualBorderBottomRightRadiusY;
        public double ActualBorderBottomLeftRadiusX => DerivedStyle.ActualBorderBottomLeftRadiusX;
        public double ActualBorderBottomLeftRadiusY => DerivedStyle.ActualBorderBottomLeftRadiusY;

        /// <summary>
        /// Computes overlap-reduced radii for the given rendering rectangle, per CSS spec §4.
        /// Horizontal and vertical axes are reduced independently.
        /// </summary>
        internal BorderRadii ComputeRadii(RRect rect) => DerivedStyle.ComputeRadii(rect);

        /// <summary>Gets a value indicating if at least one of the corners of the box is rounded.</summary>
        public bool IsRounded => DerivedStyle.IsRounded;

        public double ActualPaddingTop => DerivedStyle.ActualPaddingTop;
        public double ActualPaddingRight => DerivedStyle.ActualPaddingRight;
        public double ActualPaddingBottom => DerivedStyle.ActualPaddingBottom;
        public double ActualPaddingLeft => DerivedStyle.ActualPaddingLeft;

        /// <summary>Gets the actual horizontal border spacing for tables.</summary>
        public double ActualBorderSpacingHorizontal => DerivedStyle.ActualBorderSpacingHorizontal;

        /// <summary>Gets the actual vertical border spacing for tables.</summary>
        public double ActualBorderSpacingVertical => DerivedStyle.ActualBorderSpacingVertical;

        /// <summary>
        /// Lazily computes the combined 2D transform matrix for the <c>transform</c>/<c>transform-origin</c>
        /// properties, resolved against this box's own border-box size.
        /// </summary>
        public RMatrix ActualTransformMatrix => DerivedStyle.ActualTransformMatrix;

        /// <summary>True when this box has a non-identity CSS transform to apply at paint time.</summary>
        public bool IsTransformed => DerivedStyle.IsTransformed;

        /// <summary>Lazily computes the used value of the <c>opacity</c> property, clamped to [0, 1].</summary>
        public double ActualOpacity => DerivedStyle.ActualOpacity;

        /// <summary>True when this box's <c>opacity</c> is fully opaque.</summary>
        public bool IsOpaque => DerivedStyle.IsOpaque;

        /// <summary>Gets the actual color for the text.</summary>
        public RColor ActualColor => DerivedStyle.ActualColor;

        /// <summary>Gets the actual background color of the box.</summary>
        public RColor ActualBackgroundColor => DerivedStyle.ActualBackgroundColor;

        /// <summary>
        /// Gets the resolved <c>font-palette</c> selection for this box's used font (CSS Fonts 4), or null
        /// for the default palette.
        /// </summary>
        public RFontPalette? ActualFontPalette => DerivedStyle.ActualFontPalette;

        /// <summary>Gets the font that should be actually used to paint the text of the box.</summary>
        public RFont ActualFont => DerivedStyle.ActualFont;

        /// <summary>Gets the resolved GSUB ligature features (CSS <c>font-variant-ligatures</c>) for this box's text.</summary>
        public LigatureFeatures ActualFontVariantLigatures => DerivedStyle.ActualFontVariantLigatures;

        /// <summary>
        /// Gets the resolved CSS <c>font-variant-caps</c> feature - <see cref="FontVariantCapsFeature.None"/>
        /// when the value is <c>normal</c>, when it's a keyword the resolved font lacks full GSUB
        /// support for, or when <see cref="AddWord"/> is instead synthesizing the effect.
        /// </summary>
        public FontVariantCapsFeature ActualFontVariantCaps => DerivedStyle.ActualFontVariantCaps;

        /// <summary>Gets the resolved explicit OpenType feature tags (CSS <c>font-feature-settings</c>) for this box's text.</summary>
        public IReadOnlyList<(string Tag, int Value)> ActualFontFeatureSettings => DerivedStyle.ActualFontFeatureSettings;

        /// <summary>
        /// Gets the single combined GSUB feature request (ligatures + caps + numeric + east-asian +
        /// explicit <c>font-feature-settings</c> tags) for this box's text - the one value actually
        /// threaded into every measure/paint call.
        /// </summary>
        public TextShapingFeatures ActualTextShapingFeatures => DerivedStyle.ActualTextShapingFeatures;

        /// <summary>
        /// This box's own <see cref="FontWeight"/>, resolved to a concrete CSS Fonts numeric weight (1-1000).
        /// </summary>
        internal int ActualNumericWeight => DerivedStyle.ActualNumericWeight;

        /// <summary>
        /// This box's own <see cref="FontStretch"/> keyword, resolved to a concrete CSS Fonts numeric
        /// stretch (1-9, matching OS/2 <c>usWidthClass</c>).
        /// </summary>
        internal int ActualStretch => DerivedStyle.ActualStretch;

        /// <summary>
        /// This box's own <see cref="FontStyle"/>, resolved to a faux-italic skew factor when it's the CSS
        /// Fonts Level 4 <c>oblique &lt;angle&gt;</c> form - null otherwise.
        /// </summary>
        internal double? ActualObliqueSkewSinus => DerivedStyle.ActualObliqueSkewSinus;

        /// <summary>
        /// A cached font derived from <see cref="ActualFont"/> at a reduced size, used to synthesize
        /// <c>font-variant: small-caps</c>.
        /// </summary>
        public RFont ActualSmallCapsFont => DerivedStyle.ActualSmallCapsFont;

        /// <summary>
        /// The font this box uses for <paramref name="codepoint"/> specifically. See
        /// <see cref="DerivedStyle.ActualFontForCodepoint"/>.
        /// </summary>
        public RFont ActualFontForCodepoint(System.Text.Rune codepoint, double sizeScale = 1.0) =>
            DerivedStyle.ActualFontForCodepoint(codepoint, sizeScale);

        /// <summary>
        /// Gets the size of 1em in the specified units, per spec: an element's own computed font-size, not
        /// the font's line-spacing metric.
        /// </summary>
        public double GetEmHeight() => DerivedStyle.GetEmHeight();

        /// <summary>Gets the height of the root font in the specified units.</summary>
        public double GetRemHeight() => DerivedStyle.GetRemHeight();

        #endregion

        #region Non-cascaded helpers

        /// <summary>Ensures that the specified length is converted to pixels if necessary.</summary>
        protected string NoEms(string length)
        {
            var len = new CssLength(length);
            if (len.Unit == CssUnit.Ems)
            {
                // GetEmHeight() is the em size in layout units (points), and the result is later
                // re-parsed through ParseLength - so serialize as "pt" (identity), not "px" (which
                // now resolves at the spec-correct 0.75pt and would shrink the value on re-parse).
                length = len.ConvertEmToPoints(GetEmHeight()).ToString();
            }
            return length;
        }

        /// <summary>Like <see cref="NoEms"/>, but for <c>text-indent</c>'s compound
        /// <c>&lt;length-percentage&gt; &amp;&amp; hanging? &amp;&amp; each-line?</c> grammar - <see cref="CssLength"/>
        /// only understands a bare length, so it must be isolated from any trailing keyword before eager
        /// em-to-pt conversion can run, then the keywords reattached.</summary>
        private string NoEmsTextIndent(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var tokens = CssValueParser.GetCssTokens(value);
            if (!TextIndentGrammar.TryParse(tokens, out var length, out var hasHanging, out var hasEachLine))
                return value; // a global keyword (initial/inherit/...) or an already-invalid value - left untouched, as NoEms does

            List<string> parts = [NoEms(length.Text)];
            if (hasHanging) parts.Add(CssConstants.Hanging);
            if (hasEachLine) parts.Add(CssConstants.EachLine);

            return string.Join(' ', parts);
        }

        /// <summary>
        /// Set the style/width/color for all 4 borders on the box.<br/>
        /// if null is given for a value it will not be set.
        /// </summary>
        /// <param name="style">optional: the style to set</param>
        /// <param name="width">optional: the width to set</param>
        /// <param name="color">optional: the color to set</param>
        protected void SetAllBorders(string? style = null, string? width = null, string? color = null)
        {
            if (style != null)
                BorderLeftStyle = BorderTopStyle = BorderRightStyle = BorderBottomStyle =
                    CssProperty<LineStyle>.FromCssText(style, Map.LineStyles, LineStyle.None);
            if (width != null)
                BorderLeftWidth = BorderTopWidth = BorderRightWidth = BorderBottomWidth = width;
            if (color != null)
                BorderLeftColor = BorderTopColor = BorderRightColor = BorderBottomColor = color;
        }

        #endregion

        #region Layout-engine output (not cascaded, not derived - assigned directly by layout)

        private RPoint _location;

        /// <inheritdoc cref="Size"/>
        private RSize _size;

        /// <summary>Gets or sets the location of the box.</summary>
        public RPoint Location
        {
            get => _location;
            set
            {
                if (value.Y != _location.Y) OnBlockAxisRelocated(_location.Y, value.Y);

                // Unlike the block-axis notification above, this is not conditional on Y: a box moved
                // only in the inline axis still lands somewhere else, and a multi-column fragmentainer
                // decides membership on the inline axis too.
                if (value != _location) DiscardEmittedNothing();

                _location = value;
            }
        }

        /// <summary>Gets or sets the size of the box.</summary>
        /// <remarks>
        /// A written property rather than an auto-property because <see cref="ActualRight"/> and
        /// <see cref="ActualBottom"/> both write through it, and between them they are how nearly every
        /// layout engine states a box's extent — including the corrections engines apply <i>after</i> a
        /// box's own layout pass has finished (a flex/grid line relocation growing its container, the
        /// height epilogue, a table's row/row-group/cell aggregates). None of those routes through
        /// <see cref="Location"/> or <c>OffsetTop</c>, so without this a box could grow into a band the
        /// emitter had already observed it to be absent from, and nothing would say so.
        /// </remarks>
        public RSize Size
        {
            get => _size;
            set
            {
                // ActualBottom/ActualRight rewrite Size on every assignment, including the many that do
                // not change it, so the cheap comparison comes first - this sits on layout's hot path.
                if (value != _size) DiscardEmittedNothing();

                _size = value;
            }
        }

        /// <summary>Gets the bounds of the box.</summary>
        public RRect Bounds
        {
            get
            {
                var boundingBoxSize = new RSize(ActualBoxSizingWidth, ActualBoxSizingHeight);
                return new RRect(Location, boundingBoxSize);
            }
        }

        /// <summary>Gets the width available on the box, counting padding and margin.</summary>
        public double AvailableWidth => ActualBoxSizingWidth - ActualBorderLeftWidth - ActualPaddingLeft - ActualPaddingRight - ActualBorderRightWidth;

        public double ActualBoxSizeIncludedWidth
        {
            get
            {
                return BoxSizing.Value switch
                {
                    BoxSizingMode.ContentBox => ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth,
                    BoxSizingMode.BorderBox => 0,
                    _ => throw new HtmlRenderException("Unknown box sizing", HtmlRenderErrorType.Layout)
                };
            }
        }

        public double ActualBoxSizingWidth => Size.Width + ActualBoxSizeIncludedWidth;

        public double ActualBoxSizeIncludedHeight
        {
            get
            {
                return BoxSizing.Value switch
                {
                    BoxSizingMode.ContentBox => ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth,
                    BoxSizingMode.BorderBox => 0,
                    _ => throw new HtmlRenderException("Unknown box sizing", HtmlRenderErrorType.Layout)
                };
            }
        }

        public double ActualBoxSizingHeight => Size.Height + ActualBoxSizeIncludedHeight;

        /// <summary>Gets the right of the box. When setting, it will affect only the width of the box.</summary>
        public double ActualRight
        {
            get => Location.X + ActualBoxSizingWidth;
            set => Size = new RSize(value - ActualBoxSizeIncludedWidth - Location.X, Size.Height);
        }

        /// <summary>
        /// Gets or sets the bottom of the box.
        /// (When setting, alters only the Size.Height of the box)
        /// </summary>
        public double ActualBottom
        {
            get => Location.Y + ActualBoxSizingHeight;
            set => Size = new RSize(Size.Width, value - ActualBoxSizeIncludedHeight - Location.Y);
        }

        /// <summary>
        /// The box's own relative-positioning offsets (CSS 2.1 §9.4.3), recorded when Position is
        /// relative. A relative offset moves the box (and its descendants) visually without affecting
        /// the layout of anything around it, so in-flow consumers - a following sibling's static
        /// position, the parent's content-driven height/width - must back these out.
        /// </summary>
        internal double RelativeOffsetX { get; set; }

        /// <inheritdoc cref="RelativeOffsetX"/>
        internal double RelativeOffsetY { get; set; }

        /// <summary>
        /// The bottom edge of the box at its static (un-offset) position - what in-flow layout of
        /// following siblings and ancestors must measure against per CSS 2.1 §9.4.3;
        /// <see cref="ActualBottom"/> itself tracks the visually offset box.
        /// </summary>
        internal double StaticBottom => ActualBottom - RelativeOffsetY;

        /// <summary>
        /// The border-box top edge of the box at its static (un-offset) position — the mirror of
        /// <see cref="StaticBottom"/>, and the coordinate that decides which page's measure the box takes
        /// (<c>CssLayoutEngine.GetBoxWidth</c>), since a relative offset is visual only and must not move a
        /// box to another page's containing block any more than it moves its neighbours.
        /// </summary>
        internal double StaticTop => Location.Y - RelativeOffsetY;

        /// <summary>Gets the left of the client rectangle (Where content starts rendering).</summary>
        public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;

        /// <summary>Gets the top of the client rectangle (Where content starts rendering).</summary>
        public double ClientTop => Location.Y + ActualBorderTopWidth + ActualPaddingTop;

        /// <summary>Gets the right of the client rectangle.</summary>
        public double ClientRight => ActualRight - ActualPaddingRight - ActualBorderRightWidth;

        /// <summary>Gets the bottom of the client rectangle.</summary>
        public double ClientBottom => ActualBottom - ActualPaddingBottom - ActualBorderBottomWidth;

        /// <summary>Gets the client rectangle.</summary>
        public RRect ClientRectangle => RRect.FromLTRB(ClientLeft, ClientTop, ClientRight, ClientBottom);

        /// <summary>Gets the actual height.</summary>
        public double ActualHeight => ActualBoxSizingHeight;

        /// <summary>Gets the actual width.</summary>
        public double ActualWidth => ActualBoxSizingWidth;

        private double _collapsedMarginTop = double.NaN;

        /// <summary>The margin top value if was effected by margin collapse.</summary>
        public double CollapsedMarginTop
        {
            get => double.IsNaN(_collapsedMarginTop) ? 0 : _collapsedMarginTop;
            set => _collapsedMarginTop = value;
        }

        #endregion

        /// <summary>
        /// Inherits inheritable values from parent (or, when <paramref name="everything"/> is true, every
        /// value this method knows about - used only for a structural duplicate of the SAME source box,
        /// e.g. <see cref="CssProxyBox"/>'s repeated header/footer clone or an inline/block split, never
        /// real ancestor→descendant inheritance).
        /// </summary>
        /// <param name="box">Box to inherit the properties from; defaults to <see cref="ParentBox"/>.</param>
        /// <param name="everything">Set to true to inherit all CSS properties instead of only the inheritables.</param>
        internal void InheritStyle(CssBox? box = null, bool everything = false)
        {
            var p = box ?? ParentBox;
            if (p == null) return;

            var parentStyle = p.ComputedStyle;

            // Custom properties are inherited by default. Cloned (not shared) so a child's local override
            // never mutates the parent's or a sibling's dictionary. A property registered via @property with
            // `inherits: false` is the exception (CSS Properties & Values API §2.1): it does NOT inherit - the
            // child instead resolves it to its registered initial-value (via the var resolver), so it is
            // dropped from the inherited copy here.
            Dictionary<string, string>? customProperties;
            if (parentStyle.CustomProperties is { Count: > 0 } parentCustom)
            {
                var registered = p.HtmlContainer?.RegisteredProperties;
                if (registered is { Count: > 0 })
                {
                    Dictionary<string, string>? copy = null;
                    foreach (var (customName, customValue) in parentCustom)
                    {
                        if (registered.TryGetValue(customName, out var reg) && !reg.Inherits) continue;
                        (copy ??= new Dictionary<string, string>(parentCustom.Count))[customName] = customValue;
                    }
                    customProperties = copy;
                }
                else
                {
                    customProperties = new Dictionary<string, string>(parentCustom);
                }
            }
            else
            {
                customProperties = null;
            }

            // Every area below (Font/Text/Table/List/Pagination) is 100% inheritable - every property it
            // holds is one CssBox.InheritStyle used to copy individually here. Adopting the parent's area
            // instance directly by reference (instead of cloning this box's own area one property at a
            // time) is safe precisely because every area is copy-on-write: the parent's instance is never
            // mutated after being handed down, so sharing it costs nothing and a whole subtree that never
            // overrides anything in an area ends up with every box's copy of it ReferenceEquals the same
            // object. When this box's own area already equals the parent's (the common case), this is a
            // total no-op at both levels - no clone, no new reference even assigned.
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CustomProperties, customProperties, static (s, v) => s with { CustomProperties = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Font, parentStyle.Font, static (s, a) => s with { Font = a });
            var unicodeBidiBeforeInherit = _computedStyle.Text.UnicodeBidi;
            var verticalAlignBeforeInherit = _computedStyle.Text.VerticalAlign;
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Text, parentStyle.Text, static (s, a) => s with { Text = a });
            if (!everything)
            {
                // unicode-bidi and vertical-align are the two properties in this otherwise-100%-inherited
                // area that are CSS-spec Inherited: no - the whole-area adoption just above unconditionally
                // copied both from the parent anyway (that's the whole point of adopting the area by
                // reference), so put this box's own pre-inherit values (already defaulted to initial, or set
                // by an earlier cascade phase) back. The `everything: true` path deliberately skips this and
                // keeps the adopted values, since that path is a structural duplicate of the same source
                // element (CssProxyBox's repeated header/footer, DomParser's inline/block split) which needs
                // the source's own resolved value even though it isn't a real ancestor-descendant inheritance
                // case - unlike box-sizing's analogous everything-branch exception below, no separate explicit
                // copy is needed here for `everything: true`, since skipping this restore already leaves both
                // properties equal to the whole-adopted parentStyle.Text for the rest of the method.
                // Routed through SetPropertyValue (not a bare `with`, which always allocates) so the common
                // case - this box's own value already matches what the parent just handed down - stays a
                // total no-op, preserving the "whole unchanged subtree shares one Text instance" guarantee.
                var textArea = _computedStyle.Text;
                var restoredTextArea = textArea
                    .SetPropertyValue(textArea.UnicodeBidi, unicodeBidiBeforeInherit, static (a, v) => a with { UnicodeBidi = v })
                    .SetPropertyValue(textArea.VerticalAlign, verticalAlignBeforeInherit, static (a, v) => a with { VerticalAlign = v });
                _computedStyle = _computedStyle.AdoptArea(textArea, restoredTextArea, static (s, a) => s with { Text = a });
            }
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Table, parentStyle.Table, static (s, a) => s with { Table = a });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.List, parentStyle.List, static (s, a) => s with { List = a });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Pagination, parentStyle.Pagination, static (s, a) => s with { Pagination = a });

            // The invalidations these bypass (border/padding/opacity/transform/color/font-palette caches)
            // are intentionally skipped here - every value copied above either has no such cache, or (for
            // Color, FontPalette) is safe to leave stale since a fresh box's DerivedStyle cache starts
            // empty and this method only ever runs before anything else has read from it.
            if (!everything) return;

            // Unlike the "always" section above, none of these areas are adopted whole-by-reference: each
            // is a mix of properties this list covers and properties it doesn't (e.g. Background is
            // missing BackgroundSize, Border is missing BoxShadow - pre-existing, deliberately preserved
            // gaps in this exact list, confirmed by the previous refactor's review), so adopting a whole
            // area here would silently copy properties "everything" has never covered. Each property is
            // still routed through its owning area's own two-level copy-on-write, just individually.
            var background = _computedStyle.Background;
            background = background
                .SetPropertyValue(background.BackgroundColor, parentStyle.Background.BackgroundColor, static (a, v) => a with { BackgroundColor = v })
                .SetPropertyValue(background.BackgroundImages, parentStyle.Background.BackgroundImages, static (a, v) => a with { BackgroundImages = v })
                .SetPropertyValue(background.BackgroundPosition, parentStyle.Background.BackgroundPosition, static (a, v) => a with { BackgroundPosition = v })
                .SetPropertyValue(background.BackgroundRepeat, parentStyle.Background.BackgroundRepeat, static (a, v) => a with { BackgroundRepeat = v })
                .SetPropertyValue(background.BackgroundOrigin, parentStyle.Background.BackgroundOrigin, static (a, v) => a with { BackgroundOrigin = v })
                .SetPropertyValue(background.BackgroundClip, parentStyle.Background.BackgroundClip, static (a, v) => a with { BackgroundClip = v })
                .SetPropertyValue(background.BackgroundAttachment, parentStyle.Background.BackgroundAttachment, static (a, v) => a with { BackgroundAttachment = v })
                .SetPropertyValue(background.ObjectFit, parentStyle.Background.ObjectFit, static (a, v) => a with { ObjectFit = v })
                .SetPropertyValue(background.ObjectPosition, parentStyle.Background.ObjectPosition, static (a, v) => a with { ObjectPosition = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Background, background, static (s, a) => s with { Background = a });

            var border = _computedStyle.Border;
            border = border
                .SetPropertyValue(border.BorderTopWidth, parentStyle.Border.BorderTopWidth, static (a, v) => a with { BorderTopWidth = v })
                .SetPropertyValue(border.BorderRightWidth, parentStyle.Border.BorderRightWidth, static (a, v) => a with { BorderRightWidth = v })
                .SetPropertyValue(border.BorderBottomWidth, parentStyle.Border.BorderBottomWidth, static (a, v) => a with { BorderBottomWidth = v })
                .SetPropertyValue(border.BorderLeftWidth, parentStyle.Border.BorderLeftWidth, static (a, v) => a with { BorderLeftWidth = v })
                .SetPropertyValue(border.BorderTopColor, parentStyle.Border.BorderTopColor, static (a, v) => a with { BorderTopColor = v })
                .SetPropertyValue(border.BorderRightColor, parentStyle.Border.BorderRightColor, static (a, v) => a with { BorderRightColor = v })
                .SetPropertyValue(border.BorderBottomColor, parentStyle.Border.BorderBottomColor, static (a, v) => a with { BorderBottomColor = v })
                .SetPropertyValue(border.BorderLeftColor, parentStyle.Border.BorderLeftColor, static (a, v) => a with { BorderLeftColor = v })
                .SetPropertyValue(border.BorderTopStyle, parentStyle.Border.BorderTopStyle, static (a, v) => a with { BorderTopStyle = v })
                .SetPropertyValue(border.BorderRightStyle, parentStyle.Border.BorderRightStyle, static (a, v) => a with { BorderRightStyle = v })
                .SetPropertyValue(border.BorderBottomStyle, parentStyle.Border.BorderBottomStyle, static (a, v) => a with { BorderBottomStyle = v })
                .SetPropertyValue(border.BorderLeftStyle, parentStyle.Border.BorderLeftStyle, static (a, v) => a with { BorderLeftStyle = v })
                .SetPropertyValue(border.BorderTopLeftRadius, parentStyle.Border.BorderTopLeftRadius, static (a, v) => a with { BorderTopLeftRadius = v })
                .SetPropertyValue(border.BorderTopRightRadius, parentStyle.Border.BorderTopRightRadius, static (a, v) => a with { BorderTopRightRadius = v })
                .SetPropertyValue(border.BorderBottomRightRadius, parentStyle.Border.BorderBottomRightRadius, static (a, v) => a with { BorderBottomRightRadius = v })
                .SetPropertyValue(border.BorderBottomLeftRadius, parentStyle.Border.BorderBottomLeftRadius, static (a, v) => a with { BorderBottomLeftRadius = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Border, border, static (s, a) => s with { Border = a });

            var visualEffects = _computedStyle.VisualEffects;
            visualEffects = visualEffects
                .SetPropertyValue(visualEffects.Transform, parentStyle.VisualEffects.Transform, static (a, v) => a with { Transform = v })
                .SetPropertyValue(visualEffects.TransformOrigin, parentStyle.VisualEffects.TransformOrigin, static (a, v) => a with { TransformOrigin = v })
                .SetPropertyValue(visualEffects.Opacity, parentStyle.VisualEffects.Opacity, static (a, v) => a with { Opacity = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.VisualEffects, visualEffects, static (s, a) => s with { VisualEffects = a });

            var displayPositioning = _computedStyle.DisplayPositioning;
            displayPositioning = displayPositioning
                .SetPropertyValue(displayPositioning.Display, parentStyle.DisplayPositioning.Display, static (a, v) => a with { Display = v })
                .SetPropertyValue(displayPositioning.Float, parentStyle.DisplayPositioning.Float, static (a, v) => a with { Float = v })
                .SetPropertyValue(displayPositioning.Overflow, parentStyle.DisplayPositioning.Overflow, static (a, v) => a with { Overflow = v })
                .SetPropertyValue(displayPositioning.Position, parentStyle.DisplayPositioning.Position, static (a, v) => a with { Position = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.DisplayPositioning, displayPositioning, static (s, a) => s with { DisplayPositioning = a });

            var boxModel = _computedStyle.BoxModel;
            boxModel = boxModel
                .SetPropertyValue(boxModel.Height, parentStyle.BoxModel.Height, static (a, v) => a with { Height = v })
                .SetPropertyValue(boxModel.MaxHeight, parentStyle.BoxModel.MaxHeight, static (a, v) => a with { MaxHeight = v })
                .SetPropertyValue(boxModel.MarginBottom, parentStyle.BoxModel.MarginBottom, static (a, v) => a with { MarginBottom = v })
                .SetPropertyValue(boxModel.MarginLeft, parentStyle.BoxModel.MarginLeft, static (a, v) => a with { MarginLeft = v })
                .SetPropertyValue(boxModel.MarginRight, parentStyle.BoxModel.MarginRight, static (a, v) => a with { MarginRight = v })
                .SetPropertyValue(boxModel.MarginTop, parentStyle.BoxModel.MarginTop, static (a, v) => a with { MarginTop = v })
                .SetPropertyValue(boxModel.Left, parentStyle.BoxModel.Left, static (a, v) => a with { Left = v })
                .SetPropertyValue(boxModel.PaddingLeft, parentStyle.BoxModel.PaddingLeft, static (a, v) => a with { PaddingLeft = v })
                .SetPropertyValue(boxModel.PaddingBottom, parentStyle.BoxModel.PaddingBottom, static (a, v) => a with { PaddingBottom = v })
                .SetPropertyValue(boxModel.PaddingRight, parentStyle.BoxModel.PaddingRight, static (a, v) => a with { PaddingRight = v })
                .SetPropertyValue(boxModel.PaddingTop, parentStyle.BoxModel.PaddingTop, static (a, v) => a with { PaddingTop = v })
                .SetPropertyValue(boxModel.Top, parentStyle.BoxModel.Top, static (a, v) => a with { Top = v })
                .SetPropertyValue(boxModel.Width, parentStyle.BoxModel.Width, static (a, v) => a with { Width = v })
                .SetPropertyValue(boxModel.MaxWidth, parentStyle.BoxModel.MaxWidth, static (a, v) => a with { MaxWidth = v })
                .SetPropertyValue(boxModel.MinWidth, parentStyle.BoxModel.MinWidth, static (a, v) => a with { MinWidth = v })
                // box-sizing moved out of the "always" section (it no longer inherits, per the
                // spec-compliance fix above) - but same reasoning as BoxDecorationBreak/the break
                // properties/PdfTagType below: a structural duplicate is a fragment of the SAME
                // element, so its own resolved box-sizing must still carry over, or CssProxyBox's
                // repeated header/footer and DomParser's inline/block split would silently revert to
                // content-box regardless of what the source element actually declared.
                .SetPropertyValue(boxModel.BoxSizing, parentStyle.BoxModel.BoxSizing, static (a, v) => a with { BoxSizing = v });
            // Bottom/Right: the pre-split CssBoxProperties had a private _bottom/_right field pair that
            // this branch copied, but Bottom/Right's real getters/setters never read or wrote those fields
            // (they were independent auto-properties) - so a structural duplicate's Bottom/Right silently
            // never inherited the source box's value, always keeping the CSS initial "auto", even though
            // CSS 2.1 §9.4.3 says a relatively/absolutely positioned box's offsets should apply here too
            // (a structural duplicate is a fragment of the same source box, same as BoxDecorationBreak/
            // PdfTagType/the break properties below). Unifying storage onto ComputedStyle.BoxModel.Bottom/.Right
            // fixes this for real - see ComputedStyleTests.InheritStyle_Everything_CopiesBottomAndRight.
            boxModel = boxModel
                .SetPropertyValue(boxModel.Bottom, parentStyle.BoxModel.Bottom, static (a, v) => a with { Bottom = v })
                .SetPropertyValue(boxModel.Right, parentStyle.BoxModel.Right, static (a, v) => a with { Right = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.BoxModel, boxModel, static (s, a) => s with { BoxModel = a });

            var textDecoration = _computedStyle.TextDecoration;
            textDecoration = textDecoration
                .SetPropertyValue(textDecoration.TextDecorationLine, parentStyle.TextDecoration.TextDecorationLine, static (a, v) => a with { TextDecorationLine = v })
                .SetPropertyValue(textDecoration.TextDecorationStyle, parentStyle.TextDecoration.TextDecorationStyle, static (a, v) => a with { TextDecorationStyle = v })
                .SetPropertyValue(textDecoration.TextDecorationColor, parentStyle.TextDecoration.TextDecorationColor, static (a, v) => a with { TextDecorationColor = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.TextDecoration, textDecoration, static (s, a) => s with { TextDecoration = a });

            // Same reasoning as PdfTagType below: not inherited, but a structural duplicate of the same
            // element's box has to carry the element's own resolved value.
            var breakArea = _computedStyle.Break;
            breakArea = breakArea
                .SetPropertyValue(breakArea.BoxDecorationBreak, parentStyle.Break.BoxDecorationBreak, static (a, v) => a with { BoxDecorationBreak = v })
                // The break properties are not inherited either (css-break-3 §3.1/§3.2), so they are
                // rightly absent from the "always" section above - but a structural duplicate is a
                // fragment of the same element, and §3 attaches these values to the element, not to one of
                // its boxes. Without this a split block loses its own break-inside: avoid and a repeated
                // <thead> loses its break-after: avoid, silently, since the initial value is the
                // permissive "auto".
                .SetPropertyValue(breakArea.BreakBefore, parentStyle.Break.BreakBefore, static (a, v) => a with { BreakBefore = v })
                .SetPropertyValue(breakArea.BreakAfter, parentStyle.Break.BreakAfter, static (a, v) => a with { BreakAfter = v })
                .SetPropertyValue(breakArea.BreakInside, parentStyle.Break.BreakInside, static (a, v) => a with { BreakInside = v });
            _computedStyle = _computedStyle.AdoptArea(_computedStyle.Break, breakArea, static (s, a) => s with { Break = a });

            // Not a real inherited property (never copied in the "always" section above, so
            // ordinary parent->child cascade seeding never touches it) - but every "everything"
            // call site here (CssProxyBox's repeated-header/footer clone, DomParser's inline/block
            // box-splitting) represents a structural duplicate of the SAME source box's own
            // resolved content, not real ancestor->descendant inheritance, so its own resolved
            // tag type must carry over too.
            var generatedContent = _computedStyle.GeneratedContent;
            var newGeneratedContent = generatedContent.SetPropertyValue(generatedContent.PdfTagType, parentStyle.GeneratedContent.PdfTagType, static (a, v) => a with { PdfTagType = v });
            _computedStyle = _computedStyle.AdoptArea(generatedContent, newGeneratedContent, static (s, a) => s with { GeneratedContent = a });
        }
    }
}
