using System;

namespace PeachDrawing.Core.Geometry
{
    /// <summary>How <see cref="PathOperations.Combine"/> merges two filled shapes.</summary>
    public enum PathOperation
    {
        /// <summary>Everything covered by either shape.</summary>
        Union,

        /// <summary>Only what both shapes cover.</summary>
        Intersect,

        /// <summary>What the first shape covers and the second does not.</summary>
        Difference,

        /// <summary>What exactly one of the shapes covers.</summary>
        Xor,
    }

    /// <summary>
    /// Combines filled shapes into new ones. The result keeps the true curves of its inputs: lines stay lines and cubic Béziers stay
    /// cubic Béziers (cut where the shapes cross), rather than becoming a flattened polygon.
    /// </summary>
    public static class PathOperations
    {
        /// <summary>
        /// Adds to <paramref name="destination"/> the outline of the area produced by combining two shapes. Each input is read as a
        /// filled area under its own <see cref="GraphicsPath.FillMode"/>, with every subpath treated as closed, so an input may
        /// have holes and may cross itself. The result is one or more closed subpaths, and <paramref name="destination"/> is set to
        /// <see cref="FillMode.Nonzero"/>: outer boundaries and holes wind in opposite directions, so filling it reproduces the
        /// combined area.
        /// </summary>
        /// <param name="first">the first shape</param>
        /// <param name="second">the second shape</param>
        /// <param name="operation">how to combine them</param>
        /// <param name="destination">the path the result is added to; create it with <see cref="Canvas.GetGraphicsPath"/> to draw it with that canvas</param>
        /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
        /// <exception cref="ArgumentException">a path has a coordinate that is NaN or infinite</exception>
        /// <remarks>
        /// A shape may cross itself where <em>different</em> curves of it meet; a single cubic that loops back across itself is not cut at
        /// that crossing. Two curves that run along each other for a stretch are handled, but a very long overlap is approximated.
        /// Two pieces of an outline are told apart from each other, and from a neighbouring shape's, at a precision of about a
        /// millionth of the inputs' overall size. Boundaries that come closer than that are treated as touching, and a sliver
        /// thinner than that may be lost.
        /// </remarks>
        public static void Combine(GraphicsPath first, GraphicsPath second, PathOperation operation, GraphicsPath destination)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            ArgumentNullException.ThrowIfNull(destination);
            if (!Enum.IsDefined(operation))
                throw new ArgumentOutOfRangeException(nameof(operation));

            destination.FillMode = FillMode.Nonzero;
            PathCombiner.Combine(first, second, operation, destination);
        }
    }
}
