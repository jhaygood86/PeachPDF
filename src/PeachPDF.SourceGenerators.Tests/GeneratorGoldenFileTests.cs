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
                    { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "any",
                      "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            var generated = result.Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssPropertyRegistry.g.cs").SourceText.ToString();

            Assert.Contains("private static bool Validate_Transform(CssValueParser parser, string value) => true;", generated);
            Assert.Contains("box.Transform = value;", generated);
            Assert.Contains("[\"transform\"] = Set_Transform,", generated);
            Assert.Contains("[\"transform\"] = \"none\",", generated);
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
            Assert.Contains("element.Fill = parsed;", generated);
            Assert.Contains("[\"fill\"] = \"black\",", generated);
        }

        [Fact]
        public void Does_Not_Emit_Anything_For_An_Svg_Binding_With_ApplyIn_Manual()
        {
            var json = """
                {
                  "properties": [
                    { "name": "direction", "inherited": true, "initialValue": "ltr", "cssDataType": "any",
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
