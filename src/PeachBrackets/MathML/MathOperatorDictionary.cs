#region PeachPDF - A .NET library for rendering HTML to PDF
//
// MathML Core Appendix B.1's Operator Dictionary (https://w3c.github.io/mathml-core/#operator-dictionary):
// resolves an <mo>'s (Content, Form) pair to a spacing (lspace/rspace) and property
// (stretchy/symmetric/largeop/movablelimits) entry, replacing the flat thickmathspace approximation
// MathLayoutEngine used before. The category data below (Categories A-M, and each category's Unicode
// ranges) is transcribed directly from the spec's own Figure 25 (content/form -> category) and Figure 26
// (category -> spacing/properties) tables - verified by checking every category's transcribed range
// count against the spec's own stated count (e.g. "108 entries (31 Unicode ranges)" for category B) and
// every lspace/rspace value against MathML's own named-space-to-em ratios (thinmathspace = 3/18em,
// mediummathspace = 4/18em, thickmathspace = 5/18em - exactly the values Figure 26 uses).
//
// Cross-checked against, but deliberately NOT matched to,
// https://github.com/WebKit/WebKit/blob/main/Source/WebCore/mathml/MathMLOperatorDictionary.cpp: WebKit's
// table agrees on unambiguous cases (comma/semicolon both resolve to lspace 0/rspace thinmathspace in
// both), but disagrees on others (e.g. the integral's spacing, the colon's category) - it's a richer,
// per-character dictionary that predates MathML Core's Appendix B collapsing the classic MathML 3
// dictionary into this compact category system, not a second implementation of the same current table.
// This file follows the current MathML Core spec text, not WebKit's copy of the older one.
//
// Not transcribed: Figure 27, a second, binary-search-optimized encoding of the SAME data as Figure 25
// (the spec's reference implementation uses it for lookup speed) - redundant to reproduce, since a
// straightforward linear scan over Figure 25's own ranges (at most a few dozen operators per formula,
// nowhere near a hot path) produces identical results. Also not transcribed: Operators_fence/
// Operators_separator (Figure 24) and the "intrinsic stretch axis" character list - these inform
// fence/separator/stretch-AXIS defaults, a different piece of §3.2.4.2 this change doesn't touch.
//
#endregion

using System;
using System.Collections.Generic;

namespace PeachPDF.MathML
{
    [Flags]
    internal enum OperatorProperty
    {
        None = 0,
        Stretchy = 1 << 0,
        Symmetric = 1 << 1,
        LargeOp = 1 << 2,
        MovableLimits = 1 << 3,
    }

    /// <summary>One resolved operator-dictionary entry (MathML Core Appendix B.1's per-category
    /// spacing/properties, Figure 26) - <see cref="LSpace"/>/<see cref="RSpace"/> are already in
    /// <see cref="MathLengthUnit.Em"/>.</summary>
    internal readonly record struct OperatorDictionaryEntry(
        MathLength LSpace, MathLength RSpace, bool Stretchy, bool Symmetric, bool LargeOp, bool MovableLimits);

    internal static class MathOperatorDictionary
    {
        /// <summary>Category "Default"/"ForceDefault" (Figure 26) - identical spacing/properties for
        /// both, so this implementation doesn't need to distinguish them (see this file's header).
        /// <c>5/18em = thickmathspace</c>, matching the flat approximation this replaces.</summary>
        static readonly OperatorDictionaryEntry DefaultEntry = new(
            new MathLength(5.0 / 18, MathLengthUnit.Em), new MathLength(5.0 / 18, MathLengthUnit.Em),
            Stretchy: false, Symmetric: false, LargeOp: false, MovableLimits: false);

        /// <summary>One (Content, Form) -&gt; category range set from Figure 25, plus that category's
        /// own resolved entry from Figure 26.</summary>
        internal readonly record struct CategoryRangeSet(
            string Name, string Form, OperatorDictionaryEntry Entry, IReadOnlyList<(int Start, int End)> Ranges)
        {
            public bool Contains(int codepoint)
            {
                foreach (var (start, end) in Ranges)
                    if (codepoint >= start && codepoint <= end)
                        return true;
                return false;
            }
        }

        static CategoryRangeSet MakeCategory(
            string name, string form, double lspaceEm, double rspaceEm, OperatorProperty props, (int, int)[] ranges) =>
            new(name, form,
                new OperatorDictionaryEntry(
                    new MathLength(lspaceEm, MathLengthUnit.Em), new MathLength(rspaceEm, MathLengthUnit.Em),
                    props.HasFlag(OperatorProperty.Stretchy), props.HasFlag(OperatorProperty.Symmetric),
                    props.HasFlag(OperatorProperty.LargeOp), props.HasFlag(OperatorProperty.MovableLimits)),
                ranges);

        // ---- Figure 25 + Figure 26, combined ------------------------------------------------------

        internal static readonly CategoryRangeSet[] Categories =
        [
            MakeCategory("A", "infix", 5.0 / 18, 5.0 / 18, OperatorProperty.Stretchy,
                [(0x2190, 0x2195), (0x219A, 0x21AE), (0x21B0, 0x21B5), (0x21B9, 0x21B9), (0x21BC, 0x21D5), (0x21DA, 0x21F0), (0x21F3, 0x21FF), (0x2794, 0x2794), (0x2799, 0x2799), (0x279B, 0x27A1), (0x27A5, 0x27A6), (0x27A8, 0x27AF), (0x27B1, 0x27B1), (0x27B3, 0x27B3), (0x27B5, 0x27B5), (0x27B8, 0x27B8), (0x27BA, 0x27BE), (0x27F0, 0x27F1), (0x27F4, 0x27FF), (0x2900, 0x2920), (0x2934, 0x2937), (0x2942, 0x2975), (0x297C, 0x297F), (0x2B04, 0x2B07), (0x2B0C, 0x2B11), (0x2B30, 0x2B3E), (0x2B40, 0x2B4C), (0x2B60, 0x2B65), (0x2B6A, 0x2B6D), (0x2B70, 0x2B73), (0x2B7A, 0x2B7D), (0x2B80, 0x2B87), (0x2B95, 0x2B95), (0x2BA0, 0x2BAF), (0x2BB8, 0x2BB8)]),
            MakeCategory("B", "infix", 4.0 / 18, 4.0 / 18, OperatorProperty.None,
                [(0x002B, 0x002B), (0x002D, 0x002D), (0x00B1, 0x00B1), (0x00F7, 0x00F7), (0x0322, 0x0322), (0x2044, 0x2044), (0x2212, 0x2216), (0x2227, 0x222A), (0x2236, 0x2236), (0x2238, 0x2238), (0x228C, 0x228E), (0x2293, 0x2296), (0x2298, 0x2298), (0x229D, 0x229F), (0x22BB, 0x22BD), (0x22CE, 0x22CF), (0x22D2, 0x22D3), (0x2795, 0x2797), (0x29B8, 0x29B8), (0x29BC, 0x29BC), (0x29C4, 0x29C5), (0x29F5, 0x29FB), (0x2A1F, 0x2A2E), (0x2A38, 0x2A3A), (0x2A3E, 0x2A3E), (0x2A40, 0x2A4F), (0x2A51, 0x2A63), (0x2ADB, 0x2ADB), (0x2AF6, 0x2AF6), (0x2AFB, 0x2AFB), (0x2AFD, 0x2AFD)]),
            MakeCategory("C", "infix", 3.0 / 18, 3.0 / 18, OperatorProperty.None,
                [(0x0025, 0x0025), (0x002A, 0x002A), (0x002E, 0x002E), (0x003F, 0x0040), (0x005E, 0x005E), (0x00B7, 0x00B7), (0x00D7, 0x00D7), (0x0323, 0x0323), (0x032E, 0x032E), (0x2022, 0x2022), (0x2043, 0x2043), (0x2217, 0x2219), (0x2240, 0x2240), (0x2297, 0x2297), (0x2299, 0x229B), (0x22A0, 0x22A1), (0x22BA, 0x22BA), (0x22C4, 0x22C7), (0x22C9, 0x22CC), (0x2305, 0x2306), (0x27CB, 0x27CB), (0x27CD, 0x27CD), (0x29C6, 0x29C8), (0x29D4, 0x29D7), (0x29E2, 0x29E2), (0x2A1D, 0x2A1E), (0x2A2F, 0x2A37), (0x2A3B, 0x2A3D), (0x2A3F, 0x2A3F), (0x2A50, 0x2A50), (0x2A64, 0x2A65), (0x2ADC, 0x2ADD), (0x2AFE, 0x2AFE)]),
            MakeCategory("D", "prefix", 0, 0, OperatorProperty.None,
                [(0x0021, 0x0021), (0x002B, 0x002B), (0x002D, 0x002D), (0x00AC, 0x00AC), (0x00B1, 0x00B1), (0x0331, 0x0331), (0x2018, 0x2018), (0x201C, 0x201C), (0x2200, 0x2201), (0x2203, 0x2204), (0x2207, 0x2207), (0x2212, 0x2213), (0x221F, 0x2222), (0x2234, 0x2235), (0x223C, 0x223C), (0x22BE, 0x22BF), (0x2310, 0x2310), (0x2319, 0x2319), (0x2795, 0x2796), (0x27C0, 0x27C0), (0x299B, 0x29AF), (0x2AEC, 0x2AED)]),
            MakeCategory("E", "postfix", 0, 0, OperatorProperty.None,
                [(0x0021, 0x0022), (0x0025, 0x0027), (0x0060, 0x0060), (0x00A8, 0x00A8), (0x00B0, 0x00B0), (0x00B2, 0x00B4), (0x00B8, 0x00B9), (0x02CA, 0x02CB), (0x02D8, 0x02DA), (0x02DD, 0x02DD), (0x0311, 0x0311), (0x0320, 0x0320), (0x0325, 0x0325), (0x0327, 0x0327), (0x0331, 0x0331), (0x2019, 0x201B), (0x201D, 0x201F), (0x2032, 0x2037), (0x2057, 0x2057), (0x20DB, 0x20DC), (0x23CD, 0x23CD)]),
            MakeCategory("F", "prefix", 0, 0, OperatorProperty.Stretchy | OperatorProperty.Symmetric,
                [(0x0028, 0x0028), (0x005B, 0x005B), (0x007B, 0x007B), (0x007C, 0x007C), (0x2016, 0x2016), (0x2308, 0x2308), (0x230A, 0x230A), (0x2329, 0x2329), (0x2772, 0x2772), (0x27E6, 0x27E6), (0x27E8, 0x27E8), (0x27EA, 0x27EA), (0x27EC, 0x27EC), (0x27EE, 0x27EE), (0x2980, 0x2980), (0x2983, 0x2983), (0x2985, 0x2985), (0x2987, 0x2987), (0x2989, 0x2989), (0x298B, 0x298B), (0x298D, 0x298D), (0x298F, 0x298F), (0x2991, 0x2991), (0x2993, 0x2993), (0x2995, 0x2995), (0x2997, 0x2997), (0x2999, 0x2999), (0x29D8, 0x29D8), (0x29DA, 0x29DA), (0x29FC, 0x29FC)]),
            MakeCategory("G", "postfix", 0, 0, OperatorProperty.Stretchy | OperatorProperty.Symmetric,
                [(0x0029, 0x0029), (0x005D, 0x005D), (0x007C, 0x007C), (0x007D, 0x007D), (0x2016, 0x2016), (0x2309, 0x2309), (0x230B, 0x230B), (0x232A, 0x232A), (0x2773, 0x2773), (0x27E7, 0x27E7), (0x27E9, 0x27E9), (0x27EB, 0x27EB), (0x27ED, 0x27ED), (0x27EF, 0x27EF), (0x2980, 0x2980), (0x2984, 0x2984), (0x2986, 0x2986), (0x2988, 0x2988), (0x298A, 0x298A), (0x298C, 0x298C), (0x298E, 0x298E), (0x2990, 0x2990), (0x2992, 0x2992), (0x2994, 0x2994), (0x2996, 0x2996), (0x2998, 0x2998), (0x2999, 0x2999), (0x29D9, 0x29D9), (0x29DB, 0x29DB), (0x29FD, 0x29FD)]),
            MakeCategory("H", "prefix", 3.0 / 18, 3.0 / 18, OperatorProperty.Symmetric | OperatorProperty.LargeOp,
                [(0x222B, 0x2233), (0x2A0B, 0x2A1C)]),
            MakeCategory("I", "postfix", 0, 0, OperatorProperty.Stretchy,
                [(0x005E, 0x005F), (0x007E, 0x007E), (0x00AF, 0x00AF), (0x02C6, 0x02C7), (0x02C9, 0x02C9), (0x02CD, 0x02CD), (0x02DC, 0x02DC), (0x02F7, 0x02F7), (0x0302, 0x0302), (0x203E, 0x203E), (0x2322, 0x2323), (0x23B4, 0x23B5), (0x23DC, 0x23E1)]),
            MakeCategory("J", "prefix", 3.0 / 18, 3.0 / 18, OperatorProperty.Symmetric | OperatorProperty.LargeOp | OperatorProperty.MovableLimits,
                [(0x220F, 0x2211), (0x22C0, 0x22C3), (0x2A00, 0x2A0A), (0x2A1D, 0x2A1E), (0x2AFC, 0x2AFC), (0x2AFF, 0x2AFF)]),
            MakeCategory("K", "infix", 0, 0, OperatorProperty.None,
                [(0x002F, 0x002F), (0x005C, 0x005C), (0x005F, 0x005F), (0x2061, 0x2064), (0x2206, 0x2206)]),
            MakeCategory("L", "prefix", 3.0 / 18, 0, OperatorProperty.None,
                [(0x2145, 0x2146), (0x2202, 0x2202), (0x221A, 0x221C)]),
            MakeCategory("M", "infix", 0, 3.0 / 18, OperatorProperty.None,
                [(0x002C, 0x002C), (0x003A, 0x003A), (0x003B, 0x003B)]),
        ];

        /// <summary>Figure 24's <c>Operators_2_ascii_chars</c> special table - two-ASCII-character
        /// operator strings, each remapped to a synthetic single codepoint (<c>U+0320 + its index
        /// here</c>) before the general category lookup, per the classification algorithm below.</summary>
        static readonly string[] TwoAsciiChars =
            ["!!", "!=", "&&", "**", "*=", "++", "+=", "--", "-=", "->", "//", "/=", ":=", "<=", "<>", "==", ">=", "||"];

        /// <summary>Resolves an <c>&lt;mo&gt;</c>'s dictionary entry for its text <paramref name="content"/>
        /// and effective <paramref name="form"/> (<c>"infix"</c>/<c>"prefix"</c>/<c>"postfix"</c>), per
        /// MathML Core Appendix B.1's classification algorithm. Never fails - an operator matching no
        /// category resolves to the <c>Default</c> entry.</summary>
        public static OperatorDictionaryEntry Classify(string content, string form)
        {
            if (TryGetClassificationCodepoint(content, form, out var codepoint, out var arabicSpecialCase))
            {
                if (arabicSpecialCase)
                    return Categories[8].Entry; // category "I" - the spec's hardcoded special case

                foreach (var category in Categories)
                {
                    if (category.Form == form && category.Contains(codepoint))
                        return category.Entry;
                }
            }

            return DefaultEntry;
        }

        /// <summary>MathML Core Appendix B.1's steps for turning (Content, Form) into the codepoint the
        /// category tables above are keyed on - false means "no codepoint to classify, use Default"
        /// (content isn't 1-2 UTF-16 units long, a lone character in the always-Default U+0320-U+03FF
        /// range, or an unrecognized 2-character combination).</summary>
        static bool TryGetClassificationCodepoint(string content, string form, out int codepoint, out bool arabicSpecialCase)
        {
            codepoint = 0;
            arabicSpecialCase = false;

            if (content.Length == 1)
            {
                char c = content[0];
                if (c is >= (char)0x0320 and <= (char)0x03FF)
                    return false;
                codepoint = c;
                return true;
            }

            if (content.Length != 2)
                return false;

            // The Arabic pair special case (U+1EEF0/U+1EEF1, each a UTF-16 surrogate pair) maps directly
            // to category I when used as a postfix operator - an extreme edge case, checked defensively.
            if (form == "postfix" &&
                System.Text.Rune.TryGetRuneAt(content, 0, out var rune) &&
                rune.Value is 0x1EEF0 or 0x1EEF1)
            {
                arabicSpecialCase = true;
                return true;
            }

            char second = content[1];
            if (second is (char)0x0338 or (char)0x20D2)
            {
                // A combining overlay (negation slash / vertical line) - classify by the base character.
                codepoint = content[0];
                return true;
            }

            var asciiIndex = Array.IndexOf(TwoAsciiChars, content);
            if (asciiIndex >= 0)
            {
                codepoint = 0x0320 + asciiIndex;
                return true;
            }

            return false;
        }
    }
}
