#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary>The CSS Borders 4 corner longhand names (physical top-left first, then flow-relative start-start first).</summary>
    internal static class CornerNames
    {
        public static readonly string[] PhysicalCorners = ["top-left", "top-right", "bottom-right", "bottom-left"];
        public static readonly string[] LogicalCorners = ["start-start", "start-end", "end-start", "end-end"];

        public static string Shape(string corner) => "corner-" + corner + "-shape";

        public static string Radius(string corner) => "border-" + corner + "-radius";
    }

    /// <summary>One of the CSS Borders 4 corner shorthands: a name, the grammar it parses and the longhands it expands to.</summary>
    internal sealed class CornerShorthandProperty : ShorthandProperty
    {
        private readonly IValueConverter _converter;

        internal CornerShorthandProperty(string name, IValueConverter converter)
            : base(name, PropertyFlags.Animatable)
        {
            _converter = converter;
        }

        internal override IValueConverter Converter => _converter;
    }

    /// <summary>
    /// The shorthands of css-borders-4 §3.6 (<c>border-top-radius</c>...), §3.8 (<c>corner-shape</c>,
    /// <c>corner-top-shape</c>...) and §3.9 (<c>corner-top-left</c>, <c>corner-top</c>, <c>corner</c>).
    /// </summary>
    internal static class CornerShorthands
    {
        // Side name -> the two corners it covers, in the order the shorthand's two values apply to them.
        private static readonly (string Side, string First, string Second, bool Logical)[] Sides =
        [
            ("top", "top-left", "top-right", false),
            ("right", "top-right", "bottom-right", false),
            ("bottom", "bottom-left", "bottom-right", false),
            ("left", "top-left", "bottom-left", false),
            ("block-start", "start-start", "start-end", true),
            ("block-end", "end-start", "end-end", true),
            ("inline-start", "start-start", "end-start", true),
            ("inline-end", "start-end", "end-end", true),
        ];

        /// <summary>Hands every shorthand to <paramref name="add"/> as (name, converter, longhands, logical).</summary>
        public static void Register(Action<string, IValueConverter, string[], bool> add)
        {
            var radiusLonghands = new[]
            {
                PropertyNames.BorderTopLeftRadius, PropertyNames.BorderTopRightRadius,
                PropertyNames.BorderBottomRightRadius, PropertyNames.BorderBottomLeftRadius,
            };
            var shapeLonghands = new string[4];
            for (var i = 0; i < 4; i++) shapeLonghands[i] = CornerNames.Shape(CornerNames.PhysicalCorners[i]);

            // corner-shape: <shape>{1,4}
            add("corner-shape", CornerShapeConverter.Periodic(shapeLonghands).OrDefault(), shapeLonghands, false);

            // corner: <corner-shape> || <border-radius>
            add("corner", WithAny(
                    CornerShapeConverter.Periodic(shapeLonghands).Option().For(shapeLonghands),
                    BorderRadiusShorthandConverter.Option().For(radiusLonghands)).OrDefault(),
                Concat(shapeLonghands, radiusLonghands), false);

            foreach (var (side, first, second, logical) in Sides)
            {
                var shapes = new[] { CornerNames.Shape(first), CornerNames.Shape(second) };
                var radii = new[] { CornerNames.Radius(first), CornerNames.Radius(second) };

                // corner-top-shape: <shape>{1,2}
                add("corner-" + side + "-shape", CornerShapeConverter.Periodic(shapes).OrDefault(), shapes, logical);

                // border-top-radius: <length-percentage>{1,2} [ / <length-percentage>{1,2} ]?
                add("border-" + side + "-radius", new BorderRadiusConverter(radii).OrDefault(), radii, logical);

                // corner-top: <corner-top-shape> || <border-top-radius>
                add("corner-" + side, WithAny(
                        CornerShapeConverter.Periodic(shapes).Option().For(shapes),
                        new BorderRadiusConverter(radii).Option().For(radii)).OrDefault(),
                    Concat(shapes, radii), logical);
            }

            var corners = new List<string>();
            corners.AddRange(CornerNames.PhysicalCorners);
            corners.AddRange(CornerNames.LogicalCorners);
            foreach (var corner in corners)
            {
                var shape = CornerNames.Shape(corner);
                var radius = CornerNames.Radius(corner);

                // corner-top-left: <corner-top-left-shape> || <border-top-left-radius>
                add("corner-" + corner, WithAny(
                        CornerShapeConverter.Option().For(shape),
                        Converters.BorderRadiusConverter.Option().For(radius)).OrDefault(),
                    [shape, radius], Array.IndexOf(CornerNames.LogicalCorners, corner) >= 0);
            }
        }

        private static string[] Concat(string[] a, string[] b)
        {
            var result = new string[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }
    }
}
