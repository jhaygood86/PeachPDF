using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>text-transform: uppercase</c> and <c>lowercase</c> use Unicode's full case mappings, which can lengthen the text (the German
    /// sharp s uppercases to two letters). What is resolved per character of the original text (bidi levels, scripts) has to follow the
    /// characters to their new positions.
    /// </summary>
    public class TextTransformFullCaseMappingTests
    {
        private static async Task<string[]> WordTexts(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var box = LayoutHarness.FindById(root, "p")!;
            return [.. LayoutHarness.Descendants(box).SelectMany(b => b.Words).Select(w => w.Text).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!)];
        }

        [Fact]
        public async Task SharpS_UppercasesToTwoLetters() =>
            Assert.Equal(["STRASSE", "ÀÉ"], await WordTexts("<p id='p' style='text-transform:uppercase'>straße àé</p>"));

        [Fact]
        public async Task TheLongerText_StillSplitsAtTheSpacesAndKeepsItsLaterWords() =>
            Assert.Equal(["SS", "SS", "SS", "A"], await WordTexts("<p id='p' style='text-transform:uppercase'>ß ß ß a</p>"));

        [Fact]
        public async Task LengthChangeBeforeRightToLeftText_DoesNotMisplaceItsBidiLevels()
        {
            // Hebrew after a sharp s in a left-to-right paragraph: the Hebrew word is still its own right-to-left run, not shifted by the extra letter.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p' style='text-transform:uppercase'>ßß אבג</p>"));
            var words = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();

            Assert.Equal("SSSS", words[0].Text);
            Assert.Equal(0, words[0].BidiLevel % 2);
            Assert.Equal(1, words[1].BidiLevel % 2);
        }

        [Fact]
        public async Task Lowercase_PicksTheFinalSigmaFromContext() =>
            Assert.Equal(["ας", "σα"], await WordTexts("<p id='p' style='text-transform:lowercase'>ΑΣ ΣΑ</p>"));

        [Fact]
        public async Task TurkishLanguage_UsesTheDottedAndDotlessI()
        {
            Assert.Equal(["İI"], await WordTexts("<p id='p' lang='tr' style='text-transform:uppercase'>iı</p>"));
            Assert.Equal(["ıi"], await WordTexts("<p id='p' lang='tr' style='text-transform:lowercase'>Iİ</p>"));
            Assert.Equal(["II"], await WordTexts("<p id='p' lang='en' style='text-transform:uppercase'>iı</p>"));
        }
    }
}
