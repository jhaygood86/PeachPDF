using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Fonts
{
    /// <summary>
    /// The font and text engine (<c>src/PeachPDF/Fonts</c>, <c>src/PeachPDF/Text</c>) is being extracted into an
    /// assembly of its own, which cannot reference PeachPDF. This keeps it extractable: the day the engine again
    /// reaches into the PDF writer, the CSS layer or the HTML layer, this fails naming the file and line, long before
    /// the move is attempted and turns up dozens of them at once.
    /// </summary>
    public class EngineIndependenceTests
    {
        // Anything under PeachPDF.* other than the engine's own namespaces. Also catches "using PeachPDF;".
        private static readonly Regex OutsideReference = new(@"\bPeachPDF\.(?!Fonts\b|Text\b)\w+|^\s*using\s+PeachPDF\s*;", RegexOptions.Compiled);

        private static string? FindEngineRoot(string folder)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PeachPDF", folder);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        [Theory]
        [InlineData("Fonts")]
        [InlineData("Text")]
        public void EngineSourceDoesNotReferenceTheRestOfPeachPdf(string folder)
        {
            var root = FindEngineRoot(folder);
            Assert.SkipUnless(root != null, "The engine's source tree is not next to the test binaries.");

            var violations = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root!, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = lines[i];
                    var comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0)
                        code = code[..comment];

                    if (OutsideReference.IsMatch(code))
                        violations.Add($"{Path.GetRelativePath(root!, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                $"src/PeachPDF/{folder} must not reference the rest of PeachPDF (it is being extracted into its own assembly):\n" +
                string.Join("\n", violations));
        }
    }
}
