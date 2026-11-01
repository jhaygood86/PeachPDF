using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>
    /// A paragraph of Thai, Lao, Khmer or Burmese wraps at the words the dictionary finds. The font gives every character (a mark or a
    /// vowel sign included) an advance of one em, so a width in whole ems says how many characters a line holds.
    /// </summary>
    public class ParagraphDictionaryBreakingTests
    {
        private const double Size = 20;

        private static readonly Typeface Face = LoadFace();

        private static Typeface LoadFace()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.LineBreak, new AddOptions { FamilyName = "DictionaryBreaking-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static string[] Lines(string text, double ems, ParagraphStyle? style = null)
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));
            if (style is { } given)
            {
                builder.SetStyle(given);
            }

            var layout = builder.AddText(text).Build().Layout(ems * Size + 1);
            return layout.Lines.Select(line => text.Substring(line.Range.Start, line.Range.Length).TrimEnd()).ToArray();
        }

        [Fact]
        public void Thai_WrapsAtWords()
        {
            Assert.Equal(["ฉัน", "รัก", "ภาษา", "ไทย"], Lines("ฉันรักภาษาไทย", 3));
            Assert.Equal(["ฉันรัก", "ภาษา", "ไทย"], Lines("ฉันรักภาษาไทย", 6));
            Assert.Equal(["ฉันรัก", "ภาษาไทย"], Lines("ฉันรักภาษาไทย", 7));
        }

        [Fact]
        public void Khmer_WrapsAtWords()
        {
            Assert.Equal(["ខ្ញុំ", "ស្រលាញ់", "ភាសាខ្មែរ"], Lines("ខ្ញុំស្រលាញ់ភាសាខ្មែរ", 4));
        }

        [Fact]
        public void Lao_WrapsAtWords()
        {
            Assert.Equal(["ຂ້ອຍ", "ຮັກ", "ພາສາ", "ລາວ"], Lines("ຂ້ອຍຮັກພາສາລາວ", 4));
        }

        [Fact]
        public void Burmese_WrapsAtWords()
        {
            Assert.Equal(["ကျွန်တော်", "မြန်မာ", "စာကို", "ချစ်", "တယ်"], Lines("ကျွန်တော်မြန်မာစာကိုချစ်တယ်", 6));
        }

        [Fact]
        public void TheGeneralCategoryFallback_LeavesARunWhole()
        {
            var style = new ParagraphStyle { LineBreak = new LineBreakOptions { ComplexContext = ComplexContextBreaking.GeneralCategory } };

            Assert.Equal(["ฉันรักภาษาไทย"], Lines("ฉันรักภาษาไทย", 3, style));
        }

        [Fact]
        public void AWordThatDoesNotFit_IsBrokenByOverflowWrap_AtAClusterNotInsideOne()
        {
            var style = new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere };

            Assert.Equal(["ภาษ", "า"], Lines("ภาษา", 3, style));
        }
    }
}
