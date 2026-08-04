using System.Linq;
using Microsoft.CodeAnalysis;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>Snapshot-style assertions on the generator's actual emitted C#, for the shapes CLAUDE.md's generator section documents.</summary>
    public class GeneratorGoldenFileTests
    {
        [Fact]
        public void Emits_CssPropertyRegistry_For_A_Plain_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains(
                "private static bool Validate_Transform(CssValueParser parser, string value) => " +
                "global::PeachPDF.CSS.PropertyFactory.Instance.Create(\"transform\") is not { } knownProperty || " +
                "(global::PeachPDF.CSS.StylesheetParser.Default.ParseValue(value) is { } tokenValue && knownProperty.TrySetValue(tokenValue));",
                generated);
            Assert.Contains("private static bool Supports_Transform(CssValueParser parser, string value) => Validate_Transform(parser, value);", generated);
            Assert.Contains("box.Transform = value;", generated);
            Assert.Contains("[\"transform\"] = Set_Transform,", generated);
            Assert.Contains("[\"transform\"] = Supports_Transform,", generated);
            Assert.Contains("[\"transform\"] = \"none\",", generated);
        }

        [Fact]
        public void Emits_A_Nominal_Context_SupportsDeclaration_Overload_For_Callers_With_No_Live_Parser()
        {
            var json = """
                {
                  "properties": [
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("internal static bool SupportsDeclaration(string name, string value) =>", generated);
            Assert.Contains("SupportsDeclaration(new CssValueParser(new global::PeachPDF.Adapters.PdfSharpAdapter()), name, value);", generated);
        }

        [Fact]
        public void Emits_A_Distinct_Supports_Method_When_SupportsDataType_Overrides_The_Base_Grammar()
        {
            var json = """
                {
                  "properties": [
                    { "name": "break-before", "inherited": false, "initialValue": "auto", "cssDataType": "keyword",
                      "supportedValues": ["auto", "region"],
                      "supportsDataType": "keyword", "supportsSupportedValues": ["auto"],
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("private static bool Validate_BreakBefore(CssValueParser parser, string value) => value is \"auto\" or \"region\";", generated);
            Assert.Contains("private static bool Supports_BreakBefore(CssValueParser parser, string value) => value is \"auto\";", generated);
            Assert.Contains("[\"break-before\"] = Set_BreakBefore,", generated);
            Assert.Contains("[\"break-before\"] = Supports_BreakBefore,", generated);
        }

        [Fact]
        public void Emits_The_Real_Transform_Function_Grammar_For_The_Transform_DataType()
        {
            var json = """
                {
                  "properties": [
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "transform",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains(
                "private static bool Validate_Transform(CssValueParser parser, string value) => " +
                "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidTransformValue(value);",
                generated);
        }

        [Fact]
        public void Emits_Min_And_Max_Bounds_For_A_Standalone_Integer_DataType()
        {
            var json = """
                {
                  "properties": [
                    { "name": "order", "inherited": false, "initialValue": "0",
                      "cssDataType": { "type": "integer", "min": -1000, "max": 1000 },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("int.TryParse(value, out var parsedInt) && parsedInt >= -1000 && parsedInt <= 1000", generated);
        }

        [Fact]
        public void Emits_Keyword_Validation_With_The_Declared_Comparison()
        {
            var json = """
                {
                  "properties": [
                    { "name": "box-sizing", "inherited": false, "initialValue": "content-box", "cssDataType": "keyword",
                      "supportedValues": ["border-box", "content-box"], "keywordComparison": "ordinal",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("value is \"border-box\" or \"content-box\"", generated);
            // A case-sensitive (ordinal) keyword property must keep the plain assignment shape — no
            // canonicalization chain — since Validate_* already guarantees an exact-case match.
            Assert.Contains("box.Transform = value;", generated);
        }

        [Fact]
        public void Emits_Keyword_Storage_Canonicalized_To_Declared_Casing_For_OrdinalIgnoreCase()
        {
            var json = """
                {
                  "properties": [
                    { "name": "box-sizing", "inherited": false, "initialValue": "content-box", "cssDataType": "keyword",
                      "supportedValues": ["border-box", "content-box"], "keywordComparison": "ordinal-ignore-case",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            // Issue #598: a case-insensitively-matched keyword must be stored in its canonical (as-authored)
            // casing, not the raw input — every downstream layout/paint comparison is an ordinal match
            // against a lowercase CssConstants.* literal.
            Assert.Contains(
                "box.Transform = value.Equals(\"border-box\", global::System.StringComparison.OrdinalIgnoreCase) ? \"border-box\" : " +
                "value.Equals(\"content-box\", global::System.StringComparison.OrdinalIgnoreCase) ? \"content-box\" : value;",
                generated);
        }

        [Fact]
        public void Emits_Keyword_Storage_Canonicalized_To_Declared_Casing_For_InvariantIgnoreCase()
        {
            var json = """
                {
                  "properties": [
                    { "name": "box-sizing", "inherited": false, "initialValue": "content-box", "cssDataType": "keyword",
                      "supportedValues": ["border-box", "content-box"], "keywordComparison": "invariant-ignore-case",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains(
                "box.Transform = value.Equals(\"border-box\", global::System.StringComparison.InvariantCultureIgnoreCase) ? \"border-box\" : " +
                "value.Equals(\"content-box\", global::System.StringComparison.InvariantCultureIgnoreCase) ? \"content-box\" : value;",
                generated);
        }

        [Fact]
        public void Emits_Keyword_Storage_Unchanged_For_A_Union_Length_Or_Keyword_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "column-width", "inherited": false, "initialValue": "auto", "cssDataType": ["length", "keyword"],
                      "supportedValues": ["auto"], "keywordComparison": "ordinal-ignore-case",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            // A value that matches neither keyword (e.g. an actual length like "12px") falls through the
            // canonicalization chain unchanged rather than being coerced to a keyword.
            Assert.Contains(
                "box.Transform = value.Equals(\"auto\", global::System.StringComparison.OrdinalIgnoreCase) ? \"auto\" : value;",
                generated);
        }

        [Fact]
        public void Emits_KeywordOrValue_Validation_And_Storage_For_An_Integer_Or_Auto_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "z-index", "inherited": false, "initialValue": "auto",
                      "cssDataType": { "type": "keyword-or-value", "enumType": "AutoKeyword", "keywordMap": "Map.AutoKeywords",
                        "fallback": "AutoKeyword.Auto", "valueType": "integer" },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains(
                "private static bool Validate_ZIndex(CssValueParser parser, string value) => " +
                "Map.AutoKeywords.ContainsKey(value) || int.TryParse(value, out _);",
                generated);
            Assert.Contains(
                "box.Transform = global::PeachPDF.CSS.CssKeywordOrValueParser.FromCssText<AutoKeyword, int>(value, Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);",
                generated);
        }

        [Fact]
        public void Emits_KeywordOrValue_Validation_And_Storage_For_A_Length_Or_Auto_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "column-width", "inherited": false, "initialValue": "auto",
                      "cssDataType": { "type": "keyword-or-value", "enumType": "AutoKeyword", "keywordMap": "Map.AutoKeywords",
                        "fallback": "AutoKeyword.Auto", "valueType": "length" },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains(
                "private static bool Validate_ColumnWidth(CssValueParser parser, string value) => " +
                "Map.AutoKeywords.ContainsKey(value) || global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLength(value);",
                generated);
            Assert.Contains(
                "box.Transform = global::PeachPDF.CSS.CssKeywordOrValueParser.FromCssText<AutoKeyword, global::PeachPDF.CSS.Length>(value, Map.AutoKeywords, global::PeachPDF.CSS.Length.TryParse, AutoKeyword.Auto);",
                generated);
        }

        [Fact]
        public void Emits_False_For_An_Unsupported_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "overflow-wrap", "inherited": false, "initialValue": null, "cssDataType": "unsupported",
                      "html": { "propertyPath": null, "csharpDataType": null, "hasGetter": false, "snapshot": false } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("private static bool Set_OverflowWrap(CssValueParser parser, CssBox box, string value) => false;", generated);
            Assert.DoesNotContain("Get_OverflowWrap", generated);
        }

        [Fact]
        public void Emits_SvgPropertyRegistry_For_A_Paint_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "fill", "inherited": true, "initialValue": "black", "cssDataType": "svg-paint",
                      "svg": { "propertyPath": "Fill", "csharpDataType": "string", "invalidBehavior": "inherit" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "SvgPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("SvgValueParsers.TryParsePaint(value, ctx.Adapter, ctx.ContextColor, out var parsed)", generated);
            Assert.Contains("element.Fill = ctx.ResolveUrlPaintKind(parsed);", generated);
            Assert.Contains("[\"fill\"] = \"black\",", generated);
        }

        [Fact]
        public void Does_Not_Emit_Anything_For_An_Svg_Binding_With_ApplyIn_Manual()
        {
            var json = """
                {
                  "properties": [
                    { "name": "direction", "inherited": true, "initialValue": "ltr", "cssDataType": "cssom",
                      "svg": { "propertyPath": null, "inheritedFrom": "Direction", "invalidBehavior": "inherit", "applyIn": "manual" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "SvgPropertyRegistry.g.cs").SourceText.ToString();

            Assert.DoesNotContain("\"direction\"", generated);
        }

        [Fact]
        public void Emits_SvgPropertyRegistry_For_A_Length_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "stroke-width", "inherited": true, "initialValue": "1", "cssDataType": "svg-length",
                      "svg": { "propertyPath": "StrokeWidth", "csharpDataType": "double", "inheritedFrom": "StrokeWidth", "invalidBehavior": "inherit" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "SvgPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("SvgValueParsers.ParseLength(value, ctx.ViewportDiagonal) is not null", generated);
            Assert.Contains("var parsed = global::PeachPDF.Svg.SvgValueParsers.ParseLength(value, ctx.ViewportDiagonal);", generated);
            Assert.Contains("element.StrokeWidth = parsed.Value;", generated);
        }

        [Fact]
        public void Emits_SvgPropertyRegistry_For_A_LengthList_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "stroke-dasharray", "inherited": true, "initialValue": "none", "cssDataType": "svg-length-list",
                      "svg": { "propertyPath": "StrokeDashArray", "csharpDataType": "double[]", "inheritedFrom": "StrokeDashArray", "invalidBehavior": "inherit" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "SvgPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("SvgValueParsers.ParseDashArray(value, ctx.ViewportDiagonal) is not null", generated);
            Assert.Contains("var parsed = global::PeachPDF.Svg.SvgValueParsers.ParseDashArray(value, ctx.ViewportDiagonal);", generated);
            Assert.Contains("element.StrokeDashArray = parsed;", generated);
        }

        [Fact]
        public void Emits_ComputedStyleAreas_Field_For_A_Plain_String_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "text-align", "inherited": true, "initialValue": "start", "cssDataType": "cssom",
                      "html": { "propertyPath": "TextAlign", "csharpDataType": "string", "area": "TextArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "ComputedStyleAreas.g.cs").SourceText.ToString();

            Assert.Contains("internal sealed partial record TextArea", generated);
            Assert.Contains(
                "public string TextAlign { get; init; } = CssDefaults.GetInitialValue(\"text-align\")!;",
                generated);
        }

        [Fact]
        public void Emits_ComputedStyleAreas_Field_For_An_EnumKeyword_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "box-sizing", "inherited": false, "initialValue": "content-box",
                      "cssDataType": { "type": "enum-keyword", "enumType": "BoxSizingMode", "keywordMap": "Map.BoxSizingModes",
                        "fallback": "BoxSizingMode.ContentBox" },
                      "html": { "propertyPath": "BoxSizing", "csharpDataType": "CssProperty<BoxSizingMode>", "area": "BoxModelArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "ComputedStyleAreas.g.cs").SourceText.ToString();

            Assert.Contains(
                "public CssProperty<BoxSizingMode> BoxSizing { get; init; } = " +
                "CssProperty<global::PeachPDF.CSS.BoxSizingMode>.FromCssText(CssDefaults.GetInitialValue(\"box-sizing\")!, " +
                "Map.BoxSizingModes, global::PeachPDF.CSS.BoxSizingMode.ContentBox);",
                generated);
        }

        [Fact]
        public void Emits_ComputedStyleAreas_FromValue_Default_For_A_CssOm_Delegated_CssProperty()
        {
            // grid-template-columns/-rows' shape: csharpDataType is CssProperty<T> but cssDataType is
            // "cssom" (grammar validation and real parsing are delegated to the CSS-OM/customSetter, not
            // a generator-known FromCssText path) - the initial keyword has no corresponding parsed T, so
            // only the raw text is set and the parsed half stays a literal null.
            var json = """
                {
                  "properties": [
                    { "name": "grid-template-columns", "inherited": false, "initialValue": "none", "cssDataType": "cssom",
                      "html": { "propertyPath": "GridTemplateColumns", "csharpDataType": "CssProperty<GridTemplate>", "area": "GridArea",
                        "customSetter": "{box}.GridTemplateColumns = GridTemplateValueConverter.FromCssText({value});" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "ComputedStyleAreas.g.cs").SourceText.ToString();

            Assert.Contains(
                "public CssProperty<GridTemplate> GridTemplateColumns { get; init; } = " +
                "CssProperty<GridTemplate>.FromValue(CssDefaults.GetInitialValue(\"grid-template-columns\")!, null);",
                generated);
        }

        [Fact]
        public void Emits_ComputedStyleAreas_No_Default_For_A_NonString_NonCssProperty_Type()
        {
            // FontFamily/BackgroundImages/ListStyleImage's shape: csharpDataType is neither "string" nor
            // a CssProperty<T> wrapper (a plain nullable reference type populated only via a
            // customSetter), so there is no CssDefaults-derived default the generator can construct -
            // the field is left with its own implicit default (null).
            var json = """
                {
                  "properties": [
                    { "name": "list-style-image", "inherited": true, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "ListStyleImage", "csharpDataType": "CssImage?", "area": "ListArea",
                        "customSetter": "{box}.ListStyleImage = CssImageParser.FromCssText({value});" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "ComputedStyleAreas.g.cs").SourceText.ToString();

            Assert.Contains("public CssImage? ListStyleImage { get; init; }", generated);
            Assert.DoesNotContain("ListStyleImage { get; init; } =", generated);
        }

        [Fact]
        public void Emits_Exactly_One_Field_And_Property_For_A_Legacy_Alias_Pair_Sharing_A_PropertyPath()
        {
            // page-break-after/break-after both declare propertyPath "BreakAfter"/area "BreakArea" - they
            // must collapse to exactly one ComputedStyleAreas field and one CssBox property, not two.
            var json = """
                {
                  "properties": [
                    { "name": "break-after", "inherited": false, "initialValue": "auto", "cssDataType": "cssom",
                      "html": { "propertyPath": "BreakAfter", "csharpDataType": "string", "area": "BreakArea" } },
                    { "name": "page-break-after", "inherited": false, "initialValue": "auto", "aliasOf": "break-after", "cssDataType": "cssom",
                      "html": { "propertyPath": "BreakAfter", "csharpDataType": "string", "area": "BreakArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var areas = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "ComputedStyleAreas.g.cs").SourceText.ToString();
            var styleProperties = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Equal(1, CountOccurrences(areas, "public string BreakAfter { get; init; }"));
            Assert.Equal(1, CountOccurrences(styleProperties, "public string BreakAfter"));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        [Fact]
        public void Emits_CssBox_StyleProperties_Get_Set_Skeleton_For_A_Plain_Property()
        {
            var json = """
                {
                  "properties": [
                    { "name": "text-align", "inherited": true, "initialValue": "start", "cssDataType": "cssom",
                      "html": { "propertyPath": "TextAlign", "csharpDataType": "string", "area": "TextArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains("partial class CssBox", generated);
            Assert.Contains("get => _computedStyle.Text.TextAlign;", generated);
            Assert.Contains("var area = _computedStyle.Text;", generated);
            Assert.Contains(
                "var newArea = area.SetPropertyValue(area.TextAlign, value, static (a, v) => a with { TextAlign = v });",
                generated);
            Assert.Contains(
                "_computedStyle = _computedStyle.AdoptArea(area, newArea, static (s, a) => s with { Text = a });",
                generated);
        }

        [Fact]
        public void Emits_CssBox_StyleProperties_Invalidation_Call_When_Html_Invalidates_Is_Declared()
        {
            var json = """
                {
                  "properties": [
                    { "name": "border-top-width", "inherited": false, "initialValue": "medium",
                      "cssDataType": ["length", "keyword"], "supportedValues": ["auto"],
                      "html": { "propertyPath": "BorderTopWidth", "csharpDataType": "string", "area": "BorderArea",
                        "invalidates": "InvalidateBorderTopWidth" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains("var previousStyle = _computedStyle;", generated);
            Assert.Contains(
                "if (!ReferenceEquals(_computedStyle, previousStyle)) DerivedStyle.InvalidateBorderTopWidth();",
                generated);
        }

        [Fact]
        public void Emits_CssBox_StyleProperties_All_Invalidation_Calls_When_Html_Invalidates_Is_An_Array()
        {
            var json = """
                {
                  "properties": [
                    { "name": "border-top-width", "inherited": false, "initialValue": "medium",
                      "cssDataType": ["length", "keyword"], "supportedValues": ["auto"],
                      "html": { "propertyPath": "BorderTopWidth", "csharpDataType": "string", "area": "BorderArea",
                        "invalidates": ["InvalidateBorderTopWidth", "InvalidateBorderRadii"] } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains(
                "if (!ReferenceEquals(_computedStyle, previousStyle)) DerivedStyle.InvalidateBorderTopWidth();",
                generated);
            Assert.Contains(
                "if (!ReferenceEquals(_computedStyle, previousStyle)) DerivedStyle.InvalidateBorderRadii();",
                generated);
        }

        [Fact]
        public void Emits_FontSize_ValueComputation_For_Html_ValueComputation_FontSize()
        {
            var json = """
                {
                  "properties": [
                    { "name": "font-size", "inherited": true, "initialValue": "medium", "cssDataType": "cssom",
                      "html": { "propertyPath": "FontSize", "csharpDataType": "string", "area": "FontArea",
                        "valueComputation": "font-size" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains("var points = FontSizeResolver.Resolve(trimmed, parentSizePt, parentSizePt);", generated);
            Assert.Contains(
                "var newArea = area.SetPropertyValue(area.FontSize, resolved, static (a, v) => a with { FontSize = v });",
                generated);
        }

        [Fact]
        public void Emits_NoEms_ValueComputation_For_Html_ValueComputation_NoEms()
        {
            var json = """
                {
                  "properties": [
                    { "name": "word-spacing", "inherited": true, "initialValue": "normal",
                      "cssDataType": ["length", "keyword"], "supportedValues": ["normal"],
                      "html": { "propertyPath": "WordSpacing", "csharpDataType": "string", "area": "TextArea",
                        "valueComputation": "no-ems" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains("var resolved = NoEms(value);", generated);
            Assert.Contains(
                "var newArea = area.SetPropertyValue(area.WordSpacing, resolved, static (a, v) => a with { WordSpacing = v });",
                generated);
        }

        [Fact]
        public void Emits_TextIndent_ValueComputation_For_Html_ValueComputation_TextIndent()
        {
            var json = """
                {
                  "properties": [
                    { "name": "text-indent", "inherited": true, "initialValue": "0", "cssDataType": "cssom",
                      "html": { "propertyPath": "TextIndent", "csharpDataType": "string", "area": "TextArea",
                        "valueComputation": "text-indent" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssBox.StyleProperties.g.cs").SourceText.ToString();

            Assert.Contains("var resolved = NoEmsTextIndent(value);", generated);
        }

        [Fact]
        public void Emits_Nothing_When_There_Is_No_CssPropertiesJson_AdditionalFile()
        {
            var compilation = GeneratorTestHost.BuildCompilation(StubSources.MinimalCssBoxAndSvgElement);
            var generator = new CssPropertyGenerator();

            Microsoft.CodeAnalysis.GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
            driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
            var result = driver.GetRunResult();

            Assert.Empty(result.Results.Single().GeneratedSources);
            Assert.Empty(result.Diagnostics);
        }
    }
}
