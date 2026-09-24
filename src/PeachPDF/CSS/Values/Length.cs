#nullable disable

using System;

// ReSharper disable UnusedMember.Global


namespace PeachPDF.CSS
{
    internal struct Length : IEquatable<Length>, IComparable<Length>, IFormattable
    {
        /// <summary>
        ///     Internal layout units (points) per CSS pixel: 1px = 1/96in = 0.75pt
        ///     (CSS Values &amp; Units §6.2). The single place this ratio is defined —
        ///     every px conversion in the engine must route through this constant.
        /// </summary>
        internal const double PointsPerPx = 72d / 96d;

        /// <summary>
        ///     Gets a zero pixel length value.
        /// </summary>
        public static readonly Length Zero = new(0f, Unit.Px);

        /// <summary>
        ///     Gets the half relative length, i.e. 50%.
        /// </summary>
        public static readonly Length Half = new(50f, Unit.Percent);

        /// <summary>
        ///     Gets the full relative length, i.e. 100%.
        /// </summary>
        public static readonly Length Full = new(100f, Unit.Percent);

        /// <summary>
        ///     Gets a thin length value.
        /// </summary>
        public static readonly Length Thin = new(1f, Unit.Px);

        /// <summary>
        ///     Gets a medium length value.
        /// </summary>
        public static readonly Length Medium = new(3f, Unit.Px);

        /// <summary>
        ///     Gets a thick length value.
        /// </summary>
        public static readonly Length Thick = new(5f, Unit.Px);

        /// <summary>
        ///     Gets the missing value.
        /// </summary>
        public static readonly Length Missing = new(-1f, Unit.Ch);

        public Length(float value, Unit unit)
        {
            Value = value;
            Type = unit;
        }

        /// <summary>
        ///     Gets if the length is given in absolute units.
        ///     Such a length may be converted to pixels.
        /// </summary>
        public bool IsAbsolute =>
            Type == Unit.In || Type == Unit.Mm || Type == Unit.Pc || Type == Unit.Px ||
            Type == Unit.Pt || Type == Unit.Cm;

        /// <summary>
        ///     Gets if the length is given in relative units.
        ///     Such a length cannot be converted to pixels.
        /// </summary>
        public bool IsRelative => !IsAbsolute;

        /// <summary>
        /// Whether this length's <see cref="ToPixels"/> result needs the <c>PixelsPerPoint</c> catch-up
        /// multiply (<c>PdfGenerateConfig.PixelsPerInch / 72</c>, issue #814) to land in a box's internal,
        /// DPI-inflated layout coordinate space: an absolute unit, or a font-relative unit (<c>em</c>/
        /// <c>rem</c>/<c>ex</c>/<c>ch</c>) resolved against a true-point basis (issue #826). A percentage
        /// or container/viewport-relative unit's basis is already reported in that inflated space and
        /// must not be multiplied again. The one shared predicate every <c>PixelsPerPoint</c> catch-up
        /// call site (<c>CssValueParser.ParseLength</c>'s typed-<c>Length</c> and raw-<c>string</c>
        /// overloads, and <c>CalcEvaluator.Evaluate</c>'s per-leaf calc() scaling, issue #829) applies.
        /// </summary>
        internal bool NeedsPixelsPerPointCatchUp => IsAbsolute || IsFontRelative;

        /// <summary>
        /// Whether this length is relative to the font: <c>em</c>/<c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/
        /// <c>lh</c> (the element's own font) or their root-element counterparts <c>rem</c>/<c>rex</c>/
        /// <c>rch</c>/<c>rcap</c>/<c>ric</c>/<c>rlh</c> (CSS Values and Units 4 §6.1). The one shared
        /// predicate for "resolves against a font basis", so no call site keeps its own list of them.
        /// </summary>
        internal bool IsFontRelative =>
            Type is Unit.Em or Unit.Ex or Unit.Ch or Unit.Cap or Unit.Ic or Unit.Lh
                or Unit.Rem or Unit.Rex or Unit.Rch or Unit.Rcap or Unit.Ric or Unit.Rlh;

        /// <summary>
        /// Whether <see cref="ToPixels"/> actually reads its <c>containerWidthPt</c>/<c>containerHeightPt</c>/
        /// <c>containerInlineSizePt</c>/<c>containerBlockSizePt</c> parameters - true only for the six
        /// CSS Containment 3 §6.2 <c>cq*</c> units. Lets a caller skip
        /// <see cref="Html.Core.Dom.CssBox.GetContainerRelativeUnitBasis"/>'s ancestor walk (which finds
        /// the nearest <c>@container</c> query container) for the overwhelming majority of lengths, which
        /// don't use a container-relative unit at all - see <c>CssValueParser.ParseLength</c>.
        /// </summary>
        internal bool IsContainerRelative =>
            Type is Unit.Cqw or Unit.Cqh or Unit.Cqi or Unit.Cqb or Unit.Cqmin or Unit.Cqmax;

        /// <summary>
        ///     Gets the type of the length.
        /// </summary>
        public Unit Type { get; }

        /// <summary>
        ///     Gets the value of the length.
        /// </summary>
        public float Value { get; }

        /// <summary>
        ///     Gets the representation of the unit as a string.
        /// </summary>
        public string UnitString
        {
            get
            {
                switch (Type)
                {
                    case Unit.Px:
                        return UnitNames.Px;
                    case Unit.Em:
                        return UnitNames.Em;
                    case Unit.Ex:
                        return UnitNames.Ex;
                    case Unit.Cm:
                        return UnitNames.Cm;
                    case Unit.Mm:
                        return UnitNames.Mm;
                    case Unit.In:
                        return UnitNames.In;
                    case Unit.Pt:
                        return UnitNames.Pt;
                    case Unit.Pc:
                        return UnitNames.Pc;
                    case Unit.Ch:
                        return UnitNames.Ch;
                    case Unit.Rem:
                        return UnitNames.Rem;
                    case Unit.Cap:
                        return UnitNames.Cap;
                    case Unit.Ic:
                        return UnitNames.Ic;
                    case Unit.Lh:
                        return UnitNames.Lh;
                    case Unit.Rex:
                        return UnitNames.Rex;
                    case Unit.Rch:
                        return UnitNames.Rch;
                    case Unit.Rcap:
                        return UnitNames.Rcap;
                    case Unit.Ric:
                        return UnitNames.Ric;
                    case Unit.Rlh:
                        return UnitNames.Rlh;
                    case Unit.Vw:
                        return UnitNames.Vw;
                    case Unit.Vh:
                        return UnitNames.Vh;
                    case Unit.Vmin:
                        return UnitNames.Vmin;
                    case Unit.Vmax:
                        return UnitNames.Vmax;
                    case Unit.Percent:
                        return UnitNames.Percent;
                    case Unit.Cqw:
                        return UnitNames.Cqw;
                    case Unit.Cqh:
                        return UnitNames.Cqh;
                    case Unit.Cqi:
                        return UnitNames.Cqi;
                    case Unit.Cqb:
                        return UnitNames.Cqb;
                    case Unit.Cqmin:
                        return UnitNames.Cqmin;
                    case Unit.Cqmax:
                        return UnitNames.Cqmax;
                    case Unit.Vi:
                        return UnitNames.Vi;
                    case Unit.Vb:
                        return UnitNames.Vb;
                    case Unit.Svw:
                        return UnitNames.Svw;
                    case Unit.Svh:
                        return UnitNames.Svh;
                    case Unit.Svi:
                        return UnitNames.Svi;
                    case Unit.Svb:
                        return UnitNames.Svb;
                    case Unit.Svmin:
                        return UnitNames.Svmin;
                    case Unit.Svmax:
                        return UnitNames.Svmax;
                    case Unit.Lvw:
                        return UnitNames.Lvw;
                    case Unit.Lvh:
                        return UnitNames.Lvh;
                    case Unit.Lvi:
                        return UnitNames.Lvi;
                    case Unit.Lvb:
                        return UnitNames.Lvb;
                    case Unit.Lvmin:
                        return UnitNames.Lvmin;
                    case Unit.Lvmax:
                        return UnitNames.Lvmax;
                    case Unit.Dvw:
                        return UnitNames.Dvw;
                    case Unit.Dvh:
                        return UnitNames.Dvh;
                    case Unit.Dvi:
                        return UnitNames.Dvi;
                    case Unit.Dvb:
                        return UnitNames.Dvb;
                    case Unit.Dvmin:
                        return UnitNames.Dvmin;
                    case Unit.Dvmax:
                        return UnitNames.Dvmax;
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>
        ///     Compares the magnitude of two lengths.
        /// </summary>
        public static bool operator >=(Length a, Length b)
        {
            var result = a.CompareTo(b);
            return result == 0 || result == 1;
        }

        /// <summary>
        ///     Compares the magnitude of two lengths.
        /// </summary>
        public static bool operator >(Length a, Length b)
        {
            return a.CompareTo(b) == 1;
        }

        /// <summary>
        ///     Compares the magnitude of two lengths.
        /// </summary>
        public static bool operator <=(Length a, Length b)
        {
            var result = a.CompareTo(b);
            return result == 0 || result == -1;
        }

        /// <summary>
        ///     Compares the magnitude of two lengths.
        /// </summary>
        public static bool operator <(Length a, Length b)
        {
            return a.CompareTo(b) == -1;
        }

        /// <summary>
        ///     Compares the current length against the given one.
        /// </summary>
        /// <param name="other">The length to compare to.</param>
        /// <returns>The result of the comparison.</returns>
        public int CompareTo(Length other)
        {
            if (Type == other.Type) return Value.CompareTo(other.Value);

            if (IsAbsolute && other.IsAbsolute) return ToPixel().CompareTo(other.ToPixel());

            return 0;
        }

        public static bool TryParse(string s, out Length result)
        {
            var unitString = s.StylesheetUnit(out var value);
            var unit = GetUnit(unitString);

            if (unit != Unit.None)
            {
                result = new Length(value, unit);
                return true;
            }

            // Only a genuinely parsed bare "0" may omit its unit (CSS Values & Units §5.1) —
            // StylesheetUnit returns an empty unit string for a plain number but null when it
            // couldn't tokenize a number at all, and both leave value at 0, so the unit string
            // must be checked to avoid accepting arbitrary non-length input as zero.
            if (unitString is { Length: 0 } && value == 0f)
            {
                result = Zero;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Maps a unit name to its <see cref="Unit"/>, case-insensitively (CSS unit names are
        /// ASCII case-insensitive); <see cref="Unit.None"/> when it is not a known unit. The lower-casing
        /// only runs on the miss path, and only when the name has an upper-case letter.</summary>
        public static Unit GetUnit(string s)
        {
            var unit = GetLowercaseUnit(s);
            if (unit != Unit.None || s is null) return unit;

            foreach (var c in s)
            {
                if (char.IsUpper(c)) return GetLowercaseUnit(s.ToLowerInvariant());
            }

            return Unit.None;
        }

        private static Unit GetLowercaseUnit(string s)
        {
            return s switch
            {
                "ch" => Unit.Ch,
                "cm" => Unit.Cm,
                "em" => Unit.Em,
                "ex" => Unit.Ex,
                "in" => Unit.In,
                "mm" => Unit.Mm,
                "pc" => Unit.Pc,
                "pt" => Unit.Pt,
                "px" => Unit.Px,
                "rem" => Unit.Rem,
                "cap" => Unit.Cap,
                "ic" => Unit.Ic,
                "lh" => Unit.Lh,
                "rex" => Unit.Rex,
                "rch" => Unit.Rch,
                "rcap" => Unit.Rcap,
                "ric" => Unit.Ric,
                "rlh" => Unit.Rlh,
                "vh" => Unit.Vh,
                "vmax" => Unit.Vmax,
                "vmin" => Unit.Vmin,
                "vw" => Unit.Vw,
                "cqw" => Unit.Cqw,
                "cqh" => Unit.Cqh,
                "cqi" => Unit.Cqi,
                "cqb" => Unit.Cqb,
                "cqmin" => Unit.Cqmin,
                "cqmax" => Unit.Cqmax,
                "vi" => Unit.Vi,
                "vb" => Unit.Vb,
                "svw" => Unit.Svw,
                "svh" => Unit.Svh,
                "svi" => Unit.Svi,
                "svb" => Unit.Svb,
                "svmin" => Unit.Svmin,
                "svmax" => Unit.Svmax,
                "lvw" => Unit.Lvw,
                "lvh" => Unit.Lvh,
                "lvi" => Unit.Lvi,
                "lvb" => Unit.Lvb,
                "lvmin" => Unit.Lvmin,
                "lvmax" => Unit.Lvmax,
                "dvw" => Unit.Dvw,
                "dvh" => Unit.Dvh,
                "dvi" => Unit.Dvi,
                "dvb" => Unit.Dvb,
                "dvmin" => Unit.Dvmin,
                "dvmax" => Unit.Dvmax,
                "%" => Unit.Percent,
                _ => Unit.None
            };
        }

        public float ToPixel()
        {
            if (IsRelative)
                throw new InvalidOperationException("A relative unit cannot be converted.");

            return (float)ToPixels(0, 0, 0);
        }

        /// <summary>
        ///     Resolves this length to pixels, given the context needed for relative units
        ///     (<c>em</c>/<c>rem</c>/<c>ex</c>/<c>%</c>). This is the single source of truth for
        ///     "unit + context -> pixels" — <see cref="ToPixel"/> delegates to it for absolute units,
        ///     and layout-time consumers (e.g. calc() evaluation) use it directly for the relative case.
        /// </summary>
        /// <param name="emFactor">Pixels per 1em, i.e. the relevant font size in pixels.</param>
        /// <param name="remFactor">Pixels per 1rem, i.e. the root element's font size in pixels.</param>
        /// <param name="hundredPercent">The pixel value equivalent to 100%.</param>
        /// <param name="containerInlineSizePt">The nearest ancestor query container's own resolved
        /// inline-axis size in points (physical width for a <c>horizontal-tb</c> container, physical
        /// height for <c>vertical-rl</c>/<c>vertical-lr</c> - CSS Writing Modes 4 §7.1), for <c>cqi</c>/
        /// <c>cqmin</c>/<c>cqmax</c> - <c>null</c> when there is no eligible ancestor container (see
        /// <see cref="Html.Core.Dom.CssBox.FindNearestQueryContainer"/>), which falls back to
        /// <paramref name="viewportInlineSizePt"/> (the corresponding small-viewport unit, per CSS
        /// Containment 3 §6.2).</param>
        /// <param name="containerBlockSizePt">The nearest ancestor query container's own resolved
        /// block-axis size in points (the physical axis orthogonal to <paramref name="containerInlineSizePt"/>),
        /// for <c>cqb</c>/<c>cqmin</c>/<c>cqmax</c> - <c>null</c> with no eligible container, or when the
        /// eligible container is <c>inline-size</c>-only (it doesn't track the block axis either), which
        /// falls back to <paramref name="viewportBlockSizePt"/> for that axis.</param>
        /// <param name="viewportWidthPt">The page box's own width in points, for <c>vw</c> and its
        /// <c>sv*</c>/<c>lv*</c>/<c>dv*</c> variants, and as the <c>cqw</c> fallback - <c>null</c> when no
        /// page context is available (see <see cref="Html.Core.Dom.CssBox.GetViewportUnitBasis"/>), which
        /// resolves those units to 0. Always the page's physical width, regardless of anyone's
        /// <c>writing-mode</c> (CSS Values and Units 4 §6.2) - unlike <paramref name="viewportInlineSizePt"/>.</param>
        /// <param name="viewportHeightPt">The page box's own height in points, for <c>vh</c> and its
        /// <c>sv*</c>/<c>lv*</c>/<c>dv*</c> variants, and as the <c>cqh</c> fallback - <c>null</c> when no
        /// page context is available. Always the page's physical height, regardless of anyone's
        /// <c>writing-mode</c> - unlike <paramref name="viewportBlockSizePt"/>.</param>
        /// <param name="containerWidthPt">The nearest ancestor query container's own resolved physical
        /// width in points, for <c>cqw</c> - <c>null</c> with no eligible container, which falls back to
        /// <paramref name="viewportWidthPt"/>. Always physical, unlike <paramref name="containerInlineSizePt"/>.</param>
        /// <param name="containerHeightPt">The nearest ancestor query container's own resolved physical
        /// height in points, for <c>cqh</c> - <c>null</c> with no eligible container, or an
        /// <c>inline-size</c>-only one, which falls back to <paramref name="viewportHeightPt"/>. Always
        /// physical, unlike <paramref name="containerBlockSizePt"/>.</param>
        /// <param name="viewportInlineSizePt">The page's own size along the root element's own inline
        /// axis in points (physical width for a <c>horizontal-tb</c> root, physical height for
        /// <c>vertical-rl</c>/<c>vertical-lr</c> - CSS Writing Modes 4 §7.1), for <c>vi</c> and its
        /// <c>sv*</c>/<c>lv*</c>/<c>dv*</c> variants, and as the <c>cqi</c> no-container fallback (CSS
        /// Containment 3 §6.2's <c>svi</c> fallback) - <c>null</c> when no page context is available.</param>
        /// <param name="viewportBlockSizePt">The page's own size along the root element's own block axis
        /// in points (the physical axis orthogonal to <paramref name="viewportInlineSizePt"/>), for
        /// <c>vb</c> and its <c>sv*</c>/<c>lv*</c>/<c>dv*</c> variants, and as the <c>cqb</c> no-container
        /// fallback - <c>null</c> when no page context is available.</param>
        /// <param name="fonts">Where <c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/<c>lh</c> (and the root-element
        /// <c>rex</c>/<c>rch</c>/<c>rcap</c>/<c>ric</c>/<c>rlh</c>) read the used font's measurements, as ratios
        /// of that font's em. <c>null</c> for a caller with no font in scope, which takes the spec's fallback
        /// for each (<see cref="FontMetricRatios.Approximate"/>).</param>
        internal double ToPixels(double emFactor, double remFactor, double hundredPercent,
            double? containerInlineSizePt = null, double? containerBlockSizePt = null,
            double? viewportWidthPt = null, double? viewportHeightPt = null,
            double? containerWidthPt = null, double? containerHeightPt = null,
            double? viewportInlineSizePt = null, double? viewportBlockSizePt = null,
            IFontMetricSource fonts = null)
        {
            // The engine's internal layout unit is 1 point (PixelsPerInch defaults to 72), so
            // physical units (in/cm/mm/pc/pt) resolve directly against points. CSS px resolves
            // spec-correctly at 1px = 1/96in = 0.75pt (PointsPerPx) in every context - font sizes,
            // box lengths, and @page geometry all share this one conversion.
            return Type switch
            {
                Unit.Em => emFactor * Value,
                Unit.Rem => remFactor * Value,
                // CSS Values and Units 4 §6.1: ex/ch/cap/ic/lh are measured from the used font. Each is
                // expressed as a ratio of that font's em (IFontMetricSource) so it scales by the same
                // emFactor every other relative unit uses - which keeps every caller's basis override and
                // the PixelsPerPoint catch-up untouched. With no source the spec's own "impossible or
                // impractical to determine" fallback applies (FontMetricRatios.Approximate).
                Unit.Ex => FontRatio(fonts, FontMetric.Ex, false) * emFactor * Value,
                Unit.Ch => FontRatio(fonts, FontMetric.Ch, false) * emFactor * Value,
                Unit.Cap => FontRatio(fonts, FontMetric.Cap, false) * emFactor * Value,
                Unit.Ic => FontRatio(fonts, FontMetric.Ic, false) * emFactor * Value,
                Unit.Lh => FontRatio(fonts, FontMetric.Lh, false) * emFactor * Value,
                Unit.Rex => FontRatio(fonts, FontMetric.Ex, true) * remFactor * Value,
                Unit.Rch => FontRatio(fonts, FontMetric.Ch, true) * remFactor * Value,
                Unit.Rcap => FontRatio(fonts, FontMetric.Cap, true) * remFactor * Value,
                Unit.Ric => FontRatio(fonts, FontMetric.Ic, true) * remFactor * Value,
                Unit.Rlh => FontRatio(fonts, FontMetric.Lh, true) * remFactor * Value,
                Unit.Px => PointsPerPx * Value,
                Unit.In => // 1 in = 72 pt
                    72d * Value,
                Unit.Mm => // 1 mm = 72/25.4 pt
                    (72d / 25.4d) * Value,
                Unit.Pc => // 1 pc = 12 pt
                    12d * Value,
                Unit.Pt => // 1 pt = 1 pt
                    Value,
                Unit.Cm => // 1 cm = 72/2.54 pt
                    (72d / 2.54d) * Value,
                Unit.Percent => hundredPercent / 100d * Value,
                Unit.Cqw => (containerWidthPt ?? viewportWidthPt ?? 0d) / 100d * Value,
                Unit.Cqh => (containerHeightPt ?? viewportHeightPt ?? 0d) / 100d * Value,
                Unit.Cqi => (containerInlineSizePt ?? viewportInlineSizePt ?? 0d) / 100d * Value,
                Unit.Cqb => (containerBlockSizePt ?? viewportBlockSizePt ?? 0d) / 100d * Value,
                // CSS Containment 3 §6.2: cqmin/cqmax are the smaller/larger of cqi and cqb - not cqw/cqh.
                Unit.Cqmin => Math.Min(containerInlineSizePt ?? viewportInlineSizePt ?? 0d, containerBlockSizePt ?? viewportBlockSizePt ?? 0d) / 100d * Value,
                Unit.Cqmax => Math.Max(containerInlineSizePt ?? viewportInlineSizePt ?? 0d, containerBlockSizePt ?? viewportBlockSizePt ?? 0d) / 100d * Value,
                Unit.Vw or Unit.Svw or Unit.Lvw or Unit.Dvw =>
                    (viewportWidthPt ?? 0d) / 100d * Value,
                Unit.Vh or Unit.Svh or Unit.Lvh or Unit.Dvh =>
                    (viewportHeightPt ?? 0d) / 100d * Value,
                Unit.Vi or Unit.Svi or Unit.Lvi or Unit.Dvi =>
                    (viewportInlineSizePt ?? 0d) / 100d * Value,
                Unit.Vb or Unit.Svb or Unit.Lvb or Unit.Dvb =>
                    (viewportBlockSizePt ?? 0d) / 100d * Value,
                // CSS Values and Units 4 §6.2: vmin/vmax are the smaller/larger of vw and vh - not vi/vb.
                Unit.Vmin or Unit.Svmin or Unit.Lvmin or Unit.Dvmin =>
                    Math.Min(viewportWidthPt ?? 0d, viewportHeightPt ?? 0d) / 100d * Value,
                Unit.Vmax or Unit.Svmax or Unit.Lvmax or Unit.Dvmax =>
                    Math.Max(viewportWidthPt ?? 0d, viewportHeightPt ?? 0d) / 100d * Value,
                _ => 0d
            };
        }

        private static double FontRatio(IFontMetricSource fonts, FontMetric metric, bool rootElement) =>
            fonts?.GetRatio(metric, rootElement) ?? FontMetricRatios.Approximate(metric);

        public float To(Unit unit)
        {
            var value = ToPixel();

            return unit switch
            {
                Unit.In => // 1 in = 72 pt
                    value / 72f,
                Unit.Mm => // 1 mm = 72/25.4 pt
                    value * 25.4f / 72f,
                Unit.Pc => // 1 pc = 12 pt
                    value / 12f,
                Unit.Pt => // 1 pt = 1 pt
                    value,
                Unit.Cm => // 1 cm = 72/2.54 pt
                    value * 2.54f / 72f,
                Unit.Px => // 1 px = 0.75 pt (1/96 in)
                    (float)(value / PointsPerPx),
                _ => throw new InvalidOperationException("An absolute unit cannot be converted to a relative one.")
            };
        }

        public bool Equals(Length other)
        {
            return Value == other.Value && Type == other.Type;
        }

        internal enum Unit : byte
        {
            None,
            Px,
            Em,
            Ex,
            Cm,
            Mm,
            In,
            Pt,
            Pc,
            Ch,
            Rem,
            Vw,
            Vh,
            Vmin,
            Vmax,
            Percent,
            // CSS Containment 3 §6.2 container-relative units. Resolved against the nearest ancestor
            // query container's own content-box size (CssBox.FindNearestQueryContainer) - see ToPixels'
            // containerInlineSizePt/containerBlockSizePt parameters. With no eligible ancestor container,
            // these fall back to the corresponding small-viewport unit (Cqw -> Svw, etc.) via ToPixels'
            // viewportWidthPt/viewportHeightPt parameters, per CSS Containment 3 §6.2/CSS Values 4 §6.2.
            Cqw,
            Cqh,
            Cqi,
            Cqb,
            Cqmin,
            Cqmax,
            // CSS Values and Units 4 §6.2 small/large/dynamic viewport-percentage units. Vi/Vb (like
            // Cqi/Cqb) track the root element's/container's own writing-mode - see ToPixels'
            // viewportInlineSizePt/viewportBlockSizePt parameters. Small/large/dynamic variants all
            // resolve identically here (see ToPixels) - a PDF page box has no scrollbar or dynamic browser
            // chrome to distinguish them by.
            Vi,
            Vb,
            Svw,
            Svh,
            Svi,
            Svb,
            Svmin,
            Svmax,
            Lvw,
            Lvh,
            Lvi,
            Lvb,
            Lvmin,
            Lvmax,
            Dvw,
            Dvh,
            Dvi,
            Dvb,
            Dvmin,
            Dvmax,
            // CSS Values and Units 4 §6.1 font-relative units measured from the used font (cap height,
            // the "水" advance, the used line-height) and the root-element counterparts of ex/ch/cap/ic/lh.
            Cap,
            Ic,
            Lh,
            Rex,
            Rch,
            Rcap,
            Ric,
            Rlh
        }

        /// <summary>
        ///     Checks the equality of the two given lengths.
        /// </summary>
        /// <param name="a">The left length.</param>
        /// <param name="b">The right length.</param>
        /// <returns>True if both lengths are equal, otherwise false.</returns>
        public static bool operator ==(Length a, Length b)
        {
            return a.Equals(b);
        }

        /// <summary>
        ///     Checks the inequality of the two given lengths.
        /// </summary>
        /// <param name="a">The left length.</param>
        /// <param name="b">The right length.</param>
        /// <returns>True if both lengths are not equal, otherwise false.</returns>
        public static bool operator !=(Length a, Length b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        ///     Tests if another object is equal to this object.
        /// </summary>
        /// <param name="obj">The object to test with.</param>
        /// <returns>True if the two objects are equal, otherwise false.</returns>
        public override bool Equals(object obj)
        {
            var other = obj as Length?;

            if (other != null)
                return Equals(other.Value);

            return false;
        }

        /// <summary>
        ///     Returns a hash code that defines the current length.
        /// </summary>
        /// <returns>The integer value of the hashcode.</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            var unit = Value == 0f ? string.Empty : UnitString;
            return string.Concat(Value.ToString(), unit);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            var unit = Value == 0f ? string.Empty : UnitString;
            return string.Concat(Value.ToString(format, formatProvider), unit);
        }
    }
}