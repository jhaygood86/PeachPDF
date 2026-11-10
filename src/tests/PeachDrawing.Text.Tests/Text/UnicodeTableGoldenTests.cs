using System.Security.Cryptography;
using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Internal.Text.Bidi;
using PeachDrawing.Text.Internal.Text.Segmentation;

namespace PeachDrawing.Text.Tests.Text
{
    /// <summary>
    /// Pins what each per-codepoint property table answers for every code point from U+0000 to U+10FFFF (and a few outside it), as a SHA-256 of the
    /// answers. The tables are searched by binary search with a direct table in front of it for the low code points; the two have to agree for
    /// every code point, and the hashes were recorded from the binary search alone. A hash cannot say which code point changed: look the
    /// code points up both ways and compare.
    /// </summary>
    public class UnicodeTableGoldenTests
    {
        private const int MaxCodePoint = 0x10FFFF;

        private static readonly int[] Outside = [-1, int.MinValue, 0x110000, 0x7FFFFFFF];

        private static string HashOf(Func<int, int> lookup)
        {
            var bytes = new byte[(MaxCodePoint + 1 + Outside.Length) * 4];
            var n = 0;
            for (var cp = 0; cp <= MaxCodePoint; cp++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(n++ * 4), lookup(cp));
            foreach (var cp in Outside)
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(n++ * 4), lookup(cp));
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static void Check(string name, string actual) =>
            Assert.True(Hashes.TryGetValue(name, out var recorded) && recorded == actual,
                $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

        [Fact]
        public void ScriptTable_AnswersAsRecorded()
        {
            // Script names are strings; each is numbered by when it first appears, which is stable for a given answer sequence.
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            Check("SCRIPT", HashOf(cp =>
            {
                var script = ScriptTable.Of(cp);
                if (!numbers.TryGetValue(script, out var number))
                    numbers[script] = number = numbers.Count;
                return number;
            }));
        }

        [Fact]
        public void BidiClassTable_AnswersAsRecorded() => Check("BIDI", HashOf(cp => (int)BidiClassTable.Of(cp)));

        [Fact]
        public void LineBreak_AnswersAsRecorded() => Check("LINEBREAK", HashOf(SegmentationData.LineBreak));

        [Fact]
        public void Grapheme_AnswersAsRecorded() => Check("GRAPHEME", HashOf(SegmentationData.Grapheme));

        [Fact]
        public void Word_AnswersAsRecorded() => Check("WORD", HashOf(SegmentationData.Word));

        [Fact]
        public void Sentence_AnswersAsRecorded() => Check("SENTENCE", HashOf(SegmentationData.Sentence));

        private static readonly Dictionary<string, string> Hashes = new()
        {
            ["BIDI"] = "C24919FC9586966E800BE3F99EA046F3BFAD42AC042ED88E05A2F2EA1E20A700",
            ["GRAPHEME"] = "D3FE0BD6B79A9DE79C72158B666A05D563B7F2E440001B3B0276B61192192DD4",
            ["LINEBREAK"] = "0769241C6B4BFC352B764CAA9A3165D9F01DAF26C591770369412011C270A888",
            ["SCRIPT"] = "C67AD311EB53466B2F7302B81CCC74DC73EEAD7F6B536B75988A50E5370CC5C1",
            ["SENTENCE"] = "50E5A5B59DB8E4A3F77E867C473F5266C9818242A16C138B66A078D4755BA65B",
            ["WORD"] = "07659758CDEE1A24A01252433FBDE05F18DDFB99CCD282EC688A186F558AFDA9",
        };
    }
}
