using PeachDrawing.Text.Internal.Text.Segmentation;
using PeachDrawing.Text.Unicode;
using System.Diagnostics;
using System.Text;

namespace PeachDrawing.Text.Tests.Text.Segmentation
{
    /// <summary>
    /// Thai and Khmer line breaking through a word list. A fixture writes the text with a <c>|</c> wherever a line may
    /// end. The sentences are ordinary prose, and where a test says "Chrome" its breaks are the ones Chrome's line breaker gives for
    /// the same text (measured by laying the text out in a container of no width, which breaks at every opportunity).
    /// </summary>
    public class DictionaryLineBreakingTests
    {
        internal static string Render(string text, LineBreakOptions options = default)
        {
            var opportunities = LineBreaker.FindOpportunities(text, options);
            var result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && opportunities[i] != LineBreakOpportunity.Prohibited)
                {
                    result.Append('|');
                }

                result.Append(text[i]);
            }

            return result.ToString();
        }

        [Theory]
        // Thai
        [InlineData("ฉัน|รัก|ภาษา|ไทย")]
        [InlineData("วัน|นี้|อากาศ|ดี|มาก")]
        [InlineData("ประเทศไทย|มี|ประชากร|มากกว่า|หก|สิบ|ล้าน|คน")]
        [InlineData("เขา|ไป|ตลาด|เพื่อ|ซื้อ|ผล|ไม้|และ|ผัก")]
        [InlineData("กรุงเทพมหานคร|เป็น|เมือง|หลวง|ของ|ประเทศไทย")]
        [InlineData("ฉัน|จะ|ไป|โรงเรียน|พรุ่ง|นี้|เช้า")]
        [InlineData("ผู้|ใช้|สามารถ|เลือก|ภาษา|ที่|ต้องการ|ได้")]
        [InlineData("การ|ประชุม|จะ|เริ่ม|ใน|เวลา|เก้า|โมง|เช้า")]
        [InlineData("เด็กๆ|ชอบ|เล่น|ฟุตบอล|ใน|สวน|สาธารณะ")]
        // Khmer
        [InlineData("ខ្ញុំ|ស្រលាញ់|ភាសាខ្មែរ")]
        [InlineData("កម្ពុជា|ជា|ប្រទេស|មួយ|នៅ|អាស៊ី|អាគ្នេយ៍")]
        [InlineData("អាហារ|ខ្មែរ|មាន|រសជាតិ|ឆ្ងាញ់|ណាស់")]
        public void Sentences_BreakWhereChromeBreaksThem(string expected)
        {
            Assert.Equal(expected, Render(expected.Replace("|", "")));
        }

        [Theory]
        // Where the list decides and Chrome does not agree: Chrome splits a compound the list has as one word (school, each, in) and keeps
        // "Cambodia" whole though the list has it as two words.
        [InlineData("ប្រទេស|កម្ពុជា|មាន|ប្រជាជន|ច្រើន")]
        [InlineData("ខ្ញុំ|ទៅ|សាលារៀន|រៀងរាល់ថ្ងៃ")]
        [InlineData("រាជធានី|ភ្នំពេញ|ជាទី|ក្រុង|ធំ|បំផុត|នៅក្នុង|ប្រទេស")]
        [InlineData("ធ្វើការ")]
        [InlineData("ក្រុមហ៊ុន")]
        public void WhereChromeDiffers_TheListDecides(string expected)
        {
            Assert.Equal(expected, Render(expected.Replace("|", "")));
        }

        [Fact]
        public void LongestMatchWins_WhenTheRestCanBeCoveredEitherWay()
        {
            // Both "ตาก|ลม" and "ตา|กลม" cover the text with two words; the list also has the whole as a word.
            Assert.Equal("ตากลม", Render("ตากลม"));

            // Without the compound the longer first word is the choice ("ตาก" over "ตา").
            Assert.Equal("ตากลม|ฉัน", Render("ตากลมฉัน"));
        }

        [Fact]
        public void ALookaheadCoversTheTextBetterThanAGreedyMatch()
        {
            // "ประเทศไทย" is longer than "ประเทศ" and covers everything up to "มี"; the words after it are found either way.
            Assert.Equal("ประเทศไทย|มี", Render("ประเทศไทยมี"));
        }

        [Fact]
        public void AStretchNoWordMatches_StaysWhole_AndIsSetOffFromTheWordsAroundIt()
        {
            // These three letters start no word of the Thai list.
            Assert.Equal("ฉัน|ฃฅฦ|รัก", Render("ฉันฃฅฦรัก"));
            Assert.Equal("ฃฅฦ", Render("ฃฅฦ"));
            Assert.Equal("ฉัน|ฃฅฦ", Render("ฉันฃฅฦ"));
            Assert.Equal("ฃฅฦ|รัก", Render("ฃฅฦรัก"));
        }

        [Fact]
        public void LatinNextToThai_IsNotBrokenFromIt()
        {
            Assert.Equal("ไทยworld", Render("ไทยworld"));
            Assert.Equal("worldไทย|ไทย", Render("worldไทยไทย"));
        }

        [Theory]
        [InlineData("ไทย123ไทย", "ไทย123ไทย")]
        [InlineData("ภาษา.ไทย", "ภาษา.ไทย")]
        [InlineData("(ภาษาไทย)", "(ภาษา|ไทย)")]
        [InlineData("ภาษา\u200Bภาษาไทย", "ภาษา\u200B|ภาษา|ไทย")]
        [InlineData("ภาษา ไทยภาษาไทย", "ภาษา |ไทย|ภาษา|ไทย")]
        [InlineData("ภาษา-ไทยภาษาไทย", "ภาษา-|ไทย|ภาษา|ไทย")]
        [InlineData("ไทย๑๒๓ภาษา", "ไทย๑๒๓ภาษา")]
        [InlineData("ๆภาษา", "ๆ|ภาษา")]
        [InlineData("ก็ตาม", "ก็ตาม")]
        [InlineData("ខ្ញុំ\u200Bស្រលាញ់ភាសាខ្មែរ", "ខ្ញុំ\u200B|ស្រលាញ់|ភាសាខ្មែរ")]
        public void TextAroundARun_IsBrokenAsTheAlgorithmBreaksIt(string text, string expected)
        {
            Assert.Equal(expected, Render(text));
        }

        [Fact]
        public void AJoiner_DoesNotTakePartInMatching_AndNoWordStartsRightAfterOne()
        {
            // Chrome keeps these whole.
            Assert.Equal("ภาษา\u200Cไทย", Render("ภาษา\u200Cไทย"));
            Assert.Equal("ภาษา\u200Dไทย", Render("ภาษา\u200Dไทย"));

            // A word the list has is found through a joiner inside it, and the words after it are still found.
            Assert.Equal("ភា\u200Cសា|ខ្ញុំ", Render("ភា\u200Cសាខ្ញុំ"));
            Assert.Equal("ខ្ញុំ\u200Cស្រលាញ់|ភាសា", Render("ខ្ញុំ\u200Cស្រលាញ់ភាសា"));
        }

        [Fact]
        public void UnassignedAndLoneCharactersOfTheBlock_BreakNothing()
        {
            // U+0E3B is unassigned: it ends a run, and the letters next to it keep their LB1 class (a letter).
            Assert.Equal("ภาษา\u0E3Bไทย", Render("ภาษา\u0E3Bไทย"));

            // A lone mark, or a mark after a space, is a letter in rule LB10 and starts no word.
            Assert.Equal("\u0E31", Render("\u0E31"));
            Assert.Equal("\u0E01 |\u0E31\u0E01", Render("\u0E01 \u0E31\u0E01"));
        }

        [Fact]
        public void TheWordBreakOfTheDictionary_IsAnOpportunityLikeAnyOther()
        {
            var text = "ฉันรักภาษาไทย";
            var opportunities = LineBreaker.FindOpportunities(text);

            Assert.Equal(LineBreakOpportunity.Allowed, opportunities[3]);
            Assert.Equal(LineBreakOpportunity.Allowed, opportunities[6]);
            Assert.Equal(LineBreakOpportunity.Allowed, opportunities[10]);
            Assert.Equal(LineBreakOpportunity.Prohibited, opportunities[0]);
            Assert.Equal(LineBreakOpportunity.Prohibited, opportunities[1]);
            Assert.Equal(LineBreakOpportunity.Mandatory, opportunities[text.Length]);
        }

        // ---- options ----------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("ฉันรักภาษาไทย")]
        [InlineData("ខ្ញុំស្រលាញ់ភាសាខ្មែរ")]
        public void GeneralCategory_GivesNoOpportunityInsideARun(string text)
        {
            Assert.Equal(text, Render(text, new LineBreakOptions { ComplexContext = ComplexContextBreaking.GeneralCategory }));
            Assert.NotEqual(text, Render(text));
        }

        [Fact]
        public void TheDictionaryIsTheDefault()
        {
            Assert.Equal(ComplexContextBreaking.Dictionary, default(LineBreakOptions).ComplexContext);
            Assert.Equal(Render("ฉันรักภาษาไทย"), Render("ฉันรักภาษาไทย", new LineBreakOptions { ComplexContext = ComplexContextBreaking.Dictionary }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("th")]
        [InlineData("en")]
        [InlineData("ja")]
        [InlineData("km-KH")]
        public void TheScriptDecidesNotTheLanguage(string? language)
        {
            foreach (var strictness in new[] { LineBreakStrictness.Auto, LineBreakStrictness.Loose, LineBreakStrictness.Normal, LineBreakStrictness.Strict })
            {
                var options = new LineBreakOptions { Language = language, Strictness = strictness };
                Assert.Equal("ฉัน|รัก|ภาษา|ไทย", Render("ฉันรักภาษาไทย", options));
                Assert.Equal("ខ្ញុំ|ស្រលាញ់|ភាសាខ្មែរ", Render("ខ្ញុំស្រលាញ់ភាសាខ្មែរ", options));
            }
        }

        [Fact]
        public void KeepAll_StillBreaksBetweenTheWordsOfAScriptWithoutSpaces()
        {
            // As in Chrome: the words of a run are not "words" keep-all could join, the dictionary made them.
            Assert.Equal("ผู้|ใช้|สามารถ|เลือก|ภาษา", Render("ผู้ใช้สามารถเลือกภาษา", new LineBreakOptions { WordBreak = WordBreakMode.KeepAll }));
        }

        [Fact]
        public void BreakAll_BreaksBetweenEveryCluster_NeverInsideOne()
        {
            Assert.Equal("ผู้|ใ|ช้", Render("ผู้ใช้", new LineBreakOptions { WordBreak = WordBreakMode.BreakAll }));
        }

        [Fact]
        public void Anywhere_BreaksBetweenEveryGraphemeCluster()
        {
            Assert.Equal("ผู้|ใ|ช้", Render("ผู้ใช้", new LineBreakOptions { Strictness = LineBreakStrictness.Anywhere }));
        }

        [Fact]
        public void AHardBreakIsStillMandatory()
        {
            var opportunities = LineBreaker.FindOpportunities("ฉันรัก\nภาษาไทย");
            Assert.Equal(LineBreakOpportunity.Mandatory, opportunities[7]);
            Assert.Equal(LineBreakOpportunity.Allowed, opportunities[3]);
            Assert.Equal(LineBreakOpportunity.Allowed, opportunities[11]);
        }

        [Theory]
        [InlineData("ᨠᨡᨢᨣᨤ")]                                                 // Tai Tham
        [InlineData("ຂ້ອຍຮັກພາສາລາວ")]                                      // Lao
        [InlineData("ကျွန်တော်မြန်မာစာကိုချစ်တယ်")]                            // Burmese
        public void OtherComplexContextScripts_KeepTheFallbackOfLb1(string text)
        {
            // These have no word list: a run of one has no opportunity inside.
            Assert.Equal(text, Render(text));
        }

        // ---- the parts ---------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("Thai", 26383, 20)]
        [InlineData("Khmer", 81025, 19)]
        public void EachListLoads_SortedAndComplete(string scriptName, int count, int longest)
        {
            var dictionary = WordDictionary.For(Enum.Parse<ComplexScript>(scriptName));

            Assert.NotNull(dictionary);
            Assert.Equal(count, dictionary.Count);
            Assert.Equal(longest, dictionary.Longest);

            var lengths = new int[dictionary.Longest];
            for (int i = 0; i < dictionary.Count; i++)
            {
                var word = dictionary.WordAt(i);
                if (i > 0)
                {
                    Assert.True(dictionary.WordAt(i - 1).SequenceCompareTo(word) < 0, $"word {i} is out of order");
                }

                // Every word finds itself, as the last of the prefixes of the text that is the word.
                var codes = new int[word.Length];
                for (int j = 0; j < word.Length; j++) codes[j] = word[j];
                int found = dictionary.FindPrefixes(codes, lengths);
                Assert.True(found > 0 && lengths[found - 1] == word.Length, $"word {i} is not found");
            }
        }

        [Fact]
        public void FindPrefixes_ListsEveryWordThatStartsTheText_ShortestFirst()
        {
            var dictionary = WordDictionary.For(ComplexScript.Thai)!;
            var text = "ประเทศไทยมี".Select(c => (int)c).ToArray();
            var lengths = new int[dictionary.Longest];

            int found = dictionary.FindPrefixes(text, lengths);

            var prefixes = Enumerable.Range(0, found).Select(i => new string(text.Take(lengths[i]).Select(c => (char)c).ToArray())).ToArray();
            Assert.Contains("ประเทศ", prefixes);
            Assert.Contains("ประเทศไทย", prefixes);
            Assert.Equal(prefixes.OrderBy(p => p.Length), prefixes);
            var words = Enumerable.Range(0, dictionary.Count).Select(i => new string(dictionary.WordAt(i))).ToHashSet();
            Assert.All(prefixes, p => Assert.Contains(p, words));
        }

        [Fact]
        public void FindPrefixes_OfATextNoWordStarts_IsEmpty()
        {
            var dictionary = WordDictionary.For(ComplexScript.Thai)!;
            var lengths = new int[dictionary.Longest];

            Assert.Equal(0, dictionary.FindPrefixes([0x0F43, 0x0E01], lengths));
            Assert.Equal(0, dictionary.FindPrefixes([0x0E03], lengths));
            Assert.Equal(0, dictionary.FindPrefixes([], lengths));
            Assert.Equal(0, dictionary.FindPrefixes([0x1F600], lengths));
        }

        [Fact]
        public void WithoutAListForTheScript_ThereIsNone()
        {
            Assert.Null(WordDictionary.For(ComplexScript.None));
            Assert.Equal(ComplexScript.None, DictionarySegmenter.ScriptOf('a'));
            Assert.Equal(ComplexScript.Thai, DictionarySegmenter.ScriptOf(0x0E01));
            Assert.Equal(ComplexScript.None, DictionarySegmenter.ScriptOf(0x0E81));      // Lao and Burmese have no list yet
            Assert.Equal(ComplexScript.Khmer, DictionarySegmenter.ScriptOf(0x1780));
            Assert.Equal(ComplexScript.None, DictionarySegmenter.ScriptOf(0x1000));
        }

        [Fact]
        public void TheResourcesAreDeflate_NotBrotli()
        {
            // WebAssembly has no Brotli decoder: each list must inflate with DeflateStream, whose absence would leave no list at all.
            var assembly = typeof(WordDictionary).Assembly;
            foreach (var script in new[] { "thai", "khmer" })
            {
                var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + script + ".dict", StringComparison.Ordinal));
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var inflated = new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflated.CopyTo(output);
                Assert.True(output.Length > 100_000);
                Assert.Equal("PDW1", Encoding.ASCII.GetString(output.GetBuffer(), 0, 4));
            }
        }

        [Fact]
        public void TheListsAreSmall()
        {
            long total = 0;
            var assembly = typeof(WordDictionary).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".dict", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                total += stream.Length;
            }

            Assert.InRange(total, 250_000, 320_000);
        }

        [Fact]
        public void Parse_RefusesDataThatIsNotAList()
        {
            static byte[] Payload(int count, byte[] shared, byte[] suffixes, string magic = "PDW1")
            {
                var bytes = new List<byte>(Encoding.ASCII.GetBytes(magic));
                bytes.AddRange([0x00, 0x0E]);
                bytes.AddRange(BitConverter.GetBytes(count));
                bytes.AddRange(shared);
                bytes.AddRange(suffixes);
                return bytes.ToArray();
            }

            // Two words, "กข" and "กฃ": the second shares one character with the first (a character is its code point less 0E00, plus one).
            var good = Payload(2, [0, 1], [2, 3, 0, 4, 0]);
            var parsed = WordDictionary.Parse(good);
            Assert.NotNull(parsed);
            Assert.Equal(2, parsed.Count);
            Assert.True(parsed.WordAt(0).SequenceEqual("กข"));
            Assert.True(parsed.WordAt(1).SequenceEqual("กฃ"));

            Assert.Null(WordDictionary.Parse([]));
            Assert.Null(WordDictionary.Parse(good.AsSpan(0, 9)));
            Assert.Null(WordDictionary.Parse(Payload(2, [0, 1], [2, 3, 0, 4, 0], "XXXX")));
            Assert.Null(WordDictionary.Parse(Payload(0, [], [])));
            Assert.Null(WordDictionary.Parse(Payload(int.MaxValue, [0], [1, 0])));
            Assert.Null(WordDictionary.Parse(Payload(2, [0, 1], [2, 3, 0, 4, 0, 5, 0])));   // trailing bytes
            Assert.Null(WordDictionary.Parse(Payload(2, [0, 1], [2, 3, 0, 4])));            // the last word is not ended
            Assert.Null(WordDictionary.Parse(Payload(2, [0, 3], [2, 3, 0, 4, 0])));         // shares more than the word before has
            Assert.Null(WordDictionary.Parse(Payload(2, [1, 1], [2, 3, 0, 4, 0])));         // the first word shares
            Assert.Null(WordDictionary.Parse(Payload(2, [0, 0], [0, 4, 0])));              // an empty word
            Assert.Null(WordDictionary.Parse(Payload(1, [0], Enumerable.Repeat((byte)1, 256).Append((byte)0).ToArray())));   // longer than 255
        }

        // ---- hostile input -----------------------------------------------------------------------------------------------------------

        private static readonly (int First, int Last)[] Blocks = [(0x0E00, 0x0E7F), (0x1780, 0x17FF)];

        private static string RandomText(Random random, int length, (int First, int Last) block)
        {
            var builder = new StringBuilder(length);
            while (builder.Length < length)
            {
                // Only the characters of the Complex_Context class and the unassigned ones: the punctuation, digits and currency
                // signs of the blocks have line breaking classes of their own, which the rules of the algorithm break around.
                int cp = random.Next(block.First, block.Last + 1);
                var lineBreak = (LineBreakClass)(SegmentationData.LineBreak(cp) & 0x3F);
                if (lineBreak is LineBreakClass.SA or LineBreakClass.XX)
                {
                    builder.Append((char)cp);
                }
            }

            return builder.ToString();
        }

        [Fact]
        public void RandomText_NeverBreaksInsideAGraphemeCluster_OrNextToAJoiningCharacter()
        {
            var random = new Random(20260926);
            foreach (var block in Blocks)
            {
                for (int round = 0; round < 300; round++)
                {
                    var text = RandomText(random, random.Next(1, 60), block);
                    var opportunities = LineBreaker.FindOpportunities(text);
                    var graphemes = new HashSet<int>(Segmenter.FindGraphemeBoundaries(text));

                    for (int i = 1; i < text.Length; i++)
                    {
                        if (opportunities[i] == LineBreakOpportunity.Prohibited)
                        {
                            continue;
                        }

                        Assert.True(graphemes.Contains(i), $"a break inside a cluster at {i} of {Escape(text)}");
                        Assert.False(IsLeading(text[i - 1]), $"a break after a leading character at {i} of {Escape(text)}");
                        Assert.False(IsDependent(text[i]), $"a break before a dependent character at {i} of {Escape(text)}");
                    }

                    // The same answer every time.
                    Assert.Equal(opportunities, LineBreaker.FindOpportunities(text));
                }
            }
        }

        private static string Escape(string text) => string.Concat(text.Select(c => "\\u" + ((int)c).ToString("X4")));

        /// <summary>The Thai leading vowels and the Khmer coeng: what a break must never follow.</summary>
        private static bool IsLeading(char c) => c is >= '\u0E40' and <= '\u0E44' or '\u17D2';

        /// <summary>The marks and dependent vowels of the two scripts (the categories Mn and Mc), and the letters that only follow one.</summary>
        private static bool IsDependent(char c)
        {
            var category = char.GetUnicodeCategory(c);
            return category is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark
                || c is '\u0E30' or '\u0E32' or '\u0E33' or '\u0E45';
        }

        [Fact]
        public void ALongRun_IsBrokenInBoundedTime()
        {
            // A long stretch of words (well past the chunk the segmenter works in) and one of nonsense.
            var thai = WordDictionary.For(ComplexScript.Thai)!;
            var random = new Random(7);
            var words = new StringBuilder();
            while (words.Length < 300_000)
            {
                words.Append(thai.WordAt(random.Next(thai.Count)));
            }

            var stopwatch = Stopwatch.StartNew();
            var opportunities = LineBreaker.FindOpportunities(words.ToString());
            var wordsTime = stopwatch.Elapsed;

            Assert.True(opportunities.Count(o => o != LineBreakOpportunity.Prohibited) > 30_000);
            Assert.True(wordsTime < TimeSpan.FromSeconds(30), $"took {wordsTime}");

            stopwatch.Restart();
            var nonsense = RandomText(random, 300_000, Blocks[0]);
            var graphemes = LineBreaker.FindOpportunities(nonsense);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
            Assert.Equal(nonsense.Length + 1, graphemes.Length);
        }

        [Fact]
        public void AHugeRunOfMarksWithNoBoundary_IsBrokenNowhere()
        {
            // One consonant and a hundred thousand vowel signs and tone marks: a single cluster far longer than a chunk.
            var text = "ก" + new string('\u0E48', 100_000) + "ก";
            var stopwatch = Stopwatch.StartNew();
            var opportunities = LineBreaker.FindOpportunities(text);

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
            Assert.All(opportunities.Take(text.Length - 1).Skip(1), o => Assert.Equal(LineBreakOpportunity.Prohibited, o));
        }

        [Fact]
        public void AWordThatRepeats_IsBrokenBetweenItsCopies()
        {
            var text = string.Concat(Enumerable.Repeat("ภาษา", 20_000));
            var opportunities = LineBreaker.FindOpportunities(text);

            for (int i = 4; i < text.Length; i += 4)
            {
                Assert.NotEqual(LineBreakOpportunity.Prohibited, opportunities[i]);
            }
        }

        [Fact]
        public void ManyThreads_GetTheSameBreaks()
        {
            var texts = new[] { "ฉันรักภาษาไทย", "ខ្ញុំស្រលាញ់ភាសាខ្មែរ" };
            var expected = texts.Select(t => Render(t)).ToArray();

            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, 200, i =>
            {
                var index = i % texts.Length;
                if (Render(texts[index]) != expected[index])
                {
                    failures.Add(texts[index]);
                }
            });

            Assert.Empty(failures);
            Assert.All(expected, e => Assert.Contains('|', e));
        }
    }
}
