// Type: System.Drawing.Color
// Assembly: System.Drawing, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
// Assembly location: C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.Drawing.dll

using System;
using System.Text;

namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// Represents an ARGB (alpha, red, green, blue) color.
    /// </summary>
    internal readonly struct RColor
    {
        #region Fields and Consts

        /// <summary>
        ///     Represents a color that is null.
        /// </summary>
        /// <filterpriority>1</filterpriority>
        public static readonly RColor Empty = new RColor();

        private readonly long _value;

        // CSS Color 5 device-cmyk() carried natively - see PeachPDF.CSS.Color.IsDeviceCmyk for why no
        // naive RGB<->CMYK approximation is computed for one of these. R/G/B (read from the low 24 bits
        // of _value, which stay 0 for a CMYK-tagged color) must not be read when _isCmyk is true.
        private readonly bool _isCmyk;
        private readonly float _cyan;
        private readonly float _magenta;
        private readonly float _yellow;
        private readonly float _key;

        #endregion


        private RColor(long value)
        {
            _value = value;
            _isCmyk = false;
            _cyan = 0f;
            _magenta = 0f;
            _yellow = 0f;
            _key = 0f;
        }

        private RColor(byte alpha, float cyan, float magenta, float yellow, float key)
        {
            _value = (long)alpha << 24;
            _isCmyk = true;
            _cyan = cyan;
            _magenta = magenta;
            _yellow = yellow;
            _key = key;
        }

        /// <summary>
        /// Gets a system-defined color.
        /// </summary>
        public static RColor Transparent
        {
            get { return new RColor(0); }
        }

        /// <summary>
        ///     Gets a system-defined color that has an ARGB value of #FF000000.
        /// </summary>
        public static RColor Black
        {
            get { return FromArgb(0, 0, 0); }
        }

        /// <summary>
        /// Gets a system-defined color that has an ARGB value of #FFFFFFFF.
        /// </summary>
        public static RColor White
        {
            get { return FromArgb(255, 255, 255); }
        }

        /// <summary>
        /// Gets a system-defined color that has an ARGB value of #FFF5F5F5.
        /// </summary>
        public static RColor WhiteSmoke
        {
            get { return FromArgb(245, 245, 245); }
        }

        /// <summary>
        /// Gets a system-defined color that has an ARGB value of #FFD3D3D3.
        /// </summary>
        public static RColor LightGray
        {
            get { return FromArgb(211, 211, 211); }
        }

        /// <summary>
        ///     Gets the red component value of this <see cref="RColor" /> structure.
        /// </summary>
        public byte R
        {
            get { return (byte)((ulong)(_value >> 16) & byte.MaxValue); }
        }

        /// <summary>
        ///     Gets the green component value of this <see cref="RColor" /> structure.
        /// </summary>
        public byte G
        {
            get { return (byte)((ulong)(_value >> 8) & byte.MaxValue); }
        }

        /// <summary>
        ///     Gets the blue component value of this <see cref="RColor" /> structure.
        /// </summary>
        public byte B
        {
            get { return (byte)((ulong)_value & byte.MaxValue); }
        }

        /// <summary>
        ///     Gets the alpha component value of this <see cref="RColor" /> structure.
        /// </summary>
        public byte A
        {
            get { return (byte)((ulong)(_value >> 24) & byte.MaxValue); }
        }

        /// <summary>
        /// True if this color was authored via CSS <c>device-cmyk()</c> and is carried in its native
        /// CMYK components (<see cref="C"/>/<see cref="M"/>/<see cref="Y"/>/<see cref="K"/>) rather than
        /// RGB - <see cref="R"/>/<see cref="G"/>/<see cref="B"/> are meaningless when this is true. See
        /// <see cref="PeachPDF.Utilities.Utils.Convert(RColor)"/>, the one choke point that branches on
        /// this before building the PDF backend's color.
        /// </summary>
        public bool IsCmyk => _isCmyk;

        // Named to match PeachPDF.CSS.Color and XColor's own C/M/Y/K properties, the same names all the
        // way down the pipeline from CSS parsing to the PDF backend.
        public float C => _cyan;
        public float M => _magenta;
        public float Y => _yellow;
        public float K => _key;

        /// <summary>
        ///     Specifies whether this <see cref="RColor" /> structure is uninitialized.
        /// </summary>
        /// <returns>
        ///     This property returns true if this color is uninitialized; otherwise, false.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public bool IsEmpty
        {
            get { return _value == 0; }
        }

        /// <summary>
        ///     Tests whether two specified <see cref="RColor" /> structures are equivalent.
        /// </summary>
        /// <returns>
        ///     true if the two <see cref="RColor" /> structures are equal; otherwise, false.
        /// </returns>
        /// <param name="left">
        ///     The <see cref="RColor" /> that is to the left of the equality operator.
        /// </param>
        /// <param name="right">
        ///     The <see cref="RColor" /> that is to the right of the equality operator.
        /// </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(RColor left, RColor right)
        {
            return left.Equals(right);
        }

        /// <summary>
        ///     Tests whether two specified <see cref="RColor" /> structures are different.
        /// </summary>
        /// <returns>
        ///     true if the two <see cref="RColor" /> structures are different; otherwise, false.
        /// </returns>
        /// <param name="left">
        ///     The <see cref="RColor" /> that is to the left of the inequality operator.
        /// </param>
        /// <param name="right">
        ///     The <see cref="RColor" /> that is to the right of the inequality operator.
        /// </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(RColor left, RColor right)
        {
            return !(left == right);
        }

        /// <summary>
        ///     Creates a <see cref="RColor" /> structure from the four ARGB component (alpha, red, green, and blue) values. Although this method allows a 32-bit value to be passed for each component, the value of each component is limited to 8 bits.
        /// </summary>
        /// <returns>
        ///     The <see cref="RColor" /> that this method creates.
        /// </returns>
        /// <param name="alpha">The alpha component. Valid values are 0 through 255. </param>
        /// <param name="red">The red component. Valid values are 0 through 255. </param>
        /// <param name="green">The green component. Valid values are 0 through 255. </param>
        /// <param name="blue">The blue component. Valid values are 0 through 255. </param>
        /// <exception cref="T:System.ArgumentException">
        ///     <paramref name="alpha" />, <paramref name="red" />, <paramref name="green" />, or <paramref name="blue" /> is less than 0 or greater than 255.
        /// </exception>
        /// <filterpriority>1</filterpriority>
        public static RColor FromArgb(int alpha, int red, int green, int blue)
        {
            CheckByte(alpha);
            CheckByte(red);
            CheckByte(green);
            CheckByte(blue);
            return new RColor((uint)(red << 16 | green << 8 | blue | alpha << 24) & (long)uint.MaxValue);
        }

        /// <summary>
        ///     Creates a <see cref="RColor" /> structure from the specified 8-bit color values (red, green, and blue). The alpha value is implicitly 255 (fully opaque). Although this method allows a 32-bit value to be passed for each color component, the value of each component is limited to 8 bits.
        /// </summary>
        /// <returns>
        ///     The <see cref="RColor" /> that this method creates.
        /// </returns>
        /// <param name="red">
        ///     The red component value for the new <see cref="RColor" />. Valid values are 0 through 255.
        /// </param>
        /// <param name="green">
        ///     The green component value for the new <see cref="RColor" />. Valid values are 0 through 255.
        /// </param>
        /// <param name="blue">
        ///     The blue component value for the new <see cref="RColor" />. Valid values are 0 through 255.
        /// </param>
        /// <exception cref="T:System.ArgumentException">
        ///     <paramref name="red" />, <paramref name="green" />, or <paramref name="blue" /> is less than 0 or greater than 255.
        /// </exception>
        /// <filterpriority>1</filterpriority>
        public static RColor FromArgb(int red, int green, int blue)
        {
            return FromArgb(byte.MaxValue, red, green, blue);
        }

        /// <summary>
        /// Creates an <see cref="RColor" /> carrying native CMYK components (CSS <c>device-cmyk()</c>) -
        /// see <see cref="IsCmyk" />.
        /// </summary>
        /// <param name="alpha">The alpha component. Valid values are 0 through 255.</param>
        /// <param name="cyan">The cyan component, clamped to 0..1.</param>
        /// <param name="magenta">The magenta component, clamped to 0..1.</param>
        /// <param name="yellow">The yellow component, clamped to 0..1.</param>
        /// <param name="key">The key (black) component, clamped to 0..1.</param>
        public static RColor FromCmyk(int alpha, float cyan, float magenta, float yellow, float key)
        {
            CheckByte(alpha);
            return new RColor((byte)alpha, Math.Clamp(cyan, 0f, 1f), Math.Clamp(magenta, 0f, 1f),
                Math.Clamp(yellow, 0f, 1f), Math.Clamp(key, 0f, 1f));
        }

        /// <summary>
        ///     Tests whether the specified object is a <see cref="RColor" /> structure and is equivalent to this
        ///     <see
        ///         cref="RColor" />
        ///     structure.
        /// </summary>
        /// <returns>
        ///     true if <paramref name="obj" /> is a <see cref="RColor" /> structure equivalent to this
        ///     <see
        ///         cref="RColor" />
        ///     structure; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to test. </param>
        /// <filterpriority>1</filterpriority>
        public override bool Equals(object? obj)
        {
            if (obj is RColor color)
            {
                if (_isCmyk || color._isCmyk)
                {
                    return _isCmyk == color._isCmyk && _value == color._value &&
                           _cyan.Equals(color._cyan) && _magenta.Equals(color._magenta) &&
                           _yellow.Equals(color._yellow) && _key.Equals(color._key);
                }

                return _value == color._value;
            }

            return false;
        }

        /// <summary>
        ///     Returns a hash code for this <see cref="RColor" /> structure.
        /// </summary>
        /// <returns>
        ///     An integer value that specifies the hash code for this <see cref="RColor" />.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public override int GetHashCode()
        {
            return _isCmyk ? HashCode.Combine(_isCmyk, _value, _cyan, _magenta, _yellow, _key) : _value.GetHashCode();
        }

        /// <summary>
        ///     Converts this <see cref="RColor" /> structure to a human-readable string.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder(32);
            stringBuilder.Append(GetType().Name);
            stringBuilder.Append(" [");
            if (_isCmyk)
            {
                stringBuilder.Append("A=");
                stringBuilder.Append(A);
                stringBuilder.Append(", C=");
                stringBuilder.Append(_cyan);
                stringBuilder.Append(", M=");
                stringBuilder.Append(_magenta);
                stringBuilder.Append(", Y=");
                stringBuilder.Append(_yellow);
                stringBuilder.Append(", K=");
                stringBuilder.Append(_key);
            }
            else if (_value != 0)
            {
                stringBuilder.Append("A=");
                stringBuilder.Append(A);
                stringBuilder.Append(", R=");
                stringBuilder.Append(R);
                stringBuilder.Append(", G=");
                stringBuilder.Append(G);
                stringBuilder.Append(", B=");
                stringBuilder.Append(B);
            }
            else
                stringBuilder.Append("Empty");
            stringBuilder.Append("]");
            return stringBuilder.ToString();
        }


        #region Private methods

        private static void CheckByte(int value)
        {
            if (value >= 0 && value <= byte.MaxValue)
                return;
            throw new ArgumentException("InvalidEx2BoundArgument");
        }

        #endregion
    }
}