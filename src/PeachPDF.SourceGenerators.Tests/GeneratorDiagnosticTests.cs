using System.Linq;
using Microsoft.CodeAnalysis;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>One test per PPGxxx diagnostic — see CLAUDE.md's generator section for what each catches.</summary>
    public class GeneratorDiagnosticTests
    {
        private static string[] IdsOf(GeneratorDriverRunResult result) =>
            result.Diagnostics.Select(d => d.Id).ToArray();

        [Fact]
        public void PPG001_Fires_For_Malformed_Json_Syntax()
        {
            var result = GeneratorTestHost.Run("{ this is not valid json");

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Root_Has_No_Properties_Array()
        {
            var result = GeneratorTestHost.Run("""{"notProperties": []}""");

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG002_Fires_For_Unknown_DataType()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "bogus-type" }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json);

            Assert.Contains("PPG002", IdsOf(result));
        }

        [Fact]
        public void PPG003_Fires_For_Duplicate_Name()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } },
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG003", IdsOf(result));
        }

        [Fact]
        public void PPG004_Fires_When_Keyword_Type_Has_No_SupportedValues()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": "a", "cssDataType": "keyword",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG004", IdsOf(result));
        }

        [Fact]
        public void PPG005_Fires_When_Binding_Has_No_PropertyPath_And_No_CustomSetter()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": null, "csharpDataType": null, "area": null } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG005", IdsOf(result));
        }

        [Fact]
        public void PPG006_Fires_When_InitialValue_Key_Is_Entirely_Absent()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG006", IdsOf(result));
        }

        [Fact]
        public void PPG007_Fires_When_AliasOf_Target_Is_Missing()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": "auto", "aliasOf": "does-not-exist", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG007", IdsOf(result));
        }

        [Fact]
        public void PPG007_Fires_When_AliasOf_Target_Has_A_Different_InitialValue()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": "auto", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } },
                    { "name": "y", "inherited": false, "initialValue": "different", "aliasOf": "x", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG007", IdsOf(result));
        }

        [Fact]
        public void PPG008_Fires_When_CustomSetter_Contains_A_Return_Statement()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea",
                                "customSetter": "if (true) return false; {box}.Transform = {value};" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG008", IdsOf(result));
        }

        [Fact]
        public void PPG008_Does_Not_Fire_For_An_Identifier_That_Merely_Contains_The_Word_Return()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea",
                                "customSetter": "var returnValue = {value}; {box}.Transform = returnValue;" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.DoesNotContain("PPG008", IdsOf(result));
        }

        [Fact]
        public void PPG009_Warns_When_SupportedValues_Is_Ignored_By_The_DataType()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom", "supportedValues": ["a", "b"],
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "PPG009");
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public void PPG010_Fires_When_Svg_PropertyPath_Does_Not_Exist_On_SvgElement()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "svg": { "propertyPath": "DoesNotExist", "csharpDataType": "string", "invalidBehavior": "leave-unset" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG010", IdsOf(result));
        }

        [Fact]
        public void PPG010_Fires_When_Svg_CsharpDataType_Disagrees_With_The_Real_Member_Type()
        {
            var json = """
                {
                  "properties": [
                    { "name": "fill", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "svg": { "propertyPath": "Fill", "csharpDataType": "SvgPaint", "invalidBehavior": "leave-unset" } }
                  ]
                }
                """;

            // The stub SvgElement.Fill is a plain string, not SvgPaint, so this is a real type mismatch.
            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG010", IdsOf(result));
        }

        [Fact]
        public void PPG011_Fires_When_Html_PropertyPath_Does_Not_Exist_On_CssBox()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "DoesNotExist", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG011", IdsOf(result));
        }

        [Fact]
        public void PPG011_Fires_When_Html_PropertyPath_Has_No_Setter()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "NoSetter", "csharpDataType": "int", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG011", IdsOf(result));
        }

        [Fact]
        public void PPG011_Passes_Against_The_Real_CssBox_For_A_Correct_Entry()
        {
            var json = """
                {
                  "properties": [
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, includePeachPdfReference: true);

            Assert.DoesNotContain("PPG011", IdsOf(result));
        }

        [Fact]
        public void PPG012_Fires_For_An_Unrecognized_Area()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "NotARealArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG012", IdsOf(result));
        }

        [Fact]
        public void PPG014_Fires_When_A_Logical_Entry_Declares_A_NonNull_InitialValue()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "category": "logical", "inherited": false, "initialValue": "not-null", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": null } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG014", IdsOf(result));
        }

        [Fact]
        public void PPG014_Fires_When_ResolvesTo_Appears_On_A_NonLogical_Entry()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "resolvesTo": { "group": "margin", "axis": "block", "side": "start" },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG014", IdsOf(result));
        }

        [Fact]
        public void A_Fully_Valid_Document_Produces_No_Diagnostics()
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

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void PPG001_Fires_When_An_Entry_In_Properties_Is_Not_An_Object()
        {
            var json = """{ "properties": [ "not an object" ] }""";

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_An_Entry_Has_No_String_Name()
        {
            var json = """{ "properties": [ { "inherited": false, "initialValue": "none", "cssDataType": "cssom" } ] }""";

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_An_Entry_Is_Missing_The_Boolean_Inherited_Field()
        {
            var json = """{ "properties": [ { "name": "x", "initialValue": "none", "cssDataType": "cssom" } ] }""";

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_SupportedValues_Is_Not_An_Array()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "keyword", "supportedValues": "not-an-array",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_InitialValue_Is_Neither_A_String_Null_Nor_An_Expression_Object()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": 5, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Category_Is_Not_A_String()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "category": 5, "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Category_Is_An_Unrecognized_String()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "category": "bogus", "cssDataType": "cssom",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Html_Is_Not_An_Object()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom", "html": "not-an-object" }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Svg_Is_Not_An_Object()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom", "svg": "not-an-object" }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG001_Fires_When_Svg_Is_Missing_Required_InvalidBehavior()
        {
            var json = """
                {
                  "properties": [
                    { "name": "fill", "inherited": false, "initialValue": null, "cssDataType": "svg-paint",
                      "svg": { "propertyPath": "Fill", "csharpDataType": "string" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", IdsOf(result));
        }

        [Fact]
        public void PPG002_Fires_For_An_Unknown_DataType_Object_Shape()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": { "type": "bogus-shape" },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG002", IdsOf(result));
        }

        [Fact]
        public void PPG002_Fires_When_A_DataType_Is_Neither_A_String_Nor_An_Object()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": 5,
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG002", IdsOf(result));
        }

        [Fact]
        public void PPG010_Fires_When_Svg_PropertyPath_Names_A_Property_With_No_Setter()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "cssom",
                      "svg": { "propertyPath": "ReadOnlyOpacity", "csharpDataType": "double", "invalidBehavior": "leave-unset" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.SvgElementWithReadOnlyMember);

            Assert.Contains("PPG010", IdsOf(result));
        }

        [Fact]
        public void Parsed_DataType_Parses_Without_Diagnostics()
        {
            // Schema-reserved shape with no current entry using it (no binding, so no codegen is
            // attempted) - this only proves ParseSingleDataType reads its converter/resultType/typedValueType fields.
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null,
                      "cssDataType": { "type": "parsed", "converter": "SomeConverter.FromCssText",
                                        "resultType": "SomeResult", "typedValueType": "SomeTypedValue" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void SvgTransform_And_SvgReference_DataTypes_Parse_Without_Diagnostics()
        {
            // Schema-reserved data types with no current entry using them (no binding either, so no
            // codegen is attempted) - this only proves ParseSingleDataType's string mapping accepts them.
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "svg-transform" },
                    { "name": "y", "inherited": false, "initialValue": null, "cssDataType": "svg-reference" }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void SupportsDataType_And_SupportsSupportedValues_Parse_Without_Diagnostics()
        {
            var json = """
                {
                  "properties": [
                    { "name": "break-before", "inherited": false, "initialValue": "auto", "cssDataType": "keyword",
                      "supportedValues": ["auto", "region"], "supportsDataType": "keyword", "supportsSupportedValues": ["auto"],
                      "supportsKeywordComparison": "ordinal-ignore-case",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void KeywordOrValue_DataType_Parses_Without_Diagnostics()
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

            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void PPG011_Passes_Against_The_Real_CssBox_For_A_KeywordOrValue_Entry()
        {
            var json = """
                {
                  "properties": [
                    { "name": "z-index", "inherited": false, "initialValue": "auto",
                      "cssDataType": { "type": "keyword-or-value", "enumType": "AutoKeyword", "keywordMap": "Map.AutoKeywords",
                        "fallback": "AutoKeyword.Auto", "valueType": "integer" },
                      "html": { "propertyPath": "ZIndex", "csharpDataType": "CssProperty<CssKeywordOrValue<AutoKeyword, int>>", "area": "DisplayPositioningArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, includePeachPdfReference: true);

            Assert.DoesNotContain("PPG011", IdsOf(result));
        }

        [Fact]
        public void KeywordOrValue_UnrecognizedValueType_FailsGenerationLoudly()
        {
            // valueType is only editor/schema-validated ("integer"/"length"), not re-checked at C# parse
            // time — KeywordOrValueGrammar.Resolve is the one place that guards against an author (or a
            // future value-type addition nobody wired up yet) writing something neither
            // ValidatorExpressionBuilder nor RegistryEmitter know how to turn into real C#.
            var json = """
                {
                  "properties": [
                    { "name": "z-index", "inherited": false, "initialValue": "auto",
                      "cssDataType": { "type": "keyword-or-value", "enumType": "AutoKeyword", "keywordMap": "Map.AutoKeywords",
                        "fallback": "AutoKeyword.Auto", "valueType": "banana" },
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("CS8785", IdsOf(result));
            Assert.Contains(result.Diagnostics, d => d.GetMessage().Contains("valueType \"banana\""));
        }
    }
}
