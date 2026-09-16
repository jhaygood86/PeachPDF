#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Decodes a CFF font's Type 2 charstrings (Adobe Technical Note #5177, "The
// Type 2 Charstring Format") into the same GlyphOutline model
// GlyphOutlineDecoder builds for TrueType `glyf` outlines - issue #1117
// (background-clip: text) needed vector glyph outlines for CFF-flavored
// ("OTTO") fonts, which have no `glyf` table at all. Unlike glyf's quadratic
// on/off-curve points, Type 2 curves are already cubic, so no elevation step
// is needed - rrcurveto and its hh/vv/hv/vh variants map straight onto
// GlyphSegment.Cubic.
//
// Deliberately NOT implemented - fails soft (the glyph reports no outline,
// same contract as an absent glyf table) rather than being interpreted:
// the arithmetic/storage/conditional operators under the `12 n` escape, and
// the deprecated 4-argument (seac-style accent composition) form of
// endchar. Real-world font compilers essentially never emit these for
// ordinary text glyphs; a font that does hits the same solid-fill fallback
// every CFF font hit before this file existed.
//
#endregion

using System;
using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>
    /// Decodes one glyph's Type 2 charstring (from a font's <see cref="CffTable"/>) into a
    /// <see cref="GlyphOutline"/>.
    /// </summary>
    internal static class Type2CharstringInterpreter
    {
        private const int MaxCallDepth = 10; // Type 2 spec's own stated max subroutine nesting
        private const int MaxStackSize = 48; // Type 2 spec's own stated max operand stack size

        /// <summary>
        /// Attempts to decode <paramref name="glyphIndex"/>'s outline from <paramref name="cff"/>.
        /// Returns false (with an empty <paramref name="outline"/>) for an unsupported table
        /// (<see cref="CffTable.IsSupported"/>), an out-of-range glyph index, a malformed charstring,
        /// or one that uses an operator this interpreter does not implement (see file header) - never
        /// throws for malformed font data.
        /// </summary>
        public static bool TryGetGlyphOutline(CffTable cff, int glyphIndex, out GlyphOutline outline)
        {
            outline = new GlyphOutline();

            if (!cff.IsSupported || glyphIndex < 0 || glyphIndex >= cff.CharStrings.Count)
                return false;

            try
            {
                var interpreter = new Interpreter(cff, cff.LocalSubrsFor(glyphIndex));
                interpreter.Run(cff.CharStrings[glyphIndex], 0);
                interpreter.CloseCurrentContour();

                if (interpreter.Failed) return false;

                outline.Contours.AddRange(interpreter.Contours);
                return !outline.IsEmpty;
            }
            catch (Exception)
            {
                outline = new GlyphOutline();
                return false;
            }
        }

        /// <summary>
        /// One glyph's interpreter run: the Type 2 operand stack and pen state are shared across the
        /// whole call tree (a subroutine operates on its caller's stack, not a fresh one), so this is
        /// one mutable instance for the whole (possibly recursive) decode rather than a static method.
        /// </summary>
        private sealed class Interpreter(CffTable cff, CffIndex localSubrs)
        {
            private readonly List<double> _stack = [];
            private readonly List<GlyphContour> _contours = [];
            private double _x, _y;
            private GlyphContour? _current;
            private int _stemCount;
            private bool _widthTaken;
            private bool _ended;

            public bool Failed { get; private set; }
            public IReadOnlyList<GlyphContour> Contours => _contours;

            /// <summary>Closes and records the in-progress contour, if any - a no-op once already closed.</summary>
            public void CloseCurrentContour()
            {
                if (_current is not { } contour) return;
                _contours.Add(contour);
                _current = null;
            }

            public void Run(ReadOnlySpan<byte> cs, int depth)
            {
                if (Failed || _ended) return;
                if (depth > MaxCallDepth) { Failed = true; return; }

                var p = 0;
                while (p < cs.Length)
                {
                    if (Failed || _ended) return;

                    int b0 = cs[p];

                    // Operand encoding (distinct from CffDict's DICT operand encoding).
                    if (b0 == 28)
                    {
                        if (p + 3 > cs.Length) { Failed = true; return; }
                        Push((short)((cs[p + 1] << 8) | cs[p + 2]));
                        p += 3;
                        continue;
                    }
                    if (b0 is >= 32 and <= 246)
                    {
                        Push(b0 - 139);
                        p += 1;
                        continue;
                    }
                    if (b0 is >= 247 and <= 250)
                    {
                        if (p + 2 > cs.Length) { Failed = true; return; }
                        Push((b0 - 247) * 256 + cs[p + 1] + 108);
                        p += 2;
                        continue;
                    }
                    if (b0 is >= 251 and <= 254)
                    {
                        if (p + 2 > cs.Length) { Failed = true; return; }
                        Push(-(b0 - 251) * 256 - cs[p + 1] - 108);
                        p += 2;
                        continue;
                    }
                    if (b0 == 255)
                    {
                        if (p + 5 > cs.Length) { Failed = true; return; }
                        int fixedValue = (cs[p + 1] << 24) | (cs[p + 2] << 16) | (cs[p + 3] << 8) | cs[p + 4];
                        Push(fixedValue / 65536.0);
                        p += 5;
                        continue;
                    }

                    // b0 is 0-27 or 29-31: an operator.
                    p += 1;

                    switch (b0)
                    {
                        case 1: case 3: case 18: case 23: // hstem, vstem, hstemhm, vstemhm
                            OpStems();
                            break;

                        case 19: case 20: // hintmask, cntrmask - any leftover operands are an implicit final vstem
                        {
                            OpStems();
                            int maskBytes = (_stemCount + 7) / 8;
                            if (p + maskBytes > cs.Length) { Failed = true; return; }
                            p += maskBytes;
                            break;
                        }

                        case 21: // rmoveto
                            ConsumeMoveWidth(2);
                            if (_stack.Count != 2) { Failed = true; return; }
                            NewContour(_x + _stack[0], _y + _stack[1]);
                            _stack.Clear();
                            break;

                        case 22: // hmoveto
                            ConsumeMoveWidth(1);
                            if (_stack.Count != 1) { Failed = true; return; }
                            NewContour(_x + _stack[0], _y);
                            _stack.Clear();
                            break;

                        case 4: // vmoveto
                            ConsumeMoveWidth(1);
                            if (_stack.Count != 1) { Failed = true; return; }
                            NewContour(_x, _y + _stack[0]);
                            _stack.Clear();
                            break;

                        case 5: // rlineto - {dxa dya}+
                            for (var i = 0; i + 1 < _stack.Count; i += 2)
                                LineTo(_x + _stack[i], _y + _stack[i + 1]);
                            _stack.Clear();
                            break;

                        case 6: // hlineto - alternating horizontal/vertical single-value lines, starting horizontal
                            OpAlternatingLineto(startHorizontal: true);
                            break;

                        case 7: // vlineto - same, starting vertical
                            OpAlternatingLineto(startHorizontal: false);
                            break;

                        case 8: // rrcurveto - {dxa dya dxb dyb dxc dyc}+
                            for (var i = 0; i + 5 < _stack.Count; i += 6)
                                CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                            _stack.Clear();
                            break;

                        case 24: // rcurveline - {dxa dya dxb dyb dxc dyc}+ dxd dyd (curves, then exactly one final line)
                        {
                            int curveCount = (_stack.Count - 2) / 6;
                            var i = 0;
                            for (var c = 0; c < curveCount; c++, i += 6)
                                CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                            if (i + 1 < _stack.Count)
                                LineTo(_x + _stack[i], _y + _stack[i + 1]);
                            _stack.Clear();
                            break;
                        }

                        case 25: // rlinecurve - {dxa dya}+ dxb dyb dxc dyc dxd dyd (lines, then exactly one final curve)
                        {
                            int lineCount = (_stack.Count - 6) / 2;
                            var i = 0;
                            for (var c = 0; c < lineCount; c++, i += 2)
                                LineTo(_x + _stack[i], _y + _stack[i + 1]);
                            if (i + 5 < _stack.Count)
                                CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                            _stack.Clear();
                            break;
                        }

                        case 26: // vvcurveto - dx1? {dya dxb dyb dyc}+
                            OpVvCurveto();
                            break;

                        case 27: // hhcurveto - dy1? {dxa dxb dyb dxc}+
                            OpHhCurveto();
                            break;

                        case 30: // vhcurveto - alternating tangent direction, starting vertical
                            OpAlternatingCurveto(startHorizontal: false);
                            break;

                        case 31: // hvcurveto - alternating tangent direction, starting horizontal
                            OpAlternatingCurveto(startHorizontal: true);
                            break;

                        case 10: // callsubr
                        {
                            if (_stack.Count == 0) { Failed = true; return; }
                            var index = (int)_stack[^1] + Bias(localSubrs.Count);
                            _stack.RemoveAt(_stack.Count - 1);
                            if (index < 0 || index >= localSubrs.Count) { Failed = true; return; }
                            Run(localSubrs[index], depth + 1);
                            break;
                        }

                        case 29: // callgsubr
                        {
                            if (_stack.Count == 0) { Failed = true; return; }
                            var index = (int)_stack[^1] + Bias(cff.GlobalSubrs.Count);
                            _stack.RemoveAt(_stack.Count - 1);
                            if (index < 0 || index >= cff.GlobalSubrs.Count) { Failed = true; return; }
                            Run(cff.GlobalSubrs[index], depth + 1);
                            break;
                        }

                        case 11: // return
                            return;

                        case 14: // endchar
                            ConsumeMoveWidth(0);
                            if (_stack.Count != 0)
                            {
                                // The deprecated 4-argument seac-style accent-composition form - unsupported.
                                Failed = true;
                                return;
                            }
                            _ended = true;
                            return;

                        default: // 0,2,9,13,15-17 reserved; 12 = escape (arithmetic/storage/flex) - unimplemented
                            Failed = true;
                            return;
                    }
                }
            }

            private void Push(double value)
            {
                if (_stack.Count >= MaxStackSize) { Failed = true; return; }
                _stack.Add(value);
            }

            /// <summary>
            /// hstem/vstem/hstemhm/vstemhm's own width rule: an odd operand count means the leading
            /// operand is the glyph's width (a nominalWidthX-relative delta this reader has no use for
            /// beyond knowing to discard it), but only the first time any stack-clearing operator runs.
            /// </summary>
            private void ConsumeStemWidth()
            {
                if (_widthTaken) return;
                _widthTaken = true;
                if ((_stack.Count & 1) == 1)
                    _stack.RemoveAt(0);
            }

            private void OpStems()
            {
                ConsumeStemWidth();
                _stemCount += _stack.Count / 2;
                _stack.Clear();
            }

            /// <summary>A moveto/endchar's own width rule: more operands than the operator itself needs means the leading one is the width.</summary>
            private void ConsumeMoveWidth(int expectedArgs)
            {
                if (_widthTaken) return;
                _widthTaken = true;
                if (_stack.Count > expectedArgs)
                    _stack.RemoveAt(0);
            }

            private void NewContour(double x, double y)
            {
                CloseCurrentContour();
                _x = x;
                _y = y;
                _current = new GlyphContour(new GlyphOutlinePoint(_x, _y));
            }

            private void LineTo(double x, double y)
            {
                _x = x;
                _y = y;
                EnsureContour();
                _current!.Segments.Add(GlyphSegment.Line(new GlyphOutlinePoint(_x, _y)));
            }

            private void CurveTo(double dxa, double dya, double dxb, double dyb, double dxc, double dyc)
            {
                var c1 = new GlyphOutlinePoint(_x + dxa, _y + dya);
                var c2 = new GlyphOutlinePoint(c1.X + dxb, c1.Y + dyb);
                _x = c2.X + dxc;
                _y = c2.Y + dyc;
                EnsureContour();
                _current!.Segments.Add(GlyphSegment.Cubic(c1, c2, new GlyphOutlinePoint(_x, _y)));
            }

            /// <summary>A line/curve reached before any moveto (malformed, but seen from buggy subsetters) opens an implicit contour at the current point instead of crashing.</summary>
            private void EnsureContour() => _current ??= new GlyphContour(new GlyphOutlinePoint(_x, _y));

            private void OpAlternatingLineto(bool startHorizontal)
            {
                bool horizontal = startHorizontal;
                foreach (double v in _stack)
                {
                    if (horizontal) LineTo(_x + v, _y);
                    else LineTo(_x, _y + v);
                    horizontal = !horizontal;
                }
                _stack.Clear();
            }

            private void OpHhCurveto()
            {
                var i = 0;
                double firstDy = 0;
                if ((_stack.Count & 1) == 1) { firstDy = _stack[0]; i = 1; }

                for (; i + 3 < _stack.Count; i += 4)
                {
                    CurveTo(_stack[i], firstDy, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0);
                    firstDy = 0;
                }
                _stack.Clear();
            }

            private void OpVvCurveto()
            {
                var i = 0;
                double firstDx = 0;
                if ((_stack.Count & 1) == 1) { firstDx = _stack[0]; i = 1; }

                for (; i + 3 < _stack.Count; i += 4)
                {
                    CurveTo(firstDx, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]);
                    firstDx = 0;
                }
                _stack.Clear();
            }

            /// <summary>
            /// hvcurveto/vhcurveto: groups of 4 operands, each curve's ending tangent alternating
            /// between horizontal and vertical: a group that ends the operator may carry one extra
            /// (5th) operand - the otherwise-implied-zero coordinate on the ending tangent's own axis.
            /// </summary>
            private void OpAlternatingCurveto(bool startHorizontal)
            {
                var i = 0;
                bool horizontal = startHorizontal;
                int count = _stack.Count;

                while (i + 3 < count)
                {
                    bool isLast = count - i == 5;

                    if (horizontal)
                    {
                        double dx2 = _stack[i + 1], dy2 = _stack[i + 2];
                        double dy3 = _stack[i + 3];
                        double dx3 = isLast ? _stack[i + 4] : 0;
                        CurveTo(_stack[i], 0, dx2, dy2, dx3, dy3);
                    }
                    else
                    {
                        double dx2 = _stack[i + 1], dy2 = _stack[i + 2];
                        double dx3 = _stack[i + 3];
                        double dy3 = isLast ? _stack[i + 4] : 0;
                        CurveTo(0, _stack[i], dx2, dy2, dx3, dy3);
                    }

                    i += isLast ? 5 : 4;
                    horizontal = !horizontal;
                }
                _stack.Clear();
            }

            private static int Bias(int subrCount) => subrCount < 1240 ? 107 : subrCount < 33900 ? 1131 : 32768;
        }
    }
}
