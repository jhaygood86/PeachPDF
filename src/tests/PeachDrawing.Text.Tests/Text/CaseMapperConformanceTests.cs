using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PeachDrawing.Text.Tests.Text
{
    /// <summary>
    /// <see cref="CaseMapper"/> against the Unicode Character Database it is generated from: every unconditional entry of
    /// <c>SpecialCasing.txt</c> and every one-to-one mapping of <c>UnicodeData.txt</c>.
    /// </summary>
    public class CaseMapperConformanceTests
    {
        private static string File(string name) => Path.Combine(AppContext.BaseDirectory, name);

        private static string Decode(string codePoints) =>
            string.Concat(codePoints.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(cp => char.ConvertFromUtf32(Convert.ToInt32(cp, 16))));

        private static bool Equivalent(string expected, string actual) =>
            expected.Normalize(NormalizationForm.FormD) == actual.Normalize(NormalizationForm.FormD);

        [Fact]
        public void TheGreekCapitalsWithAToneAreComposed() =>
            Assert.Equal("\u03AA\u0301", CaseMapper.ToUpper("\u0390"));       // not the iota, the diaeresis and the acute apart

        [Fact]
        public void UnconditionalSpecialCasingEntries_AreReproduced()
        {
            var checkedEntries = 0;
            foreach (var raw in System.IO.File.ReadLines(File("SpecialCasing.txt")))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;

                var fields = line.Split(';').Select(f => f.Trim()).ToArray();
                if (fields.Length > 4 && fields[4].Length > 0) continue;     // a context rule, covered by the language tests

                var text = char.ConvertFromUtf32(Convert.ToInt32(fields[0], 16));
                // SpecialCasing writes its mappings decomposed; CaseMapper composes them, which is the same text canonically.
                Assert.True(Equivalent(Decode(fields[1]), CaseMapper.ToLower(text)), $"lower of U+{fields[0]}");
                Assert.True(Equivalent(Decode(fields[2]), CaseMapper.ToTitle(text)), $"title of U+{fields[0]}");
                Assert.True(Equivalent(Decode(fields[3]), CaseMapper.ToUpper(text)), $"upper of U+{fields[0]}");
                checkedEntries++;
            }

            Assert.True(checkedEntries > 100);
        }

        [Fact]
        public void EveryOneToOneMappingOfUnicodeData_IsReproduced()
        {
            var special = new HashSet<int>();
            foreach (var raw in System.IO.File.ReadLines(File("SpecialCasing.txt")))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length > 0 && line.Split(';').Select(f => f.Trim()).ToArray() is { } f && !(f.Length > 4 && f[4].Length > 0))
                    special.Add(Convert.ToInt32(f[0], 16));
            }

            var failures = new List<string>();
            foreach (var raw in System.IO.File.ReadLines(File("UnicodeData.txt")))
            {
                var fields = raw.Split(';');
                if (fields.Length < 15 || fields[1].EndsWith(", First>", StringComparison.Ordinal) || fields[1].EndsWith(", Last>", StringComparison.Ordinal)) continue;

                var codePoint = Convert.ToInt32(fields[0], 16);
                if (special.Contains(codePoint) || codePoint == 0x03A3 || codePoint == 'I' || codePoint == 'J' || codePoint == 0x012E || codePoint == 0x0307) continue;

                var text = char.ConvertFromUtf32(codePoint);
                if (fields[12].Length > 0 && char.ConvertFromUtf32(Convert.ToInt32(fields[12], 16)) != CaseMapper.ToUpper(text)) failures.Add($"upper U+{fields[0]}");
                if (fields[13].Length > 0 && char.ConvertFromUtf32(Convert.ToInt32(fields[13], 16)) != CaseMapper.ToLower(text)) failures.Add($"lower U+{fields[0]}");
                var title = fields[14].Length > 0 ? fields[14] : fields[12];
                if (title.Length > 0 && char.ConvertFromUtf32(Convert.ToInt32(title, 16)) != CaseMapper.ToTitle(text)) failures.Add($"title U+{fields[0]}");
            }

            Assert.Empty(failures);
        }
    }
}
