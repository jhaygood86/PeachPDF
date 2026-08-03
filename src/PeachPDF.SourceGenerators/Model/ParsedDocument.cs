using System;
using System.Collections.Generic;
using PeachPDF.SourceGenerators.Json;

namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>
    /// The result of parsing one <c>css-properties.json</c> AdditionalText. Equality is deliberately
    /// (Path, SourceText) only, not a deep comparison of <see cref="Entries"/>/<see cref="Diagnostics"/>
    /// - parsing is a pure function of the source text, so two documents with equal text are
    /// guaranteed to parse to equal entries, and comparing the (much cheaper) source text is enough
    /// for the incremental pipeline to cache correctly without needing every model type in this
    /// namespace to implement structural <see cref="IEquatable{T}"/> itself.
    /// </summary>
    internal sealed class ParsedDocument : IEquatable<ParsedDocument>
    {
        public string Path { get; }
        public string SourceText { get; }
        public IReadOnlyList<PropertyEntry> Entries { get; }
        public IReadOnlyList<ModelDiagnostic> Diagnostics { get; }

        public ParsedDocument(string path, string sourceText, IReadOnlyList<PropertyEntry> entries, IReadOnlyList<ModelDiagnostic> diagnostics)
        {
            Path = path;
            SourceText = sourceText;
            Entries = entries;
            Diagnostics = diagnostics;
        }

        public static ParsedDocument Parse(string path, string sourceText)
        {
            if (!JsonParser.TryParse(sourceText, out var root, out var jsonErrors))
            {
                var diagnostics = new List<ModelDiagnostic>();
                foreach (var error in jsonErrors)
                    diagnostics.Add(new ModelDiagnostic(DiagnosticCode.JsonMalformed, error.Message, error.Line, error.Column));
                return new ParsedDocument(path, sourceText, System.Array.Empty<PropertyEntry>(), diagnostics);
            }

            var (entries, diagnostics2) = PropertyModelParser.Parse(root!);
            return new ParsedDocument(path, sourceText, entries, diagnostics2);
        }

        public bool Equals(ParsedDocument? other) =>
            other is not null && Path == other.Path && SourceText == other.SourceText;

        public override bool Equals(object? obj) => Equals(obj as ParsedDocument);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Path.GetHashCode() * 31) ^ SourceText.GetHashCode();
            }
        }
    }
}
