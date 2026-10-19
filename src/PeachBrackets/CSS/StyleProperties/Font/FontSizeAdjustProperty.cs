#nullable disable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// CSS Fonts 5 §3.2: <c>none | [ ex-height | cap-height | ch-width | ic-width | ic-height ]? [ from-font | &lt;number [0,∞]&gt; ]</c>.
    /// The authored text is kept; <see cref="FontSizeAdjustGrammar.TryParse"/> turns it into the metric and factor where the
    /// font is created (HTML <c>DerivedStyle.ActualFont</c>, SVG <c>SvgTreeBuilder.ComputeFontContext</c>).
    /// </summary>
    internal sealed class FontSizeAdjustProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator.
        internal static readonly IValueConverter ValueGrammar = new FontSizeAdjustValueConverter();

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal FontSizeAdjustProperty()
            : base(PropertyNames.FontSizeAdjust, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private sealed class FontSizeAdjustValueConverter : IValueConverter
        {
            public IPropertyValue Convert(IReadOnlyList<Token> value)
            {
                var tokens = value.ToArray();
                return FontSizeAdjustGrammar.TryParse(tokens, out _) ? new AdjustValue(tokens) : null;
            }

            public IPropertyValue Construct(Property[] properties) => properties.Guard<AdjustValue>();

            private sealed class AdjustValue : IPropertyValue
            {
                public AdjustValue(IEnumerable<Token> tokens) => Original = new TokenValue(tokens);

                public string CssText => Original.Text.ToLowerInvariant();

                public TokenValue Original { get; }

                public TokenValue ExtractFor(string name) => Original;
            }
        }
    }

    /// <summary>The metrics <c>font-size-adjust</c> can match (CSS Fonts 5 §3.2).</summary>
    internal enum FontSizeAdjustMetric : byte
    {
        ExHeight,
        CapHeight,
        ChWidth,
        IcWidth,
        IcHeight
    }

    /// <summary>
    /// The shared grammar for <c>font-size-adjust</c>: validates at parse time (CSS-OM) and resolves the authored text
    /// to a metric and a factor at font-creation time, so the two layers cannot disagree.
    /// </summary>
    internal static class FontSizeAdjustGrammar
    {
        /// <summary>
        /// Parses a value. Returns false when invalid. On success <paramref name="adjust"/> is null for <c>none</c>;
        /// otherwise the metric and the factor (<see cref="double.NaN"/> for <c>from-font</c>).
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out (FontSizeAdjustMetric Metric, double Value)? adjust)
        {
            adjust = null;
            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length == 0 || toks.Length > 2) return false;

            if (toks.Length == 1 && toks[0] is { Type: TokenType.Ident } only && only.Data.Isi(Keywords.None)) return true;

            var metric = FontSizeAdjustMetric.ExHeight;
            var i = 0;

            if (toks[0].Type == TokenType.Ident && TryMetric(toks[0].Data.ToString(), out var named))
            {
                metric = named;
                i = 1;
            }

            if (i != toks.Length - 1) return false;

            var factor = toks[i];
            if (factor is { Type: TokenType.Ident } fromFont && fromFont.Data.Isi(Keywords.FromFont))
            {
                adjust = (metric, double.NaN);
                return true;
            }

            if (factor is { Type: TokenType.Number } number && number.Value >= 0f)
            {
                adjust = (metric, number.Value);
                return true;
            }

            return false;
        }

        /// <summary>Parses authored text (already validated by the CSS-OM); null for <c>none</c>, empty or anything unparsable.</summary>
        internal static (FontSizeAdjustMetric Metric, double Value)? Resolve(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 2) return null;

            var metric = FontSizeAdjustMetric.ExHeight;
            var i = 0;

            if (TryMetric(parts[0], out var named))
            {
                metric = named;
                i = 1;
            }

            if (i != parts.Length - 1) return null;

            if (parts[i].Equals(Keywords.FromFont, System.StringComparison.OrdinalIgnoreCase)) return (metric, double.NaN);

            return double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0
                ? (metric, number)
                : null;
        }

        /// <summary>The measurement a <c>font-size-adjust</c> metric keyword matches. <c>ic-height</c> is measured as <c>ic-width</c>: the font layer exposes no vertical advance for the ideograph.</summary>
        internal static FontMetric ToFontMetric(FontSizeAdjustMetric metric) => metric switch
        {
            FontSizeAdjustMetric.CapHeight => FontMetric.Cap,
            FontSizeAdjustMetric.ChWidth => FontMetric.Ch,
            FontSizeAdjustMetric.IcWidth or FontSizeAdjustMetric.IcHeight => FontMetric.Ic,
            _ => FontMetric.Ex,
        };

        private static bool TryMetric(string name, out FontSizeAdjustMetric metric)
        {
            switch (name.ToLowerInvariant())
            {
                case "ex-height": metric = FontSizeAdjustMetric.ExHeight; return true;
                case "cap-height": metric = FontSizeAdjustMetric.CapHeight; return true;
                case "ch-width": metric = FontSizeAdjustMetric.ChWidth; return true;
                case "ic-width": metric = FontSizeAdjustMetric.IcWidth; return true;
                case "ic-height": metric = FontSizeAdjustMetric.IcHeight; return true;
                default: metric = default; return false;
            }
        }
    }
}
