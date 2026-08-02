using System.Collections.Immutable;
using PeachPDF.SourceGenerators;
using PeachPDF.SourceGenerators.Json;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators.Tests
{
    /// <summary>Direct unit tests for the model/equality types the incremental pipeline relies on, and
    /// small pure-function edges not otherwise reachable through a full generator run.</summary>
    public class GeneratorModelUnitTests
    {
        [Fact]
        public void EquatableArray_TreatsSameElementsAsEqual()
        {
            var a = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
            var b = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
            var c = new EquatableArray<int>(ImmutableArray.Create(1, 2, 4));

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not an array"));
        }

        [Fact]
        public void EquatableArray_DefaultInstanceBehavesAsEmpty()
        {
            var uninitialized = default(EquatableArray<int>);

            Assert.Equal(0, uninitialized.GetHashCode());
            Assert.True(uninitialized.Equals(EquatableArray<int>.Empty));
            Assert.Empty(uninitialized);
        }

        [Fact]
        public void EquatableArray_EnumeratesInOrder()
        {
            var array = new[] { "a", "b", "c" }.ToEquatableArray();

            Assert.Equal(["a", "b", "c"], array);
            Assert.Equal("b", array[1]);

            System.Collections.IEnumerable nonGeneric = array;
            var seen = new System.Collections.Generic.List<object?>();
            foreach (var item in nonGeneric) seen.Add(item);
            Assert.Equal(["a", "b", "c"], seen);
        }

        [Fact]
        public void TargetMember_EqualityComparesEveryField()
        {
            var a = new TargetMember("Transform", "string", hasSetter: true);
            var b = new TargetMember("Transform", "string", hasSetter: true);
            var differentType = new TargetMember("Transform", "int", hasSetter: true);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(differentType));
            Assert.False(a.Equals("not a member"));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void TargetTypeModel_TryGetMember_FindsByNameAndReportsMissing()
        {
            var members = new[] { new TargetMember("Transform", "string", true) }.ToEquatableArray();
            var model = new TargetTypeModel(true, members, true, members);

            Assert.True(model.TryGetCssBoxMember("Transform", out var found));
            Assert.Equal("string", found.TypeDisplay);
            Assert.False(model.TryGetCssBoxMember("DoesNotExist", out var missing));
            Assert.Equal(default, missing);
            Assert.True(model.TryGetSvgElementMember("Transform", out _));
        }

        [Fact]
        public void TargetTypeModel_EqualityComparesEveryField()
        {
            var members = new[] { new TargetMember("Transform", "string", true) }.ToEquatableArray();
            var a = new TargetTypeModel(true, members, true, members);
            var b = new TargetTypeModel(true, members, true, members);
            var differentFound = new TargetTypeModel(false, members, true, members);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(differentFound));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals((object?)null));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ParsedDocument_EqualityIsPathAndSourceTextOnly()
        {
            var a = ParsedDocument.Parse("css-properties.json", "{}");
            var sameTextDifferentEntries = ParsedDocument.Parse("css-properties.json", "{}");
            var differentPath = ParsedDocument.Parse("other.json", "{}");
            var differentText = ParsedDocument.Parse("css-properties.json", "{ }");

            Assert.True(a.Equals(sameTextDifferentEntries));
            Assert.True(a.Equals((object)sameTextDifferentEntries));
            Assert.False(a.Equals(differentPath));
            Assert.False(a.Equals(differentText));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals((object?)"not a document"));
            Assert.Equal(a.GetHashCode(), sameTextDifferentEntries.GetHashCode());
        }

        [Fact]
        public void Diagnostics_For_FallsBackToJsonMalformed_ForAnUnmappedCode()
        {
            // PPG013 (UnknownPropertyNameConstant) is reserved but not yet implemented - Diagnostics.For's
            // default arm is what a future unmapped code would hit today.
            Assert.Same(Diagnostics.JsonMalformed, Diagnostics.For(DiagnosticCode.UnknownPropertyNameConstant));
        }

        [Fact]
        public void JsonValue_HasProperty_MirrorsTryGetProperty()
        {
            JsonParser.TryParse("""{ "name": "x" }""", out var root, out var errors);

            Assert.Empty(errors);
            Assert.True(root!.HasProperty("name"));
            Assert.False(root.HasProperty("missing"));
        }

        [Fact]
        public void JsonParser_ReportsAnErrorForTrailingContentAfterTheRootValue()
        {
            // Utf8JsonReader itself rejects a second top-level value (it throws before JsonParser's own
            // explicit trailing-content check ever runs) - either way, TryParse must fail with a located error.
            var ok = JsonParser.TryParse("""{} {}""", out var root, out var errors);

            Assert.False(ok);
            Assert.Null(root);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void JsonParser_ReportsUnexpectedEndOfInput_ForAnEmptyDocument()
        {
            var ok = JsonParser.TryParse("", out var root, out var errors);

            Assert.False(ok);
            Assert.Null(root);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void JsonParser_ReportsAnErrorForAStrayClosingBracketAsTheRootValue()
        {
            var ok = JsonParser.TryParse("]", out var root, out var errors);

            Assert.False(ok);
            Assert.Null(root);
            Assert.NotEmpty(errors);
        }
    }
}
