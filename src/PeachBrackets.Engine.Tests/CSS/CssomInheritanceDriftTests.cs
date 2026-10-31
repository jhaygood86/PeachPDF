using System.Runtime.CompilerServices;
using System.Text.Json;
using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// <c>css-properties.json</c> is the one place a longhand's inheritance and CSS-OM shape are declared. The
    /// classes generated from its <c>cssom</c> blocks cannot disagree with it; these guard the hand-written
    /// remainder (properties whose class carries extra grammar members) and the registration itself.
    /// </summary>
    public class CssomInheritanceDriftTests
    {
        private static JsonElement[] Entries()
        {
            var path = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "PeachBrackets", "css-properties.json"));
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.GetProperty("properties").EnumerateArray().Select(e => e.Clone()).ToArray();
        }

        private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

        [Fact]
        public void EveryJsonLonghand_HasTheInheritanceTheJsonDeclares()
        {
            var drift = new List<string>();
            foreach (var entry in Entries())
            {
                var name = entry.GetProperty("name").GetString()!;
                var property = PropertyFactory.Instance.CreateLonghand(name);
                if (property is null) continue;

                var expected = entry.GetProperty("inherited").GetBoolean();
                if (property.CanBeInherited != expected)
                    drift.Add($"{name}: json inherited={expected}, CSS-OM CanBeInherited={property.CanBeInherited}");
            }

            Assert.True(drift.Count == 0, string.Join("\n", drift));
        }

        [Fact]
        public void EveryCssomBlock_IsRegisteredAsALonghand()
        {
            var missing = Entries()
                .Where(e => e.TryGetProperty("cssom", out _))
                .Select(e => e.GetProperty("name").GetString()!)
                .Where(n => PropertyFactory.Instance.CreateLonghand(n) is null)
                .ToList();

            Assert.Empty(missing);
        }

        // Spec inheritance of properties css-properties.json does not (yet) describe: CSS Text 3 text-justify
        // and SVG 2 text-anchor are both inherited.
        [Theory]
        [InlineData("text-justify")]
        [InlineData("text-anchor")]
        [InlineData("cursor")]
        public void SpecInheritedLonghandsOutsideTheJson_AreInherited(string name) =>
            Assert.True(PropertyFactory.Instance.CreateLonghand(name)!.CanBeInherited);
    }
}
