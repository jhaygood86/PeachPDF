using PeachDrawing.Text.Internal.Fonts;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text
{
    /// <summary>
    /// Puts a variable face where a <see cref="TypefaceQuery"/> asks for it: the weight, width and slant of the query select a location in
    /// the face's design space (CSS Fonts 4 section 5.2, "font-weight, font-width and font-style are applied to the font's variation
    /// axes"), and the query's own axis settings then override it.
    /// </summary>
    /// <remarks>
    /// A face that declares what it covers (an <c>@font-face</c> range, section 4.4) is only set inside it: a request outside the range is
    /// brought to its nearest end, so a face declared as weights 300 to 500 is drawn at 500 for a bold request, and then still needs
    /// the bold that the range cannot supply.
    /// </remarks>
    internal static class VariableMatching
    {
        /// <summary>The angle of the slant that <c>font-style: italic</c> uses when a font has a slant axis but no italic axis.</summary>
        private const double DefaultObliqueAngle = 14;

        internal static (Typeface Typeface, SyntheticStyle Synthesis) Apply(Typeface face, SyntheticStyle synthesis, in TypefaceQuery query, FaceRanges? declared = null)
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
                double requested = declared?.Weight.Clamp(query.Weight) ?? query.Weight;
                double value = Math.Clamp(requested, weight.Minimum, weight.Maximum);
                settings.Add(new AxisSetting(AxisTags.Weight, value));

                // The face is as bold as its axis allows: faking more would embolden outlines that are already at the heaviest design.
                // Faking is only still wanted when the request is bold and the axis (or the declared range) cannot reach it. That is decided
                // here from what the axis can draw, since a range that is declared wider than the axis would have told the matcher it could.
                if (value >= 600 || query.Weight < 600)
                    synthesis &= ~SyntheticStyle.Bold;
                else
                    synthesis |= SyntheticStyle.Bold;
            }

            if (Axis(AxisTags.Width) is { } width)
            {
                double requested = query.WidthPercent ?? WidthClasses.ToPercent(query.Width);
                if (declared is not null)
                    requested = declared.Width.Clamp(requested);

                settings.Add(new AxisSetting(AxisTags.Width, Math.Clamp(requested, width.Minimum, width.Maximum)));
            }

            var oblique = declared?.Oblique;
            if (Axis(AxisTags.Italic) is { } italic)
            {
                settings.Add(new AxisSetting(AxisTags.Italic, query.IsItalic ? Math.Clamp(1, italic.Minimum, italic.Maximum) : Math.Clamp(0, italic.Minimum, italic.Maximum)));
                if (query.IsItalic)
                    synthesis &= ~SyntheticStyle.Italic;
            }
            else if ((query.IsItalic || oblique is not null) && Axis(AxisTags.Slant) is { } slant)
            {
                // CSS oblique angles lean to the right, and the slant axis measures a lean to the right as negative. Upright text
                // is at angle 0, which a range that does not include 0 brings to its own nearest end.
                double angle = query.IsItalic ? query.ObliqueAngle ?? DefaultObliqueAngle : 0;
                if (oblique is { } covered)
                    angle = covered.Clamp(angle);

                settings.Add(new AxisSetting(AxisTags.Slant, Math.Clamp(-angle, slant.Minimum, slant.Maximum)));
                if (query.IsItalic)
                    synthesis &= ~SyntheticStyle.Italic;
            }

            else if (query.IsItalic && !face.IsItalic)
            {
                // Neither axis can lean the face, whatever its rule declared, so the lean is still to be faked.
                synthesis |= SyntheticStyle.Italic;
            }

            if (query.Axes is { } explicitSettings)
                settings.AddRange(explicitSettings);

            return (face.WithAxes(settings), synthesis);
        }
    }
}
