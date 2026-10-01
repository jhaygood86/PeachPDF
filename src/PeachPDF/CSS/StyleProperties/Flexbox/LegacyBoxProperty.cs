#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The container-side properties of the pre-standard <c>display: -webkit-box</c> ("flexbox 2009") model:
    /// <c>box-orient</c>, <c>box-direction</c>, <c>box-pack</c> and <c>box-align</c>, under both the <c>-webkit-</c>
    /// and <c>-moz-</c> prefixes. They are parsed under their own names and then translated onto the standard flex
    /// properties by <see cref="Html.Core.Parse.LegacyBox"/> - they have no computed-value storage of their own. (The item-side
    /// <c>box-flex</c> and <c>box-ordinal-group</c> are plain aliases of <c>flex-grow</c> and <c>order</c>, see
    /// <see cref="VendorPropertyAliases"/>.)
    /// </summary>
    internal sealed class LegacyBoxProperty : Property
    {
        internal static readonly string[] Prefixes = ["-webkit-", "-moz-"];

        internal const string Orient = "box-orient";
        internal const string Direction = "box-direction";
        internal const string Pack = "box-pack";
        internal const string Align = "box-align";

        private static IValueConverter Keywords(params string[] keywords)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in keywords) map[k] = k;
            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).ToConverter().OrGlobalValue();
        }

        private static readonly FrozenDictionary<string, IValueConverter> Converters = new Dictionary<string, IValueConverter>
        {
            [Orient] = Keywords("horizontal", "vertical", "inline-axis", "block-axis"),
            [Direction] = Keywords("normal", "reverse"),
            [Pack] = Keywords("start", "end", "center", "justify"),
            [Align] = Keywords("start", "end", "center", "baseline", "stretch"),
        }.ToFrozenDictionary();

        private readonly IValueConverter _converter;

        internal LegacyBoxProperty(string prefix, string baseName)
            : base(prefix + baseName)
        {
            _converter = Converters[baseName];
        }

        internal override IValueConverter Converter => _converter;

        /// <summary>The base name (<c>box-orient</c>…) of <paramref name="propertyName"/> if it is a legacy box property, else <see langword="null"/>.</summary>
        internal static string? BaseNameOf(string propertyName)
        {
            foreach (var prefix in Prefixes)
            {
                if (!propertyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                var rest = propertyName.Substring(prefix.Length);
                foreach (var baseName in Converters.Keys)
                {
                    if (rest.Equals(baseName, StringComparison.OrdinalIgnoreCase)) return baseName;
                }
            }

            return null;
        }
    }
}
