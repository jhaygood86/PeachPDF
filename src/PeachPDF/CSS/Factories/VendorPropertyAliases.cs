#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The legacy vendor-prefixed (<c>-webkit-</c>, <c>-moz-</c>) and pre-standard spellings of properties PeachPDF
    /// supports under their standard name. An alias is resolved to its canonical name <em>before</em> a property is
    /// created, so the property object (and everything downstream of it: the cascade, <c>var()</c> resolution,
    /// <c>inherit</c>/<c>initial</c>/<c>revert</c>, <c>@supports</c>, CSSOM reads) only ever sees the standard name.
    /// </summary>
    internal static class VendorPropertyAliases
    {
        // Only properties whose prefixed form took the standard grammar. Anything with a genuinely different legacy
        // grammar (the -webkit-box-* flexbox, -webkit-column-break-*, -webkit-gradient()) is deliberately absent.
        private static readonly FrozenDictionary<string, string> Table = Build();

        /// <summary>Resolves <paramref name="name"/> to its standard name, or returns it unchanged if it is not an alias.</summary>
        internal static string Canonicalize(string name) =>
            Table.TryGetValue(name, out var canonical) ? canonical : name;

        private static FrozenDictionary<string, string> Build()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PropertyNames.WordWrap] = PropertyNames.OverflowWrap,
                [PropertyNames.WebkitBackdropFilter] = PropertyNames.BackdropFilter,
            };

            // Properties that were shipped behind both -webkit- and -moz- (or just one) before unprefixing.
            var prefixed = new[]
            {
                PropertyNames.Transform, PropertyNames.TransformOrigin, PropertyNames.TransformStyle,
                PropertyNames.Perspective, PropertyNames.PerspectiveOrigin, PropertyNames.BackfaceVisibility,
                PropertyNames.BoxShadow, PropertyNames.BoxSizing,
                PropertyNames.BorderRadius, PropertyNames.BorderTopLeftRadius, PropertyNames.BorderTopRightRadius,
                PropertyNames.BorderBottomLeftRadius, PropertyNames.BorderBottomRightRadius,
                PropertyNames.BorderImage,
                PropertyNames.BackgroundClip, PropertyNames.BackgroundOrigin, PropertyNames.BackgroundSize,
                PropertyNames.ClipPath, PropertyNames.Filter, PropertyNames.Opacity,
                PropertyNames.Columns, PropertyNames.ColumnCount, PropertyNames.ColumnWidth, PropertyNames.ColumnGap,
                PropertyNames.ColumnFill, PropertyNames.ColumnSpan, PropertyNames.ColumnRule,
                PropertyNames.ColumnRuleWidth, PropertyNames.ColumnRuleStyle, PropertyNames.ColumnRuleColor,
                PropertyNames.Hyphens, PropertyNames.TabSize,
                PropertyNames.Flex, PropertyNames.FlexFlow, PropertyNames.FlexDirection, PropertyNames.FlexWrap,
                PropertyNames.FlexGrow, PropertyNames.FlexShrink, PropertyNames.FlexBasis, PropertyNames.Order,
                PropertyNames.AlignItems, PropertyNames.AlignSelf, PropertyNames.AlignContent, PropertyNames.JustifyContent,
                PropertyNames.TextDecoration, PropertyNames.TextDecorationLine, PropertyNames.TextDecorationStyle,
                PropertyNames.TextDecorationColor, PropertyNames.TextDecorationSkipInk,
                PropertyNames.FontFeatureSettings, PropertyNames.FontKerning,
                PropertyNames.WritingMode, PropertyNames.TextOrientation,
            };

            foreach (var name in prefixed)
            {
                map["-webkit-" + name] = name;
                map["-moz-" + name] = name;
            }

            // The item side of the 2009 flexbox model. box-flex's <number> and box-ordinal-group's <integer> mean the
            // same thing as flex-grow and order; the container side needs value translation (LegacyBox).
            foreach (var prefix in new[] { "-webkit-", "-moz-" })
            {
                map[prefix + "box-flex"] = PropertyNames.FlexGrow;
                map[prefix + "box-ordinal-group"] = PropertyNames.Order;
            }

            map["-webkit-line-clamp"] = PropertyNames.LineClamp;

            // WebKit's logical box properties predate margin-inline-*/padding-inline-*.
            foreach (var (legacy, standard) in new[]
            {
                ("margin-start", PropertyNames.MarginInlineStart), ("margin-end", PropertyNames.MarginInlineEnd),
                ("padding-start", PropertyNames.PaddingInlineStart), ("padding-end", PropertyNames.PaddingInlineEnd),
            })
            {
                map["-webkit-" + legacy] = standard;
                map["-moz-" + legacy] = standard;
            }

            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }
}
