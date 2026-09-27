using System;
using System.Globalization;

namespace PeachDrawing.Text
{
    /// <summary>
    /// An inclusive range of values along one design axis: the weights, widths or slants a face declares that it covers (the range form
    /// of the CSS <c>@font-face</c> descriptors <c>font-weight</c>, <c>font-stretch</c> and <c>font-style: oblique</c>).
    /// </summary>
    /// <remarks>
    /// A range whose two ends are equal is a single value. The constructor puts the ends in order, so the smaller is always
    /// <see cref="Minimum"/>.
    /// </remarks>
    public readonly struct AxisRange : IEquatable<AxisRange>
    {
        /// <summary>Creates the range between two values, in whichever order they are given.</summary>
        /// <param name="first">One end of the range.</param>
        /// <param name="second">The other end of the range.</param>
        public AxisRange(double first, double second)
        {
            Minimum = Math.Min(first, second);
            Maximum = Math.Max(first, second);
        }

        /// <summary>Creates the range that holds one value.</summary>
        /// <param name="value">The value.</param>
        public AxisRange(double value)
            : this(value, value)
        {
        }

        /// <summary>The lowest value of the range.</summary>
        public double Minimum { get; }

        /// <summary>The highest value of the range.</summary>
        public double Maximum { get; }

        /// <summary>Whether a value lies inside the range, both ends included.</summary>
        /// <param name="value">The value to test.</param>
        public bool Contains(double value) => value >= Minimum && value <= Maximum;

        /// <summary>The value of the range that is nearest to <paramref name="value"/>: the value itself when it is inside.</summary>
        /// <param name="value">The value to bring into the range.</param>
        public double Clamp(double value) => Math.Clamp(value, Minimum, Maximum);

        /// <inheritdoc/>
        public bool Equals(AxisRange other) => Minimum == other.Minimum && Maximum == other.Maximum;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is AxisRange other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Minimum, Maximum);

        /// <summary>Whether two ranges have the same ends.</summary>
        /// <param name="left">The first range.</param>
        /// <param name="right">The second range.</param>
        public static bool operator ==(AxisRange left, AxisRange right) => left.Equals(right);

        /// <summary>Whether two ranges differ in an end.</summary>
        /// <param name="left">The first range.</param>
        /// <param name="right">The second range.</param>
        public static bool operator !=(AxisRange left, AxisRange right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
        {
            var minimum = Minimum.ToString("R", CultureInfo.InvariantCulture);
            return Minimum == Maximum ? minimum : minimum + ".." + Maximum.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
