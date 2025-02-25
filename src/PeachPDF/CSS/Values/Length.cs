#nullable disable

using System;

// ReSharper disable UnusedMember.Global


namespace PeachPDF.CSS
{
    public struct Length : IEquatable<Length>, IComparable<Length>, IFormattable
    {
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

            if (value == 0f)
            {
                result = Zero;
                return true;
            }

            result = default;
            return false;
        }

        public static Unit GetUnit(string s)
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
                "vh" => Unit.Vh,
                "vmax" => Unit.Vmax,
                "vmin" => Unit.Vmin,
                "vw" => Unit.Vw,
                "%" => Unit.Percent,
                _ => Unit.None
            };
        }

        public float ToPixel()
        {
            return Type switch
            {
                Unit.In => // 1 in = 2.54 cm
                    Value * 96f,
                Unit.Mm => // 1 mm = 0.1 cm
                    Value * 5f * 96f / 127f,
                Unit.Pc => // 1 pc = 12 pt
                    Value * 12f * 96f / 72f,
                Unit.Pt => // 1 pt = 1/72 in
                    Value * 96f / 72f,
                Unit.Cm => // 1 cm = 50/127 in
                    Value * 50f * 96f / 127f,
                Unit.Px => // 1 px = 1/96 in
                    Value,
                _ => throw new InvalidOperationException("A relative unit cannot be converted.")
            };
        }

        public float To(Unit unit)
        {
            var value = ToPixel();

            return unit switch
            {
                Unit.In => // 1 in = 2.54 cm
                    value / 96f,
                Unit.Mm => // 1 mm = 0.1 cm
                    value * 127f / (5f * 96f),
                Unit.Pc => // 1 pc = 12 pt
                    value * 72f / (12f * 96f),
                Unit.Pt => // 1 pt = 1/72 in
                    value * 72f / 96f,
                Unit.Cm => // 1 cm = 50/127 in
                    value * 127f / (50f * 96f),
                Unit.Px => // 1 px = 1/96 in
                    value,
                _ => throw new InvalidOperationException("An absolute unit cannot be converted to a relative one.")
            };
        }

        public bool Equals(Length other)
        {
            return Value == other.Value && Type == other.Type;
        }

        public enum Unit : byte
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
            Percent
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