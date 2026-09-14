using System;
using System.Globalization;

namespace PeachPDF
{
    /// <summary>
    /// A CSS length or percentage for the declarative document-building API
    /// (<see cref="PdfGenerator.CreateDocument"/>) - every size-shaped parameter there (page size,
    /// margins, padding, border width, spacing, column widths, font size) is a <see cref="PdfLength"/>
    /// rather than a raw <see cref="double"/>. Internally this formats to a canonical CSS length token
    /// (e.g. <c>"12pt"</c>, <c>"1.5in"</c>, <c>"50%"</c>) and is resolved by PeachPDF's own existing CSS
    /// length-parsing machinery at layout time - this is not a new, independent unit-conversion
    /// implementation.
    /// </summary>
    public readonly struct PdfLength
    {
        /// <summary>The numeric magnitude, in <see cref="Unit"/>.</summary>
        public double Value { get; }

        /// <summary>The unit <see cref="Value"/> is expressed in.</summary>
        public PdfLengthUnit Unit { get; }

        /// <summary>Creates a length of <paramref name="value"/> <paramref name="unit"/>s (points, if omitted).</summary>
        public PdfLength(double value, PdfLengthUnit unit = PdfLengthUnit.Point)
        {
            Value = value;
            Unit = unit;
        }

        /// <summary>An unsuffixed number defaults to points, matching this API's existing call-site convention (e.g. <c>container.Padding(10)</c>).</summary>
        public static implicit operator PdfLength(double points) => new(points, PdfLengthUnit.Point);

        /// <summary>See the <see cref="double"/> overload.</summary>
        public static implicit operator PdfLength(int points) => new(points, PdfLengthUnit.Point);

        /// <summary>A length in points (1/72 inch).</summary>
        public static PdfLength Points(double value) => new(value, PdfLengthUnit.Point);

        /// <summary>A length in CSS pixels (1px = 1/96in = 0.75pt).</summary>
        public static PdfLength Pixels(double value) => new(value, PdfLengthUnit.Pixel);

        /// <summary>A length in inches.</summary>
        public static PdfLength Inches(double value) => new(value, PdfLengthUnit.Inch);

        /// <summary>A length in centimeters.</summary>
        public static PdfLength Centimeters(double value) => new(value, PdfLengthUnit.Centimeter);

        /// <summary>A length in millimeters.</summary>
        public static PdfLength Millimeters(double value) => new(value, PdfLengthUnit.Millimeter);

        /// <summary>A length in picas (1pc = 12pt).</summary>
        public static PdfLength Picas(double value) => new(value, PdfLengthUnit.Pica);

        /// <summary>A percentage of whatever basis the property being set resolves percentages against.</summary>
        public static PdfLength Percent(double value) => new(value, PdfLengthUnit.Percent);

        /// <summary>A length relative to the current element's own font size.</summary>
        public static PdfLength Em(double value) => new(value, PdfLengthUnit.Em);

        /// <summary>A length relative to the document root's font size.</summary>
        public static PdfLength Rem(double value) => new(value, PdfLengthUnit.Rem);

        /// <summary>Zero points - the same as <c>default(PdfLength)</c>, spelled out for readability at call sites.</summary>
        public static PdfLength Zero => new(0, PdfLengthUnit.Point);

        private static readonly string[] UnitSuffixes =
        [
            "pt", "px", "in", "cm", "mm", "pc", "%", "em", "rem"
        ];

        /// <summary>
        /// Formats this length as a canonical CSS length token (e.g. <c>"12pt"</c>, <c>"50%"</c>) - the
        /// exact text every internal <see cref="Html.Core.Dom.CssBox"/> string-typed length/percentage
        /// property already accepts and resolves via the real CSS parsing pipeline.
        /// </summary>
        internal string ToCssText() =>
            string.Create(CultureInfo.InvariantCulture, $"{Value}{UnitSuffixes[(int)Unit]}");

        /// <summary>
        /// Resolves this length to points directly (the same fixed physical-unit ratios
        /// <see cref="Html.Core.Parse.CssValueParser"/>'s own length resolution uses, e.g. 1in = 72pt,
        /// 1px = 0.75pt), for the one context that needs a page's own sheet size as a plain number before
        /// any <see cref="Html.Core.Dom.CssBox"/> exists to resolve a CSS string against.
        /// <see cref="PdfLengthUnit.Percent"/>/<see cref="PdfLengthUnit.Em"/>/<see cref="PdfLengthUnit.Rem"/>
        /// have no meaningful absolute size and throw.
        /// </summary>
        internal double ToPoints() => Unit switch
        {
            PdfLengthUnit.Point => Value,
            PdfLengthUnit.Pixel => Value * (72d / 96d),
            PdfLengthUnit.Inch => Value * 72d,
            PdfLengthUnit.Centimeter => Value * (72d / 2.54d),
            PdfLengthUnit.Millimeter => Value * (72d / 25.4d),
            PdfLengthUnit.Pica => Value * 12d,
            _ => throw new InvalidOperationException(
                $"A {Unit} length has no absolute size and cannot be used for a page's own sheet size.")
        };

        /// <summary>Returns the canonical CSS length token this value represents (e.g. <c>"12pt"</c>).</summary>
        public override string ToString() => ToCssText();
    }
}
