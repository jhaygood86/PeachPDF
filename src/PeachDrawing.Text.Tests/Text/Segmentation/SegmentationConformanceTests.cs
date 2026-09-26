using PeachDrawing.Text.Unicode;
using System.Globalization;
using System.Text;

namespace PeachDrawing.Text.Tests.Text.Segmentation
{
    /// <summary>
    /// Runs the boundary algorithms of UAX #14 and UAX #29 against Unicode's own conformance files (Unicode 18.0.0, linked from
    /// <c>assets/unicode/</c>), every line of them: <c>LineBreakTest.txt</c>, <c>GraphemeBreakTest.txt</c>,
    /// <c>WordBreakTest.txt</c> and <c>SentenceBreakTest.txt</c>. A line is a string written as code points with a division sign
    /// (U+00F7) where a boundary falls and a multiplication sign (U+00D7) where it does not.
    /// </summary>
    public class SegmentationConformanceTests
    {
        private const string Break = "÷";
        private const string NoBreak = "×";

        private sealed record Case(int LineNumber, string Text, int[] Boundaries, string Source);

        private static IEnumerable<Case> Read(string file)
        {
            var path = Path.Combine(AppContext.BaseDirectory, file);
            int lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Length == 0 || line[0] == '#') continue;

                var body = line;
                int comment = body.IndexOf('#');
                if (comment >= 0) body = body.Substring(0, comment);

                var text = new StringBuilder();
                var boundaries = new List<int>();
                foreach (var token in body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token == Break)
                    {
                        boundaries.Add(text.Length);
                    }
                    else if (token != NoBreak)
                    {
                        text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    }
                }

                yield return new Case(lineNumber, text.ToString(), boundaries.ToArray(), line);
            }
        }

        private static void AssertAll(string file, Func<string, int[]> find)
        {
            int total = 0;
            var failures = new List<string>();
            foreach (var test in Read(file))
            {
                total++;
                var actual = find(test.Text);
                if (!actual.SequenceEqual(test.Boundaries) && failures.Count < 10)
                {
                    failures.Add($"line {test.LineNumber}: expected [{string.Join(",", test.Boundaries)}] got [{string.Join(",", actual)}]   {test.Source}");
                }
            }

            Assert.True(total > 500, $"{file} has only {total} cases.");
            Assert.True(failures.Count == 0, $"{file}: {failures.Count}+ of {total} cases fail:\n" + string.Join("\n", failures));
        }

        [Fact]
        public void GraphemeBoundaries_MatchGraphemeBreakTest() =>
            AssertAll("GraphemeBreakTest.txt", text => Segmenter.FindGraphemeBoundaries(text));

        [Fact]
        public void WordBoundaries_MatchWordBreakTest() =>
            AssertAll("WordBreakTest.txt", text => Segmenter.FindWordBoundaries(text));

        [Fact]
        public void SentenceBoundaries_MatchSentenceBreakTest() =>
            AssertAll("SentenceBreakTest.txt", text => Segmenter.FindSentenceBoundaries(text));

        [Fact]
        public void LineBreakOpportunities_MatchLineBreakTest()
        {
            // The conformance file is for the default rules, in which a small kana is a non-starter: the strict setting.
            var options = new LineBreakOptions { Strictness = LineBreakStrictness.Strict };
            AssertAll("LineBreakTest.txt", text =>
            {
                var opportunities = LineBreaker.FindOpportunities(text, options);

                // The file writes the start of the text as a position that does not break, which is index 0.
                Assert.Equal(LineBreakOpportunity.Prohibited, opportunities[0]);

                var breaks = new List<int>();
                for (int i = 1; i < opportunities.Length; i++)
                {
                    if (opportunities[i] != LineBreakOpportunity.Prohibited) breaks.Add(i);
                }

                return breaks.ToArray();
            });
        }
    }
}
