using PeachDrawing.Text.Unicode;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="CssBidiParagraphResolver"/> writes plain ASCII in a left-to-right paragraph out without running the bidi, script and
    /// joining analyses. These run the real analyses over random ASCII and require exactly the values it writes.
    /// </summary>
    public class CssBidiParagraphResolverAsciiTests
    {
        private static string RandomAscii(Random random, int maxLength)
        {
            var length = random.Next(1, maxLength);
            var characters = new char[length];

            for (var i = 0; i < length; i++)
            {
                characters[i] = random.Next(4) switch
                {
                    0 => (char)random.Next('a', 'z' + 1),
                    1 => (char)random.Next('A', 'Z' + 1),
                    2 => (char)random.Next('0', '9' + 1),
                    _ => (char)random.Next(0, 0x80),
                };
            }

            return new string(characters);
        }

        [Fact]
        public void TheRealAnalyses_GiveTheValuesTheAsciiPathWrites()
        {
            var random = new Random(23);

            for (var n = 0; n < 3000; n++)
            {
                var text = RandomAscii(random, 40);

                var levels = Bidi.Analyze(text, BaseDirection.Ltr, []).Levels;
                Assert.All(levels, level => Assert.Equal(0, level));

                var codepoints = text.Select(c => (int)c).ToList();
                var resolved = Scripts.ResolveLooked(codepoints.Select(Scripts.Of).ToList());
                var expected = text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') ? "Latin" : "Common";
                Assert.All(resolved, script => Assert.Equal(expected, script));

                Assert.All(ArabicJoining.Resolve(codepoints), form => Assert.Equal(ArabicJoiningForm.None, form));
            }
        }

        [Fact]
        public async Task AParagraphOfPlainEnglish_GetsLatinScriptsZeroLevelsAndNoJoining()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>Order 66, <b>bold</b> and <i>italic</i>.</p>"));

            var textBoxes = Descendants(LayoutHarness.FindById(root, "p")!).Where(b => b.Text is { Length: > 0 }).ToList();

            Assert.NotEmpty(textBoxes);
            Assert.All(textBoxes, b =>
            {
                Assert.Equal(b.Text!.Length, b.BidiLevels!.Length);
                Assert.All(b.BidiLevels, level => Assert.Equal(0, level));
                Assert.All(b.CharScripts!, script => Assert.Equal("Latin", script));
                Assert.All(b.JoiningForms!, form => Assert.Equal(ArabicJoiningForm.None, form));
            });
        }

        [Fact]
        public async Task ADigitsAndPunctuationParagraph_StaysCommon()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>12, 34 - 56 (78)</p>"));

            var box = Descendants(LayoutHarness.FindById(root, "p")!).First(b => b.Text is { Length: > 0 });

            Assert.All(box.CharScripts!, script => Assert.Equal("Common", script));
        }

        [Fact]
        public async Task ArabicTextInTheSameDocument_StillGoesThroughTheRealAnalyses()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>abc مرحبا</p>"));

            var box = Descendants(LayoutHarness.FindById(root, "p")!).First(b => b.Text is { Length: > 0 });

            Assert.Contains(box.BidiLevels!, level => level != 0);
            Assert.Contains(box.CharScripts!, script => script == "Arabic");
        }

        private static IEnumerable<CssBox> Descendants(CssBox box) =>
            box.Boxes.SelectMany(child => new[] { child }.Concat(Descendants(child)));
    }
}
