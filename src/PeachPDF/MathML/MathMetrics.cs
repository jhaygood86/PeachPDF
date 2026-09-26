using PeachDrawing.Text.OpenType;
using System;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;

namespace PeachPDF.MathML
{
    /// <summary>
    /// Scales a resolved math font's <see cref="MathConstantsTable"/> (design units) into the working
    /// unit space <c>RGraphics</c> measures/draws in, via the same
    /// <c>sizePt / unitsPerEm * PixelsPerPoint</c> formula <c>FontAdapter.ScaleDesignUnits</c> already
    /// uses for vertical metrics. When the resolved font has no MATH table, each named accessor falls
    /// back to a fixed ratio of the current size instead (MathML Core's own documented fallback
    /// strategy) - see each accessor's own fallback value, drawn from either the OpenType MATH spec's
    /// own "Suggested" column or common TeX-book defaults where the spec leaves the choice open.
    /// </summary>
    internal sealed class MathMetrics
    {
        readonly MathConstantsTable? _constants;

        public double UnitsPerEm { get; }
        public double PixelsPerPoint { get; }
        public Func<double, Html.Adapters.RFont> ResolveFont { get; }
        public RGraphics Graphics { get; }

        /// <summary>The root element's font, for <c>rex</c>/<c>rch</c>/<c>rcap</c>/<c>ric</c>/<c>rlh</c>; null takes each
        /// unit's spec fallback.</summary>
        public IFontMetricSource? RootFonts { get; }

        /// <summary>What 1rem is, in points.</summary>
        public double RootSizePt { get; }

        public MathMetrics(MathTable? mathTable, double unitsPerEm, double pixelsPerPoint,
            Func<double, Html.Adapters.RFont> resolveFont, RGraphics graphics,
            IFontMetricSource? rootFonts = null, double rootSizePt = 0)
        {
            RootFonts = rootFonts;
            RootSizePt = rootSizePt;
            _constants = mathTable?.Constants;
            UnitsPerEm = unitsPerEm;
            PixelsPerPoint = pixelsPerPoint;
            ResolveFont = resolveFont;
            Graphics = graphics;
        }

        double Scale(double designUnits, double sizePt) => designUnits * sizePt / UnitsPerEm * PixelsPerPoint;

        double Val(Func<MathConstantsTable, double> select, double fallbackEm, double sizePt) =>
            _constants is { } c ? Scale(select(c), sizePt) : fallbackEm * sizePt * PixelsPerPoint;

        public double ScriptPercentScaleDown => _constants is { } c ? c.ScriptPercentScaleDown / 100.0 : 0.8;
        public double ScriptScriptPercentScaleDown => _constants is { } c ? c.ScriptScriptPercentScaleDown / 100.0 : 0.6;

        public double AxisHeight(double sizePt) => Val(c => c.AxisHeight, 0.25, sizePt);
        public double FractionRuleThickness(double sizePt) => Val(c => c.FractionRuleThickness, 0.04, sizePt);
        public double FractionNumeratorGapMin(double sizePt) => Val(c => c.FractionNumeratorGapMin, 0.04, sizePt);
        public double FractionDenominatorGapMin(double sizePt) => Val(c => c.FractionDenominatorGapMin, 0.04, sizePt);
        public double RadicalRuleThickness(double sizePt) => Val(c => c.RadicalRuleThickness, 0.04, sizePt);
        public double RadicalVerticalGap(double sizePt) => Val(c => c.RadicalVerticalGap, 0.05, sizePt);
        public double RadicalExtraAscender(double sizePt) => Val(c => c.RadicalExtraAscender, 0.04, sizePt);
        public double RadicalKernBeforeDegree(double sizePt) => Val(c => c.RadicalKernBeforeDegree, 5.0 / 18, sizePt);
        public double RadicalKernAfterDegree(double sizePt) => Val(c => c.RadicalKernAfterDegree, -10.0 / 18, sizePt);
        public double RadicalDegreeBottomRaisePercent(double sizePt) => _constants?.RadicalDegreeBottomRaisePercent ?? 60;
        public double SubscriptShiftDown(double sizePt) => Val(c => c.SubscriptShiftDown, 0.15, sizePt);
        public double SubscriptTopMax(double sizePt) => Val(c => c.SubscriptTopMax, 0.4, sizePt);
        public double SuperscriptShiftUp(double sizePt) => Val(c => c.SuperscriptShiftUp, 0.36, sizePt);
        public double SubSuperscriptGapMin(double sizePt) => Val(c => c.SubSuperscriptGapMin, 0.15, sizePt);
        public double StretchStackGapAboveMin(double sizePt) => Val(c => c.StretchStackGapAboveMin, 0.1, sizePt);
        public double StackGapMin(double sizePt) => Val(c => c.StackGapMin, 0.15, sizePt);

        /// <summary>MathML's <c>thickmathspace</c> named length (5 mu = 5/18 em) - this engine's
        /// simplified default inter-operator spacing (see MathLayoutEngine's file header).</summary>
        public double ThickMathSpace(double sizePt) => sizePt * 5.0 / 18 * PixelsPerPoint;

        double OwnRatio(FontMetric metric, double sizePt) =>
            FontMetricMeasurement.Ratio(ResolveFont(sizePt), metric, PixelsPerPoint);

        double RootRatio(FontMetric metric) => RootFonts?.GetRatio(metric, true) ?? FontMetricRatios.Approximate(metric);

        /// <summary>Converts a <see cref="MathLength"/> to the working unit space at
        /// <paramref name="sizePt"/> (the reference size <c>em</c>/<c>%</c>/<c>mu</c> resolve against).</summary>
        public double Length(MathLength length, double sizePt) => length.Unit switch
        {
            MathLengthUnit.Pt => length.Value * PixelsPerPoint,
            MathLengthUnit.Px => length.Value * 0.75 * PixelsPerPoint,
            MathLengthUnit.In => length.Value * 72 * PixelsPerPoint,
            MathLengthUnit.Cm => length.Value * 72 / 2.54 * PixelsPerPoint,
            MathLengthUnit.Mm => length.Value * 72 / 25.4 * PixelsPerPoint,
            MathLengthUnit.Pc => length.Value * 12 * PixelsPerPoint,
            MathLengthUnit.Em => length.Value * sizePt * PixelsPerPoint,
            // ex/ch/cap/ic/lh are measured from the math font at this size (CSS Values and Units 4 §6.1.1); each
            // ratio is a fraction of that font's em, so it scales by the same size em/% do.
            MathLengthUnit.Ex => length.Value * OwnRatio(FontMetric.Ex, sizePt) * sizePt * PixelsPerPoint,
            MathLengthUnit.Ch => length.Value * OwnRatio(FontMetric.Ch, sizePt) * sizePt * PixelsPerPoint,
            MathLengthUnit.Cap => length.Value * OwnRatio(FontMetric.Cap, sizePt) * sizePt * PixelsPerPoint,
            MathLengthUnit.Ic => length.Value * OwnRatio(FontMetric.Ic, sizePt) * sizePt * PixelsPerPoint,
            MathLengthUnit.Lh => length.Value * OwnRatio(FontMetric.Lh, sizePt) * sizePt * PixelsPerPoint,
            MathLengthUnit.Rem => length.Value * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Rex => length.Value * RootRatio(FontMetric.Ex) * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Rch => length.Value * RootRatio(FontMetric.Ch) * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Rcap => length.Value * RootRatio(FontMetric.Cap) * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Ric => length.Value * RootRatio(FontMetric.Ic) * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Rlh => length.Value * RootRatio(FontMetric.Lh) * RootSizePt * PixelsPerPoint,
            MathLengthUnit.Percent => length.Value / 100.0 * sizePt * PixelsPerPoint,
            MathLengthUnit.Mu => length.Value / 18.0 * sizePt * PixelsPerPoint,
            _ => 0,
        };
    }
}
