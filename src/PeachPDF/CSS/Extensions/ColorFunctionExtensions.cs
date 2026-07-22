#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Resolves the CSS color <em>function</em> forms from a tokenized value into a concrete
    /// <see cref="Color"/>: the legacy/CSS Color 4 <c>rgb()/rgba()/hsl()/hsla()/hwb()/gray()</c> and the
    /// CSS Color 4/5 <c>lab()/oklab()/lch()/oklch()/color-mix()</c>. Tokenization is the CSS-OM's job, so
    /// this consumes the already-parsed <see cref="FunctionToken"/> stream (nested functions arrive as a
    /// single token) rather than re-scanning the source text.
    /// </summary>
    internal static class ColorFunctionExtensions
    {
        /// <summary>
        /// Fully resolves a tokenized color value to a concrete <see cref="Color"/> - named colors and
        /// hex via <see cref="ValueExtensions.ToColor"/>, plus every color <em>function</em>
        /// (rgb/hsl/hwb/gray/lab/oklab/lch/oklch/color-mix). Unlike <c>ToColor</c> (which the CSS-OM
        /// cascade uses and which deliberately preserves a function's specified form for serialization),
        /// this is the render-layer resolver: it always computes the sRGB value.
        /// </summary>
        public static Color? ToResolvedColor(this IEnumerable<Token> value)
        {
            var basic = value.ToColor();
            if (basic.HasValue) return basic;

            var element = value.OnlyOrDefault();

            // A hex color nested inside a function (e.g. a color-mix() operand) tokenizes as a Hash
            // keyword token rather than a ColorToken, so ToColor misses it - resolve it here.
            if (element is { Type: TokenType.Hash } hash) return Color.FromHex(hash.Data);

            return element is FunctionToken function ? ParseColorFunction(function) : null;
        }

        public static Color? ParseColorFunction(FunctionToken function)
        {
            var name = function.Data;
            var args = function.ArgumentTokens;

            if (name.Equals(FunctionNames.Rgb, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(FunctionNames.Rgba, StringComparison.OrdinalIgnoreCase))
                return ParseRgb(args);

            if (name.Equals(FunctionNames.Hsl, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(FunctionNames.Hsla, StringComparison.OrdinalIgnoreCase))
                return ParseHsl(args);

            if (name.Equals(FunctionNames.Hwb, StringComparison.OrdinalIgnoreCase))
                return ParseHwb(args);

            if (name.Equals(FunctionNames.Gray, StringComparison.OrdinalIgnoreCase))
                return ParseGray(args);

            if (name.Equals(FunctionNames.Lab, StringComparison.OrdinalIgnoreCase))
                return ParseRect(args, Color.FromLab, abReference: 125.0, lReference: 100.0);
            if (name.Equals(FunctionNames.Oklab, StringComparison.OrdinalIgnoreCase))
                return ParseRect(args, Color.FromOklab, abReference: 0.4, lReference: 1.0);
            if (name.Equals(FunctionNames.Lch, StringComparison.OrdinalIgnoreCase))
                return ParsePolar(args, Color.FromLch, chromaReference: 150.0, lReference: 100.0);
            if (name.Equals(FunctionNames.Oklch, StringComparison.OrdinalIgnoreCase))
                return ParsePolar(args, Color.FromOklch, chromaReference: 0.4, lReference: 1.0);

            if (name.Equals(FunctionNames.ColorMix, StringComparison.OrdinalIgnoreCase))
                return ParseColorMix(args);

            return null;
        }

        // ── rgb()/rgba(): 3 components (int or % of 255) + optional alpha ─────────
        private static Color? ParseRgb(IEnumerable<Token> args)
        {
            if (!Extract(args, 3, out var comps, out var alphaTok)) return null;
            var r = AsTokens(comps[0]).ToRgbComponent();
            var g = AsTokens(comps[1]).ToRgbComponent();
            var b = AsTokens(comps[2]).ToRgbComponent();
            if (r is null || g is null || b is null) return null;
            var a = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return new Color(r.Value, g.Value, b.Value, Clamp01Byte(a));
        }

        // ── hsl()/hsla(): hue angle + saturation% + lightness% + optional alpha ──
        private static Color? ParseHsl(IEnumerable<Token> args)
        {
            if (!Extract(args, 3, out var comps, out var alphaTok)) return null;
            var hue = HueDegrees(comps[0]);
            var sat = AsTokens(comps[1]).ToPercent();
            var light = AsTokens(comps[2]).ToPercent();
            if (hue is null || sat is null || light is null) return null;
            var a = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return Color.FromHsla((float)(hue.Value / 360.0), sat.Value.NormalizedValue, light.Value.NormalizedValue, a);
        }

        // ── hwb(): hue angle + whiteness% + blackness% + optional alpha ──────────
        private static Color? ParseHwb(IEnumerable<Token> args)
        {
            if (!Extract(args, 3, out var comps, out var alphaTok)) return null;
            var hue = HueDegrees(comps[0]);
            var white = AsTokens(comps[1]).ToPercent();
            var black = AsTokens(comps[2]).ToPercent();
            if (hue is null || white is null || black is null) return null;
            var a = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return Color.FromHwba((float)(hue.Value / 360.0), white.Value.NormalizedValue, black.Value.NormalizedValue, a);
        }

        // ── gray(): a single lightness component + optional alpha ────────────────
        private static Color? ParseGray(IEnumerable<Token> args)
        {
            if (!Extract(args, 1, out var comps, out var alphaTok)) return null;
            var value = AsTokens(comps[0]).ToRgbComponent();
            if (value is null) return null;
            var a = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return Color.FromGray(value.Value, a);
        }

        // ── lab()/oklab(): L + a + b + optional alpha ────────────────────────────
        private static Color? ParseRect(IEnumerable<Token> args, Func<double, double, double, float, Color> build, double abReference, double lReference)
        {
            if (!Extract(args, 3, out var comps, out var alphaTok)) return null;
            var l = Component(comps[0], lReference);
            var a = Component(comps[1], abReference);
            var b = Component(comps[2], abReference);
            if (l is null || a is null || b is null) return null;
            var alpha = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return build(l.Value, a.Value, b.Value, alpha);
        }

        // ── lch()/oklch(): L + C + hue angle + optional alpha ────────────────────
        private static Color? ParsePolar(IEnumerable<Token> args, Func<double, double, double, float, Color> build, double chromaReference, double lReference)
        {
            if (!Extract(args, 3, out var comps, out var alphaTok)) return null;
            var l = Component(comps[0], lReference);
            var c = Component(comps[1], chromaReference);
            var h = HueDegrees(comps[2]);
            if (l is null || c is null || h is null) return null;
            var alpha = alphaTok is null ? 1f : AsTokens(alphaTok).ToAlphaValue() ?? 1f;
            return build(l.Value, c.Value, h.Value, alpha);
        }

        // ── color-mix(in <space> [<hue-method>], c1 [p1], c2 [p2]) ───────────────
        private static Color? ParseColorMix(IEnumerable<Token> args)
        {
            var segments = SplitByComma(args);
            if (segments.Count != 3) return null;

            var config = segments[0].Where(t => t.Type is not TokenType.Whitespace)
                .Select(t => t.Data.ToLowerInvariant()).ToList();
            if (config.Count < 2 || config[0] != "in") return null;
            var space = MapSpace(config[1]);
            var hue = MapHue(config, 2);

            if (!ParseMixOperand(segments[1], out var c1, out var p1)) return null;
            if (!ParseMixOperand(segments[2], out var c2, out var p2)) return null;

            // CSS Color 5 §3 normalization: a missing percentage is 100% minus the other; percentages
            // scale to sum 1, and a pre-normalization sum below 100% scales the result alpha down.
            if (p1 is null && p2 is null) { p1 = 0.5; p2 = 0.5; }
            else if (p1 is null) p1 = Math.Max(0, 1.0 - p2.Value);
            else if (p2 is null) p2 = Math.Max(0, 1.0 - p1.Value);

            var sum = p1.Value + p2.Value;
            if (sum <= 0) return null;
            var alphaMultiplier = sum < 1.0 ? sum : 1.0;
            var t = p2.Value / sum;

            return Color.Mix(c1, c2, t, space, hue, alphaMultiplier);
        }

        private static bool ParseMixOperand(List<Token> segment, out Color color, out double? percent)
        {
            color = default;
            percent = null;

            var tokens = new List<Token>();
            foreach (var token in segment)
            {
                if (token.Type != TokenType.Whitespace) tokens.Add(token);
            }
            if (tokens.Count == 0) return false;

            // The optional percentage may appear before or after the color (CSS Color 5 §3.1), e.g.
            // both `red 30%` and `30% red` are valid.
            if (tokens[^1].Type == TokenType.Percentage)
            {
                percent = AsTokens(tokens[^1]).ToPercent()?.NormalizedValue;
                tokens.RemoveAt(tokens.Count - 1);
            }
            else if (tokens[0].Type == TokenType.Percentage)
            {
                percent = AsTokens(tokens[0]).ToPercent()?.NormalizedValue;
                tokens.RemoveAt(0);
            }

            // A color-mix operand may itself be any color form (named/hex/rgb/oklch/...), so resolve it
            // through the full render-layer resolver rather than the named/hex-only ToColor.
            var resolved = tokens.ToResolvedColor();
            if (resolved is null) return false;
            color = resolved.Value;
            return true;
        }

        // ── Token helpers ────────────────────────────────────────────────────────

        // Partitions a function's argument tokens into `count` component tokens and an optional alpha
        // token. Whitespace and commas separate; a "/" delimiter (CSS Color 4) marks the alpha that
        // follows. Without a slash, a trailing extra value (legacy rgba/hsla comma alpha) is the alpha.
        private static bool Extract(IEnumerable<Token> args, int count, out List<Token> components, out Token alpha)
        {
            components = [];
            alpha = null;

            var values = new List<Token>();
            var slashIndex = -1;
            foreach (var token in args)
            {
                switch (token.Type)
                {
                    case TokenType.Whitespace:
                    case TokenType.Comma:
                        continue;
                    case TokenType.Delim when token.Data == "/":
                        slashIndex = values.Count;
                        continue;
                    default:
                        values.Add(token);
                        break;
                }
            }

            if (values.Count < count) return false;
            for (var i = 0; i < count; i++) components.Add(values[i]);

            var alphaAt = slashIndex >= 0 ? slashIndex : count;
            if (alphaAt < values.Count) alpha = values[alphaAt];
            return true;
        }

        private static List<List<Token>> SplitByComma(IEnumerable<Token> tokens)
        {
            var result = new List<List<Token>>();
            var current = new List<Token>();
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Comma)
                {
                    result.Add(current);
                    current = [];
                }
                else
                {
                    current.Add(token);
                }
            }
            result.Add(current);
            return result;
        }

        private static Token[] AsTokens(Token token) => [token];

        // A number (returned as-is) or a percentage resolved against `reference`; "none" resolves to 0.
        private static double? Component(Token token, double reference)
        {
            if (token.Type == TokenType.Ident && token.Data.Equals("none", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            var number = AsTokens(token).ToSingle();
            if (number.HasValue) return number.Value;
            var percent = AsTokens(token).ToPercent();
            return percent.HasValue ? percent.Value.NormalizedValue * reference : null;
        }

        private static double? HueDegrees(Token token)
        {
            if (token.Type == TokenType.Ident && token.Data.Equals("none", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            var angle = AsTokens(token).ToAngleNumber();
            if (!angle.HasValue) return null;
            return angle.Value.Type switch
            {
                Angle.Unit.Grad => angle.Value.Value * 0.9,
                Angle.Unit.Turn => angle.Value.Value * 360.0,
                Angle.Unit.Rad => angle.Value.Value * 180.0 / Math.PI,
                _ => angle.Value.Value
            };
        }

        private static byte Clamp01Byte(float alpha) => (byte)Math.Round(Math.Clamp(alpha, 0f, 1f) * 255);

        private static ColorSpaceMath.Space MapSpace(string name) => name switch
        {
            "srgb-linear" => ColorSpaceMath.Space.SrgbLinear,
            "display-p3" => ColorSpaceMath.Space.DisplayP3,
            "lab" => ColorSpaceMath.Space.Lab,
            "oklab" => ColorSpaceMath.Space.Oklab,
            "xyz" or "xyz-d65" => ColorSpaceMath.Space.XyzD65,
            "xyz-d50" => ColorSpaceMath.Space.XyzD50,
            "hsl" => ColorSpaceMath.Space.Hsl,
            "hwb" => ColorSpaceMath.Space.Hwb,
            "lch" => ColorSpaceMath.Space.Lch,
            "oklch" => ColorSpaceMath.Space.Oklch,
            _ => ColorSpaceMath.Space.Srgb
        };

        private static ColorSpaceMath.HueMethod MapHue(List<string> idents, int startIndex)
        {
            for (var i = startIndex; i + 1 < idents.Count; i++)
            {
                if (idents[i + 1] != "hue") continue;
                return idents[i] switch
                {
                    "longer" => ColorSpaceMath.HueMethod.Longer,
                    "increasing" => ColorSpaceMath.HueMethod.Increasing,
                    "decreasing" => ColorSpaceMath.HueMethod.Decreasing,
                    _ => ColorSpaceMath.HueMethod.Shorter
                };
            }
            return ColorSpaceMath.HueMethod.Shorter;
        }
    }
}
