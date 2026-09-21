using PeachPDF.CSS;
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
    /// <c>CalculateInsetOutsetColor</c>) and reproduce it exactly, including the integer truncation and
    /// the 255.99998 scale factor Blink uses - see <see cref="Dark"/>/<see cref="Light"/>.
    ///
    /// The part that is not obvious, and that a naive "just darken one pair of sides" implementation
    /// gets visibly wrong, is <see cref="Shade"/>'s two ends. A color at or near black cannot be
    /// darkened into a visible edge, so both faces lighten instead, one step apart: without that, a
    /// bare <c>border: 2px inset</c> over default black text - where <c>border-color</c>'s initial
    /// <c>currentColor</c> resolves to black - would paint black-on-black and the bevel would vanish.
    /// A color at or near white cannot be lightened into one either, so there the lit face keeps the
    /// declared color rather than clipping to white.
    /// </remarks>
    internal static class BorderBevelColors
    {
        /// <summary>
        /// The four line styles that derive their faces from their color rather than painting it as
        /// declared - everything this class handles. Shared rather than re-listed per call site,
        /// because a fifth arm added here and missed at one of them is a silent divergence: the style
        /// would shade in one place and paint flat in another.
        /// </summary>
        internal static bool IsBeveled(LineStyle style) =>
            style is LineStyle.Inset or LineStyle.Outset or LineStyle.Groove or LineStyle.Ridge;

        /// <summary>
        /// The color a <c>currentColor</c> border side resolves to when <see cref="IsBeveled"/> holds
        /// for that side - the base whose two faces are the familiar <c>#9a9a9a</c> over
        /// <c>#eeeeee</c> of an unstyled <c>&lt;hr&gt;</c> or a bare <c>border: 2px inset</c>.
        /// </summary>
        /// <remarks>
        /// Blink substitutes this fixed light color for the box's own <c>color</c> when it resolves a
        /// border side's <c>currentColor</c> and that side is bevelled
        /// (<c>ComputedStyleUtils::BorderSideColor</c>), because shading a bevel from the text color
        /// produces no usable edge at either end of the range - default black text would give a
        /// black-on-black frame. Measured against Chrome 153: <c>border: 20px inset</c> paints
        /// <c>#9a9a9a</c>/<c>#eeeeee</c> whatever <c>color</c> is - black, red, white, <c>#808080</c>,
        /// even <c>transparent</c> or a translucent color, since the base is fully opaque. A
        /// *declared* color is untouched, which is why <c>border: 20px inset #808080</c> still shades
        /// to <c>#2c2c2c</c>/<c>#d4d4d4</c>.
        /// <para>
        /// The substitution is of the resolution base, not of the painted result: it happens where
        /// <c>currentColor</c> becomes a real color, and <see cref="Shade"/> then shades whatever came
        /// out. Blink reports the *unsubstituted* color from <c>getComputedStyle</c>, so this is a used
        /// value for painting only. See <c>DerivedStyle.ResolveBorderSideColor</c>, which applies it per
        /// side, and exempts a table display type the way Blink does.
        /// </para>
        /// </remarks>
        internal static readonly RColor CurrentColorBase = RColor.FromArgb(238, 238, 238);

        /// <summary>
        /// Blink converts its 0..1 float channels back to bytes with this factor and a truncating cast,
        /// not by multiplying by 255 and rounding. Matching it is what makes the results land on the
        /// same byte as Chrome's rather than one off.
        /// </summary>
        private const double ChannelScale = 255.99998;

        /// <summary>How far <see cref="Dark"/>/<see cref="Light"/> move the color's brightest channel.</summary>
        private const double ShadeStep = 0.33;

        /// <summary>
        /// At or below this relative luminance a color is too dark to darken into a visible edge, and
        /// both faces lighten instead. It is the luminance of <c>rgb(32, 32, 32)</c>: a gray of 32
        /// lightens, a gray of 33 darkens.
        /// </summary>
        private const double NearBlackLuminance = 0.014443844;

        /// <summary>
        /// Above this relative luminance a color is too light to lighten into a visible edge, and the
        /// lit face keeps the declared color instead. It is the luminance of <c>rgb(235, 235, 235)</c>,
        /// and the comparison is strict: a gray of 235 still lightens, a gray of 236 does not.
        /// </summary>
        private const double NearWhiteLuminance = 0.83077;

        /// <summary>
        /// The darkened face of a bevel: scales every channel so the brightest one drops by
        /// <see cref="ShadeStep"/>. A color dark enough that every channel scales to zero comes out
        /// black. That on its own is not what makes <see cref="Shade"/> lighten instead - it decides on
        /// the declared color's luminance (see <see cref="NearBlackLuminance"/>), so a dark chromatic
        /// color above the threshold keeps the black this returns.
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
        /// One face of a bevel, mirroring Blink's <c>CalculateInsetOutsetColor</c>. Normally the
        /// darkened face is <see cref="Dark"/> and the lit face is <see cref="Light"/>. At either end of
        /// the luminance range one of those stops producing a visible edge, so the rule changes: at or
        /// below <see cref="NearBlackLuminance"/> both faces lighten, the lit one twice, so they stay
        /// distinguishable from each other; above <see cref="NearWhiteLuminance"/> the lit face keeps
        /// the declared color, because lightening it would only clip toward white.
        /// </summary>
        internal static RColor Shade(RColor color, bool darken)
        {
            var luminance = RelativeLuminance(color);

            if (luminance <= NearBlackLuminance)
            {
                var light = Light(color);
                return darken ? light : Light(light);
            }

            if (darken)
                return Dark(color);

            return luminance > NearWhiteLuminance ? color : Light(color);
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

        /// <summary>
        /// WCAG 2 relative luminance. Alpha plays no part: Blink thresholds the declared color, not
        /// whatever it will end up composited over.
        /// </summary>
        private static double RelativeLuminance(RColor c) =>
            0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

        private static double Linearize(byte channel)
        {
            var s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }
}
