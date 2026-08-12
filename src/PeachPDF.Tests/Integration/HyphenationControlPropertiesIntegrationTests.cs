using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for CSS Text 4's hyphenation control properties (issue #713): <c>hyphenate-character</c>,
    /// <c>hyphenate-limit-chars</c>, <c>hyphenate-limit-lines</c>, <c>hyphenate-limit-last</c>, and
    /// <c>hyphenate-limit-zone</c>, plus the hidden Prince-XML compat aliases. Cascade-only assertions
    /// confirm parsing/storage; the rest lay real narrow, <c>hyphens: auto</c> content out and inspect
    /// the resulting <see cref="CssRect"/>/<see cref="CssLineBox"/> content, per this repo's convention
    /// that a token being accepted is not proof a feature actually changes rendered output.
    /// </summary>
    public class HyphenationControlPropertiesIntegrationTests
    {
        private const string LongWord = "antidisestablishmentarianism";

        // ─── hyphenate-character ────────────────────────────────────────────────

        [Fact]
        public async Task HyphenateCharacter_DefaultsToAuto()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("auto", box.HyphenateCharacter);
        }

        [Fact]
        public async Task HyphenateCharacter_Auto_UsesHyphenMinusGlyph()
        {
            var box = await FindWordsBoxAsync(EnDoc($"<p id='p' style='width:80px;hyphens:auto'>{LongWord}</p>"));

            Assert.True(box.Words.Count > 1, "expected the word to be split into multiple fragments");
            Assert.Contains(box.Words, w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task HyphenateCharacter_CustomString_ReplacesTheGlyphAtEveryBreak()
        {
            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style=\"width:80px;hyphens:auto;hyphenate-character:'='\">{LongWord}</p>"));

            Assert.True(box.Words.Count > 1, "expected the word to be split into multiple fragments");
            Assert.Contains(box.Words, w => w.Text!.EndsWith('='));
            Assert.DoesNotContain(box.Words, w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task HyphenateCharacter_EmptyString_BreaksWithNoVisibleGlyph()
        {
            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-character:\"\"'>{LongWord}</p>"));

            Assert.True(box.Words.Count > 1, "expected the word to be split into multiple fragments");
            Assert.DoesNotContain(box.Words, w => w.Text!.EndsWith('-') || w.Text!.EndsWith('='));
            // The split still happened - the first fragment is a real (shorter) prefix of the word.
            Assert.True(box.Words[0].Text!.Length < LongWord.Length);
        }

        [Theory]
        [InlineData("-prince-hyphenate-character")]
        public async Task PrinceHyphenateCharacter_AliasesTheCanonicalProperty(string aliasName)
        {
            var box = await FindByIdAsync($"<p id='p' style=\"{aliasName}:'x'\">text</p>");
            Assert.Equal("\"x\"", box.HyphenateCharacter);
        }

        // ─── hyphenate-limit-chars ──────────────────────────────────────────────

        [Fact]
        public async Task HyphenateLimitChars_DefaultsToAuto()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("auto", box.HyphenateLimitChars);
        }

        [Fact]
        public async Task HyphenateLimitChars_WordMinimumLargerThanWord_SuppressesHyphenationEntirely()
        {
            // LongWord is 29 characters; a word-minimum of 50 can never be satisfied.
            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-limit-chars:50'>{LongWord}</p>"));

            Assert.Single(box.Words);
            Assert.Equal(LongWord, box.Words[0].Text);
        }

        [Fact]
        public async Task HyphenateLimitChars_BeforeMinimumLargerThanEveryCandidate_SuppressesHyphenationEntirely()
        {
            var candidates = HyphenationEngine.FindHyphenationPoints(LongWord, "en");
            Assert.NotEmpty(candidates);
            var beforeMinimum = candidates.Max() + 1; // larger than any real candidate's own "before" length

            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-limit-chars:auto {beforeMinimum} auto'>{LongWord}</p>"));

            Assert.Single(box.Words);
            Assert.Equal(LongWord, box.Words[0].Text);
        }

        [Fact]
        public async Task HyphenateLimitChars_AfterMinimumLargerThanEveryCandidate_SuppressesHyphenationEntirely()
        {
            var candidates = HyphenationEngine.FindHyphenationPoints(LongWord, "en");
            Assert.NotEmpty(candidates);
            // The largest possible "after" length any candidate can offer is the word's own length minus
            // its smallest candidate offset.
            var afterMinimum = LongWord.Length - candidates.Min() + 1;

            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-limit-chars:auto auto {afterMinimum}'>{LongWord}</p>"));

            Assert.Single(box.Words);
            Assert.Equal(LongWord, box.Words[0].Text);
        }

        [Theory]
        [InlineData("hyphenate-before:9", "auto 9 auto")]
        [InlineData("-prince-hyphenate-before:9", "auto 9 auto")]
        [InlineData("hyphenate-after:9", "auto auto 9")]
        [InlineData("-prince-hyphenate-after:9", "auto auto 9")]
        public async Task PrinceHyphenateBeforeAfter_ComposeIntoHyphenateLimitChars(string declaration, string expected)
        {
            var box = await FindByIdAsync($"<p id='p' style='{declaration}'>text</p>");
            Assert.Equal(expected, box.HyphenateLimitChars);
        }

        [Fact]
        public async Task PrinceHyphenateBeforeThenAfter_ComposeTogetherWithoutClobbering()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphenate-before:3;hyphenate-after:4'>text</p>");
            Assert.Equal("auto 3 4", box.HyphenateLimitChars);
        }

        // hyphenate-before/-after compose into hyphenate-limit-chars's own storage via a read-modify-
        // write custom setter rather than a real independent field (no combined property of its own
        // exists in Prince's model - see HyphenateLimitCharsGrammar's doc comment) - so, unlike two
        // ordinary CSS properties, these two declarations do NOT cascade independently: whichever one
        // appears later in the same rule's declaration list wins outright for the whole shared value,
        // the same "last declaration in source order wins" rule an ordinary property follows against
        // itself. Pinned here so a future change to declaration-application order doesn't silently
        // change which one wins without a test noticing.
        [Fact]
        public async Task PrinceHyphenateBefore_DeclaredBeforeHyphenateLimitChars_IsOverriddenByIt()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphenate-before:9;hyphenate-limit-chars:6 3 2'>text</p>");
            Assert.Equal("6 3 2", box.HyphenateLimitChars);
        }

        [Fact]
        public async Task PrinceHyphenateBefore_DeclaredAfterHyphenateLimitChars_OverridesItsBeforeComponent()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphenate-limit-chars:6 3 2;hyphenate-before:9'>text</p>");
            Assert.Equal("6 9 2", box.HyphenateLimitChars);
        }

        // ─── hyphenate-limit-lines ──────────────────────────────────────────────

        [Fact]
        public async Task HyphenateLimitLines_DefaultsToNoLimit()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.True(box.HyphenateLimitLines.Value is { IsKeyword: true, Keyword: NoLimitKeyword.NoLimit });
        }

        [Theory]
        [InlineData("hyphenate-limit-lines:3")]
        [InlineData("hyphenate-lines:3")]
        [InlineData("-prince-hyphenate-limit-lines:3")]
        public async Task HyphenateLimitLines_IntegerAndPrinceAliases_StoreTheSameValue(string declaration)
        {
            var box = await FindByIdAsync($"<p id='p' style='{declaration}'>text</p>");
            Assert.True(box.HyphenateLimitLines.Value is { IsValue: true, Value: 3 });
        }

        [Fact]
        public async Task HyphenateLimitLines_One_NeverProducesTwoConsecutiveHyphenatedLines()
        {
            var content = string.Join(' ', System.Linq.Enumerable.Repeat(LongWord, 8));

            const string style = "width:100px;font-size:10px;hyphens:auto";

            var unconstrained = await LayoutHarness.LayoutAsync(EnDoc(
                $"<p id='p' style='{style}'>{content}</p>"));
            var unconstrainedLines = HyphenatedLineFlags(LayoutHarness.FindById(unconstrained.Root, "p")!);

            // The scenario must actually exercise the mechanism: without a limit, at least two
            // consecutive lines end up hyphenated back to back.
            Assert.Contains(
                System.Linq.Enumerable.Range(1, unconstrainedLines.Count - 1),
                i => unconstrainedLines[i - 1] && unconstrainedLines[i]);

            var constrained = await LayoutHarness.LayoutAsync(EnDoc(
                $"<p id='p' style='{style};hyphenate-limit-lines:1'>{content}</p>"));
            var constrainedLines = HyphenatedLineFlags(LayoutHarness.FindById(constrained.Root, "p")!);

            for (var i = 1; i < constrainedLines.Count; i++)
            {
                Assert.False(constrainedLines[i - 1] && constrainedLines[i],
                    $"lines {i - 1} and {i} both ended in a hyphen despite hyphenate-limit-lines:1");
            }
        }

        private static System.Collections.Generic.List<bool> HyphenatedLineFlags(CssBox block) =>
            block.LineBoxes
                .Select(line => line.Words.Count > 0 && line.Words[^1].Text is { Length: > 0 } t && t.EndsWith('-'))
                .ToList();

        // ─── hyphenate-limit-last (cascade only - see HyphenateLimitLastEnforcementTests and ───────
        // ─── HyphenateLimitLastPaginationIntegrationTests for its layout effect, issue #723) ─────

        [Fact]
        public async Task HyphenateLimitLast_DefaultsToNone()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal(HyphenateLimitLast.None, box.HyphenateLimitLast.Value);
        }

        [Theory]
        [InlineData("always")]
        [InlineData("column")]
        [InlineData("page")]
        [InlineData("spread")]
        public async Task HyphenateLimitLast_ParsesEveryKeyword(string value)
        {
            var box = await FindByIdAsync($"<p id='p' style='hyphenate-limit-last:{value}'>text</p>");
            Assert.Equal(value, box.HyphenateLimitLast.Value.ToString().ToLowerInvariant());
        }

        [Fact]
        public async Task HyphenateLimitLast_IsInherited()
        {
            var html = Wrap("<div style='hyphenate-limit-last:always'><p id='p'>text</p></div>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var box = LayoutHarness.FindById(root, "p")!;
            Assert.Equal(HyphenateLimitLast.Always, box.HyphenateLimitLast.Value);
        }

        // ─── hyphenate-limit-zone ───────────────────────────────────────────────

        [Fact]
        public async Task HyphenateLimitZone_DefaultsToZero()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("0", box.HyphenateLimitZone);
        }

        [Fact]
        public async Task HyphenateLimitZone_ExplicitZero_StillHyphenates()
        {
            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-limit-zone:0'>{LongWord}</p>"));

            Assert.True(box.Words.Count > 1, "expected the word to be split into multiple fragments");
        }

        [Fact]
        public async Task HyphenateLimitZone_LargerThanTheLineBox_PrefersAnUnhyphenatedWrapOverAnySplit()
        {
            // A zone wider than the line box itself means the unfilled space left by NOT hyphenating
            // never exceeds it, so a hyphenation attempt is never worth making.
            var box = await FindWordsBoxAsync(EnDoc(
                $"<p id='p' style='width:80px;hyphens:auto;hyphenate-limit-zone:1000pt'>{LongWord}</p>"));

            Assert.Single(box.Words);
            Assert.Equal(LongWord, box.Words[0].Text);
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static string EnDoc(string body) =>
            $"<!DOCTYPE html><html lang=\"en\"><head></head><body style='margin:0'>{body}</body></html>";

        private static string Wrap(string body) => LayoutHarness.Wrap(body);

        private static async Task<CssBox> FindByIdAsync(string bodyFragment)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Wrap(bodyFragment));
            return LayoutHarness.FindById(root, "p")!;
        }

        private static async Task<CssBox> FindWordsBoxAsync(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var element = LayoutHarness.FindById(root, "p")!;
            return element.Words.Count > 0 ? element : element.Boxes.First(b => b.Words.Count > 0);
        }
    }
}
