using PeachDrawing.Core;
using System;
using System.Numerics;

namespace PeachBrackets.Svg
{
    /// <summary>The box <c>transform-origin</c> and a percentage in a transform are measured in (CSS Transforms 1 section 6, SVG elements).</summary>
    internal enum SvgTransformBox
    {
        /// <summary>The nearest SVG viewport's view box: the default of an SVG element.</summary>
        ViewBox,

        /// <summary>The element's object bounding box (<c>content-box</c> also maps here).</summary>
        FillBox,

        /// <summary>The object bounding box grown by half the stroke width (<c>border-box</c> also maps here).</summary>
        StrokeBox,
    }

    /// <summary>One coordinate of a <c>transform-origin</c>: user units, or a percentage of the reference box.</summary>
    internal readonly record struct SvgOriginComponent(double Value, bool IsPercent);

    /// <summary>
    /// The <c>transform-origin</c> of an SVG element with the <c>transform-box</c> it is measured in. The transform then turns about that point
    /// (<c>translate(o) transform translate(-o)</c>) instead of the user-space origin, which is what an SVG element's initial origin is.
    /// </summary>
    internal sealed class SvgTransformOrigin
    {
        private readonly SvgOriginComponent _x;
        private readonly SvgOriginComponent _y;
        private readonly double _z;
        private readonly SvgTransformBox _box;
        private readonly Rect _viewBox;

        private SvgTransformOrigin(SvgOriginComponent x, SvgOriginComponent y, double z, SvgTransformBox box, Rect viewBox)
        {
            _x = x;
            _y = y;
            _z = z;
            _box = box;
            _viewBox = viewBox;
        }

        /// <summary>The <c>transform-box</c> keywords; <c>content-box</c> is the fill box and <c>border-box</c> the stroke box for an SVG element, anything else is the default.</summary>
        public static SvgTransformBox ParseBox(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "fill-box" or "content-box" => SvgTransformBox.FillBox,
            "stroke-box" or "border-box" => SvgTransformBox.StrokeBox,
            _ => SvgTransformBox.ViewBox,
        };

        /// <summary>
        /// Reads a <c>transform-origin</c> value (one to three of <c>left</c>/<c>center</c>/<c>right</c>/<c>top</c>/<c>bottom</c>/a length/a percentage; the third is a z offset, a length only). Null
        /// when the value is absent or not one: the element keeps the user-space origin.
        /// </summary>
        public static SvgTransformOrigin? Parse(string? value, SvgTransformBox box, Rect viewBox, ISvgLengthBasis? basis)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length is 0 or > 3)
                return null;

            var first = tokens[0].ToLowerInvariant();
            var second = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "center";

            // A keyword on the other axis puts the pair the other way round: `top left`, and a lone `top` or `bottom` is the vertical one.
            if (first is "top" or "bottom" || second is "left" or "right")
                (first, second) = tokens.Length == 1 ? ("center", first) : (second, first);

            if (!TryComponent(first, horizontal: true, basis, out var x) || !TryComponent(second, horizontal: false, basis, out var y))
                return null;

            // The z offset is a length: a keyword or a percentage there makes the whole value invalid.
            var z = 0.0;
            if (tokens.Length == 3)
            {
                if (tokens[2].EndsWith('%') || SvgValueParsers.ParseLength(tokens[2], null, basis) is not { } depth)
                    return null;

                z = depth;
            }

            return new SvgTransformOrigin(x, y, z, box, viewBox);
        }

        private static bool TryComponent(string token, bool horizontal, ISvgLengthBasis? basis, out SvgOriginComponent component)
        {
            switch (token)
            {
                case "center": component = new SvgOriginComponent(50, true); return true;
                case "left" when horizontal: component = new SvgOriginComponent(0, true); return true;
                case "right" when horizontal: component = new SvgOriginComponent(100, true); return true;
                case "top" when !horizontal: component = new SvgOriginComponent(0, true); return true;
                case "bottom" when !horizontal: component = new SvgOriginComponent(100, true); return true;
            }

            if (token.EndsWith('%') && double.TryParse(token[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                component = new SvgOriginComponent(percent, true);
                return true;
            }

            if (SvgValueParsers.ParseLength(token, null, basis) is { } length)
            {
                component = new SvgOriginComponent(length, false);
                return true;
            }

            component = default;
            return false;
        }

        /// <summary><paramref name="matrix"/> turned about this origin on <paramref name="element"/>.</summary>
        public Matrix3x2 Apply(Matrix3x2 matrix, SvgElement element)
        {
            var box = ReferenceBox(element, _box, _viewBox);

            var ox = (float)(box.X + (_x.IsPercent ? _x.Value / 100 * box.Width : _x.Value));
            var oy = (float)(box.Y + (_y.IsPercent ? _y.Value / 100 * box.Height : _y.Value));

            // Row-vector convention: move the origin to the user-space origin, apply the transform, move back.
            return Matrix3x2.CreateTranslation(-ox, -oy) * matrix * Matrix3x2.CreateTranslation(ox, oy);
        }

        /// <summary>The origin point in <paramref name="element"/>'s user space: the point its transform (or, for <c>perspective-origin</c>, its vanishing point) is about.</summary>
        public (double X, double Y, double Z) Resolve(SvgElement element)
        {
            var box = ReferenceBox(element, _box, _viewBox);
            return (box.X + (_x.IsPercent ? _x.Value / 100 * box.Width : _x.Value), box.Y + (_y.IsPercent ? _y.Value / 100 * box.Height : _y.Value), _z);
        }

        /// <summary><paramref name="matrix"/> turned about this origin on <paramref name="element"/>, the z offset included.</summary>
        public Matrix4x4 Apply(Matrix4x4 matrix, SvgElement element)
        {
            var box = ReferenceBox(element, _box, _viewBox);

            var ox = (float)(box.X + (_x.IsPercent ? _x.Value / 100 * box.Width : _x.Value));
            var oy = (float)(box.Y + (_y.IsPercent ? _y.Value / 100 * box.Height : _y.Value));

            return Matrix4x4.CreateTranslation(-ox, -oy, -(float)_z) * matrix * Matrix4x4.CreateTranslation(ox, oy, (float)_z);
        }

        /// <summary>The rectangle a percentage in a transform or its origin is of: the view box, the element's object bounding box, or that grown by half the stroke width.</summary>
        internal static Rect ReferenceBox(SvgElement element, SvgTransformBox box, Rect viewBox) => box switch
        {
            SvgTransformBox.ViewBox => viewBox,
            SvgTransformBox.FillBox => SvgGeometryBounds.GetBoundingBox(element) ?? default,
            _ => Stroked(element),
        };

        private static Rect Stroked(SvgElement element)
        {
            var fill = SvgGeometryBounds.GetBoundingBox(element) ?? default;
            if (element.Stroke.Kind == SvgPaintKind.None)
                return fill;

            var half = element.StrokeWidth / 2;
            return new Rect(fill.X - half, fill.Y - half, fill.Width + element.StrokeWidth, fill.Height + element.StrokeWidth);
        }
    }
}

namespace PeachBrackets.Svg
{
    /// <summary>A CSS <c>transform</c> with a percentage that is of a <c>fill-box</c> or <c>stroke-box</c>: parsed again against the element's geometry when it is read.</summary>
    internal sealed class SvgDeferredTransform(string text, SvgTransformContext context, SvgTransformBox box, bool allowCalc)
    {
        public SvgParsedTransform? Resolve(SvgElement element)
        {
            var reference = SvgTransformOrigin.ReferenceBox(element, box, default);
            return SvgTransformParser.ParseFull(text, context with { ReferenceWidth = reference.Width, ReferenceHeight = reference.Height }, allowCalc: allowCalc);
        }
    }
}
