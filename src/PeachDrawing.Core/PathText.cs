using PeachDrawing.Core.Geometry;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>Which side of a path the text sits on.</summary>
    public enum PathTextSide
    {
        /// <summary>Text runs in the path's direction, with the top of the letters toward the left of it (SVG's default).</summary>
        Left,

        /// <summary>Text reads the path from its far end, upside down relative to <see cref="Left"/>.</summary>
        Right,
    }

    /// <summary>Which part of the text is placed at <see cref="PathTextOptions.StartOffset"/>.</summary>
    public enum PathTextAnchor
    {
        /// <summary>The start of the text.</summary>
        Start,

        /// <summary>The middle of the text.</summary>
        Middle,

        /// <summary>The end of the text.</summary>
        End,
    }

    /// <summary>How <see cref="PathText.Layout"/> places text on a path.</summary>
    /// <param name="StartOffset">how far along the path the <paramref name="Anchor"/> of the text goes</param>
    /// <param name="Anchor">which part of the text sits at <paramref name="StartOffset"/></param>
    /// <param name="Side">which side of the path the text sits on</param>
    /// <param name="LetterSpacing">extra space added after every glyph, in the canvas's user units</param>
    /// <param name="Shaping">shaping features (ligatures, language, and so on), or <see langword="null"/> for the defaults</param>
    public readonly record struct PathTextOptions(
        double StartOffset = 0,
        PathTextAnchor Anchor = PathTextAnchor.Start,
        PathTextSide Side = PathTextSide.Left,
        double LetterSpacing = 0,
        ShapeSettings? Shaping = null);

    /// <summary>One glyph placed on a path.</summary>
    /// <param name="GlyphIndex">the glyph's index in the typeface</param>
    /// <param name="Advance">how much room the glyph takes along the path</param>
    /// <param name="Transform">maps the glyph's own space, with its origin on the baseline at the start of the glyph, onto the canvas</param>
    public readonly record struct PathGlyph(int GlyphIndex, double Advance, Matrix3x2 Transform);

    /// <summary>
    /// Text set along a path: each glyph is placed at its own distance along the path and turned to follow it, using the shaper's
    /// glyphs and advances, so ligatures, kerning and other shaping survive.
    /// </summary>
    public static class PathText
    {
        /// <summary>
        /// The transform that puts a glyph centred on a point of a path and turns it to follow the path there. In the glyph's own
        /// space the origin is the glyph's centre on its baseline.
        /// </summary>
        /// <param name="path">the measured path</param>
        /// <param name="distance">where the glyph's centre goes, measured from the start of the path (or, for <see cref="PathTextSide.Right"/>, from its end)</param>
        /// <param name="side">which side of the path the text sits on</param>
        /// <param name="normalOffset">how far to move the glyph perpendicular to the path: positive moves it to the right of the direction of travel (below the path, for one running left to right on a y-down canvas)</param>
        /// <param name="rotationDegrees">extra rotation, in degrees, applied on top of following the path</param>
        /// <returns>the transform, or <see langword="null"/> when <paramref name="distance"/> falls off either end of the path</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/></exception>
        public static Matrix3x2? GetGlyphFrame(PathMeasure path, double distance, PathTextSide side = PathTextSide.Left,
            double normalOffset = 0, double rotationDegrees = 0)
        {
            ArgumentNullException.ThrowIfNull(path);

            // Side "right" reads the path from its far end.
            var along = side == PathTextSide.Right ? path.Length - distance : distance;
            if (along < 0 || along > path.Length)
                return null;

            var sample = path.PointAtLength(along);
            var tangentDegrees = sample.TangentDegrees + (side == PathTextSide.Right ? 180 : 0);
            var tangent = tangentDegrees * (Math.PI / 180.0);

            // The normal offset moves the glyph perpendicular to the path.
            var x = sample.X - Math.Sin(tangent) * normalOffset;
            var y = sample.Y + Math.Cos(tangent) * normalOffset;

            var rotation = (tangentDegrees + rotationDegrees) * (Math.PI / 180.0);
            // Sine and cosine are taken in double and only then narrowed, so the frame is as exact as a matrix of floats can be.
            var cos = Math.Cos(rotation);
            var sin = Math.Sin(rotation);
            return new Matrix3x2((float)cos, (float)sin, (float)-sin, (float)cos, (float)x, (float)y);
        }

        /// <summary>
        /// Shapes <paramref name="text"/> and places each glyph along <paramref name="path"/>. A glyph whose centre falls off either
        /// end of the path is left out.
        /// </summary>
        /// <param name="typeface">the typeface to shape with</param>
        /// <param name="size">the font size, in the canvas's user units</param>
        /// <param name="text">the text to set</param>
        /// <param name="path">the measured path to set it along</param>
        /// <param name="options">where and how to place it</param>
        /// <returns>the glyphs that land on the path, in reading order</returns>
        /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
        public static IReadOnlyList<PathGlyph> Layout(Typeface typeface, double size, string text, PathMeasure path, PathTextOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(path);

            var result = new List<PathGlyph>();
            double unitsPerEm = typeface.Metrics.UnitsPerEm;
            if (unitsPerEm == 0 || text.Length == 0 || path.IsEmpty)
                return result;

            var run = Shaper.Shape(typeface, text, options.Shaping ?? ShapeSettings.Default);
            var scale = size / unitsPerEm;

            var advances = new double[run.Glyphs.Count];
            double total = 0;
            for (var i = 0; i < advances.Length; i++)
            {
                var glyph = run.Glyphs[i];
                advances[i] = (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + options.LetterSpacing;
                total += advances[i];
            }

            var start = options.StartOffset + options.Anchor switch
            {
                PathTextAnchor.Middle => -total / 2,
                PathTextAnchor.End => -total,
                _ => 0,
            };

            var pen = start;
            for (var i = 0; i < advances.Length; i++)
            {
                var glyph = run.Glyphs[i];
                var advance = advances[i];
                var centre = pen + advance / 2;
                pen += advance;

                if (glyph.IsHiddenIgnorable || GetGlyphFrame(path, centre, options.Side) is not { } frame)
                    continue;

                // The glyph's own origin is the start of its advance on the baseline, nudged by the shaper's offsets.
                var local = Matrix3x2.CreateTranslation((float)(-advance / 2 + glyph.XOffset * scale), (float)(-glyph.YOffset * scale));
                result.Add(new PathGlyph(glyph.GlyphIndex, advance, local.Then(frame)));
            }

            return result;
        }

        /// <summary>
        /// Draws <paramref name="text"/> along <paramref name="path"/> in one colour. See <see cref="Layout"/> for how it is placed.
        /// </summary>
        /// <param name="canvas">the canvas to draw onto</param>
        /// <param name="text">the text to draw</param>
        /// <param name="typeface">the typeface to draw with</param>
        /// <param name="size">the font size, in the canvas's user units</param>
        /// <param name="color">the colour of the text</param>
        /// <param name="path">the path to draw along</param>
        /// <param name="options">where and how to place the text</param>
        /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
        public static void DrawStringAlongPath(this Canvas canvas, string text, Typeface typeface, double size, PaintColor color,
            GraphicsPath path, PathTextOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(path);

            var glyphs = Layout(typeface, size, text, new PathMeasure(path), options);

            // A Font's size is a true point size; a canvas's user unit is 1 / PixelsPerPoint of one.
            var font = new TypefaceFont(typeface, size / canvas.PixelsPerPoint);
            foreach (var glyph in glyphs)
            {
                canvas.PushTransform(glyph.Transform);
                canvas.DrawGlyphs([new GlyphPlacement(glyph.GlyphIndex, 0, 0)], font, color);
                canvas.PopTransform();
            }
        }
    }
}
