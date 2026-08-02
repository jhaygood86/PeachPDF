using System.IO;
using System.Linq;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;

namespace PeachPDF.CSS
{
    internal sealed class DeclarationCondition : StylesheetNode, IConditionFunction
    {
        private readonly Property _property;
        private readonly TokenValue _tokenValue;

        public DeclarationCondition(Property property, TokenValue tokenValue)
        {
            _property = property;
            _tokenValue = tokenValue;
        }

        /// <summary>
        /// The @supports oracle: does PeachPDF's rendering pipeline actually accept this declaration -
        /// not merely "does the CSS-OM's generic grammar parse it" (the old
        /// <c>_property.TrySetValue(_tokenValue)</c> check), which is a proven mismatch in both
        /// directions (<c>animation-name</c> parses fine there but PeachPDF never renders animations;
        /// <c>fill</c>/<c>stroke</c> aren't registered there at all despite full SVG support). Delegates
        /// to the generated <see cref="CssPropertyRegistry.SupportsDeclaration(string, string)"/>/
        /// <see cref="SvgPropertyRegistry.SupportsDeclaration"/> instead - the same registries
        /// <c>CssUtils</c>/<c>SvgTreeBuilder.ApplyCommon</c> dispatch through for real. Checked with OR,
        /// not AND: a single shared stylesheet can't know at parse time whether a rule will end up
        /// matching an HTML box or an SVG element (an inline <c>&lt;svg&gt;</c> shares its host
        /// document's one <see cref="Html.Core.CssData"/>), so a declaration valid for either counts.
        /// </summary>
        public bool Check()
        {
            var name = _property.Name.ToLowerInvariant();
            var value = _tokenValue.Text;

            if (CssPropertyRegistry.SupportsDeclaration(name, value) || SvgPropertyRegistry.SupportsDeclaration(name, value))
                return true;

            if (PropertyFactory.Instance.IsShorthand(name))
            {
                // css-properties.json is longhand-only (Layer A/CSS-OM expands a shorthand into its
                // longhands before Layer B - CssUtils/CssPropertyRegistry - ever sees a name), so a
                // shorthand condition (e.g. `@supports (background: red)`) never matches the direct
                // check above. A CSS-wide keyword (CSS Cascade & Inheritance 4 §7.3) is always valid
                // for a recognized property regardless of its grammar; otherwise it's "supported"
                // here iff every longhand the shorthand expands to is one Layer B actually
                // dispatches - deliberately coarser than validating `value` against the shorthand's
                // own grammar (Layer A already proved the condition parsed at all; re-deriving
                // shorthand-value splitting would duplicate Layer A's grammar, which this repo's
                // "one parser" convention rules out - see CLAUDE.md).
                if (CssGlobalKeywords.TryParse(value, out _))
                    return true;

                return PropertyFactory.Instance.GetLonghands(name)
                    .All(longhand => CssPropertyRegistry.SupportsDeclaration(longhand, CssPropertyRegistry.GetInitialValue(longhand) ?? string.Empty));
            }

            // Same CSS-wide-keyword rule as above, for a longhand: gated on the property actually
            // being one Layer B recognizes by name, since @supports on a genuinely unknown property
            // must fail regardless of value (an unrecognized name has no "own grammar" to accept the
            // keyword into).
            return CssGlobalKeywords.TryParse(value, out _)
                   && (CssPropertyRegistry.IsKnownProperty(name) || SvgPropertyRegistry.IsKnownProperty(name));
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var delcaration = formatter.Declaration(_property.Name, _tokenValue.Text, _property.IsImportant);
            writer.Write($"({delcaration})");
        }
    }
}