using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>One cluster of a measured run: the characters that are drawn as one unit, and where.</summary>
    /// <param name="Start">the index of the cluster's first UTF-16 code unit in the text</param>
    /// <param name="End">the index one past its last</param>
    /// <param name="X">where the cluster starts along the run, from the run's left edge</param>
    /// <param name="Width">how far the cluster advances, letter spacing and word spacing included</param>
    public readonly record struct TextRunCluster(int Start, int End, double X, double Width);

    /// <summary>What a run of text measures: its advance, the box its ink covers, the metrics of its font, and its clusters.</summary>
    public sealed class TextRunMetrics
    {
        /// <summary>Metrics of a run that takes no room: everything is 0.</summary>
        public TextRunMetrics()
        {
        }

        /// <summary>The distance the pen travels along the run, spacing included.</summary>
        public double Width { get; internal set; }

        /// <summary>The ink's left edge, from the run's left edge (negative when it reaches back past it).</summary>
        public double InkLeft { get; internal set; }

        /// <summary>The ink's right edge, from the run's left edge.</summary>
        public double InkRight { get; internal set; }

        /// <summary>How far the ink rises above the alphabetic baseline (negative when it all lies below it).</summary>
        public double InkAscent { get; internal set; }

        /// <summary>How far the ink falls below the alphabetic baseline (negative when it all lies above it).</summary>
        public double InkDescent { get; internal set; }

        /// <summary>How far the font rises above the alphabetic baseline, as its own metrics say.</summary>
        public double FontAscent { get; internal set; }

        /// <summary>How far the font falls below the alphabetic baseline, as its own metrics say.</summary>
        public double FontDescent { get; internal set; }

        /// <summary>The space the font adds between lines, beyond its ascent and descent.</summary>
        public double FontLineGap { get; internal set; }

        /// <summary>How far the em square rises above the baseline: the font's size shared between ascent and descent in the proportion the font has them.</summary>
        public double EmAscent { get; internal set; }

        /// <summary>How far the em square falls below the baseline.</summary>
        public double EmDescent { get; internal set; }

        /// <summary>The hanging baseline, above the alphabetic one.</summary>
        public double HangingBaseline { get; internal set; }

        /// <summary>The ideographic baseline.</summary>
        public double IdeographicBaseline { get; internal set; }

        /// <summary>The clusters, in the order the text has them.</summary>
        public IReadOnlyList<TextRunCluster> Clusters { get; internal set; } = [];
    }

    /// <summary>
    /// Measures a run of text in a <see cref="Font"/> the way a canvas does: shaped with the given features, each character moved on by the letter spacing and each space by
    /// the word spacing as well, with the box its glyphs' outlines cover and the metrics of the font. The same shaping <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette, ShapeSettings?)"/> draws with,
    /// so what is measured is what is drawn.
    /// </summary>
    public static class TextRunMeasure
    {
        /// <summary>Measures <paramref name="text"/> set in <paramref name="font"/>.</summary>
        /// <param name="font">the font</param>
        /// <param name="text">the text; a character a canvas does not draw (the null character) takes no room</param>
        /// <param name="letterSpacing">added after every cluster, in the font's units of size</param>
        /// <param name="wordSpacing">added after every space, besides the letter spacing</param>
        /// <param name="features">how the text is shaped, or <see langword="null"/> for the defaults</param>
        /// <exception cref="ArgumentNullException"><paramref name="font"/> or <paramref name="text"/> is <see langword="null"/></exception>
        public static TextRunMetrics Measure(Font font, string text, double letterSpacing = 0, double wordSpacing = 0, ShapeSettings? features = null)
        {
            ArgumentNullException.ThrowIfNull(font);
            ArgumentNullException.ThrowIfNull(text);

            var typeface = font.Typeface ?? throw new InvalidOperationException("The font has no typeface to measure with.");
            var metrics = typeface.Metrics;
            var scale = font.Size / metrics.UnitsPerEm;
            var result = new TextRunMetrics();

            // The metrics the font gives for a line of its text.
            var ascent = metrics.NormalLineAscent * scale;
            var descent = metrics.NormalLineDescent * scale;
            result.FontAscent = ascent;
            result.FontDescent = descent;
            result.FontLineGap = Math.Max(0, metrics.NormalLineGap * scale);
            var total = ascent + descent;
            result.EmAscent = total > 0 ? font.Size * ascent / total : 0;
            result.EmDescent = total > 0 ? font.Size - result.EmAscent : 0;
            result.HangingBaseline = result.EmAscent * 2 / 3;
            result.IdeographicBaseline = result.EmDescent / 2;

            var shaped = text.Replace('\0', '﻿');
            var settings = features ?? ShapeSettings.Default;
            if (shaped.Length == 0)
                return result;

            var run = Shaper.Shape(typeface, shaped, settings);
            var clusters = new List<TextRunCluster>();
            double pen = 0;
            double left = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity, bottom = double.PositiveInfinity;

            for (var i = 0; i < run.Glyphs.Count; i++)
            {
                var glyph = run.Glyphs[i];
                var advance = glyph.IsHiddenIgnorable ? 0 : (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale;

                if (typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline) && outline is not null && !outline.IsEmpty)
                {
                    foreach (var contour in outline.Contours)
                    {
                        var (px, py) = (contour.Start.X, contour.Start.Y);
                        Include(px, py);
                        foreach (var segment in contour.Segments)
                        {
                            if (segment.IsCubic)
                            {
                                for (var step = 1; step <= 8; step++)
                                {
                                    var t = step / 8.0;
                                    var u = 1 - t;
                                    Include(u * u * u * px + 3 * u * u * t * segment.Control1.X + 3 * u * t * t * segment.Control2.X + t * t * t * segment.End.X,
                                        u * u * u * py + 3 * u * u * t * segment.Control1.Y + 3 * u * t * t * segment.Control2.Y + t * t * t * segment.End.Y);
                                }
                            }
                            else
                            {
                                Include(segment.End.X, segment.End.Y);
                            }

                            (px, py) = (segment.End.X, segment.End.Y);
                        }

                        void Include(double x, double y)
                        {
                            var gx = pen + (glyph.XOffset + x) * scale;
                            var gy = (glyph.YOffset + y) * scale;
                            left = Math.Min(left, gx);
                            right = Math.Max(right, gx);
                            top = Math.Max(top, gy);
                            bottom = Math.Min(bottom, gy);
                        }
                    }
                }

                // Spacing follows each cluster's last glyph; a space adds the word spacing too.
                var next = i + 1 < run.Glyphs.Count ? run.Glyphs[i + 1] : (PlacedGlyph?)null;
                var endsCluster = next is null || next.Value.ClusterStart != glyph.ClusterStart;
                var spacing = 0.0;
                if (endsCluster && !glyph.IsHiddenIgnorable)
                {
                    spacing = letterSpacing;
                    if (glyph.ClusterStart < shaped.Length && shaped[glyph.ClusterStart] == ' ')
                        spacing += wordSpacing;
                }

                var clusterWidth = advance + spacing;
                if (clusters.Count > 0 && clusters[^1].Start == glyph.ClusterStart)
                {
                    var previous = clusters[^1];
                    clusters[^1] = previous with { Width = previous.Width + clusterWidth };
                }
                else
                {
                    clusters.Add(new TextRunCluster(glyph.ClusterStart, glyph.ClusterStart + Math.Max(1, glyph.ClusterLength), pen, clusterWidth));
                }

                pen += clusterWidth;
            }

            result.Width = pen;
            result.Clusters = clusters;
            if (left <= right)
            {
                result.InkLeft = left;
                result.InkRight = right;
                result.InkAscent = top;
                result.InkDescent = -bottom;
            }

            return result;
        }
    }
}
