// Portions derived from FreePDF (https://github.com/io7/FreePDF), MIT License,
// Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany.
// See THIRD-PARTY-LICENSES.md in this package.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// The Code 128 symbology (ISO/IEC 15417) for ASCII text. The code sets A, B and C are chosen so the code is as
    /// short as possible; runs of digits use code set C, two digits per character.
    /// </summary>
    /// <remarks>
    /// For GS1-128, put <see cref="Fnc1"/> first and between variable-length element strings, or build the text with
    /// <see cref="Gs1Elements.ToCode128Text(string)"/>.
    /// </remarks>
    public static class Code128
    {
        /// <summary>
        /// The character that stands for the FNC1 function character: U+E0F1, from the Private Use Area, so no
        /// letter of real text turns into FNC1 by accident.
        /// </summary>
        public const char Fnc1 = '';

        /// <summary>
        /// Checks that the text is not empty and holds only ASCII characters and <see cref="Fnc1"/>.
        /// </summary>
        /// <param name="text">The text to check.</param>
        public static void Check(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0 || text.Any(ch => ch > (char)127 && ch != Fnc1))
                throw new ArgumentException(InvalidMessage(text), nameof(text));
        }

        static string InvalidMessage(string text)
            => $"'{text}' cannot be encoded as Code 128: it must not be empty and may hold only ASCII characters and Code128.Fnc1.";

        // Bar and space widths of the 107 symbol characters (ISO/IEC 15417, table 1), starting with a bar.
        static readonly string[] Patterns =
        [
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
        ];

        const int SetA = 0;
        const int SetB = 1;
        const int SetC = 2;

        const int ShiftValue = 98;
        const int Fnc1Value = 102;
        const int StartAValue = 103;
        const int StopValue = 106;

        /// <summary>
        /// The symbol characters of the code: the start character, the data and the check character, not the stop
        /// character.
        /// </summary>
        public static int[] GetSymbolValues(string text)
        {
            Check(text);
            int n = text.Length;

            // best[i, s]: fewest symbol characters for text[i..] when set s is active at i, switching allowed.
            // direct[i, s]: the same without switching at i first.
            const int unreachable = Int32.MaxValue / 4;
            var best = new int[n + 1, 3];
            var direct = new int[n + 1, 3];
            var directStep = new Step[n + 1, 3];
            var switchTo = new int[n + 1, 3];
            for (int i = n - 1; i >= 0; i--)
            {
                char ch = text[i];
                for (int set = 0; set < 3; set++)
                {
                    int cost = unreachable;
                    Step step = Step.None;
                    if (set == SetC)
                    {
                        if (ch == Fnc1)
                            (cost, step) = (1 + best[i + 1, SetC], Step.Single);
                        else if (i + 1 < n && IsDigit(ch) && IsDigit(text[i + 1]))
                            (cost, step) = (1 + best[i + 2, SetC], Step.Pair);
                    }
                    else
                    {
                        int other = set == SetA ? SetB : SetA;
                        if (InSet(ch, set))
                            (cost, step) = (1 + best[i + 1, set], Step.Single);
                        else if (InSet(ch, other))
                            (cost, step) = (2 + best[i + 1, set], Step.Shift);
                    }
                    direct[i, set] = cost;
                    directStep[i, set] = step;
                }
                for (int set = 0; set < 3; set++)
                {
                    best[i, set] = direct[i, set];
                    switchTo[i, set] = -1;
                    for (int target = 0; target < 3; target++)
                    {
                        if (target != set && 1 + direct[i, target] < best[i, set])
                        {
                            best[i, set] = 1 + direct[i, target];
                            switchTo[i, set] = target;
                        }
                    }
                }
            }

            // Start in the set that needs fewest characters; on a tie prefer B, then C, then A.
            int current = SetB;
            foreach (int set in new[] { SetC, SetA })
            {
                if (direct[0, set] < direct[0, current])
                    current = set;
            }
            if (direct[0, current] >= unreachable)
                throw new ArgumentException(InvalidMessage(text));

            var values = new List<int> { StartAValue + current };
            int position = 0;
            bool justSwitched = true;
            while (position < n)
            {
                if (!justSwitched && switchTo[position, current] >= 0)
                {
                    int target = switchTo[position, current];
                    values.Add(SwitchValue(target));
                    current = target;
                    justSwitched = true;
                    continue;
                }
                justSwitched = false;
                char ch = text[position];
                switch (directStep[position, current])
                {
                    case Step.Single:
                        values.Add(ValueIn(ch, current));
                        position++;
                        break;

                    case Step.Pair:
                        values.Add((ch - '0') * 10 + (text[position + 1] - '0'));
                        position += 2;
                        break;

                    case Step.Shift:
                        values.Add(ShiftValue);
                        values.Add(ValueIn(ch, current == SetA ? SetB : SetA));
                        position++;
                        break;

                    default:
                        throw new InvalidOperationException("Code 128 encoding has no step for this character.");
                }
            }

            int sum = values[0];
            for (int i = 1; i < values.Count; i++)
                sum += i * values[i];
            values.Add(sum % 103);
            return values.ToArray();
        }

        /// <summary>
        /// The modules of the code from the start to the stop character, true for bar, without quiet zones.
        /// Each symbol character is 11 modules wide, the stop character 13.
        /// </summary>
        public static bool[] GetModules(string text)
        {
            int[] values = GetSymbolValues(text);
            var modules = new List<bool>(values.Length * 11 + 13);
            foreach (int value in values)
                AppendPattern(modules, Patterns[value]);
            AppendPattern(modules, Patterns[StopValue]);
            return modules.ToArray();
        }

        static void AppendPattern(List<bool> modules, string pattern)
        {
            bool bar = true;
            foreach (char width in pattern)
            {
                for (int k = 0; k < width - '0'; k++)
                    modules.Add(bar);
                bar = !bar;
            }
        }

        enum Step : byte
        {
            None,
            Single,
            Pair,
            Shift,
        }

        static bool IsDigit(char ch) => ch is >= '0' and <= '9';

        // Set A holds ASCII 0 to 95, set B 32 to 127; both hold FNC1.
        static bool InSet(char ch, int set)
            => ch == Fnc1 || (set == SetA ? ch <= 95 : ch is >= ' ' and <= (char)127);

        static int ValueIn(char ch, int set)
        {
            if (ch == Fnc1)
                return Fnc1Value;
            if (set == SetA && ch < ' ')
                return ch + 64;
            return ch - ' ';
        }

        // Code A is 101 in sets B and C, code B 100 in sets A and C, code C 99 in sets A and B.
        static int SwitchValue(int target) => target switch
        {
            SetA => 101,
            SetB => 100,
            _ => 99,
        };

        /// <summary>
        /// The text shown with the bars: the code without FNC1, control characters as spaces.
        /// </summary>
        /// <param name="text">The code text.</param>
        /// <returns>The human-readable form.</returns>
        public static string HumanReadableText(string text)
            => new(text.Where(ch => ch != Fnc1).Select(ch => ch < ' ' || ch == (char)127 ? ' ' : ch).ToArray());
    }
}
