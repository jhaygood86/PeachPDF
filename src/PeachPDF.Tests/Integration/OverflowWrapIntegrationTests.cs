using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>End-to-end coverage for CSS Text 3's overflow-wrap property and word-wrap alias.</summary>
    public class OverflowWrapIntegrationTests
    {
        private const string LongWord = "Chargoggagoggmanchauggagoggchaubunagungamaugg";

        [Theory]
        [InlineData("overflow-wrap:break-word")]
        [InlineData("overflow-wrap:anywhere")]
        [InlineData("word-wrap:break-word")]
        [InlineData("word-wrap:anywhere")]
        public async Task EmergencyValues_WrapAnOverlongInlineWord(string declaration)
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt'>before
                    <em id='word' style='{declaration}; letter-spacing:1pt'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;
            var fragments = WordsOf(word).ToList();

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.NotEmpty(fragments);
            Assert.All(fragments, fragment =>
                Assert.True(fragment.Right <= paragraph.ClientRight + 0.5,
                    $"'{fragment.Text}' overflowed {fragment.Right} > {paragraph.ClientRight}"));
        }

        [Fact]
        public async Task Normal_LeavesTheSameOverlongWordUnbroken()
        {
            var html = LayoutHarness.Wrap($"<p id='p' style='width:70pt'><em id='word'>{LongWord}</em></p>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;
            var fragments = WordsOf(word).ToList();

            Assert.Equal(1, LinesWithText(paragraph));
            Assert.Contains(fragments, fragment => fragment.Right > paragraph.ClientRight + 0.5);
        }

        [Fact]
        public async Task Normal_AdjacentInlineElementsRemainOneUnbreakableToken()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:55pt; font-size:16pt"><span>abcdefgh</span>ijklmnop</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.Single(textLines);
            Assert.Contains(textLines[0].Words, word => word.Right > paragraph.ClientRight + 0.5);
        }

        [Fact]
        public async Task InlineMarkup_PreservesBidiLevelWrapOpportunity()
        {
            var html = LayoutHarness.Wrap("""
                <p id="plain" style="width:30pt; font-size:16pt">abcאבג</p>
                <p id="markup" style="width:30pt; font-size:16pt"><span>abc</span>אבג</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var plain = LayoutHarness.FindById(root, "plain")!;
            var markup = LayoutHarness.FindById(root, "markup")!;

            Assert.Equal(LinesWithText(plain), LinesWithText(markup));
            Assert.True(LinesWithText(markup) > 1);
        }

        [Fact]
        public async Task NormalOpportunityBeforeOverlongWord_HasPriorityOverEmergencyBreak()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt'>short
                    <em style='overflow-wrap:anywhere'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(textLines.Count > 1);
            Assert.Equal("short", string.Concat(textLines[0].Words.Select(word => word.Text)));
        }

        [Fact]
        public async Task NormalOpportunityBeforeCrossSpanWord_HasPriorityOverEmergencyBreak()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:75pt; font-size:16pt; overflow-wrap:anywhere">prefix <span>abcdef</span>ghijklmnop</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstLine = paragraph.LineBoxes.First(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

            Assert.DoesNotContain(firstLine.Words, word => word.Text?.Contains('a') == true);
            Assert.True(paragraph.LineBoxes.Count(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))) > 1);
        }

        [Fact]
        public async Task EmergencyOpportunityAfterAnywhereSpan_WrapsFollowingNormalText()
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="p" style="width:70pt; font-size:16pt"><span style="overflow-wrap:anywhere">{{LongWord}}</span><span style="overflow-wrap:normal">WWWW</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var lines = paragraph.LineBoxes
                .Where(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)))
                .Select(line => string.Concat(line.Words.Select(word => word.Text)))
                .ToList();

            Assert.Equal("WWWW", lines[^1]);
            Assert.DoesNotContain(lines.SkipLast(1), line => line.Contains('W'));
        }

        [Fact]
        public async Task VerticalEmergencyOpportunityAfterAnywhereSpan_WrapsFollowingNormalText()
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="p" style="width:80pt; height:70pt; writing-mode:vertical-rl; font-size:16pt"><span style="overflow-wrap:anywhere">{{LongWord}}</span><span style="overflow-wrap:normal">WWWW</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var columns = paragraph.LineBoxes
                .Where(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)))
                .Select(line => string.Concat(line.Words.Select(word => word.Text)))
                .ToList();

            Assert.Equal("WWWW", columns[^1]);
            Assert.DoesNotContain(columns.SkipLast(1), column => column.Contains('W'));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task EmojiSpan_UsesOrdinaryBreaksBetweenGraphemeClusters(string separator)
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <div id="text" style="width:180pt; font-size:16pt; overflow-wrap:anywhere; white-space:pre-wrap">Northline Office B.V.{{separator}}<span id="emoji" style="font-family:Emoji; font-size:21pt">😀😀😀😀😀😀😀😀😀😀</span></div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;
            var firstLine = text.LineBoxes.First(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

            Assert.True(firstLine.Words.Any(word => word.Text?.Contains("😀", StringComparison.Ordinal) == true),
                string.Join(" | ", text.LineBoxes.Select(line =>
                    string.Concat(line.Words.Select(word => word.Text)))));
            Assert.True(text.LineBoxes.Count(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text))) > 1);
        }

        [Fact]
        public async Task EmojiSequences_RemainWholeOrdinaryBreakUnits()
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.ColorEmojiSequences));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: EmojiSequences; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="text" style="font:18pt EmojiSequences">🏳️‍🌈👩‍💻🇺🇸👍🏽❤️</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;
            var words = WordsOf(text).Where(word => !string.IsNullOrEmpty(word.Text)).ToList();

            Assert.Equal(["🏳️‍🌈", "👩‍💻", "🇺🇸", "👍🏽", "❤️"], words.Select(word => word.Text));
        }

        [Fact]
        public async Task Normal_EmojiClustersWrapAtOrdinaryOpportunities()
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="text" style="width:60pt; font:21pt Emoji">😀😀😀😀😀😀😀😀</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;
            var lines = text.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(lines.Count > 1);
            Assert.All(lines.SelectMany(line => line.Words), word =>
                Assert.True(word.Right <= text.ClientRight + 0.5, $"'{word.Text}' overflowed the paragraph"));
        }

        [Theory]
        [InlineData("A©B")]
        [InlineData("1️⃣2️⃣3️⃣")]
        [InlineData("❨A")]
        public async Task Normal_NonBreakingEmojiClassesDoNotGainEmojiWrapOpportunities(string value)
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="text" style="width:12pt; font-size:18pt">{{value}}</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;

            Assert.Equal(1, LinesWithText(text));
            Assert.Contains(WordsOf(text), word => word.Right > text.ClientRight + 0.5);
        }

        [Theory]
        [InlineData("😀)")]
        [InlineData("(😀")]
        [InlineData("$😀")]
        [InlineData("😀%")]
        public async Task Normal_EmojiRemainAttachedToProhibitivePunctuation(string value)
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="text" style="width:12pt; font-size:18pt">{{value}}</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;

            Assert.Equal(1, LinesWithText(text));
        }

        [Theory]
        [InlineData("👍", "🏽")]
        [InlineData("✌", "\uFE0F")]
        [InlineData("👩‍", "💻")]
        [InlineData("🇺", "🇸")]
        [InlineData("ᄀ", "ᅡ")]
        public async Task Anywhere_InlineMarkupDoesNotSplitEmojiGraphemes(string before, string after)
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="text" style="width:12pt; font-size:18pt; overflow-wrap:anywhere">{{before}}<span>{{after}}</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;

            Assert.Equal(1, LinesWithText(text));
        }

        [Fact]
        public async Task Anywhere_MultiOwnerZwjSequenceRemainsOneGrapheme()
        {
            var html = LayoutHarness.Wrap("""
                <p id="text" style="width:12pt; font-size:18pt; overflow-wrap:anywhere"><span>👩</span><span>‍</span><span>💻</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;

            Assert.Equal(1, LinesWithText(text));
        }

        [Fact]
        public async Task RegionalIndicatorParityContinuesAcrossInlineOwners()
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.ColorEmojiSequences));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: EmojiSequences; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="text" style="font:18pt EmojiSequences"><span>🇺</span><span id="second">🇸🇯🇵</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var second = LayoutHarness.FindById(root, "second")!;
            var words = WordsOf(second).Where(word => !string.IsNullOrEmpty(word.Text)).ToList();

            Assert.Equal(["🇸", "🇯🇵"], words.Select(word => word.Text));
        }

        [Fact]
        public async Task RegionalIndicatorParityIncludesNestedPredecessorText()
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.ColorEmojiSequences));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: EmojiSequences; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p style="font:18pt EmojiSequences"><span><i>🇺</i><i>🇸</i></span><span id="next">🇯🇵</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var next = LayoutHarness.FindById(root, "next")!;
            var word = Assert.Single(WordsOf(next), word => !string.IsNullOrEmpty(word.Text));

            Assert.Equal("🇯🇵", word.Text);
        }

        [Fact]
        public async Task RegionalIndicatorParityDoesNotCrossBlockBoundaries()
        {
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.ColorEmojiSequences));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: EmojiSequences; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p style="font:18pt EmojiSequences">🇺</p>
                <p id="next" style="font:18pt EmojiSequences">🇸🇯🇵</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var next = LayoutHarness.FindById(root, "next")!;
            var words = WordsOf(next).Where(word => !string.IsNullOrEmpty(word.Text)).ToList();

            Assert.Equal(["🇸🇯", "🇵"], words.Select(word => word.Text));
        }

        [Fact]
        public async Task ReplacedElementResetsRegionalIndicatorParity()
        {
            const string png =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.ColorEmojiSequences));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: EmojiSequences; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p style="font:18pt EmojiSequences">🇺<img src="data:image/png;base64,{{png}}"><span id="next">🇯🇵</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var next = LayoutHarness.FindById(root, "next")!;
            var word = Assert.Single(WordsOf(next), word => !string.IsNullOrEmpty(word.Text));

            Assert.Equal("🇯🇵", word.Text);
        }

        [Fact]
        public async Task SegoeEmojiSequence_MatchesBrowserLineBreaks()
        {
            if (!OperatingSystem.IsWindows()) return;

            var html = LayoutHarness.Wrap("""
                <div id="text" style="box-sizing:border-box;width:330px;font-family:Calibri,Noto Color Emoji,Noto Emoji,Segoe UI Emoji,Segoe UI Symbol,sans-serif;font-size:24px;color:#16324f;text-align:left;font-weight:bold;overflow-wrap:anywhere;white-space:pre-wrap;">Northline Office B.V. <span style="color:rgb(31, 31, 31);font-size:28px;font-weight:400">🥰💀✌️🌴🐢🐐🍄⚽🍻👑📸😬👀🚨🏡🕊️🏆😻🌟🧿🍀🎨🍜</span> sl;fasdfaslfkl;askdfl;asdkfl;kasf;kasd;f; asdl;fkl;as fkl;askfl;sk l;asdfk l;asdk l;asdkfl; askf;laskfl;kl; asfkasdfl;</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var text = LayoutHarness.FindById(root, "text")!;
            var lines = text.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            var firstLineText = string.Concat(lines[0].Words.Select(word => word.Text));
            Assert.True(firstLineText.EndsWith("🥰💀✌️", StringComparison.Ordinal),
                string.Join(" | ", lines.Select(line => string.Join(", ", line.Words.Select(word =>
                    $"{word.Text}({word.Left:F2}+{word.Width:F2}={word.Right:F2})")))));
            Assert.Equal("🌴🐢🐐🍄⚽🍻👑📸", string.Concat(lines[1].Words.Select(word => word.Text)));
            Assert.Equal("😬👀🚨🏡🕊️🏆😻🌟", string.Concat(lines[2].Words.Select(word => word.Text)));
            Assert.StartsWith("🧿🍀🎨🍜", string.Concat(lines[3].Words.Select(word => word.Text)));
            Assert.DoesNotContain(lines[3].Words, word => word.Text?.StartsWith("sl;", StringComparison.Ordinal) == true);
        }

        [Fact]
        public async Task AdjacentInlineSpan_WrapsAtEmergencyBoundaryWhenNoGraphemeFitsRemainingSpace()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:56pt; font-size:16pt; overflow-wrap:anywhere">MMMM<span>WWWWWW</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(textLines.Count > 1);
            Assert.DoesNotContain(textLines[0].Words, word => word.Text?.Contains('W') == true);
            Assert.Contains(textLines[1].Words, word => word.Text?.Contains('W') == true);
        }

        [Fact]
        public async Task FontFallbackFragment_CanUseEmergencyBreakAtSuppressedBoundary()
        {
            var latinFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Latin; src: url('data:font/truetype;base64,{{latinFont}}') format('truetype'); }
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="p" style="width:56pt; font:16pt Latin, Emoji; overflow-wrap:anywhere">MMMM😀😀😀😀😀😀</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.DoesNotContain(textLines[0].Words, word => word.Text?.Contains("😀", StringComparison.Ordinal) == true);
            Assert.Contains(textLines.Skip(1).SelectMany(line => line.Words),
                word => word.Text?.Contains("😀", StringComparison.Ordinal) == true);
            Assert.All(textLines.SelectMany(line => line.Words), word =>
                Assert.True(word.Right <= paragraph.ClientRight + 0.5, $"'{word.Text}' overflowed the paragraph"));
        }

        [Fact]
        public async Task WhitespaceBeforeFontFallbackSequence_HasPriority()
        {
            var latinFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Latin; src: url('data:font/truetype;base64,{{latinFont}}') format('truetype'); }
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="p" style="width:75pt; font:16pt Latin, Emoji; overflow-wrap:anywhere">prefix <span>MMMM😀😀😀😀</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstLine = paragraph.LineBoxes.First(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

            Assert.DoesNotContain(firstLine.Words, word => word.Text?.Contains('M') == true);
            Assert.True(paragraph.LineBoxes.Count(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))) > 1);
        }

        [Fact]
        public async Task VerticalWhitespaceBeforeFontFallbackSequence_HasPriority()
        {
            var latinFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Latin; src: url('data:font/truetype;base64,{{latinFont}}') format('truetype'); }
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <p id="p" style="width:80pt; height:75pt; writing-mode:vertical-rl; font:16pt Latin, Emoji; overflow-wrap:anywhere">prefix <span>MMMM😀😀😀😀</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.DoesNotContain(paragraph.LineBoxes[0].Words, word => word.Text?.Contains('M') == true);
            Assert.True(paragraph.LineBoxes.Count > 1);
        }

        [Fact]
        public async Task WhitespaceBeforeFreshLineFittingSpan_RemainsPreferredWrapOpportunity()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:75pt; font-size:16pt; overflow-wrap:anywhere">prefix <span id="word">abcdef</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstLine = paragraph.LineBoxes.First(line => line.Words.Any(rect => !string.IsNullOrEmpty(rect.Text)));

            Assert.DoesNotContain(firstLine.Words, rect => rect.Text?.Contains('a') == true);
            Assert.Contains(paragraph.LineBoxes.SelectMany(line => line.Words), rect => rect.Text?.Contains('a') == true);
        }

        [Fact]
        public async Task TrailingWhitespace_DoesNotMakeFreshLineFittingWordOverlong()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:50pt; font-size:16pt; overflow-wrap:anywhere">xx abcdef </p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstLine = paragraph.LineBoxes.First(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

            Assert.DoesNotContain(firstLine.Words, word => word.Text?.Contains('a') == true);
        }

        [Fact]
        public async Task HyphenBeforeFreshLineFittingText_RemainsPreferredWrapOpportunity()
        {
            var html = LayoutHarness.Wrap("""
                <p id="p" style="width:75pt; font-size:16pt; overflow-wrap:anywhere"><span>prefix-</span>abcdef</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstLine = paragraph.LineBoxes.First(line => line.Words.Any(rect => !string.IsNullOrEmpty(rect.Text)));

            Assert.DoesNotContain(firstLine.Words, rect => rect.Text?.Contains('a') == true);
        }

        [Fact]
        public async Task FreshLineFit_UsesTheProspectiveLinesFloatConstraint()
        {
            var html = LayoutHarness.Wrap("""
                <div style="width:100pt">
                    <div style="float:right; width:60pt; height:10pt"></div>
                    <p id="p" style="margin:0; font-size:16pt; overflow-wrap:anywhere">xx abcdefgh</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(textLines.Count > 1);
            Assert.DoesNotContain(textLines[0].Words, word => word.Text?.Contains('a') == true);
            Assert.Contains(textLines[1].Words, word => word.Text == "abcdefgh");
        }

        [Fact]
        public async Task FreshLineFit_UsesNormalStyleAfterFirstLine()
        {
            var html = LayoutHarness.Wrap("""
                <style>p::first-line { font-size:28pt }</style>
                <p id="p" style="width:70pt; font-size:16pt; overflow-wrap:anywhere">xx abcdef</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line =>
                line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(textLines.Count > 1);
            Assert.DoesNotContain(textLines[0].Words, word => word.Text?.Contains('a') == true);
            Assert.Contains(textLines[1].Words, word => word.Text == "abcdef");
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task OverlongInlineSpan_RespectsVerticalOrdinaryWrapPriority(string separator)
        {
            var html = LayoutHarness.Wrap($$"""
                <p id="p" style="writing-mode:vertical-rl; width:80pt; height:100pt; font-size:16pt; overflow-wrap:anywhere">prefix{{separator}}<span>abcdefghijklmnop</span></p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var firstColumn = paragraph.LineBoxes[0];

            if (separator.Length == 0)
                Assert.Contains(firstColumn.Words, rect => rect.Text?.Contains('a') == true);
            else
                Assert.DoesNotContain(firstColumn.Words, rect => rect.Text?.Contains('a') == true);
            Assert.True(paragraph.LineBoxes.Count > 1);
        }

        [Fact]
        public async Task PropertyInheritsIntoInlineText()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; overflow-wrap:anywhere'>
                    <em id='word'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;

            Assert.Equal(OverflowWrap.Anywhere, word.OverflowWrap.Value);
            Assert.True(LinesWithText(paragraph) > 1);
        }

        [Fact]
        public async Task NoWrap_DisablesEmergencyWrapping()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; white-space:nowrap; overflow-wrap:anywhere'>{LongWord}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(1, LinesWithText(paragraph));
            Assert.Contains(WordsOf(paragraph), word => word.Right > paragraph.ClientRight + 0.5);
        }

        [Theory]
        [InlineData("nowrap")]
        [InlineData("pre")]
        public async Task NestedNonWrappingRun_DoesNotReduceItsParentsMinContentWidth(string whiteSpace)
        {
            var html = LayoutHarness.Wrap($@"
                <div style='display:grid; grid-template-columns:min-content'>
                    <div id='item'>
                        <span id='run' style='white-space:{whiteSpace}; overflow-wrap:anywhere'>{LongWord}</span>
                    </div>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var item = LayoutHarness.FindById(root, "item")!;
            var run = LayoutHarness.FindById(root, "run")!;
            var word = Assert.Single(WordsOf(run));

            Assert.True(item.ClientRight + 0.5 >= word.Right,
                $"the {whiteSpace} run overflowed its min-content-sized parent: {word.Right} > {item.ClientRight}");
            Assert.True(item.ActualWidth + 0.5 >= word.Width,
                $"the parent was narrower than the unbreakable word: {item.ActualWidth} < {word.Width}");
        }

        [Fact]
        public async Task OrdinaryLayout_DoesNotEagerlyMeasureEveryAnywhereGraphemeForMinContent()
        {
            var text = string.Join(' ', Enumerable.Repeat("ninechars", 100));
            var html = LayoutHarness.Wrap($"<p id='p' style='width:400pt; overflow-wrap:anywhere'>{text}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var words = WordsOf(LayoutHarness.FindById(root, "p")!).ToList();

            Assert.True(words.Count >= 100);
            Assert.All(words, word => Assert.Null(word.OverflowWrapMinWidth));
        }

        [Fact]
        public async Task CombiningSequence_RemainsOneGraphemeAtEveryEmergencyBreak()
        {
            const string grapheme = "a\u0301";
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:18pt; overflow-wrap:anywhere'>{string.Concat(Enumerable.Repeat(grapheme, 12))}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.All(paragraph.Words, word =>
                Assert.False(word.Text?.StartsWith('\u0301') == true, "a line began with a detached combining mark"));
        }

        [Fact]
        public async Task AnywhereButNotBreakWord_ReducesMinContentWidth()
        {
            static async Task<double> GridItemWidth(string value)
            {
                var html = LayoutHarness.Wrap($@"
                    <div style='display:grid; grid-template-columns:min-content'>
                        <span id='item' style='overflow-wrap:{value}'>{LongWord}</span>
                    </div>");
                var (root, _) = await LayoutHarness.LayoutAsync(html);
                return LayoutHarness.FindById(root, "item")!.ActualWidth;
            }

            var anywhere = await GridItemWidth("anywhere");
            var breakWord = await GridItemWidth("break-word");

            Assert.True(anywhere < breakWord / 2,
                $"anywhere should contribute grapheme opportunities to min-content: {anywhere} vs {breakWord}");
        }

        [Fact]
        public async Task AdjacentInlineElements_ContributeOneUnbreakableMinContentRun()
        {
            var html = LayoutHarness.Wrap("""
                <div style="display:grid; grid-template-columns:min-content">
                    <div id="item" style="font-size:16pt"><span>abcdefgh</span>ijklmnop</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var item = LayoutHarness.FindById(root, "item")!;
            var words = WordsOf(item).ToList();

            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.True(word.Right <= item.ClientRight + 0.5,
                $"'{word.Text}' overflowed the min-content-sized item"));
        }

        [Fact]
        public async Task AnywhereSpanEndsItsMinContentRunBeforeFollowingNormalText()
        {
            static async Task<double> GridItemWidth(string content)
            {
                var html = LayoutHarness.Wrap($$"""
                    <div style="display:grid; grid-template-columns:min-content">
                        <div id="item" style="font-size:16pt">{{content}}</div>
                    </div>
                    """);
                var (root, _) = await LayoutHarness.LayoutAsync(html);
                return LayoutHarness.FindById(root, "item")!.ActualWidth;
            }

            var mixed = await GridItemWidth("""<span style="overflow-wrap:anywhere">iiiiiiii</span>WWWW""");
            var suffixOnly = await GridItemWidth("WWWW");

            Assert.Equal(suffixOnly, mixed, 0.5);
        }

        [Fact]
        public async Task AnywhereDoesNotSplitCrossInlineGraphemeForMinContent()
        {
            static async Task<double> GridItemWidth(string content)
            {
                var html = LayoutHarness.Wrap($$"""
                    <div style="display:grid; grid-template-columns:min-content">
                        <div id="item" style="font-size:18pt; overflow-wrap:anywhere">{{content}}</div>
                    </div>
                    """);
                var (root, _) = await LayoutHarness.LayoutAsync(html);
                return LayoutHarness.FindById(root, "item")!.ActualWidth;
            }

            var crossInline = await GridItemWidth("👍<span>🏽</span>");
            var singleRun = await GridItemWidth("👍🏽");

            Assert.Equal(singleRun, crossInline, 0.5);
        }

        [Fact]
        public async Task AnywhereSyntheticFontFragmentsContributeIndependentMinContentGraphemes()
        {
            var latinFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var emojiFont = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));
            var html = LayoutHarness.Wrap($$"""
                <style>
                    @font-face { font-family: Latin; src: url('data:font/truetype;base64,{{latinFont}}') format('truetype'); }
                    @font-face { font-family: Emoji; src: url('data:font/truetype;base64,{{emojiFont}}') format('truetype'); }
                </style>
                <div style="display:grid; grid-template-columns:min-content">
                    <div id="item" style="font:18pt Latin, Emoji; overflow-wrap:anywhere">i😀i😀</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var item = LayoutHarness.FindById(root, "item")!;
            var widestGrapheme = WordsOf(item).Max(word => word.Width);

            Assert.True(item.ActualWidth <= widestGrapheme + 0.5,
                $"min-content width {item.ActualWidth} exceeded the widest grapheme {widestGrapheme}");
        }

        [Fact]
        public async Task AnywhereReducesAnAutoTablesMinContentWidth()
        {
            var html = LayoutHarness.Wrap($@"
                <div id='container' style='width:70pt'>
                    <table id='table' style='border-spacing:0'>
                        <tr><td id='cell' style='padding:0; overflow-wrap:anywhere'>{LongWord}</td></tr>
                    </table>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "container")!;
            var table = LayoutHarness.FindById(root, "table")!;
            var cell = LayoutHarness.FindById(root, "cell")!;

            Assert.True(table.ActualWidth <= container.ActualWidth + 0.5,
                $"the auto table exceeded its available width: {table.ActualWidth} > {container.ActualWidth}");
            Assert.All(WordsOf(cell), word => Assert.True(word.Right <= cell.ClientRight + 0.5,
                $"'{word.Text}' overflowed the intrinsically-sized cell"));
        }

        [Fact]
        public async Task VerticalWritingMode_UsesEmergencyBreaksAlongItsInlineAxis()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='writing-mode:vertical-rl; width:80pt; height:70pt; overflow-wrap:anywhere'>
                    {LongWord}
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.All(WordsOf(paragraph), word =>
                Assert.True(word.Bottom <= paragraph.ClientBottom + 0.5,
                    $"'{word.Text}' overflowed {word.Bottom} > {paragraph.ClientBottom}"));
        }

        [Fact]
        public async Task FreshRelayout_RestoresAndRechoosesEmergencySplits()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; overflow-wrap:anywhere'>{LongWord}</p>");

            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(html, 2, (root, _) =>
            {
                var paragraph = LayoutHarness.FindById(root, "p")!;
                return string.Join('|', paragraph.LineBoxes.Select(line =>
                    string.Concat(line.Words.Select(word => word.Text))));
            });

            Assert.Equal(snapshots[0], snapshots[1]);
            Assert.Contains('|', snapshots[0]);
        }

        private static int LinesWithText(CssBox box) =>
            box.LineBoxes.Count(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

        private static System.Collections.Generic.IEnumerable<CssRect> WordsOf(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(descendant => descendant.Words);
    }
}
