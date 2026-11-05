using System.Collections.Generic;
using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Hvgl
{
    /// <summary>
    /// Turns the instructions Apple's HVF library hands to its render callback into a <see cref="GlyphOutline"/>. The library
    /// has already done the work of an hvgl glyph (blending the axes, placing the parts of a composite); what arrives is a
    /// stream of paths made of points on the curve and quadratic curves, in font units, y up.
    /// </summary>
    /// <remarks>
    /// Kept free of any native call so the stream can be scripted in a test on any host.
    /// A path starts at its first point; a later point is a line from the one before, and a quadratic curve runs from the one before
    /// through its control point to its end. A path is closed by the contour model itself, so closing one only ends it.
    /// </remarks>
    internal sealed class HvfOutlineBuilder
    {
        /// <summary>What the library asks of the callback (<c>HVFPartRenderInstruction</c>).</summary>
        internal enum Instruction : uint
        {
            BeginPart = 0,
            BeginPath = 1,
            AddPoint = 2,
            AddLine = 3,
            AddQuad = 4,
            AddCubic = 5,
            ClosePath = 6,
            EndPath = 7,
            EndPart = 8,
            Stop = 9
        }

        /// <summary>What the callback answers (<c>HVFPartRenderAction</c>).</summary>
        internal enum Action : uint
        {
            Continue = 0,
            Skip = 1,
            Stop = 2
        }

        private readonly GlyphOutline _outline = new();
        private OutlineContour? _contour;
        private OutlinePoint _current;

        /// <summary>The paths drawn so far, ended.</summary>
        internal GlyphOutline Finish()
        {
            EndContour();
            return _outline;
        }

        /// <summary>
        /// Takes one instruction. The parameters are the doubles of the instruction's payload in order: a point is (x, y), a quadratic
        /// curve is (control x, control y, end x, end y), a cubic curve is (control 1, control 2, end), each as an x, y pair.
        /// </summary>
        internal Action Handle(Instruction instruction, double a = 0, double b = 0, double c = 0, double d = 0, double e = 0, double f = 0)
        {
            switch (instruction)
            {
                case Instruction.BeginPath:
                    EndContour();
                    break;

                case Instruction.AddPoint:
                case Instruction.AddLine:
                    LineTo(new OutlinePoint(a, b));
                    break;

                case Instruction.AddQuad:
                    QuadTo(new OutlinePoint(a, b), new OutlinePoint(c, d));
                    break;

                case Instruction.AddCubic:
                    CubicTo(new OutlinePoint(a, b), new OutlinePoint(c, d), new OutlinePoint(e, f));
                    break;

                case Instruction.ClosePath:
                case Instruction.EndPath:
                case Instruction.Stop:
                    EndContour();
                    break;

                case Instruction.BeginPart:
                case Instruction.EndPart:
                    // The parts of a composite arrive with their transforms applied; their paths simply follow one another.
                    break;
            }

            return Action.Continue;
        }

        private void EndContour()
        {
            if (_contour is { } contour)
            {
                _outline.ContourList.Add(contour);
                _contour = null;
            }
        }

        private void Start(OutlinePoint at)
        {
            _contour = new OutlineContour(at);
            _current = at;
        }

        private void LineTo(OutlinePoint to)
        {
            if (_contour is null)
            {
                Start(to);
                return;
            }

            _contour.SegmentList.Add(OutlineSegment.Line(to));
            _current = to;
        }

        private void QuadTo(OutlinePoint control, OutlinePoint to)
        {
            if (_contour is null)
                Start(control);

            // HVF draws a straight edge as a curve whose control point is one of its ends.
            if (control == _current || control == to)
            {
                LineTo(to);
                return;
            }

            // A quadratic curve is the cubic curve whose controls lie two thirds of the way to the quadratic one.
            var c1 = new OutlinePoint(_current.X + 2.0 / 3.0 * (control.X - _current.X), _current.Y + 2.0 / 3.0 * (control.Y - _current.Y));
            var c2 = new OutlinePoint(to.X + 2.0 / 3.0 * (control.X - to.X), to.Y + 2.0 / 3.0 * (control.Y - to.Y));
            _contour!.SegmentList.Add(OutlineSegment.Cubic(c1, c2, to));
            _current = to;
        }

        private void CubicTo(OutlinePoint control1, OutlinePoint control2, OutlinePoint to)
        {
            if (_contour is null)
                Start(control1);

            _contour!.SegmentList.Add(OutlineSegment.Cubic(control1, control2, to));
            _current = to;
        }
    }
}
