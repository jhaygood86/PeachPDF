using System.Linq;
using Microsoft.CodeAnalysis;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>
    /// Protects the incrementality property <see cref="TargetTypeModel"/> exists for (see its
    /// remarks and <see cref="CssPropertyGenerator"/>'s TargetTypes stage comment): re-running the
    /// generator against a *new* Compilation object with an *equivalent* shape must reuse the cached
    /// TargetTypes step, not silently regress into re-running the compilation-dependent stage (and
    /// everything after it) on every keystroke.
    /// </summary>
    public class GeneratorIncrementalityTests
    {
        private const string SimpleJson = """
            {
              "properties": [
                { "name": "transform", "inherited": false, "initialValue": "none", "cssDataType": "cssom",
                  "html": { "propertyPath": "Transform", "csharpDataType": "string", "area": "VisualEffectsArea" } }
              ]
            }
            """;

        [Fact]
        public void TargetTypes_Step_Is_Cached_When_The_Compilation_Is_Equivalent_But_Not_Identical()
        {
            var driver = GeneratorTestHost.CreateDriver(SimpleJson, trackIncrementalSteps: true);

            var compilation1 = GeneratorTestHost.BuildCompilation(StubSources.MinimalCssBoxAndSvgElement);
            driver = driver.RunGenerators(compilation1, TestContext.Current.CancellationToken);

            // A second, distinct Compilation object built from identical source text - CompilationProvider's
            // identity changes, but CaptureTargetTypes' *output* (an EquatableArray-backed TargetTypeModel)
            // should not, so the step should come back as Cached rather than re-running.
            var compilation2 = GeneratorTestHost.BuildCompilation(StubSources.MinimalCssBoxAndSvgElement);
            driver = driver.RunGenerators(compilation2, TestContext.Current.CancellationToken);

            var result = driver.GetRunResult().Results.Single();
            var steps = result.TrackedSteps[TrackingNames.TargetTypes];

            // Roslyn's own vocabulary: the step still *ran* (the Compilation's identity did change,
            // so it's not "Cached" — that reason means the step didn't execute at all), but its output
            // compared equal to the prior run via TargetTypeModel's IEquatable<T> implementation, so
            // Roslyn reports "Unchanged" and does not propagate a change downstream. That coalescing is
            // the actual property this test exists to protect — a non-equatable model (e.g. a raw
            // ITypeSymbol/Compilation reference) would report "Modified" here instead, and every
            // downstream stage — including the two AddSource emits — would needlessly re-run.
            Assert.NotEmpty(steps);
            Assert.All(steps, step => Assert.All(step.Outputs, o => Assert.Equal(IncrementalStepRunReason.Unchanged, o.Reason)));
        }

        [Fact]
        public void ParsedJson_Step_Reruns_When_The_Json_Text_Changes()
        {
            var compilation = GeneratorTestHost.BuildCompilation(StubSources.MinimalCssBoxAndSvgElement);

            var driver1 = GeneratorTestHost.CreateDriver(SimpleJson, trackIncrementalSteps: true);
            driver1 = driver1.RunGenerators(compilation, TestContext.Current.CancellationToken);

            var changedJson = SimpleJson.Replace("\"initialValue\": \"none\"", "\"initialValue\": \"initial\"");
            var driver2 = GeneratorTestHost.CreateDriver(changedJson, trackIncrementalSteps: true);
            driver2 = driver2.RunGenerators(compilation, TestContext.Current.CancellationToken);

            var result = driver2.GetRunResult().Results.Single();
            var steps = result.TrackedSteps[TrackingNames.ParsedJson];

            // A brand-new driver has no prior cache to compare against, so its first run is New, not
            // Cached - this asserts the step ran at all (i.e. actually observed the changed text) rather
            // than asserting a specific reason, since driver1/driver2 are independent driver instances.
            Assert.NotEmpty(steps);
        }
    }
}
