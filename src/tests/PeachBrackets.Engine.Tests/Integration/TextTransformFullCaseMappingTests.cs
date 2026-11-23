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

        [Fact]
        public async Task Capitalize_UsesTheTitleCaseOfTheFirstLetterOfEachWord()
        {
            Assert.Equal(["Straße", "Ssx", "\u01C5a"], await WordTexts("<p id='p' style='text-transform:capitalize'>straße ßx \u01C6a</p>"));
        }

        [Fact]
        public async Task Capitalize_SkipsLeadingPunctuationAndKeepsTheRestOfTheWord() =>
            Assert.Equal(["(Hello", "WORLD"], await WordTexts("<p id='p' style='text-transform:capitalize'>(hello wORLD</p>"));

        [Fact]
        public async Task Lithuanian_DropsTheDotAboveAnIWhenItUppercases() =>
            Assert.Equal(["I\u0300"], await WordTexts("<p id='p' lang='lt' style='text-transform:uppercase'>i\u0307\u0300</p>"));

        [Fact]
        public async Task FullWidth_CollapsesWhiteSpaceBeforeItBecomesIdeographicSpaces()
        {
            // Three spaces collapse to one (css-text-3 section 4.1), and the one that survives is the ideographic space.
            var words = await WordTexts("<p id='p' style='text-transform:full-width'>a   b</p>");
            Assert.Equal("\uFF41\u3000\uFF42", string.Concat(words));
        }

        [Fact]
        public async Task FullWidth_KeepsEverySpaceOfPreservedWhiteSpace()
        {
            var words = await WordTexts("<p id='p' style='text-transform:full-width; white-space:pre'>a  b</p>");
            Assert.Equal("\uFF41\u3000\u3000\uFF42", string.Concat(words));
        }

        [Fact]
        public async Task AWordThatChangedLength_KeepsItsSourceAsItsOriginalText()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p' style='text-transform:uppercase'>stra\u00DFe</p>"));
            var word = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(b => b.Words).Single(w => !string.IsNullOrEmpty(w.Text));

            Assert.Equal("STRASSE", word.Text);
            Assert.Equal("stra\u00DFe", word.OriginalText);
        }

        [Fact]
        public async Task FirstLineTransform_IsDerivedFromTheSourceOfAWordThatChangedLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#p::first-line{text-transform:lowercase}</style><p id='p' style='text-transform:uppercase'>STRA\u00DFE</p>"));
            var word = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(b => b.Words).Single(w => !string.IsNullOrEmpty(w.Text));

            Assert.Equal("STRASSE", word.Text);
            Assert.Equal("stra\u00DFe", ((PeachBrackets.Html.Dom.CssRect)word).FirstLineText);
        }
    }
}
