using System;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// The OpenType width classes (<c>usWidthClass</c>, 1 to 9) and the percentages of the normal width CSS gives their
    /// <c>font-stretch</c> keywords, the two scales the engine converts between.
    /// </summary>
    internal static class WidthClasses
    {
        /// <summary>The percentage of the normal width of each class, ultra-condensed to ultra-expanded.</summary>
        private static readonly double[] Percentages = [50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200];

        /// <summary>The percentage of the normal width that <c>font-stretch: normal</c> is.</summary>
        internal const double Normal = 100;

        /// <summary>The percentage of a width class; a class outside 1 to 9 is brought into it.</summary>
        internal static double ToPercent(int widthClass) => Percentages[Math.Clamp(widthClass, 1, 9) - 1];

        /// <summary>The class whose percentage is nearest to <paramref name="percent"/>.</summary>
        internal static int FromPercent(double percent)
        {
            int best = 5;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < Percentages.Length; i++)
            {
                double distance = Math.Abs(Percentages[i] - percent);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i + 1;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// The weights, widths and oblique angles a registered face covers (CSS Fonts 4 section 4.4: an <c>@font-face</c> descriptor is a
    /// range), which is what nearest-face matching tests a request against and what a variable face's axes are kept inside.
    /// </summary>
    /// <param name="Weight">The weights covered, on the CSS scale.</param>
    /// <param name="Width">The widths covered, as percentages of the normal width.</param>
    /// <param name="Oblique">The oblique angles covered in degrees leaning to the right, or <see langword="null"/> when the face declares none.</param>
    internal sealed record FaceRanges(AxisRange Weight, AxisRange Width, AxisRange? Oblique)
    {
        /// <summary>The ranges of a face that covers one weight and one width and declares no oblique range.</summary>
        internal static FaceRanges Point(int weight, int widthClass) =>
            new(new AxisRange(weight), new AxisRange(WidthClasses.ToPercent(widthClass)), null);
    }

    /// <summary>What a request for a face asks of a family.</summary>
    /// <param name="Weight">The weight wanted.</param>
    /// <param name="IsItalic">Whether an italic or oblique face is wanted.</param>
    /// <param name="WidthPercent">The width wanted, as a percentage of the normal width.</param>
    /// <param name="Codepoint">A character the face has to cover, or <see langword="null"/>.</param>
    internal readonly record struct FaceRequest(int Weight, bool IsItalic, double WidthPercent, Rune? Codepoint = null);
}
