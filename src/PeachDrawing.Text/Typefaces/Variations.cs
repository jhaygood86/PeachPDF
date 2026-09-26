using System.Collections.Generic;

namespace PeachDrawing.Text
{
    /// <summary>
    /// A value for one axis of a variable font, such as weight 650: what to ask <see cref="Typeface.WithAxes"/> for.
    /// </summary>
    /// <param name="Tag">The four-character tag of the axis, such as <c>wght</c>. See <see cref="AxisTags"/> for the registered ones.</param>
    /// <param name="Value">The value in the axis's own units (a weight of 650, a width of 87.5 percent, an optical size of 12 points).</param>
    public readonly record struct AxisSetting(string Tag, double Value);

    /// <summary>
    /// The tags of the axes the OpenType specification registers.
    /// </summary>
    public static class AxisTags
    {
        /// <summary>Weight (<c>wght</c>): the same scale as CSS <c>font-weight</c>, 1 to 1000.</summary>
        public const string Weight = "wght";

        /// <summary>Width (<c>wdth</c>): a percentage of the normal width, the scale of CSS <c>font-stretch</c>.</summary>
        public const string Width = "wdth";

        /// <summary>Italic (<c>ital</c>): 0 for upright, 1 for italic.</summary>
        public const string Italic = "ital";

        /// <summary>Slant (<c>slnt</c>): the angle of the slant in degrees counter-clockwise from vertical, so negative for a lean to the right.</summary>
        public const string Slant = "slnt";

        /// <summary>Optical size (<c>opsz</c>): the size in points the letter shapes are drawn for.</summary>
        public const string OpticalSize = "opsz";
    }

    /// <summary>
    /// An axis of a variable font: a way its design varies, with the range of values it covers.
    /// </summary>
    public sealed class VariationAxis
    {
        internal VariationAxis(string tag, string? name, double minimum, double defaultValue, double maximum, bool isHidden)
        {
            Tag = tag;
            Name = name;
            Minimum = minimum;
            Default = defaultValue;
            Maximum = maximum;
            IsHidden = isHidden;
        }

        /// <summary>The four-character tag, such as <c>wght</c>.</summary>
        public string Tag { get; }

        /// <summary>The name the font gives the axis, such as <c>Weight</c>, or <see langword="null"/> when it gives none.</summary>
        public string? Name { get; }

        /// <summary>The lowest value the axis covers.</summary>
        public double Minimum { get; }

        /// <summary>The value the font's default design is drawn at.</summary>
        public double Default { get; }

        /// <summary>The highest value the axis covers.</summary>
        public double Maximum { get; }

        /// <summary>Whether the font asks that the axis is not shown to the people who choose fonts.</summary>
        public bool IsHidden { get; }
    }

    /// <summary>
    /// A named location in the design space of a variable font, such as <c>Bold Condensed</c>.
    /// </summary>
    public sealed class NamedVariation
    {
        internal NamedVariation(string? name, IReadOnlyList<AxisSetting> settings)
        {
            Name = name;
            Settings = settings;
        }

        /// <summary>The name the font gives the location, or <see langword="null"/> when it gives none.</summary>
        public string? Name { get; }

        /// <summary>The value on every axis of the font, in the order of <see cref="Typeface.Axes"/>.</summary>
        public IReadOnlyList<AxisSetting> Settings { get; }
    }
}
