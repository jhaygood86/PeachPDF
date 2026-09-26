using PeachDrawing.Text;
using PeachPDF.CSS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Turns the cascaded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of a box into the one string that travels down the
    /// font-creation chain (it is part of every font cache key, so it has to be a value), and turns that string into the axis settings
    /// the font set is asked for.
    /// </summary>
    /// <remarks>
    /// The encoded form is <c>tag=value;tag=value</c>, preceded by <c>-opsz;</c> when <c>font-optical-sizing: none</c> switches the
    /// automatic optical size off. It is <see langword="null"/> for the common case of neither (initial values), so nothing changes
    /// for a document that does not use variable fonts.
    /// </remarks>
    internal static class FontVariationSettingsResolver
    {
        private const string OpticalSizingOff = "-opsz";

        /// <summary>The encoded settings for a box, or <see langword="null"/> when it has the initial values.</summary>
        internal static string? Encode(FontOpticalSizingMode opticalSizing, string cascaded)
        {
            var text = new StringBuilder();
            if (opticalSizing == FontOpticalSizingMode.None)
            {
                text.Append(OpticalSizingOff);
            }

            foreach (var (tag, value) in ParseSettings(cascaded))
            {
                if (text.Length > 0)
                {
                    text.Append(';');
                }

                text.Append(tag).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture));
            }

            return text.Length == 0 ? null : text.ToString();
        }

        /// <summary>Parses a cascaded <c>font-variation-settings</c> string such as <c>"wght" 650, "wdth" 80</c>; <c>normal</c> is an empty list.</summary>
        internal static IReadOnlyList<(string Tag, double Value)> ParseSettings(string cascaded)
        {
            var settings = new List<(string, double)>();
            if (string.IsNullOrWhiteSpace(cascaded) || cascaded.Trim() == Keywords.Normal)
            {
                return settings;
            }

            foreach (var entry in cascaded.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var tag = parts[0].Trim('"', '\'');
                if (tag.Length == 4 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                {
                    settings.Add((tag, value));
                }
            }

            return settings;
        }

        /// <summary>
        /// The axis settings to ask the font set for: the automatic optical size (the font size in CSS pixels, unless switched off), and then
        /// the box's own settings, which win over it. The weight, width and slant are not among them: the font set derives those from
        /// the query itself, inside the range the face declares.
        /// </summary>
        /// <param name="encoded">The value of <see cref="Encode"/>.</param>
        /// <param name="sizeInPixels">The font size in CSS pixels.</param>
        internal static IReadOnlyList<AxisSetting> ToAxes(string? encoded, double sizeInPixels)
        {
            var axes = new List<AxisSetting>();
            var explicitSettings = new List<AxisSetting>();
            bool opticalSizingAuto = true;

            if (encoded is not null)
            {
                foreach (var token in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token == OpticalSizingOff)
                    {
                        opticalSizingAuto = false;
                        continue;
                    }

                    int equals = token.IndexOf('=');
                    if (equals == 4 && double.TryParse(token.AsSpan(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        explicitSettings.Add(new AxisSetting(token[..4], value));
                    }
                }
            }

            if (opticalSizingAuto)
            {
                axes.Add(new AxisSetting(AxisTags.OpticalSize, sizeInPixels));
            }

            axes.AddRange(explicitSettings);
            return axes;
        }
    }
}
