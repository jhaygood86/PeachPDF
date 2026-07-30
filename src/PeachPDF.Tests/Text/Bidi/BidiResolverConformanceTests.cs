using PeachPDF.Text.Bidi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PeachPDF.Tests.Text.Bidi
{
    /// <summary>
    /// Runs <see cref="BidiResolver"/> against Unicode's own official conformance suite,
    /// <c>BidiCharacterTest.txt</c> (Unicode 17.0.0, linked from <c>assets/unicode/</c> - see
    /// <c>assets/unicode/generate_bidi_tables.py</c>), rather than a hand-picked subset: the real file is
    /// only ~92k test cases and runs in one pass well within a normal test timeout, so there is no reason
    /// to trust a smaller curated slice when the authoritative one is available. Per the file's own header,
    /// each line is one paragraph/one line of text, so this exercises the algorithm through rule L2
    /// inclusively (L3/L4 are explicitly out of scope of this file, and covered by
    /// <see cref="BidiMirrorResolverTests"/> instead).
    /// </summary>
    public class BidiResolverConformanceTests
    {
        private sealed record TestCase(int[] Codepoints, int ParagraphDirectionField, int ExpectedParagraphLevel, int?[] ExpectedLevels, int[] ExpectedVisualOrder);

        private static IEnumerable<TestCase> ReadTestCases()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "BidiCharacterTest.txt");
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;

                var fields = line.Split(';');
                if (fields.Length != 5) continue;

                var codepoints = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    .ToArray();
                var direction = int.Parse(fields[1], CultureInfo.InvariantCulture);
                var paragraphLevel = int.Parse(fields[2], CultureInfo.InvariantCulture);
                var levels = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t == "x" ? (int?)null : int.Parse(t, CultureInfo.InvariantCulture))
                    .ToArray();
                var visualOrder = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
                    .ToArray();

                yield return new TestCase(codepoints, direction, paragraphLevel, levels, visualOrder);
            }
        }

        private static string ToText(int[] codepoints)
        {
            var sb = new StringBuilder(codepoints.Length);
            foreach (var cp in codepoints) sb.Append((System.Text.Rune)cp);
            return sb.ToString();
        }

        [Fact]
        public void Resolve_AgainstOfficialConformanceSuite_MatchesLevelsAndVisualOrder()
        {
            var total = 0;
            var levelFailures = new List<string>();
            var orderFailures = new List<string>();

            foreach (var testCase in ReadTestCases())
            {
                total++;
                var text = ToText(testCase.Codepoints);

                var direction = testCase.ParagraphDirectionField switch
                {
                    0 => BidiParagraphDirection.Ltr,
                    1 => BidiParagraphDirection.Rtl,
                    _ => BidiParagraphDirection.Auto
                };

                var result = BidiResolver.Resolve(text, direction);

                if (result.ParagraphLevel != testCase.ExpectedParagraphLevel)
                {
                    if (levelFailures.Count < 20)
                        levelFailures.Add($"[{string.Join(' ', testCase.Codepoints.Select(c => c.ToString("X")))}] paragraph level {result.ParagraphLevel} != expected {testCase.ExpectedParagraphLevel}");
                    continue;
                }

                // Codepoints are BMP-only in this file's actual data (verified: no surrogate pairs appear
                // in BidiCharacterTest.txt), so codepoint index == UTF-16 char index here.
                var levelMismatch = false;
                for (var i = 0; i < testCase.ExpectedLevels.Length; i++)
                {
                    var expected = testCase.ExpectedLevels[i];
                    if (expected is null) continue; // 'x' - X9-removed character, level not checked
                    if (result.Levels[i] != expected.Value) { levelMismatch = true; break; }
                }
                if (levelMismatch)
                {
                    if (levelFailures.Count < 20)
                        levelFailures.Add($"[{string.Join(' ', testCase.Codepoints.Select(c => c.ToString("X")))}] levels {string.Join(' ', result.Levels)} != expected {string.Join(' ', testCase.ExpectedLevels.Select(l => l?.ToString() ?? "x"))}");
                    continue;
                }

                var runs = BidiResolver.ReorderLine(result.Levels, 0, text.Length);
                var visualOrder = new List<int>();
                foreach (var run in runs)
                {
                    if (run.IsRtl)
                    {
                        for (var i = run.Start + run.Length - 1; i >= run.Start; i--)
                            if (testCase.ExpectedLevels[i] is not null) visualOrder.Add(i);
                    }
                    else
                    {
                        for (var i = run.Start; i < run.Start + run.Length; i++)
                            if (testCase.ExpectedLevels[i] is not null) visualOrder.Add(i);
                    }
                }

                if (!visualOrder.SequenceEqual(testCase.ExpectedVisualOrder))
                {
                    if (orderFailures.Count < 20)
                        orderFailures.Add($"[{string.Join(' ', testCase.Codepoints.Select(c => c.ToString("X")))}] visual order {string.Join(' ', visualOrder)} != expected {string.Join(' ', testCase.ExpectedVisualOrder)}");
                }
            }

            var failureCount = levelFailures.Count + orderFailures.Count;
            var message = $"{failureCount} mismatches out of {total} conformance cases.\n" +
                          $"Level failures (first 20): {string.Join("\n", levelFailures)}\n" +
                          $"Order failures (first 20): {string.Join("\n", orderFailures)}";

            Assert.True(levelFailures.Count == 0 && orderFailures.Count == 0, message);
        }
    }
}
