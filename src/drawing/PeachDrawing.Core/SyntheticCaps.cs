using System;
using System.Collections.Generic;
using PeachDrawing.Text.Shaping;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Small capitals for a font that has none of its own: the lower-case letters (and, for the <c>all</c> modes, the capitals too) are drawn as capitals at a reduced size.
    /// <see cref="TextRunMeasure"/> measures a run this way and a host draws it piece by piece with <see cref="Split"/>, so what is measured is what is drawn.
    /// </summary>
    public static class SyntheticCaps
    {
        /// <summary>The size of a synthesized small capital relative to the font's size.</summary>
        public const double Scale = 0.72;

        /// <summary>Whether a run set in <paramref name="font"/> with these <paramref name="features"/> is drawn with synthesized small capitals: the caps feature asks for small or petite capitals and the font has no such glyphs.</summary>
        /// <param name="font">the font</param>
        /// <param name="features">how the text is shaped, or <see langword="null"/> for the defaults</param>
        public static bool Applies(Font font, ShapeSettings? features)
        {
            ArgumentNullException.ThrowIfNull(font);
            return features is { } settings && Tag(settings.Caps) is { } tag && font.Typeface is { } typeface && !typeface.SupportsFeatures(new HashSet<string> { tag });
        }

        /// <summary>The text with its lower-case letters as capitals, and for each UTF-16 position whether the character is drawn at the reduced size.</summary>
        /// <param name="text">the text</param>
        /// <param name="caps">the caps feature asked for</param>
        /// <param name="small">for each character of the result, whether it is a small capital</param>
        public static string Convert(string text, CapsMode caps, out bool[] small)
        {
            ArgumentNullException.ThrowIfNull(text);
            small = new bool[text.Length];
            var all = caps is CapsMode.AllSmallCaps or CapsMode.AllPetiteCaps;
            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsLower(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    small[i] = true;
                }
                else if (all && char.IsUpper(chars[i]))
                {
                    small[i] = true;
                }
            }

            return new string(chars);
        }

        /// <summary>Splits a run into the pieces drawn at one size each: the text, where it starts and whether it is drawn at the reduced size.</summary>
        /// <param name="text">the text as <see cref="Convert"/> returned it</param>
        /// <param name="small">the flags <see cref="Convert"/> returned</param>
        public static IReadOnlyList<(string Text, int Start, bool Small)> Split(string text, bool[] small)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(small);
            var pieces = new List<(string, int, bool)>();
            var start = 0;
            for (var i = 1; i <= text.Length; i++)
            {
                if (i == text.Length || small[i] != small[start])
                {
                    pieces.Add((text[start..i], start, small[start]));
                    start = i;
                }
            }

            return pieces;
        }

        private static string? Tag(CapsMode caps) => caps switch
        {
            CapsMode.SmallCaps or CapsMode.AllSmallCaps => "smcp",
            CapsMode.PetiteCaps or CapsMode.AllPetiteCaps => "pcap",
            _ => null,
        };
    }
}
