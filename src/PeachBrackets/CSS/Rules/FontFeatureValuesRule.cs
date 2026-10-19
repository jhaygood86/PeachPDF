#nullable disable

using System.IO;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A <c>@font-feature-values</c> at-rule (CSS Fonts Module Level 4): associates named OpenType
    /// feature-value aliases (declared via nested <c>@styleset</c>/<c>@character-variant</c>/
    /// <c>@swash</c>/<c>@ornaments</c>/<c>@annotation</c>/<c>@stylistic</c> blocks, each a
    /// <see cref="FontFeatureValueSetRule"/>) with one or more font families, so
    /// <c>font-variant-alternates</c> functions (<c>styleset()</c>, etc.) can reference a name instead
    /// of a raw OpenType feature index. The prelude is a <c>&lt;family-name&gt;#</c> list (unlike
    /// <see cref="FontPaletteValuesRule"/>'s single dashed-ident name), captured as raw text and split/
    /// normalized at Layer B (<see cref="PeachPDF.Html.Core.RegisteredFontFeatureValues"/>).
    /// </summary>
    internal sealed class FontFeatureValuesRule : Rule
    {
        internal FontFeatureValuesRule(StylesheetParser parser)
            : base(RuleType.FontFeatureValues, parser)
        {
            Rules = new RuleList(this);
        }

        /// <summary>The raw, unsplit <c>&lt;family-name&gt;#</c> prelude text.</summary>
        public string FamilyList { get; set; }

        /// <summary>The nested <c>@styleset</c>/<c>@character-variant</c>/<c>@swash</c>/
        /// <c>@ornaments</c>/<c>@annotation</c>/<c>@stylistic</c> blocks.</summary>
        public RuleList Rules { get; }

        protected override void ReplaceWith(IRule rule)
        {
            if (rule is FontFeatureValuesRule other) FamilyList = other.FamilyList;
            base.ReplaceWith(rule);
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            writer.Write(formatter.Rule("@font-feature-values", FamilyList, rules));
        }
    }
}
