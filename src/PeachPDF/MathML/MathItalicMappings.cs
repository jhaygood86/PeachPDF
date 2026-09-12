#region PeachPDF - A .NET library for rendering HTML to PDF
//
// MathML Core Appendix C.1 "italic mappings" (https://w3c.github.io/mathml-core/#italic-mappings) -
// the fixed table the UA-stylesheet rule "mi { text-transform: math-auto }" (Core §4.2) uses to convert
// a single-character mi's content to the corresponding Unicode "Mathematical Italic" codepoint. Values
// transcribed directly from the spec's own table, not derived algorithmically from the Unicode
// Mathematical Alphanumeric Symbols block layout, since a handful of entries (h, nabla, partial, the
// Greek variant-form letters) fall outside that block's otherwise-regular structure.
//
#endregion

using System.Collections.Generic;

namespace PeachPDF.MathML
{
    /// <summary>Looks up a character's MathML Core "math-auto" italic mapping (Appendix C.1).</summary>
    internal static class MathItalicMappings
    {
        /// <summary>One contiguous source range mapped to a target range via a fixed codepoint delta -
        /// covers the bulk of the table (Latin/Greek upper/lowercase) without an entry per character.</summary>
        readonly record struct Range(int Start, int End, int Delta);

        static readonly Range[] Ranges =
        [
            new Range(0x0041, 0x005A, 0x1D3F3), // A-Z
            new Range(0x0061, 0x0067, 0x1D3ED), // a-g
            // 'h' (0x68) is the one break in the otherwise-contiguous a-z run - see Exceptions.
            new Range(0x0069, 0x007A, 0x1D3ED), // i-z
            new Range(0x0391, 0x03A9, 0x1D351), // Greek capital Alpha-Omega (0x3A2 is unassigned, so
                                                 // including it in the range is harmless - it can never
                                                 // appear as real input text)
            new Range(0x03B1, 0x03C9, 0x1D34B), // Greek lowercase alpha-omega (includes final sigma ς)
        ];

        /// <summary>Single-character exceptions that fall outside the regular ranges above - either
        /// because the Unicode Mathematical Alphanumeric Symbols block reassigns the slot (h, dotless
        /// i/j) or because the target is a pre-existing letterlike symbol rather than a block member
        /// (nabla, partial) or a Greek "variant form" letter with its own separate mapping.</summary>
        static readonly Dictionary<int, int> Exceptions = new()
        {
            [0x0068] = 0x210E,  // h -> PLANCK CONSTANT
            [0x0131] = 0x1D6A4, // ı (dotless i)
            [0x0237] = 0x1D6A5, // ȷ (dotless j)
            [0x03F4] = 0x1D6F3, // ϴ (Greek capital theta symbol)
            [0x2207] = 0x1D6FB, // ∇ (nabla)
            [0x2202] = 0x1D715, // ∂ (partial differential)
            [0x03F5] = 0x1D716, // ϵ (Greek lunate epsilon symbol)
            [0x03D1] = 0x1D717, // ϑ (Greek theta symbol)
            [0x03F0] = 0x1D718, // ϰ (Greek kappa symbol)
            [0x03D5] = 0x1D719, // ϕ (Greek phi symbol)
            [0x03F1] = 0x1D71A, // ϱ (Greek rho symbol)
            [0x03D6] = 0x1D71B, // ϖ (Greek pi symbol)
        };

        /// <summary>Resolves <paramref name="c"/>'s math-italic mapping, if it has one.</summary>
        public static bool TryGetItalic(char c, out int italicCodePoint)
        {
            if (Exceptions.TryGetValue(c, out italicCodePoint))
                return true;

            foreach (var range in Ranges)
            {
                if (c >= range.Start && c <= range.End)
                {
                    italicCodePoint = c + range.Delta;
                    return true;
                }
            }

            italicCodePoint = 0;
            return false;
        }
    }
}
