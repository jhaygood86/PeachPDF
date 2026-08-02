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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } },
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": "auto", "aliasOf": "does-not-exist", "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": "auto", "cssDataType": "any",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } },
                    { "name": "y", "inherited": false, "initialValue": "different", "aliasOf": "x", "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any", "supportedValues": ["a", "b"],
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "fill", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "x", "category": "logical", "inherited": false, "initialValue": "not-null", "cssDataType": "any",
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
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "any",
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
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "any",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Empty(result.Diagnostics);
        }
    }
}
