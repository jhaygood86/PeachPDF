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
    /// The EAN-13, EAN-8 and UPC-A symbologies (ISO/IEC 15420): check digits, modules and the drawing with
    /// human-readable digits under the bars.
    /// </summary>
    internal static class EanUpcSymbology
    {
        internal enum Kind
        {
            Ean13,
            Ean8,
            UpcA,
        }

        // Set A (odd parity) patterns of the digits 0 to 9; each is 7 modules: space, bar, space, bar.
        static readonly string[] SetA =
            ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];

        // Which of the six left-hand digits of an EAN-13 use set B (even parity), by the leading digit.
        static readonly string[] Ean13Parity =
            ["AAAAAA", "AABABB", "AABBAB", "AABBBA", "ABAABB", "ABBAAB", "ABBBAA", "ABABAB", "ABABBA", "ABBABA"];

        /// <summary>
        /// Number of digits without the check digit.
        /// </summary>
        internal static int DataDigits(Kind kind) => kind switch
        {
            Kind.Ean13 => 12,
            Kind.Ean8 => 7,
            _ => 11,
        };

        /// <summary>
        /// The quiet zones the drawing reserves left and right of the bars, in modules.
        /// </summary>
        internal static (int Left, int Right) QuietZones(Kind kind) => kind switch
        {
            Kind.Ean13 => (11, 7),
            Kind.Ean8 => (7, 7),
            _ => (9, 9),
        };

        static string Name(Kind kind) => kind switch
        {
            Kind.Ean13 => "EAN-13",
            Kind.Ean8 => "EAN-8",
            _ => "UPC-A",
        };

        /// <summary>
        /// Checks that the text is all digits and of the right length, with or without the check digit,
        /// and that a given check digit is right.
        /// </summary>
        internal static void Check(string text, Kind kind)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            int data = DataDigits(kind);
            if ((text.Length != data && text.Length != data + 1) || text.Any(ch => ch is < '0' or > '9'))
                throw new ArgumentException($"'{text}' is not a valid {Name(kind)} code: it must be {data} digits, or {data + 1} with the check digit.");
            if (text.Length == data + 1 && text[data] != CheckDigit(text.Substring(0, data)))
                throw new ArgumentException($"The check digit of the {Name(kind)} code '{text}' is wrong; it should be {CheckDigit(text.Substring(0, data))}.");
        }

        /// <summary>
        /// The code with its check digit.
        /// </summary>
        internal static string Complete(string text, Kind kind)
            => text.Length == DataDigits(kind) ? text + CheckDigit(text) : text;

        /// <summary>
        /// The GS1 check digit: weights 3 and 1 alternately from the rightmost data digit, rounded up to ten.
        /// </summary>
        internal static char CheckDigit(string digits)
        {
            int sum = 0;
            for (int i = 0; i < digits.Length; i++)
            {
                int weight = (digits.Length - i) % 2 == 1 ? 3 : 1;
                sum += (digits[i] - '0') * weight;
            }
            return (char)('0' + (10 - sum % 10) % 10);
        }

        /// <summary>
        /// The modules from the start guard to the end guard, true for bar.
        /// </summary>
        internal static bool[] Modules(string code, Kind kind)
        {
            // UPC-A is an EAN-13 with a leading zero.
            if (kind == Kind.UpcA)
                code = "0" + code;

            var modules = new List<bool>(95);
            Append(modules, "101");
            if (kind == Kind.Ean8)
            {
                for (int i = 0; i < 4; i++)
                    Append(modules, SetA[code[i] - '0']);
                Append(modules, "01010");
                for (int i = 4; i < 8; i++)
                    Append(modules, SetC(code[i]));
            }
            else
            {
                string parity = Ean13Parity[code[0] - '0'];
                for (int i = 1; i < 7; i++)
                    Append(modules, parity[i - 1] == 'A' ? SetA[code[i] - '0'] : SetB(code[i]));
                Append(modules, "01010");
                for (int i = 7; i < 13; i++)
                    Append(modules, SetC(code[i]));
            }
            Append(modules, "101");
            return modules.ToArray();
        }

        // Set C (right-hand) patterns are set A with bars and spaces swapped; set B patterns are set C reversed.
        static string SetC(char digit) => new(SetA[digit - '0'].Select(ch => ch == '0' ? '1' : '0').ToArray());

        static string SetB(char digit) => new(SetC(digit).Reverse().ToArray());

        static void Append(List<bool> modules, string pattern)
        {
            foreach (char ch in pattern)
                modules.Add(ch == '1');
        }

        /// <summary>
        /// Whether the bar module at this index reaches down into the text line: the guard bars and,
        /// for UPC-A, the first and last digit.
        /// </summary>
        internal static bool IsLong(int index, Kind kind)
        {
            int length = kind == Kind.Ean8 ? 67 : 95;
            int center = length / 2;
            bool guard = index < 3 || index >= length - 3 || (index >= center - 2 && index <= center + 2);
            bool upcOuterDigit = kind == Kind.UpcA && (index < 10 || index >= length - 10);
            return guard || upcOuterDigit;
        }
    }
}
