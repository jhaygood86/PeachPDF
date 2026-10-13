using PeachDrawing.Text.Unicode;

namespace PeachDrawing.Text.Tests.Text.Segmentation
{
    /// <summary>
    /// The line breaker keeps its working arrays on the thread for the next text instead of allocating them for every one. The answer
    /// for a text must not depend on what the thread broke before it: a longer text leaves stale entries past a shorter one's end,
    /// and a text past the size limit takes the unshared path.
    /// </summary>
    public class LineBreakScratchBufferTests
    {
        private static readonly string[] Texts =
        [
            "Hello, world! This-is a test/of breaking.",
            "ab",
            string.Join(" ", Enumerable.Range(0, 900).Select(i => "word" + i)),
            "日本語のテキスト、折り返し。",
            "a b",
            new string('x', 5000) + " tail",
            "\U0001F1E9\U0001F1EA\U0001F1EB\U0001F1F7 flags",
            string.Empty,
        ];

        /// <summary>The answer a thread that has broken nothing before gives, which no earlier text can have influenced.</summary>
        private static LineBreakOpportunity[] OnAFreshThread(string text)
        {
            LineBreakOpportunity[]? result = null;
            var thread = new Thread(() => result = LineBreaker.FindOpportunities(text));
            thread.Start();
            thread.Join();

            return result!;
        }

        [Fact]
        public void AnAnswerDoesNotDependOnWhatTheThreadBrokeBefore()
        {
            var expected = Texts.Select(OnAFreshThread).ToArray();

            // Long before short, short before long, and the same text twice, so every text is broken over the leftovers of another.
            foreach (var order in new[] { new[] { 2, 1, 0, 4, 3 }, new[] { 5, 4, 5, 1, 6, 0 }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1, 0 } })
            {
                foreach (var index in order)
                {
                    Assert.Equal(expected[index], LineBreaker.FindOpportunities(Texts[index]));
                }
            }
        }

        [Fact]
        public void ATextPastTheSharedBufferLimitIsStillBrokenCorrectly()
        {
            var text = string.Join(" ", Enumerable.Repeat("lorem ipsum", 1500));

            Assert.True(text.Length > 4096);

            var opportunities = LineBreaker.FindOpportunities(text);

            Assert.Equal(text.Length + 1, opportunities.Length);
            Assert.Equal(OnAFreshThread(text), opportunities);
            Assert.NotEqual(LineBreakOpportunity.Prohibited, opportunities[text.IndexOf(' ') + 1]);
        }
    }
}
