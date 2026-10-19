using System;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A deliberate refusal by a <see cref="Canvas"/> to draw something its output format or conformance level forbids (a
    /// transparency group in PDF/A-1, say). It is an <see cref="InvalidOperationException"/> that a painter lets propagate to
    /// the caller as it is, rather than folding it into a generic paint failure the way it does an unexpected error.
    /// </summary>
    public abstract class CanvasConstraintException : InvalidOperationException
    {
        /// <summary>Creates the exception with a message naming what was refused and why.</summary>
        /// <param name="message">what the canvas refused to draw, and why</param>
        protected CanvasConstraintException(string message)
            : base(message)
        {
        }
    }
}
