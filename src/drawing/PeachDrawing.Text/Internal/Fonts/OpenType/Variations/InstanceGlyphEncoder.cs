using System;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// Writes the glyphs of a variable font at one location as ordinary <c>glyf</c> data: the points and component offsets with the
    /// location's deltas applied and no hinting instructions, so that the result is a static font's glyph. A PDF cannot embed a
    /// variable font, so an instance is embedded this way.
    /// </summary>
    internal static class InstanceGlyphEncoder
    {
        // Composite-glyph component flags.
        private const int Arg1And2AreWords = 0x0001;
        private const int ArgsAreXyValues = 0x0002;
        private const int WeHaveAScale = 0x0008;
        private const int MoreComponents = 0x0020;
        private const int WeHaveAnXAndYScale = 0x0040;
        private const int WeHaveATwoByTwo = 0x0080;
        private const int WeHaveInstructions = 0x0100;

        /// <summary>The bytes of a glyph at <paramref name="variation"/>, and its left side bearing (the x of its leftmost point).</summary>
        /// <returns>The glyph data, empty for a glyph without outline; the length is always even.</returns>
        internal static byte[] Encode(OpenTypeFontface face, int glyph, VariationCoordinates variation, out int xMin)
        {
            xMin = 0;
            var source = face.glyf.GetGlyphData(glyph);
            if (source.Length < 10)
            {
                return [];
            }

            var gvar = variation.IsDefault ? null : face.Variations?.Gvar;
            int contours = BigEndian.I16(source, 0);
            return contours >= 0
                ? EncodeSimple(source, contours, glyph, variation, gvar, out xMin)
                : EncodeComposite(face, source, glyph, variation, gvar, out xMin);
        }

        private static byte[] EncodeSimple(ReadOnlySpan<byte> source, int contours, int glyph, VariationCoordinates variation,
            GvarTable? gvar, out int xMin)
        {
            xMin = 0;
            if (contours == 0)
            {
                return [];
            }

            var endPoints = new int[contours];
            for (int i = 0; i < contours; i++)
            {
                endPoints[i] = BigEndian.U16(source, 10 + i * 2);
            }

            int pointCount = endPoints[contours - 1] + 1;
            int at = 10 + contours * 2;
            int instructionLength = BigEndian.U16(source, at);
            at += 2 + instructionLength;

            var flags = new byte[pointCount];
            for (int i = 0; i < pointCount;)
            {
                byte flag = source[at++];
                flags[i++] = flag;
                if ((flag & 0x08) != 0)
                {
                    int repeat = source[at++];
                    while (repeat-- > 0 && i < pointCount)
                    {
                        flags[i++] = flag;
                    }
                }
            }

            var x = new double[pointCount + 4];
            var y = new double[pointCount + 4];
            int value = 0;
            for (int i = 0; i < pointCount; i++)
            {
                int flag = flags[i];
                if ((flag & 0x02) != 0)
                {
                    int delta = source[at++];
                    value += (flag & 0x10) != 0 ? delta : -delta;
                }
                else if ((flag & 0x10) == 0)
                {
                    value += BigEndian.I16(source, at);
                    at += 2;
                }

                x[i] = value;
            }

            value = 0;
            for (int i = 0; i < pointCount; i++)
            {
                int flag = flags[i];
                if ((flag & 0x04) != 0)
                {
                    int delta = source[at++];
                    value += (flag & 0x20) != 0 ? delta : -delta;
                }
                else if ((flag & 0x20) == 0)
                {
                    value += BigEndian.I16(source, at);
                    at += 2;
                }

                y[i] = value;
            }

            if (gvar is not null)
            {
                var originalX = (double[])x.Clone();
                var originalY = (double[])y.Clone();
                var dx = new double[pointCount + 4];
                var dy = new double[pointCount + 4];
                if (gvar.TryAddDeltas(glyph, variation.Normalized, pointCount + 4, originalX, originalY, endPoints, dx, dy))
                {
                    for (int i = 0; i < pointCount; i++)
                    {
                        x[i] += dx[i];
                        y[i] += dy[i];
                    }
                }
            }

            var xs = new int[pointCount];
            var ys = new int[pointCount];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < pointCount; i++)
            {
                xs[i] = ToInt16(x[i]);
                ys[i] = ToInt16(y[i]);
                minX = Math.Min(minX, xs[i]);
                maxX = Math.Max(maxX, xs[i]);
                minY = Math.Min(minY, ys[i]);
                maxY = Math.Max(maxY, ys[i]);
            }

            xMin = minX;
            var stream = new MemoryStream();
            WriteInt16(stream, contours);
            WriteInt16(stream, minX);
            WriteInt16(stream, minY);
            WriteInt16(stream, maxX);
            WriteInt16(stream, maxY);
            foreach (int end in endPoints)
            {
                WriteInt16(stream, end);
            }

            WriteInt16(stream, 0);      // no instructions: a location's deltas would invalidate them

            var newFlags = new byte[pointCount];
            var xBytes = new MemoryStream();
            var yBytes = new MemoryStream();
            int previousX = 0, previousY = 0;
            for (int i = 0; i < pointCount; i++)
            {
                int flag = flags[i] & 0x01;     // only on-curve survives
                int deltaX = xs[i] - previousX;
                int deltaY = ys[i] - previousY;
                previousX = xs[i];
                previousY = ys[i];

                if (deltaX == 0)
                {
                    flag |= 0x10;
                }
                else if (Math.Abs(deltaX) <= 255)
                {
                    flag |= 0x02 | (deltaX > 0 ? 0x10 : 0);
                    xBytes.WriteByte((byte)Math.Abs(deltaX));
                }
                else
                {
                    WriteInt16(xBytes, deltaX);
                }

                if (deltaY == 0)
                {
                    flag |= 0x20;
                }
                else if (Math.Abs(deltaY) <= 255)
                {
                    flag |= 0x04 | (deltaY > 0 ? 0x20 : 0);
                    yBytes.WriteByte((byte)Math.Abs(deltaY));
                }
                else
                {
                    WriteInt16(yBytes, deltaY);
                }

                newFlags[i] = (byte)flag;
            }

            stream.Write(newFlags);
            stream.Write(xBytes.ToArray());
            stream.Write(yBytes.ToArray());
            return PadToEven(stream);
        }

        private static byte[] EncodeComposite(OpenTypeFontface face, ReadOnlySpan<byte> source, int glyph, VariationCoordinates variation,
            GvarTable? gvar, out int xMin)
        {
            // Read the components (their offsets are the points gvar moves) ...
            var components = new List<(int Flags, int Glyph, int Arg1, int Arg2, byte[] Transform)>();
            int at = 10;
            while (true)
            {
                int flags = BigEndian.U16(source, at);
                int componentGlyph = BigEndian.U16(source, at + 2);
                at += 4;
                int arg1, arg2;
                if ((flags & Arg1And2AreWords) != 0)
                {
                    arg1 = (flags & ArgsAreXyValues) != 0 ? BigEndian.I16(source, at) : BigEndian.U16(source, at);
                    arg2 = (flags & ArgsAreXyValues) != 0 ? BigEndian.I16(source, at + 2) : BigEndian.U16(source, at + 2);
                    at += 4;
                }
                else
                {
                    arg1 = (flags & ArgsAreXyValues) != 0 ? (sbyte)source[at] : source[at];
                    arg2 = (flags & ArgsAreXyValues) != 0 ? (sbyte)source[at + 1] : source[at + 1];
                    at += 2;
                }

                int transformLength = (flags & WeHaveAScale) != 0 ? 2 : (flags & WeHaveAnXAndYScale) != 0 ? 4 : (flags & WeHaveATwoByTwo) != 0 ? 8 : 0;
                components.Add((flags, componentGlyph, arg1, arg2, source.Slice(at, transformLength).ToArray()));
                at += transformLength;

                if ((flags & MoreComponents) == 0)
                {
                    break;
                }
            }

            // ... apply the deltas ...
            double[]? moveX = null, moveY = null;
            if (gvar is not null)
            {
                int total = components.Count + 4;
                var dx = new double[total];
                var dy = new double[total];
                if (gvar.TryAddDeltas(glyph, variation.Normalized, total, null, null, null, dx, dy))
                {
                    moveX = dx;
                    moveY = dy;
                }
            }

            // ... and the outline's bounds, which the glyph header carries.
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            if (GlyphOutlineDecoder.TryGetGlyphOutline(face, glyph, out var outline, variation))
            {
                double lowX = double.MaxValue, lowY = double.MaxValue, highX = double.MinValue, highY = double.MinValue;
                void Include(Outlines.OutlinePoint p)
                {
                    lowX = Math.Min(lowX, p.X);
                    highX = Math.Max(highX, p.X);
                    lowY = Math.Min(lowY, p.Y);
                    highY = Math.Max(highY, p.Y);
                }

                foreach (var contour in outline.Contours)
                {
                    Include(contour.Start);
                    for (int s = 0; s < contour.Segments.Count; s++)
                    {
                        var segment = contour.Segments[s];
                        if (segment.IsCubic)
                        {
                            Include(segment.Control1);
                            Include(segment.Control2);
                        }

                        Include(segment.End);
                    }
                }

                minX = ToInt16(Math.Floor(lowX));
                minY = ToInt16(Math.Floor(lowY));
                maxX = ToInt16(Math.Ceiling(highX));
                maxY = ToInt16(Math.Ceiling(highY));
            }

            xMin = minX;
            var stream = new MemoryStream();
            WriteInt16(stream, -1);
            WriteInt16(stream, minX);
            WriteInt16(stream, minY);
            WriteInt16(stream, maxX);
            WriteInt16(stream, maxY);

            for (int i = 0; i < components.Count; i++)
            {
                var (flags, componentGlyph, arg1, arg2, transform) = components[i];
                if ((flags & ArgsAreXyValues) != 0 && moveX is not null)
                {
                    arg1 = ToInt16(arg1 + moveX[i]);
                    arg2 = ToInt16(arg2 + moveY![i]);
                }

                // The instructions go, and a moved offset may no longer fit a byte.
                flags &= ~WeHaveInstructions;
                bool fitsBytes = (flags & ArgsAreXyValues) != 0
                    ? arg1 is >= sbyte.MinValue and <= sbyte.MaxValue && arg2 is >= sbyte.MinValue and <= sbyte.MaxValue
                    : arg1 is >= 0 and <= byte.MaxValue && arg2 is >= 0 and <= byte.MaxValue;
                if (fitsBytes)
                {
                    flags &= ~Arg1And2AreWords;
                }
                else
                {
                    flags |= Arg1And2AreWords;
                }

                WriteInt16(stream, flags);
                WriteInt16(stream, componentGlyph);
                if ((flags & Arg1And2AreWords) != 0)
                {
                    WriteInt16(stream, arg1);
                    WriteInt16(stream, arg2);
                }
                else
                {
                    stream.WriteByte((byte)arg1);
                    stream.WriteByte((byte)arg2);
                }

                stream.Write(transform);
            }

            return PadToEven(stream);
        }

        private static int ToInt16(double value) => (int)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);

        private static void WriteInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static byte[] PadToEven(MemoryStream stream)
        {
            if ((stream.Length & 1) != 0)
            {
                stream.WriteByte(0);
            }

            return stream.ToArray();
        }
    }
}
