using PeachDrawing.Core;
using System.Numerics;

namespace PeachPDF.Svg
{
    /// <summary>Paints the page content lying behind an inline SVG, for the SVG's <c>BackgroundImage</c>.</summary>
    internal interface ISvgPageBackdrop
    {
        /// <summary>
        /// Paints what was on the page before the SVG's own content into <paramref name="g"/>, a raster graphics whose user space is the
        /// page's layout space. False when there is nothing to paint (the page layer is then simply absent).
        /// </summary>
        bool Paint(Canvas g);
    }

    /// <summary>
    /// What an SVG whose filters read <c>BackgroundImage</c> needs while it is painted, carried on the <see cref="Canvas"/> it paints
    /// to (see <see cref="SvgBackdropSlot"/>). The backdrop of a filtered element is everything painted before it inside the current
    /// isolation group: the page behind an inline SVG, then the SVG's own earlier content. Neither is kept anywhere while painting, so
    /// both are repainted on demand into a bitmap - the SVG's by walking its scene graph again from the root and stopping at the element.
    /// </summary>
    /// <remarks>
    /// Only the graphics that paints the document's root elements carries one; a group's isolated tile does not, which is exactly right,
    /// because an isolation group's backdrop starts empty.
    /// </remarks>
    internal sealed class SvgBackdropContext(SvgDocument document, ISvgPageBackdrop? page)
    {
        public SvgDocument Document { get; } = document;

        /// <summary>The page layer, or null for an SVG that is not inline in an HTML page.</summary>
        public ISvgPageBackdrop? Page { get; } = page;

        /// <summary>The transform in effect where the document is placed, before its viewBox mapping.</summary>
        public Matrix3x2 Frame { get; set; }

        /// <summary>The rectangle, in <see cref="Frame"/>'s space, the document is clipped to.</summary>
        public Rect ViewportRect { get; set; }

        /// <summary>The viewBox-to-viewport mapping the document's root elements are painted under.</summary>
        public Matrix3x2 ViewBoxMatrix { get; set; }

        /// <summary>The user-space size root elements resolve percentages against.</summary>
        public (double Width, double Height) Viewport { get; set; }

        /// <summary>How many backdrop repaints enclose this one; bounds a chain of filters that read each other's backdrop.</summary>
        public int Depth { get; init; }

        /// <summary>Set on a repaint: the element at which painting stops, because it and everything after it is not backdrop.</summary>
        public SvgElement? StopElement { get; init; }

        /// <summary>True once <see cref="StopElement"/> was reached; from then on nothing more is painted.</summary>
        public bool Stopped { get; private set; }

        /// <summary>Whether <paramref name="element"/> must not be painted by this repaint (it is the stop element, or comes after it).</summary>
        public bool ShouldSkip(SvgElement element)
        {
            if (Stopped)
                return true;

            if (StopElement is null || !ReferenceEquals(element, StopElement))
                return false;

            Stopped = true;
            return true;
        }
    }

    /// <summary>
    /// The <see cref="SvgBackdropContext"/> in effect on a <see cref="Canvas"/> while an SVG that reads <c>BackgroundImage</c> is painted
    /// to it. Kept beside the canvas rather than on it: only <see cref="SvgRenderer"/> sets and reads it, so the drawing abstraction has
    /// no reason to carry it.
    /// </summary>
    internal static class SvgBackdropSlot
    {
        private sealed class Holder
        {
            public SvgBackdropContext? Value;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Canvas, Holder> Slots = new();

        public static SvgBackdropContext? Get(Canvas canvas) => Slots.TryGetValue(canvas, out var holder) ? holder.Value : null;

        public static void Set(Canvas canvas, SvgBackdropContext? value) => Slots.GetOrCreateValue(canvas).Value = value;
    }
}
