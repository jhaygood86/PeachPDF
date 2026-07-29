using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
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

        public string BorderTopWidth
        {
            get => _computedStyle.BorderTopWidth;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderTopWidth, value, static (s, v) => s with { BorderTopWidth = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderTopWidth();
            }
        }

        public string BorderRightWidth
        {
            get => _computedStyle.BorderRightWidth;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderRightWidth, value, static (s, v) => s with { BorderRightWidth = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderRightWidth();
            }
        }

        public string BorderBottomWidth
        {
            get => _computedStyle.BorderBottomWidth;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderBottomWidth, value, static (s, v) => s with { BorderBottomWidth = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderBottomWidth();
            }
        }

        public string BorderLeftWidth
        {
            get => _computedStyle.BorderLeftWidth;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderLeftWidth, value, static (s, v) => s with { BorderLeftWidth = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderLeftWidth();
            }
        }

        public string BorderTopStyle
        {
            get => _computedStyle.BorderTopStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopStyle, value, static (s, v) => s with { BorderTopStyle = v });
        }

        public string BorderRightStyle
        {
            get => _computedStyle.BorderRightStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderRightStyle, value, static (s, v) => s with { BorderRightStyle = v });
        }

        public string BorderBottomStyle
        {
            get => _computedStyle.BorderBottomStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomStyle, value, static (s, v) => s with { BorderBottomStyle = v });
        }

        public string BorderLeftStyle
        {
            get => _computedStyle.BorderLeftStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderLeftStyle, value, static (s, v) => s with { BorderLeftStyle = v });
        }

        public string BorderTopColor
        {
            get => _computedStyle.BorderTopColor;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderTopColor, value, static (s, v) => s with { BorderTopColor = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderTopColor();
            }
        }

        public string BorderRightColor
        {
            get => _computedStyle.BorderRightColor;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderRightColor, value, static (s, v) => s with { BorderRightColor = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderRightColor();
            }
        }

        public string BorderBottomColor
        {
            get => _computedStyle.BorderBottomColor;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderBottomColor, value, static (s, v) => s with { BorderBottomColor = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderBottomColor();
            }
        }

        public string BorderLeftColor
        {
            get => _computedStyle.BorderLeftColor;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderLeftColor, value, static (s, v) => s with { BorderLeftColor = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderLeftColor();
            }
        }

        public string BorderTopLeftRadius
        {
            get => _computedStyle.BorderTopLeftRadius;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderTopLeftRadius, value, static (s, v) => s with { BorderTopLeftRadius = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderTopLeftRadius();
            }
        }

        public string BorderTopRightRadius
        {
            get => _computedStyle.BorderTopRightRadius;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderTopRightRadius, value, static (s, v) => s with { BorderTopRightRadius = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderTopRightRadius();
            }
        }

        public string BorderBottomRightRadius
        {
            get => _computedStyle.BorderBottomRightRadius;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderBottomRightRadius, value, static (s, v) => s with { BorderBottomRightRadius = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderBottomRightRadius();
            }
        }

        public string BorderBottomLeftRadius
        {
            get => _computedStyle.BorderBottomLeftRadius;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.BorderBottomLeftRadius, value, static (s, v) => s with { BorderBottomLeftRadius = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateBorderBottomLeftRadius();
            }
        }

        public string BorderSpacing
        {
            get => _computedStyle.BorderSpacing;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderSpacing, value, static (s, v) => s with { BorderSpacing = v });
        }

        public string BorderCollapse
        {
            get => _computedStyle.BorderCollapse;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderCollapse, value, static (s, v) => s with { BorderCollapse = v });
        }

        #endregion

        #region Transform, opacity, clip-path, aspect-ratio, box-shadow

        public string Transform
        {
            get => _computedStyle.Transform;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.Transform, value, static (s, v) => s with { Transform = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateTransform();
            }
        }

        public string TransformOrigin
        {
            get => _computedStyle.TransformOrigin;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.TransformOrigin, value, static (s, v) => s with { TransformOrigin = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateTransform();
            }
        }

        public string Opacity
        {
            get => _computedStyle.Opacity;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.Opacity, value, static (s, v) => s with { Opacity = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateOpacity();
            }
        }

        public string ClipPath
        {
            get => _computedStyle.ClipPath;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ClipPath, value, static (s, v) => s with { ClipPath = v });
        }

        public string AspectRatio
        {
            get => _computedStyle.AspectRatio;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.AspectRatio, value, static (s, v) => s with { AspectRatio = v });
        }

        public string BoxShadow
        {
            get => _computedStyle.BoxShadow;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BoxShadow, value, static (s, v) => s with { BoxShadow = v });
        }

        public string BoxSizing
        {
            get => _computedStyle.BoxSizing;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BoxSizing, value, static (s, v) => s with { BoxSizing = v });
        }

        #endregion

        #region Counters, page, pdf-tag-type

        public string CounterIncrement
        {
            get => _computedStyle.CounterIncrement;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CounterIncrement, value, static (s, v) => s with { CounterIncrement = v });
        }

        public string CounterReset
        {
            get => _computedStyle.CounterReset;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CounterReset, value, static (s, v) => s with { CounterReset = v });
        }

        public string CounterSet
        {
            get => _computedStyle.CounterSet;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CounterSet, value, static (s, v) => s with { CounterSet = v });
        }

        public string StringSet
        {
            get => _computedStyle.StringSet;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.StringSet, value, static (s, v) => s with { StringSet = v });
        }

        public string PageName
        {
            get => _computedStyle.PageName;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PageName, value, static (s, v) => s with { PageName = v });
        }

        public string PdfTagType
        {
            get => _computedStyle.PdfTagType;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PdfTagType, value, static (s, v) => s with { PdfTagType = v });
        }

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

        public string MarginTop
        {
            get => _computedStyle.MarginTop;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginTop, value, static (s, v) => s with { MarginTop = v });
        }

        public string MarginRight
        {
            get => _computedStyle.MarginRight;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginRight, value, static (s, v) => s with { MarginRight = v });
        }

        public string MarginBottom
        {
            get => _computedStyle.MarginBottom;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginBottom, value, static (s, v) => s with { MarginBottom = v });
        }

        public string MarginLeft
        {
            get => _computedStyle.MarginLeft;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginLeft, value, static (s, v) => s with { MarginLeft = v });
        }

        public string PaddingTop
        {
            get => _computedStyle.PaddingTop;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.PaddingTop, value, static (s, v) => s with { PaddingTop = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidatePaddingTop();
            }
        }

        public string PaddingRight
        {
            get => _computedStyle.PaddingRight;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.PaddingRight, value, static (s, v) => s with { PaddingRight = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidatePaddingRight();
            }
        }

        public string PaddingBottom
        {
            get => _computedStyle.PaddingBottom;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.PaddingBottom, value, static (s, v) => s with { PaddingBottom = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidatePaddingBottom();
            }
        }

        public string PaddingLeft
        {
            get => _computedStyle.PaddingLeft;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.PaddingLeft, value, static (s, v) => s with { PaddingLeft = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidatePaddingLeft();
            }
        }

        #endregion

        #region Break, box-decoration-break, orphans/widows

        public string BreakBefore
        {
            get => _computedStyle.BreakBefore;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakBefore, value, static (s, v) => s with { BreakBefore = v });
        }

        public string BreakInside
        {
            get => _computedStyle.BreakInside;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakInside, value, static (s, v) => s with { BreakInside = v });
        }

        public string BreakAfter
        {
            get => _computedStyle.BreakAfter;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakAfter, value, static (s, v) => s with { BreakAfter = v });
        }

        // css-break-3 §6.2, honored at both the page breaks of a block box and the line-box breaks of an
        // inline one. Read by Html/Core/Paint/BoxDecorationGeometry, which owns the whole decision and
        // resolves it against the per-rectangle geometry the fragment tree carries
        // (Html/Core/Fragments/SliceGeometry) - deliberately not against BoxFragment's
        // IsFirstFragment/IsLastFragment, whose span includes descendants. Layout also reserves room for
        // cloned decorations, so a cloned border at a break pushes content rather than overlapping it.
        public string BoxDecorationBreak
        {
            get => _computedStyle.BoxDecorationBreak;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BoxDecorationBreak, value, static (s, v) => s with { BoxDecorationBreak = v });
        }

        // Enforced in CssBox.PerformLayoutImp, for plain (non-multi-column) block flow only: a box whose
        // lines would straddle a page boundary with too few before/after it is pushed whole to the next
        // page. That is coarser than the spec, which pulls only the minimum lines across the break, and
        // it is skipped for a box taller than one page (pushing it whole would just recreate the same
        // violation). Inert inside multicol, whose whole-child fragmentation never splits a child in the
        // first place, so it cannot strand a line.
        public string Orphans
        {
            get => _computedStyle.Orphans;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Orphans, value, static (s, v) => s with { Orphans = v });
        }

        public string Widows
        {
            get => _computedStyle.Widows;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Widows, value, static (s, v) => s with { Widows = v });
        }

        #endregion

        #region Box position and size

        public string Left
        {
            get => _computedStyle.Left;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Left, value, static (s, v) => s with { Left = v });
        }

        public string Top
        {
            get => _computedStyle.Top;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Top, value, static (s, v) => s with { Top = v });
        }

        public string Bottom
        {
            get => _computedStyle.Bottom;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Bottom, value, static (s, v) => s with { Bottom = v });
        }

        public string Right
        {
            get => _computedStyle.Right;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Right, value, static (s, v) => s with { Right = v });
        }

        public string Width
        {
            get => _computedStyle.Width;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Width, value, static (s, v) => s with { Width = v });
        }

        public string MaxWidth
        {
            get => _computedStyle.MaxWidth;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MaxWidth, value, static (s, v) => s with { MaxWidth = v });
        }

        public string MinWidth
        {
            get => _computedStyle.MinWidth;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MinWidth, value, static (s, v) => s with { MinWidth = v });
        }

        public string Height
        {
            get => _computedStyle.Height;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Height, value, static (s, v) => s with { Height = v });
        }

        public string MaxHeight
        {
            get => _computedStyle.MaxHeight;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MaxHeight, value, static (s, v) => s with { MaxHeight = v });
        }

        public string MinHeight
        {
            get => _computedStyle.MinHeight;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MinHeight, value, static (s, v) => s with { MinHeight = v });
        }

        #endregion

        #region Background

        public string BackgroundColor
        {
            get => _computedStyle.BackgroundColor;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundColor, value, static (s, v) => s with { BackgroundColor = v });
        }

        public IReadOnlyList<CssImage>? BackgroundImages
        {
            get => _computedStyle.BackgroundImages;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundImages, value, static (s, v) => s with { BackgroundImages = v });
        }

        public string BackgroundPosition
        {
            get => _computedStyle.BackgroundPosition;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundPosition, value, static (s, v) => s with { BackgroundPosition = v });
        }

        public string BackgroundRepeat
        {
            get => _computedStyle.BackgroundRepeat;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundRepeat, value, static (s, v) => s with { BackgroundRepeat = v });
        }

        public string BackgroundSize
        {
            get => _computedStyle.BackgroundSize;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundSize, value, static (s, v) => s with { BackgroundSize = v });
        }

        public string BackgroundOrigin
        {
            get => _computedStyle.BackgroundOrigin;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundOrigin, value, static (s, v) => s with { BackgroundOrigin = v });
        }

        public string BackgroundClip
        {
            get => _computedStyle.BackgroundClip;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundClip, value, static (s, v) => s with { BackgroundClip = v });
        }

        public string BackgroundAttachment
        {
            get => _computedStyle.BackgroundAttachment;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundAttachment, value, static (s, v) => s with { BackgroundAttachment = v });
        }

        public string ObjectFit
        {
            get => _computedStyle.ObjectFit;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ObjectFit, value, static (s, v) => s with { ObjectFit = v });
        }

        public string ObjectPosition
        {
            get => _computedStyle.ObjectPosition;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ObjectPosition, value, static (s, v) => s with { ObjectPosition = v });
        }

        #endregion

        #region Color, content, display, direction, float, clear, position

        public string Color
        {
            get => _computedStyle.Color;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.Color, value, static (s, v) => s with { Color = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateColor();
            }
        }

        public string Content
        {
            get => _computedStyle.Content;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Content, value, static (s, v) => s with { Content = v });
        }

        public string Display
        {
            get
            {
                if (_computedStyle.Float is not CssConstants.None)
                {
                    return _computedStyle.Display switch
                    {
                        "inline" => "block",
                        "inline-block" => "block",
                        "inline-table" => "table",
                        "table-row" => "block",
                        "table-row-group" => "block",
                        "table-column" => "block",
                        "table-column-group" => "block",
                        "table-cell" => "block",
                        "table-caption" => "block",
                        "table-header-group" => "block",
                        "table-footer-group" => "block",
                        "inline-flex" => "flex",
                        "inline-grid" => "grid",
                        _ => _computedStyle.Display
                    };
                }

                return _computedStyle.Display;
            }
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Display, value, static (s, v) => s with { Display = v });
        }

        public string Direction
        {
            get => _computedStyle.Direction;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Direction, value, static (s, v) => s with { Direction = v });
        }

        public string EmptyCells
        {
            get => _computedStyle.EmptyCells;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.EmptyCells, value, static (s, v) => s with { EmptyCells = v });
        }

        public string Float
        {
            get => _computedStyle.Float;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Float, value, static (s, v) => s with { Float = v });
        }

        public string Clear
        {
            get => _computedStyle.Clear;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Clear, value, static (s, v) => s with { Clear = v });
        }

        public string Position
        {
            get => _computedStyle.Position;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Position, value, static (s, v) => s with { Position = v });
        }

        /// <summary>True for a positioned element: <c>position</c> of relative, absolute, fixed, or sticky.</summary>
        public bool IsPositioned => DerivedStyle.IsPositioned;

        #endregion

        #region Line-height, vertical-align, text-*

        public string LineHeight
        {
            get => _computedStyle.LineHeight;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.LineHeight, value, static (s, v) => s with { LineHeight = v });
        }

        /// <summary>Gets the line height. Recomputed fresh every call, not cached.</summary>
        public double ActualLineHeight => DerivedStyle.ActualLineHeight;

        public string VerticalAlign
        {
            get => _computedStyle.VerticalAlign;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.VerticalAlign, value, static (s, v) => s with { VerticalAlign = v });
        }

        public string TextIndent
        {
            get => _computedStyle.TextIndent;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextIndent, NoEms(value), static (s, v) => s with { TextIndent = v });
        }

        /// <summary>Gets the text indentation (on first line only).</summary>
        public double ActualTextIndent => DerivedStyle.ActualTextIndent;

        public string TextAlign
        {
            get => _computedStyle.TextAlign;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextAlign, value, static (s, v) => s with { TextAlign = v });
        }

        public string TextDecorationLine
        {
            get => _computedStyle.TextDecorationLine;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationLine, value, static (s, v) => s with { TextDecorationLine = v });
        }

        public string TextDecorationStyle
        {
            get => _computedStyle.TextDecorationStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationStyle, value, static (s, v) => s with { TextDecorationStyle = v });
        }

        public string TextDecorationColor
        {
            get => _computedStyle.TextDecorationColor;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationColor, value, static (s, v) => s with { TextDecorationColor = v });
        }

        public string TextTransform
        {
            get => _computedStyle.TextTransform;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextTransform, value, static (s, v) => s with { TextTransform = v });
        }

        public string WhiteSpace
        {
            get => _computedStyle.WhiteSpace;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WhiteSpace, value, static (s, v) => s with { WhiteSpace = v });
        }

        public string Visibility
        {
            get => _computedStyle.Visibility;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Visibility, value, static (s, v) => s with { Visibility = v });
        }

        public string WordSpacing
        {
            get => _computedStyle.WordSpacing;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WordSpacing, NoEms(value), static (s, v) => s with { WordSpacing = v });
        }

        /// <summary>Gets the actual width of whitespace between words.</summary>
        public double ActualWordSpacing => DerivedStyle.ActualWordSpacing;

        public string LetterSpacing
        {
            get => _computedStyle.LetterSpacing;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.LetterSpacing, NoEms(value), static (s, v) => s with { LetterSpacing = v });
        }

        /// <summary>Gets the actual extra space added between each pair of adjacent characters.</summary>
        public double ActualLetterSpacing => DerivedStyle.ActualLetterSpacing;

        /// <summary>Measures the width of whitespace between words (set <see cref="ActualWordSpacing"/>).</summary>
        protected void MeasureWordSpacing(RGraphics g) => DerivedStyle.MeasureWordSpacing(g);

        /// <summary>Measures the extra space added between each pair of adjacent characters (set <see cref="ActualLetterSpacing"/>).</summary>
        protected void MeasureLetterSpacing() => DerivedStyle.MeasureLetterSpacing();

        public string WordBreak
        {
            get => _computedStyle.WordBreak;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WordBreak, value, static (s, v) => s with { WordBreak = v });
        }

        /// <summary>
        /// <c>none</c>/<c>manual</c>/<c>auto</c>. <c>manual</c> and <c>auto</c> both honor an explicit
        /// soft hyphen (U+00AD) as a line-break opportunity; only <c>auto</c> additionally implies
        /// dictionary-based automatic hyphenation, which PeachPDF does not implement (see
        /// docs/html-css-support.md). Assigning through the property directly (not a specified-value
        /// resolver) since <c>auto</c> behaves like <c>manual</c> here.
        /// </summary>
        public string Hyphens
        {
            get => _computedStyle.Hyphens;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Hyphens, value, static (s, v) => s with { Hyphens = v });
        }

        #endregion

        #region Font

        public string? FontFamily
        {
            get => _computedStyle.FontFamily;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontFamily, value, static (s, v) => s with { FontFamily = v });
        }

        /// <summary>
        /// The full, unresolved <c>font-family</c> list as authored (e.g. <c>"Latin", "Symbols", serif</c>),
        /// as opposed to <see cref="FontFamily"/> which the cascade already collapses to the first family
        /// that exists. Retained so per-codepoint font matching can walk the whole stack. Inherited alongside <see cref="FontFamily"/>.
        /// </summary>
        public string? FontFamilyList
        {
            get => _computedStyle.FontFamilyList;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontFamilyList, value, static (s, v) => s with { FontFamilyList = v });
        }

        public string FontSize
        {
            get => _computedStyle.FontSize;
            set
            {
                // calc()-family text is resolved directly by ActualFont's own ParseLength call later
                // (including any em/rem terms it contains) - leave it untouched here. A plain "Nem" is
                // the only case needing eager conversion at this point (to points, using the parent's
                // already-resolved font size); everything else - keywords like "medium"/"larger", or any
                // other single-unit length - is left as-is for ActualFont's own resolution later.
                string resolved;
                if (!CssValueParser.IsCalcFunction(value) &&
                    CssValueParser.GetCssTokens(value) is [UnitToken unitToken] &&
                    Length.GetUnit(unitToken.Unit) == Length.Unit.Em &&
                    ParentBox is { } parent)
                {
                    var points = unitToken.Value * parent.ActualFont.Size;
                    resolved = $"{points.ToString("0.0", System.Globalization.NumberFormatInfo.InvariantInfo)}pt";
                }
                else
                {
                    resolved = value;
                }

                _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontSize, resolved, static (s, v) => s with { FontSize = v });
            }
        }

        public string FontStyle
        {
            get => _computedStyle.FontStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontStyle, value, static (s, v) => s with { FontStyle = v });
        }

        public string FontVariant
        {
            get => _computedStyle.FontVariant;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontVariant, value, static (s, v) => s with { FontVariant = v });
        }

        public string FontWeight
        {
            get => _computedStyle.FontWeight;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontWeight, value, static (s, v) => s with { FontWeight = v });
        }

        public string FontStretch
        {
            get => _computedStyle.FontStretch;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontStretch, value, static (s, v) => s with { FontStretch = v });
        }

        public string FontPalette
        {
            get => _computedStyle.FontPalette;
            set
            {
                var previous = _computedStyle;
                _computedStyle = previous.SetPropertyValue(previous.FontPalette, value, static (s, v) => s with { FontPalette = v });
                if (!ReferenceEquals(_computedStyle, previous)) DerivedStyle.InvalidateFontPalette();
            }
        }

        #endregion

        #region Overflow, list-style

        public string Overflow
        {
            get => _computedStyle.Overflow;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Overflow, value, static (s, v) => s with { Overflow = v });
        }

        public string ListStylePosition
        {
            get => _computedStyle.ListStylePosition;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStylePosition, value, static (s, v) => s with { ListStylePosition = v });
        }

        public CssImage? ListStyleImage
        {
            get => _computedStyle.ListStyleImage;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStyleImage, value, static (s, v) => s with { ListStyleImage = v });
        }

        public string ListStyleType
        {
            get => _computedStyle.ListStyleType;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStyleType, value, static (s, v) => s with { ListStyleType = v });
        }

        #endregion

        #region Z-index

        public string ZIndex
        {
            get => _computedStyle.ZIndex;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ZIndex, value, static (s, v) => s with { ZIndex = v });
        }

        #endregion

        #region Flex container/item

        public string FlexDirection
        {
            get => _computedStyle.FlexDirection;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexDirection, value, static (s, v) => s with { FlexDirection = v });
        }

        public string FlexWrap
        {
            get => _computedStyle.FlexWrap;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexWrap, value, static (s, v) => s with { FlexWrap = v });
        }

        public string JustifyContent
        {
            get => _computedStyle.JustifyContent;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.JustifyContent, value, static (s, v) => s with { JustifyContent = v });
        }

        public string AlignItems
        {
            get => _computedStyle.AlignItems;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.AlignItems, value, static (s, v) => s with { AlignItems = v });
        }

        public string AlignContent
        {
            get => _computedStyle.AlignContent;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.AlignContent, value, static (s, v) => s with { AlignContent = v });
        }

        public string FlexGrow
        {
            get => _computedStyle.FlexGrow;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexGrow, value, static (s, v) => s with { FlexGrow = v });
        }

        public string FlexShrink
        {
            get => _computedStyle.FlexShrink;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexShrink, value, static (s, v) => s with { FlexShrink = v });
        }

        public string FlexBasis
        {
            get => _computedStyle.FlexBasis;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexBasis, value, static (s, v) => s with { FlexBasis = v });
        }

        public string AlignSelf
        {
            get => _computedStyle.AlignSelf;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.AlignSelf, value, static (s, v) => s with { AlignSelf = v });
        }

        public string Order
        {
            get => _computedStyle.Order;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Order, value, static (s, v) => s with { Order = v });
        }

        /// <summary>Gap between flex items (column-gap = main-axis gap for row direction).</summary>
        public string FlexRowGap
        {
            get => _computedStyle.FlexRowGap;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexRowGap, value, static (s, v) => s with { FlexRowGap = v });
        }

        public string FlexColumnGap
        {
            get => _computedStyle.FlexColumnGap;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FlexColumnGap, value, static (s, v) => s with { FlexColumnGap = v });
        }

        #endregion

        #region Grid container/item

        public CssProperty<GridTemplate> GridTemplateColumns
        {
            get => _computedStyle.GridTemplateColumns;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridTemplateColumns, value, static (s, v) => s with { GridTemplateColumns = v });
        }

        public CssProperty<GridTemplate> GridTemplateRows
        {
            get => _computedStyle.GridTemplateRows;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridTemplateRows, value, static (s, v) => s with { GridTemplateRows = v });
        }

        public string GridTemplateAreas
        {
            get => _computedStyle.GridTemplateAreas;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridTemplateAreas, value, static (s, v) => s with { GridTemplateAreas = v });
        }

        public string GridAutoColumns
        {
            get => _computedStyle.GridAutoColumns;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridAutoColumns, value, static (s, v) => s with { GridAutoColumns = v });
        }

        public string GridAutoRows
        {
            get => _computedStyle.GridAutoRows;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridAutoRows, value, static (s, v) => s with { GridAutoRows = v });
        }

        public string GridAutoFlow
        {
            get => _computedStyle.GridAutoFlow;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridAutoFlow, value, static (s, v) => s with { GridAutoFlow = v });
        }

        public string JustifyItems
        {
            get => _computedStyle.JustifyItems;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.JustifyItems, value, static (s, v) => s with { JustifyItems = v });
        }

        public string JustifySelf
        {
            get => _computedStyle.JustifySelf;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.JustifySelf, value, static (s, v) => s with { JustifySelf = v });
        }

        public string GridColumnStart
        {
            get => _computedStyle.GridColumnStart;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridColumnStart, value, static (s, v) => s with { GridColumnStart = v });
        }

        public string GridColumnEnd
        {
            get => _computedStyle.GridColumnEnd;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridColumnEnd, value, static (s, v) => s with { GridColumnEnd = v });
        }

        public string GridRowStart
        {
            get => _computedStyle.GridRowStart;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridRowStart, value, static (s, v) => s with { GridRowStart = v });
        }

        public string GridRowEnd
        {
            get => _computedStyle.GridRowEnd;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.GridRowEnd, value, static (s, v) => s with { GridRowEnd = v });
        }

        /// <summary>Transient parent→child conduit set by <see cref="CssLayoutEngineGrid"/> immediately before
        /// it lays out a <c>subgrid</c> grid item, so the child adopts the parent's spanned tracks (CSS Grid
        /// Level 2 §9). Layout-only - not a cascaded property, cleared by the parent after the child lays out.</summary>
        internal GridSubgridContext? SubgridContext { get; set; }

        #endregion

        #region Multi-column

        public string ColumnCount
        {
            get => _computedStyle.ColumnCount;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnCount, value, static (s, v) => s with { ColumnCount = v });
        }

        public string ColumnWidth
        {
            get => _computedStyle.ColumnWidth;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnWidth, value, static (s, v) => s with { ColumnWidth = v });
        }

        public string ColumnFill
        {
            get => _computedStyle.ColumnFill;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnFill, value, static (s, v) => s with { ColumnFill = v });
        }

        public string ColumnSpan
        {
            get => _computedStyle.ColumnSpan;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnSpan, value, static (s, v) => s with { ColumnSpan = v });
        }

        public string ColumnRuleWidth
        {
            get => _computedStyle.ColumnRuleWidth;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnRuleWidth, value, static (s, v) => s with { ColumnRuleWidth = v });
        }

        public string ColumnRuleStyle
        {
            get => _computedStyle.ColumnRuleStyle;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnRuleStyle, value, static (s, v) => s with { ColumnRuleStyle = v });
        }

        public string ColumnRuleColor
        {
            get => _computedStyle.ColumnRuleColor;
            set => _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ColumnRuleColor, value, static (s, v) => s with { ColumnRuleColor = v });
        }

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
                BorderLeftStyle = BorderTopStyle = BorderRightStyle = BorderBottomStyle = style;
            if (width != null)
                BorderLeftWidth = BorderTopWidth = BorderRightWidth = BorderBottomWidth = width;
            if (color != null)
                BorderLeftColor = BorderTopColor = BorderRightColor = BorderBottomColor = color;
        }

        #endregion

        #region Layout-engine output (not cascaded, not derived - assigned directly by layout)

        private RPoint _location;

        /// <summary>Gets or sets the location of the box.</summary>
        public RPoint Location
        {
            get => _location;
            set
            {
                if (value.Y != _location.Y) OnBlockAxisRelocated(_location.Y, value.Y);
                _location = value;
            }
        }

        /// <summary>Gets or sets the size of the box.</summary>
        public RSize Size { get; set; }

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
                return BoxSizing switch
                {
                    CssConstants.ContentBox => ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth,
                    CssConstants.BorderBox => 0,
                    _ => throw new HtmlRenderException("Unknown box sizing", HtmlRenderErrorType.Layout)
                };
            }
        }

        public double ActualBoxSizingWidth => Size.Width + ActualBoxSizeIncludedWidth;

        public double ActualBoxSizeIncludedHeight
        {
            get
            {
                return BoxSizing switch
                {
                    CssConstants.ContentBox => ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth,
                    CssConstants.BorderBox => 0,
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

            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.CustomProperties, customProperties, static (s, v) => s with { CustomProperties = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderSpacing, parentStyle.BorderSpacing, static (s, v) => s with { BorderSpacing = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderCollapse, parentStyle.BorderCollapse, static (s, v) => s with { BorderCollapse = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Color, parentStyle.Color, static (s, v) => s with { Color = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.EmptyCells, parentStyle.EmptyCells, static (s, v) => s with { EmptyCells = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WhiteSpace, parentStyle.WhiteSpace, static (s, v) => s with { WhiteSpace = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Visibility, parentStyle.Visibility, static (s, v) => s with { Visibility = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextIndent, parentStyle.TextIndent, static (s, v) => s with { TextIndent = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextAlign, parentStyle.TextAlign, static (s, v) => s with { TextAlign = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextTransform, parentStyle.TextTransform, static (s, v) => s with { TextTransform = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.VerticalAlign, parentStyle.VerticalAlign, static (s, v) => s with { VerticalAlign = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontFamily, parentStyle.FontFamily, static (s, v) => s with { FontFamily = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontFamilyList, parentStyle.FontFamilyList, static (s, v) => s with { FontFamilyList = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontSize, parentStyle.FontSize, static (s, v) => s with { FontSize = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontStyle, parentStyle.FontStyle, static (s, v) => s with { FontStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontVariant, parentStyle.FontVariant, static (s, v) => s with { FontVariant = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontWeight, parentStyle.FontWeight, static (s, v) => s with { FontWeight = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontStretch, parentStyle.FontStretch, static (s, v) => s with { FontStretch = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.FontPalette, parentStyle.FontPalette, static (s, v) => s with { FontPalette = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStyleImage, parentStyle.ListStyleImage, static (s, v) => s with { ListStyleImage = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStylePosition, parentStyle.ListStylePosition, static (s, v) => s with { ListStylePosition = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ListStyleType, parentStyle.ListStyleType, static (s, v) => s with { ListStyleType = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.LineHeight, parentStyle.LineHeight, static (s, v) => s with { LineHeight = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WordSpacing, parentStyle.WordSpacing, static (s, v) => s with { WordSpacing = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.LetterSpacing, parentStyle.LetterSpacing, static (s, v) => s with { LetterSpacing = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.WordBreak, parentStyle.WordBreak, static (s, v) => s with { WordBreak = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Direction, parentStyle.Direction, static (s, v) => s with { Direction = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BoxSizing, parentStyle.BoxSizing, static (s, v) => s with { BoxSizing = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Orphans, parentStyle.Orphans, static (s, v) => s with { Orphans = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Widows, parentStyle.Widows, static (s, v) => s with { Widows = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Hyphens, parentStyle.Hyphens, static (s, v) => s with { Hyphens = v });

            // The invalidations these bypass (border/padding/opacity/transform/color/font-palette caches)
            // are intentionally skipped here - every value copied above either has no such cache, or (for
            // Color, FontPalette) is safe to leave stale since a fresh box's DerivedStyle cache starts
            // empty and this method only ever runs before anything else has read from it.
            if (!everything) return;

            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundColor, parentStyle.BackgroundColor, static (s, v) => s with { BackgroundColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundImages, parentStyle.BackgroundImages, static (s, v) => s with { BackgroundImages = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundPosition, parentStyle.BackgroundPosition, static (s, v) => s with { BackgroundPosition = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundRepeat, parentStyle.BackgroundRepeat, static (s, v) => s with { BackgroundRepeat = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundOrigin, parentStyle.BackgroundOrigin, static (s, v) => s with { BackgroundOrigin = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundClip, parentStyle.BackgroundClip, static (s, v) => s with { BackgroundClip = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BackgroundAttachment, parentStyle.BackgroundAttachment, static (s, v) => s with { BackgroundAttachment = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ObjectFit, parentStyle.ObjectFit, static (s, v) => s with { ObjectFit = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.ObjectPosition, parentStyle.ObjectPosition, static (s, v) => s with { ObjectPosition = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopWidth, parentStyle.BorderTopWidth, static (s, v) => s with { BorderTopWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderRightWidth, parentStyle.BorderRightWidth, static (s, v) => s with { BorderRightWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomWidth, parentStyle.BorderBottomWidth, static (s, v) => s with { BorderBottomWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderLeftWidth, parentStyle.BorderLeftWidth, static (s, v) => s with { BorderLeftWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopColor, parentStyle.BorderTopColor, static (s, v) => s with { BorderTopColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderRightColor, parentStyle.BorderRightColor, static (s, v) => s with { BorderRightColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomColor, parentStyle.BorderBottomColor, static (s, v) => s with { BorderBottomColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderLeftColor, parentStyle.BorderLeftColor, static (s, v) => s with { BorderLeftColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopStyle, parentStyle.BorderTopStyle, static (s, v) => s with { BorderTopStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderRightStyle, parentStyle.BorderRightStyle, static (s, v) => s with { BorderRightStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomStyle, parentStyle.BorderBottomStyle, static (s, v) => s with { BorderBottomStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderLeftStyle, parentStyle.BorderLeftStyle, static (s, v) => s with { BorderLeftStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopLeftRadius, parentStyle.BorderTopLeftRadius, static (s, v) => s with { BorderTopLeftRadius = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderTopRightRadius, parentStyle.BorderTopRightRadius, static (s, v) => s with { BorderTopRightRadius = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomRightRadius, parentStyle.BorderBottomRightRadius, static (s, v) => s with { BorderBottomRightRadius = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BorderBottomLeftRadius, parentStyle.BorderBottomLeftRadius, static (s, v) => s with { BorderBottomLeftRadius = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Transform, parentStyle.Transform, static (s, v) => s with { Transform = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TransformOrigin, parentStyle.TransformOrigin, static (s, v) => s with { TransformOrigin = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Opacity, parentStyle.Opacity, static (s, v) => s with { Opacity = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Display, parentStyle.Display, static (s, v) => s with { Display = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Float, parentStyle.Float, static (s, v) => s with { Float = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Height, parentStyle.Height, static (s, v) => s with { Height = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MaxHeight, parentStyle.MaxHeight, static (s, v) => s with { MaxHeight = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginBottom, parentStyle.MarginBottom, static (s, v) => s with { MarginBottom = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginLeft, parentStyle.MarginLeft, static (s, v) => s with { MarginLeft = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginRight, parentStyle.MarginRight, static (s, v) => s with { MarginRight = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MarginTop, parentStyle.MarginTop, static (s, v) => s with { MarginTop = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Left, parentStyle.Left, static (s, v) => s with { Left = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Overflow, parentStyle.Overflow, static (s, v) => s with { Overflow = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PaddingLeft, parentStyle.PaddingLeft, static (s, v) => s with { PaddingLeft = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PaddingBottom, parentStyle.PaddingBottom, static (s, v) => s with { PaddingBottom = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PaddingRight, parentStyle.PaddingRight, static (s, v) => s with { PaddingRight = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PaddingTop, parentStyle.PaddingTop, static (s, v) => s with { PaddingTop = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationLine, parentStyle.TextDecorationLine, static (s, v) => s with { TextDecorationLine = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationStyle, parentStyle.TextDecorationStyle, static (s, v) => s with { TextDecorationStyle = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.TextDecorationColor, parentStyle.TextDecorationColor, static (s, v) => s with { TextDecorationColor = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Top, parentStyle.Top, static (s, v) => s with { Top = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Position, parentStyle.Position, static (s, v) => s with { Position = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Width, parentStyle.Width, static (s, v) => s with { Width = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MaxWidth, parentStyle.MaxWidth, static (s, v) => s with { MaxWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.MinWidth, parentStyle.MinWidth, static (s, v) => s with { MinWidth = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Bottom, parentStyle.Bottom, static (s, v) => s with { Bottom = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.Right, parentStyle.Right, static (s, v) => s with { Right = v });
            // Same reasoning as PdfTagType below: not inherited, but a structural duplicate of the same
            // element's box has to carry the element's own resolved value.
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BoxDecorationBreak, parentStyle.BoxDecorationBreak, static (s, v) => s with { BoxDecorationBreak = v });
            // The break properties are not inherited either (css-break-3 §3.1/§3.2), so they are rightly
            // absent from the "always" section above - but a structural duplicate is a fragment of the
            // same element, and §3 attaches these values to the element, not to one of its boxes. Without
            // this a split block loses its own break-inside: avoid and a repeated <thead> loses its
            // break-after: avoid, silently, since the initial value is the permissive "auto".
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakBefore, parentStyle.BreakBefore, static (s, v) => s with { BreakBefore = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakAfter, parentStyle.BreakAfter, static (s, v) => s with { BreakAfter = v });
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.BreakInside, parentStyle.BreakInside, static (s, v) => s with { BreakInside = v });
            // Not a real inherited property (never copied in the "always" section above, so
            // ordinary parent->child cascade seeding never touches it) - but every "everything"
            // call site here (CssProxyBox's repeated-header/footer clone, DomParser's inline/block
            // box-splitting) represents a structural duplicate of the SAME source box's own
            // resolved content, not real ancestor->descendant inheritance, so its own resolved
            // tag type must carry over too.
            _computedStyle = _computedStyle.SetPropertyValue(_computedStyle.PdfTagType, parentStyle.PdfTagType, static (s, v) => s with { PdfTagType = v });
        }
    }
}
