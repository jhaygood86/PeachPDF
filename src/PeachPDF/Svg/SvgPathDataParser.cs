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
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parses the SVG <c>d</c> path data mini-language (<c>M/L/H/V/C/S/Q/T/A/Z</c>, upper or lower
    /// case) into a normalized list of <see cref="PathSegment"/>s. Malformed/truncated input stops
    /// parsing early and returns whatever was successfully read so far, rather than throwing.
    /// </summary>
    internal static class SvgPathDataParser
    {
        public static IReadOnlyList<PathSegment> Parse(string? d)
        {
            var segments = new List<PathSegment>();

            if (string.IsNullOrWhiteSpace(d))
                return segments;

            var pos = 0;
            char? currentCommand = null;

            double curX = 0, curY = 0;
            double subpathStartX = 0, subpathStartY = 0;

            // Tracks the "other" control point of the previous curve command, for S/T reflection,
            // and which command family last ran (so reflection only applies right after a matching
            // curve command, per spec).
            double lastCubicControlX = 0, lastCubicControlY = 0;
            double lastQuadControlX = 0, lastQuadControlY = 0;
            char lastCommandUpper = '\0';

            while (true)
            {
                SvgNumberScanner.SkipSeparators(d, ref pos);

                if (pos >= d.Length)
                    break;

                char command;

                if (char.IsAsciiLetter(d[pos]))
                {
                    command = d[pos];
                    pos++;
                }
                else if (currentCommand is { } prev)
                {
                    // Implicit command repetition. A moveto followed by extra coordinate pairs
                    // repeats as an implicit lineto (upper/lower case follows the moveto's case).
                    command = prev switch
                    {
                        'M' => 'L',
                        'm' => 'l',
                        _ => prev,
                    };
                }
                else
                {
                    break;
                }

                var isRelative = char.IsAsciiLetterLower(command);
                var upper = char.ToUpperInvariant(command);
                double x = 0, y = 0, x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                bool ok;

                switch (upper)
                {
                    case 'M':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (isRelative) { x += curX; y += curY; }

                        curX = x; curY = y;
                        subpathStartX = x; subpathStartY = y;
                        segments.Add(PathSegment.MoveTo(x, y));
                        break;
                    }

                    case 'L':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (isRelative) { x += curX; y += curY; }

                        curX = x; curY = y;
                        segments.Add(PathSegment.LineTo(x, y));
                        break;
                    }

                    case 'H':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x);
                        if (!ok) goto stop;

                        curX = isRelative ? curX + x : x;
                        segments.Add(PathSegment.LineTo(curX, curY));
                        break;
                    }

                    case 'V':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out y);
                        if (!ok) goto stop;

                        curY = isRelative ? curY + y : y;
                        segments.Add(PathSegment.LineTo(curX, curY));
                        break;
                    }

                    case 'C':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x1)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y1)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out x2)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y2)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (isRelative)
                        {
                            x1 += curX; y1 += curY;
                            x2 += curX; y2 += curY;
                            x += curX; y += curY;
                        }

                        segments.Add(PathSegment.CubicBezierTo(x1, y1, x2, y2, x, y));
                        lastCubicControlX = x2; lastCubicControlY = y2;
                        curX = x; curY = y;
                        break;
                    }

                    case 'S':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x2)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y2)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (lastCommandUpper is 'C' or 'S')
                        {
                            x1 = 2 * curX - lastCubicControlX;
                            y1 = 2 * curY - lastCubicControlY;
                        }
                        else
                        {
                            x1 = curX;
                            y1 = curY;
                        }

                        if (isRelative)
                        {
                            x2 += curX; y2 += curY;
                            x += curX; y += curY;
                        }

                        segments.Add(PathSegment.CubicBezierTo(x1, y1, x2, y2, x, y));
                        lastCubicControlX = x2; lastCubicControlY = y2;
                        curX = x; curY = y;
                        break;
                    }

                    case 'Q':
                    {
                        double qx = 0, qy = 0;

                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out qx)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out qy)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (isRelative)
                        {
                            qx += curX; qy += curY;
                            x += curX; y += curY;
                        }

                        AddQuadraticAsCubic(segments, curX, curY, qx, qy, x, y);
                        lastQuadControlX = qx; lastQuadControlY = qy;
                        curX = x; curY = y;
                        break;
                    }

                    case 'T':
                    {
                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        double qx, qy;

                        if (lastCommandUpper is 'Q' or 'T')
                        {
                            qx = 2 * curX - lastQuadControlX;
                            qy = 2 * curY - lastQuadControlY;
                        }
                        else
                        {
                            qx = curX;
                            qy = curY;
                        }

                        if (isRelative) { x += curX; y += curY; }

                        AddQuadraticAsCubic(segments, curX, curY, qx, qy, x, y);
                        lastQuadControlX = qx; lastQuadControlY = qy;
                        curX = x; curY = y;
                        break;
                    }

                    case 'A':
                    {
                        double rx = 0, ry = 0, rotation = 0;
                        bool largeArc = false, sweep = false;

                        ok = SvgNumberScanner.TryReadNumber(d, ref pos, out rx)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out ry)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out rotation)
                             && SvgNumberScanner.TryReadFlag(d, ref pos, out largeArc)
                             && SvgNumberScanner.TryReadFlag(d, ref pos, out sweep)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out x)
                             && SvgNumberScanner.TryReadNumber(d, ref pos, out y);

                        if (!ok) goto stop;

                        if (isRelative) { x += curX; y += curY; }

                        segments.Add(PathSegment.ArcTo(Math.Abs(rx), Math.Abs(ry), rotation, largeArc, sweep, x, y));
                        curX = x; curY = y;
                        break;
                    }

                    case 'Z':
                    {
                        segments.Add(PathSegment.ClosePath());
                        curX = subpathStartX; curY = subpathStartY;
                        currentCommand = null;
                        lastCommandUpper = '\0';
                        continue; // Z takes no arguments and cannot implicitly repeat
                    }

                    default:
                        goto stop;
                }

                currentCommand = command;
                lastCommandUpper = upper;
            }

            stop:
            return segments;
        }

        /// <summary>
        /// Converts a quadratic Bezier (start P0, control Q, end P1) to the mathematically
        /// equivalent cubic Bezier: C1 = P0 + 2/3*(Q-P0), C2 = P1 + 2/3*(Q-P1).
        /// </summary>
        private static void AddQuadraticAsCubic(List<PathSegment> segments, double x0, double y0, double qx, double qy, double x1, double y1)
        {
            var c1X = x0 + 2.0 / 3.0 * (qx - x0);
            var c1Y = y0 + 2.0 / 3.0 * (qy - y0);
            var c2X = x1 + 2.0 / 3.0 * (qx - x1);
            var c2Y = y1 + 2.0 / 3.0 * (qy - y1);
            segments.Add(PathSegment.CubicBezierTo(c1X, c1Y, c2X, c2Y, x1, y1));
        }
    }
}
