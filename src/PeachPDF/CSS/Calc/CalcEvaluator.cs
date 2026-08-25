using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The layout-time context needed to resolve relative units within a calc-family expression: how
    /// many pixels 1em/1rem are, and what 100% means for this particular call site (e.g. containing-block
    /// width for <c>width</c>, parent font-size for <c>font-size</c>'s em-relative resolution).
    /// </summary>
    internal readonly struct CalcContext
    {
        public CalcContext(double hundredPercent, double emFactor, double remFactor, bool returnPoints = false,
            double? containerInlineSizePt = null, double? containerBlockSizePt = null,
            double? viewportWidthPt = null, double? viewportHeightPt = null,
            double? containerWidthPt = null, double? containerHeightPt = null,
            double? viewportInlineSizePt = null, double? viewportBlockSizePt = null,
            double pixelsPerPoint = 1.0)
        {
            HundredPercent = hundredPercent;
            EmFactor = emFactor;
            RemFactor = remFactor;
            ReturnPoints = returnPoints;
            ContainerInlineSizePt = containerInlineSizePt;
            ContainerBlockSizePt = containerBlockSizePt;
            ViewportWidthPt = viewportWidthPt;
            ViewportHeightPt = viewportHeightPt;
            ContainerWidthPt = containerWidthPt;
            ContainerHeightPt = containerHeightPt;
            ViewportInlineSizePt = viewportInlineSizePt;
            ViewportBlockSizePt = viewportBlockSizePt;
            PixelsPerPoint = pixelsPerPoint;
        }

        public double HundredPercent { get; }
        public double EmFactor { get; }
        public double RemFactor { get; }

        /// <summary>
        /// Mirrors ParseLength's returnPoints parameter: when the caller's em/rem/percent factors are
        /// already expressed in points (as CssBox.FontSize's caller does), a bare <c>pt</c> leaf
        /// must bypass the normal pt-&gt;px factor rather than being converted into this already-points space.
        /// </summary>
        public bool ReturnPoints { get; }

        /// <summary>See <see cref="Length.ToPixels"/>'s parameter of the same name - the nearest ancestor
        /// query container's own inline/block axis size, for a <c>cqi</c>/<c>cqb</c>/<c>cqmin</c>/
        /// <c>cqmax</c> leaf inside calc().</summary>
        public double? ContainerInlineSizePt { get; }
        public double? ContainerBlockSizePt { get; }

        /// <summary>See <see cref="Length.ToPixels"/>'s parameter of the same name - the page box's own
        /// physical size, for a <c>vw</c>/<c>vh</c>/etc. leaf inside calc(), and the <c>cqw</c>/<c>cqh</c>
        /// no-container fallback.</summary>
        public double? ViewportWidthPt { get; }
        public double? ViewportHeightPt { get; }

        /// <summary>See <see cref="Length.ToPixels"/>'s parameter of the same name - the nearest ancestor
        /// query container's own physical size, for a <c>cqw</c>/<c>cqh</c> leaf inside calc().</summary>
        public double? ContainerWidthPt { get; }
        public double? ContainerHeightPt { get; }

        /// <summary>See <see cref="Length.ToPixels"/>'s parameter of the same name - the page's own size
        /// along the root element's own inline/block axis, for a <c>vi</c>/<c>vb</c>/etc. leaf inside
        /// calc(), and the <c>cqi</c>/<c>cqb</c> no-container fallback.</summary>
        public double? ViewportInlineSizePt { get; }
        public double? ViewportBlockSizePt { get; }

        /// <summary>
        /// The ambient <c>PixelsPerPoint</c> (<c>PdfGenerateConfig.PixelsPerInch / 72</c>) an absolute or
        /// <c>em</c>/<c>rem</c>/<c>ex</c>/<c>ch</c> calc() leaf needs multiplied in, mirroring
        /// <see cref="PeachPDF.Html.Core.Parse.CssValueParser.ParseLength(PeachPDF.CSS.Length, double, PeachPDF.Html.Core.Dom.CssBox)"/>'s own
        /// catch-up multiply for a literal length (issues #814/#826) - see
        /// <see cref="CalcEvaluator.Evaluate"/>'s <c>DimensionCalcNode</c> case (issue #829). Defaults to
        /// <c>1.0</c> (a no-op) for every call site with no real box/adapter in scope (font-size
        /// resolution, <c>@page</c> margins), matching every other existing call site of this idiom.
        /// </summary>
        public double PixelsPerPoint { get; }
    }

    /// <summary>
    /// Evaluates a validated calc-family AST to a pixel-space number. This is the one place calc()
    /// numbers actually get computed — called only from Layer B (<see cref="PeachPDF.Html.Core.Parse.CssValueParser.ParseLength(string, double, double, double, string, bool, double?, double?, double?, double?, double?, double?, double?, double?, double)"/>),
    /// since only layout has the <see cref="CalcContext"/> a percentage/em/rem leaf needs to resolve.
    /// Reuses <see cref="Length.ToPixels"/> for every leaf, so no unit-conversion arithmetic is duplicated
    /// here. A null result signals a divide-by-zero; per the type-checker's rules every legal divisor is
    /// a constant that Layer A already folds and rejects at validation time, so in practice this is a
    /// defensive fallback rather than a load-bearing check.
    /// </summary>
    internal static class CalcEvaluator
    {
        public static double? Evaluate(CalcNode node, CalcContext context)
        {
            switch (node)
            {
                case NumberCalcNode number:
                    return number.Value;

                // ReturnPoints mirrors the same-named short-circuit below for a bare pt leaf: a
                // ReturnPoints=true context (today, only FontSizeResolver's font-size resolution) wants
                // its result in true CSS points, never the box's PixelsPerPoint-inflated layout space, so
                // no leaf in that context - Pt or otherwise - ever receives the catch-up multiply below.
                case DimensionCalcNode { Unit: Length.Unit.Pt } dimension when context.ReturnPoints:
                    return dimension.Value;

                case DimensionCalcNode dimension:
                {
                    var asLength = new Length((float)dimension.Value, dimension.Unit);
                    var pixels = asLength.ToPixels(context.EmFactor, context.RemFactor, context.HundredPercent,
                        context.ContainerInlineSizePt, context.ContainerBlockSizePt,
                        context.ViewportWidthPt, context.ViewportHeightPt,
                        context.ContainerWidthPt, context.ContainerHeightPt,
                        context.ViewportInlineSizePt, context.ViewportBlockSizePt);

                    // Per-leaf mirror of Length.NeedsPixelsPerPointCatchUp's use in
                    // CssValueParser.ParseLength (issues #814/#826/#829): an absolute or em/rem/ex/ch leaf
                    // resolves to a true-point value above and needs one extra multiply to land in the
                    // box's PixelsPerPoint-inflated internal layout space. A percentage/container/
                    // viewport-relative leaf's basis is already reported in that inflated space, so it
                    // must not be double-scaled - and, per the ReturnPoints case above, no leaf is scaled
                    // at all in a ReturnPoints=true context.
                    var needsCatchUpMultiply = !context.ReturnPoints && asLength.NeedsPixelsPerPointCatchUp;
                    return needsCatchUpMultiply ? pixels * context.PixelsPerPoint : pixels;
                }

                case PercentageCalcNode percentage:
                    return new Length((float)percentage.Value, Length.Unit.Percent)
                        .ToPixels(context.EmFactor, context.RemFactor, context.HundredPercent);

                case AngleCalcNode angle:
                    // An angle leaf evaluates to radians (the canonical angle unit), so an angle-typed
                    // calc such as `calc(1turn * 0.35)` yields 0.35 turn in radians (a Number leaf stays a
                    // unitless scalar). Length calc()s never contain an angle leaf, so this never perturbs
                    // length evaluation; the only current caller is conic-gradient stop-position parsing.
                    return new Angle((float)angle.Value, angle.Unit).ToRadian();

                case TimeCalcNode time:
                    // Canonical time unit is milliseconds. No layout property evaluates a time calc() today
                    // (these nodes only arise from @property `syntax: "<time>"` validation, which type-checks
                    // rather than evaluates), so this case is defensive completeness paralleling the angle leaf.
                    return new Time((float)time.Value, time.Unit).ToMilliseconds();

                case ResolutionCalcNode resolution:
                    // Canonical resolution unit is dots-per-pixel; same @property-only provenance as TimeCalcNode.
                    return new Resolution((float)resolution.Value, resolution.Unit).ToDotsPerPixel();

                case UnaryCalcNode unary:
                {
                    var operand = Evaluate(unary.Operand, context);
                    return operand is null ? null : unary.Negative ? -operand.Value : operand.Value;
                }

                case BinaryCalcNode binary:
                {
                    var left = Evaluate(binary.Left, context);
                    var right = Evaluate(binary.Right, context);
                    if (left is null || right is null) return null;

                    return binary.Operator switch
                    {
                        '+' => left.Value + right.Value,
                        '-' => left.Value - right.Value,
                        '*' => left.Value * right.Value,
                        '/' => right.Value != 0d ? left.Value / right.Value : null,
                        _ => null
                    };
                }

                case CallCalcNode call:
                {
                    var values = new List<double>(call.Arguments.Count);
                    foreach (var argument in call.Arguments)
                    {
                        var value = Evaluate(argument, context);
                        if (value is null) return null;
                        values.Add(value.Value);
                    }

                    if (call.Name.Isi(FunctionNames.Min)) return Min(values);
                    if (call.Name.Isi(FunctionNames.Max)) return Max(values);
                    if (call.Name.Isi(FunctionNames.Clamp) && values.Count == 3)
                        return values[0] > values[2] ? values[2] : Math.Clamp(values[1], values[0], values[2]);

                    return null;
                }

                default:
                    return null;
            }
        }

        private static double Min(List<double> values)
        {
            var result = values[0];
            for (var i = 1; i < values.Count; i++) if (values[i] < result) result = values[i];
            return result;
        }

        private static double Max(List<double> values)
        {
            var result = values[0];
            for (var i = 1; i < values.Count; i++) if (values[i] > result) result = values[i];
            return result;
        }
    }
}
