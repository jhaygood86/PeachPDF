using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves the two shaded faces of a beveled line style (<c>inset</c>/<c>outset</c>, and the two
    /// halves a <c>groove</c>/<c>ridge</c> is built from) for borders, outlines and collapsed table
    /// border segments alike.
    /// </summary>
    /// <remarks>
    /// CSS 2.1 §8.5.3 leaves the exact shading UA-defined, so "correct" here means "what a reader
    /// comparing against a browser expects". These transforms were derived by sampling Chrome's own
    /// rasterization (Blink's <c>Color::Dark</c>/<c>Color::Light</c> and
    /// <c>CalculateBorderStyleColor</c>) and reproduce it exactly, including the integer truncation and
    /// the 255.99998 scale factor Blink uses - see <see cref="Dark"/>/<see cref="Light"/>.
    ///
    /// The part that is not obvious, and that a naive "just darken one pair of sides" implementation
    /// gets visibly wrong, is <see cref="Shade"/>'s fallback: a color at or near black cannot be
    /// darkened into a visible edge, so both faces lighten instead, one step apart. Without it a plain
    /// <c>border: 2px inset black</c> - which is what a UA-default <c>&lt;fieldset&gt;</c>/
    /// <c>&lt;table border=1&gt;</c> amounts to - paints black-on-black and the bevel disappears.
    /// </remarks>
    internal static class BorderBevelColors
    {
        /// <summary>
        /// Blink converts its 0..1 float channels back to bytes with this factor and a truncating cast,
        /// not by multiplying by 255 and rounding. Matching it is what makes the results land on the
        /// same byte as Chrome's rather than one off.
        /// </summary>
        private const double ChannelScale = 255.99998;

        /// <summary>How far <see cref="Dark"/>/<see cref="Light"/> move the color's brightest channel.</summary>
        private const double ShadeStep = 0.33;

        /// <summary>
        /// The WCAG contrast ratio a color must still have against its own darkened form for darkening
        /// to read as an edge at all. Measured from Chrome: a gray of 32 (ratio 1.289) lightens, a gray
        /// of 33 (ratio 1.304) darkens.
        /// </summary>
        private const double MinBevelContrastRatio = 1.3;

        /// <summary>
        /// The darkened face of a bevel: scales every channel so the brightest one drops by
        /// <see cref="ShadeStep"/>. Black (and anything whose channels all scale to zero) stays black -
        /// <see cref="Shade"/> is what notices that and lightens instead.
        /// </summary>
        internal static RColor Dark(RColor c)
        {
            var v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
            if (v <= 0) return c;

            var multiplier = Math.Max(0, (v - ShadeStep) / v);
            return Scale(c, multiplier);
        }

        /// <summary>
        /// The lightened face of a bevel: scales every channel so the brightest one rises by
        /// <see cref="ShadeStep"/>, saturating at full brightness. Black has nothing to scale, so it
        /// maps to Blink's own "lightened black" constant instead.
        /// </summary>
        internal static RColor Light(RColor c)
        {
            var v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
            if (v <= 0) return RColor.FromArgb(c.A, 0x54, 0x54, 0x54);

            var multiplier = Math.Min(1, v + ShadeStep) / v;
            return Scale(c, multiplier);
        }

        /// <summary>
        /// One face of a bevel. Normally the darkened face is <see cref="Dark"/> and the lit face is
        /// <see cref="Light"/>; when darkening would leave too little contrast to read as an edge (see
        /// <see cref="MinBevelContrastRatio"/>) both faces lighten instead, the lit one twice, so they
        /// stay distinguishable from each other.
        /// </summary>
        internal static RColor Shade(RColor color, bool darken)
        {
            var dark = Dark(color);
            if (ContrastRatio(color, dark) >= MinBevelContrastRatio)
                return darken ? dark : Light(color);

            var light = Light(color);
            return darken ? light : Light(light);
        }

        /// <summary>
        /// The shaded color for one side of a box whose style is <c>inset</c> or <c>outset</c>.
        /// <c>inset</c> darkens the top and left and lights the bottom and right; <c>outset</c> is its
        /// mirror image. This per-side flip is the entire 3D effect - shading all four sides alike
        /// produces a flat two-tone frame instead of a bevel.
        /// </summary>
        internal static RColor ForSide(RColor color, Border side, bool inset) =>
            Shade(color, (side is Border.Top or Border.Left) == inset);

        /// <summary>
        /// <see cref="ForSide"/> for a collapsed-border segment, which has no owning box and so no real
        /// side: css-tables-3 grid lines run either along a row (behaves like a top edge) or down a
        /// column (behaves like a left edge).
        /// </summary>
        internal static RColor ForSegment(RColor color, bool isHorizontal, bool inset) =>
            ForSide(color, isHorizontal ? Border.Top : Border.Left, inset);

        private static RColor Scale(RColor c, double multiplier) =>
            RColor.FromArgb(
                c.A,
                Channel(c.R, multiplier),
                Channel(c.G, multiplier),
                Channel(c.B, multiplier));

        private static int Channel(byte value, double multiplier) =>
            Math.Clamp((int)(multiplier * (value / 255.0) * ChannelScale), 0, 255);

        /// <summary>WCAG 2 contrast ratio between two opaque colors.</summary>
        private static double ContrastRatio(RColor a, RColor b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            return la > lb
                ? (la + 0.05) / (lb + 0.05)
                : (lb + 0.05) / (la + 0.05);
        }

        private static double RelativeLuminance(RColor c) =>
            0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

        private static double Linearize(byte channel)
        {
            var s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }
}
