using System.Runtime.CompilerServices;
using System.Text.Json;
using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// <c>css-properties.json</c> is the one place a longhand's inheritance and CSS-OM shape are declared. The
    /// classes generated from its <c>cssom</c> blocks cannot disagree with it; these guard the registration itself and
    /// that no longhand slips back to a hand-written class.
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

        /// <summary>
        /// The entries that deliberately have no <c>cssom</c> block, each with the reason it cannot be generated.
        /// Everything else in the JSON generates its CSS-OM class.
        /// </summary>
        private static readonly Dictionary<string, string> NotGenerated = new()
        {
            ["-webkit-backdrop-filter"] = "a vendor alias, canonicalized to backdrop-filter before any class is created",
            ["word-wrap"] = "a legacy alias, canonicalized to overflow-wrap before any class is created",
            ["x"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["y"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["cx"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["cy"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["r"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["rx"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
            ["ry"] = "an SvgOnlyProperty instance: DeclarationCondition tells those apart by type",
        };

        [Fact]
        public void EveryJsonEntry_GeneratesItsCssomClass_ExceptTheReviewedExclusions()
        {
            var ungenerated = Entries()
                .Where(e => !e.TryGetProperty("cssom", out _))
                .Select(e => e.GetProperty("name").GetString()!)
                .Where(n => !NotGenerated.ContainsKey(n))
                .ToList();

            Assert.True(ungenerated.Count == 0,
                "Give these a \"cssom\" block in css-properties.json (or a reviewed reason in NotGenerated): " + string.Join(", ", ungenerated));
        }

        [Fact]
        public void TheExclusions_StillHaveNoCssomBlock()
        {
            var stale = Entries()
                .Where(e => e.TryGetProperty("cssom", out _))
                .Select(e => e.GetProperty("name").GetString()!)
                .Where(NotGenerated.ContainsKey)
                .ToList();

            Assert.Empty(stale);
        }
    }
}
