using System;

namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// How far a line box, or one inline box on it, reaches on each side of the baseline — the pair
    /// <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">CSS 2.1 §10.8.1</see> builds a
    /// line box's height out of.
    /// </summary>
    /// <remarks>
    /// The two sides are kept apart rather than summed because they are maximised independently: the
    /// inline box that reaches highest above a line's baseline need not be the one that reaches lowest
    /// below it, so a line can be taller than any single <c>line-height</c> on it. Collapsing to a
    /// height any earlier than <see cref="Height"/> assumes one shared baseline offset, which holds only
    /// while every font on the line is the same size.
    /// </remarks>
    /// <param name="AboveBaseline">the ascent side, including its half of the leading</param>
    /// <param name="BelowBaseline">the descent side, including its half of the leading</param>
    internal readonly record struct LineBoxExtent(double AboveBaseline, double BelowBaseline)
    {
        /// <summary>
        /// The whole thickness of the line box this extent describes.
        /// </summary>
        public double Height => AboveBaseline + BelowBaseline;

        /// <summary>
        /// The smallest extent covering both this one and <paramref name="other"/>, taking each side's
        /// maximum on its own — how a line box accumulates the inline boxes placed on it.
        /// </summary>
        public LineBoxExtent Union(LineBoxExtent other) =>
            new(Math.Max(AboveBaseline, other.AboveBaseline), Math.Max(BelowBaseline, other.BelowBaseline));
    }
}
