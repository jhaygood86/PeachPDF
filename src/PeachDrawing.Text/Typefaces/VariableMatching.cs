using System;
using System.Collections.Generic;

namespace PeachDrawing.Text
{
    /// <summary>
    /// Puts a variable face where a <see cref="TypefaceQuery"/> asks for it: the weight, width and slant of the query select a location in
    /// the face's design space (CSS Fonts 4 section 5.2, "font-weight, font-width and font-style are applied to the font's variation
    /// axes"), and the query's own axis settings then override it.
    /// </summary>
    internal static class VariableMatching
    {
        // The OpenType width classes and the percentages CSS gives their keywords (font-stretch, ultra-condensed to ultra-expanded).
        private static readonly double[] WidthPercent = [50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200];

        /// <summary>The angle of the slant that <c>font-style: italic</c> uses when a font has a slant axis but no italic axis.</summary>
        private const double DefaultObliqueAngle = 14;

        internal static (Typeface Typeface, SyntheticStyle Synthesis) Apply(Typeface face, SyntheticStyle synthesis, in TypefaceQuery query)
        {
            var settings = new List<AxisSetting>();
            VariationAxis? Axis(string tag)
            {
                foreach (var axis in face.Axes)
                {
                    if (axis.Tag == tag)
                        return axis;
                }

                return null;
            }

            if (Axis(AxisTags.Weight) is { } weight)
            {
                double value = Math.Clamp(query.Weight, weight.Minimum, weight.Maximum);
                settings.Add(new AxisSetting(AxisTags.Weight, value));

                // The face is as bold as its axis allows: faking more would embolden outlines that are already at the heaviest design.
                // Faking is only still wanted when the request is bold and the axis cannot reach it.
                if (value >= 600 || query.Weight < 600)
                    synthesis &= ~SyntheticStyle.Bold;
            }

            if (Axis(AxisTags.Width) is { } width)
            {
                int index = Math.Clamp(query.Width, 1, 9) - 1;
                settings.Add(new AxisSetting(AxisTags.Width, Math.Clamp(WidthPercent[index], width.Minimum, width.Maximum)));
            }

            if (Axis(AxisTags.Italic) is { } italic)
            {
                settings.Add(new AxisSetting(AxisTags.Italic, query.IsItalic ? Math.Clamp(1, italic.Minimum, italic.Maximum) : Math.Clamp(0, italic.Minimum, italic.Maximum)));
                if (query.IsItalic)
                    synthesis &= ~SyntheticStyle.Italic;
            }
            else if (query.IsItalic && Axis(AxisTags.Slant) is { } slant)
            {
                settings.Add(new AxisSetting(AxisTags.Slant, Math.Clamp(-DefaultObliqueAngle, slant.Minimum, slant.Maximum)));
                synthesis &= ~SyntheticStyle.Italic;
            }

            if (query.Axes is { } explicitSettings)
                settings.AddRange(explicitSettings);

            return (face.WithAxes(settings), synthesis);
        }
    }
}
