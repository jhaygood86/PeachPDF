using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using PeachPDF.SourceGenerators;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>An in-memory <see cref="AdditionalText"/> so tests don't need a real file on disk.</summary>
    internal sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }

    /// <summary>Drives <see cref="CssPropertyGenerator"/> over an in-memory compilation for tests.</summary>
    internal static class GeneratorTestHost
    {
        /// <summary>An assembly reference carrying the real <c>CssBox</c>/<c>SvgElement</c> shape, for PPG010/PPG011 tests.</summary>
        public static MetadataReference PeachPdfReference { get; } =
            MetadataReference.CreateFromFile(typeof(PeachPDF.PdfGenerator).Assembly.Location);

        /// <summary>
        /// Every BCL reference assembly the current runtime trusts - the standard way to get a
        /// complete, portable reference set for an in-memory Compilation without hand-picking
        /// individual assemblies (which reliably misses one, e.g. System.Runtime or netstandard,
        /// depending on what a given test's stub source happens to use).
        /// </summary>
        private static readonly MetadataReference[] CoreReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(System.IO.Path.PathSeparator)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToArray();

        public static CSharpCompilation BuildCompilation(string? extraSource = null, bool includePeachPdfReference = false)
        {
            var references = new System.Collections.Generic.List<MetadataReference>(CoreReferences);
            if (includePeachPdfReference) references.Add(PeachPdfReference);

            var syntaxTrees = extraSource is null
                ? ImmutableArray<SyntaxTree>.Empty
                : ImmutableArray.Create<SyntaxTree>(CSharpSyntaxTree.ParseText(extraSource));

            return CSharpCompilation.Create(
                "PeachPDF.SourceGenerators.TestAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        public static GeneratorDriver CreateDriver(string json, bool trackIncrementalSteps = false)
        {
            var additionalText = new InMemoryAdditionalText("css-properties.json", json);
            var generator = new CssPropertyGenerator();
            var options = new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackIncrementalSteps);

            return CSharpGeneratorDriver.Create(
                [generator.AsSourceGenerator()],
                additionalTexts: [additionalText],
                driverOptions: options);
        }

        public static GeneratorDriverRunResult Run(string json, string? extraSource = null, bool includePeachPdfReference = false)
        {
            var compilation = BuildCompilation(extraSource, includePeachPdfReference);
            var driver = CreateDriver(json);
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }
    }
}
