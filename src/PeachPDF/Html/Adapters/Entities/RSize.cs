// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using System;

namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// Stores an ordered pair of floating-point numbers, typically the width and height of a rectangle.
    /// </summary>
    public record struct RSize(double Width, double Height)
    {
        /// <summary>
        ///     Gets a <see cref="RSize" /> structure that has a
        ///     <see
        ///         cref="RSize.Height" />
        ///     and
        ///     <see
        ///         cref="RSize.Width" />
        ///     value of 0.
        /// </summary>
        /// <returns>
        ///     A <see cref="RSize" /> structure that has a
        ///     <see
        ///         cref="RSize.Height" />
        ///     and
        ///     <see
        ///         cref="RSize.Width" />
        ///     value of 0.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public static readonly RSize Empty = new RSize();

        /// <summary>
        ///     Initializes a new instance of the <see cref="RSize" /> structure from the specified existing
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </summary>
        /// <param name="size">
        ///     The <see cref="RSize" /> structure from which to create the new
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </param>
        public RSize(RSize size) : this(size.Width, size.Height)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RSize" /> structure from the specified <see cref="RPoint" /> structure.
        /// </summary>
        /// <param name="pt">The <see cref="RPoint" /> structure from which to initialize this <see cref="RSize" /> structure.</param>
        public RSize(RPoint pt) : this(pt.X, pt.Y)
        { }

        /// <summary>
        ///     Gets a value that indicates whether this <see cref="RSize" /> structure has zero width and height.
        /// </summary>
        /// <returns>
        ///     This property returns true when this <see cref="RSize" /> structure has both a width and height of zero; otherwise, false.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public bool IsEmpty
        {
            get
            {
                if (Math.Abs(Width) < 0.0001)
                    return Math.Abs(Height) < 0.0001;
                else
                    return false;
            }
        }

        /// <summary>
        ///     Converts the specified <see cref="RSize" /> structure to a
        ///     <see cref="RPoint" /> structure.
        /// </summary>
        /// <returns>The <see cref="RPoint" /> structure to which this operator converts.</returns>
        /// <param name="size">The <see cref="RSize" /> structure to be converted
        /// </param>
        public static explicit operator RPoint(RSize size)
        {
            return new RPoint(size.Width, size.Height);
        }

        /// <summary>
        ///     Adds the width and height of one <see cref="RSize" /> structure to the width and height of another
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </summary>
        /// <returns>
        ///     A <see cref="RSize" /> structure that is the result of the addition operation.
        /// </returns>
        /// <param name="sz1">
        ///     The first <see cref="RSize" /> structure to add.
        /// </param>
        /// <param name="sz2">
        ///     The second <see cref="RSize" /> structure to add.
        /// </param>
        /// <filterpriority>3</filterpriority>
        public static RSize operator +(RSize sz1, RSize sz2)
        {
            return Add(sz1, sz2);
        }

        /// <summary>
        ///     Subtracts the width and height of one <see cref="RSize" /> structure from the width and height of another
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </summary>
        /// <returns>
        ///     A <see cref="RSize" /> that is the result of the subtraction operation.
        /// </returns>
        /// <param name="sz1">
        ///     The <see cref="RSize" /> structure on the left side of the subtraction operator.
        /// </param>
        /// <param name="sz2">
        ///     The <see cref="RSize" /> structure on the right side of the subtraction operator.
        /// </param>
        /// <filterpriority>3</filterpriority>
        public static RSize operator -(RSize sz1, RSize sz2)
        {
            return Subtract(sz1, sz2);
        }

        /// <summary>
        ///     Adds the width and height of one <see cref="RSize" /> structure to the width and height of another
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </summary>
        /// <returns>
        ///     A <see cref="RSize" /> structure that is the result of the addition operation.
        /// </returns>
        /// <param name="sz1">
        ///     The first <see cref="RSize" /> structure to add.
        /// </param>
        /// <param name="sz2">
        ///     The second <see cref="RSize" /> structure to add.
        /// </param>
        public static RSize Add(RSize sz1, RSize sz2)
        {
            return new RSize(sz1.Width + sz2.Width, sz1.Height + sz2.Height);
        }

        /// <summary>
        ///     Subtracts the width and height of one <see cref="RSize" /> structure from the width and height of another
        ///     <see
        ///         cref="RSize" />
        ///     structure.
        /// </summary>
        /// <returns>
        ///     A <see cref="RSize" /> structure that is a result of the subtraction operation.
        /// </returns>
        /// <param name="sz1">
        ///     The <see cref="RSize" /> structure on the left side of the subtraction operator.
        /// </param>
        /// <param name="sz2">
        ///     The <see cref="RSize" /> structure on the right side of the subtraction operator.
        /// </param>
        public static RSize Subtract(RSize sz1, RSize sz2)
        {
            return new RSize(sz1.Width - sz2.Width, sz1.Height - sz2.Height);
        }
    }
}