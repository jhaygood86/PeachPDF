using System.Linq;
using Microsoft.CodeAnalysis;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>The <c>cssom</c> block: PPG018 and the shape of <c>CssomProperties.g.cs</c>.</summary>
    public class GeneratorCssomTests
    {
        private static string Generated(string json) =>
            GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement)
                .Results.Single().GeneratedSources
                .Single(s => s.HintName == "CssomProperties.g.cs").SourceText.ToString();

        [Fact]
        public void PPG018_Fires_When_Cssom_Block_Has_No_Converter()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "length", "cssom": { "animatable": true } }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG018", result.Diagnostics.Select(d => d.Id));
        }

        [Fact]
        public void Cssom_That_Is_Not_An_Object_Reports_Malformed_Json()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "length", "cssom": "nope" }
                  ]
                }
                """;

            var result = GeneratorTestHost.Run(json, StubSources.MinimalCssBoxAndSvgElement);

            Assert.Contains("PPG001", result.Diagnostics.Select(d => d.Id));
        }

        [Fact]
        public void Emits_A_Property_Class_Whose_Inheritance_Comes_From_The_Entry()
        {
            var json = """
                {
                  "properties": [
                    { "name": "caption-side", "inherited": true, "initialValue": "top", "cssDataType": "length",
                      "cssom": { "converter": "Converters.CaptionSideConverter" } },
                    { "name": "border-top-color", "inherited": false, "initialValue": "currentcolor", "cssDataType": "length",
                      "cssom": { "converter": "Converters.CurrentColorConverter", "animatable": true, "registryAnimatable": true } }
                  ]
                }
                """;

            var source = Generated(json);

            Assert.Contains("internal sealed class CaptionSideProperty : Property", source);
            Assert.Contains("(Converters.CaptionSideConverter).OrDefault();", source);
            Assert.Contains(": base(\"caption-side\", PropertyFlags.Inherited)", source);
            Assert.Contains(": base(\"border-top-color\", PropertyFlags.Animatable)", source);
            Assert.Contains("AddLonghand(\"caption-side\", () => new CaptionSideProperty());", source);
            Assert.Contains("AddLonghand(\"border-top-color\", () => new BorderTopColorProperty(), true);", source);
        }

        [Fact]
        public void Emits_Every_Flag_And_The_Font_Registration_And_An_Explicit_Class_Name()
        {
            var json = """
                {
                  "properties": [
                    { "name": "-webkit-x", "inherited": true, "initialValue": "0", "cssDataType": "length",
                      "cssom": { "converter": "Converters.LengthConverter", "className": "CustomName",
                                 "hashless": true, "unitless": true, "animatable": true, "font": true } }
                  ]
                }
                """;

            var source = Generated(json);

            Assert.Contains("internal sealed class CustomName : Property", source);
            Assert.Contains("PropertyFlags.Inherited | PropertyFlags.Hashless | PropertyFlags.Unitless | PropertyFlags.Animatable", source);
            Assert.Contains("AddLonghand(\"-webkit-x\", () => new CustomName(), false, true);", source);
        }

        [Fact]
        public void Entries_Without_A_Cssom_Block_Emit_Only_The_Empty_Registration()
        {
            var json = """
                {
                  "properties": [
                    { "name": "x", "inherited": false, "initialValue": null, "cssDataType": "length" }
                  ]
                }
                """;

            var source = Generated(json);

            Assert.DoesNotContain("AddLonghand(", source);
            Assert.Contains("private void RegisterGeneratedLonghands()", source);
        }
    }
}
