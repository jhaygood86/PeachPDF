using PeachDrawing.Text.Unicode;
using System;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// The line breaking algorithm of UAX #14, rules LB1 to LB31, with the tailorings CSS Text 3 names.
    /// </summary>
    internal static class LineBreakAlgorithm
    {
        private const int ClassMask = 0x3F;
        private const int DottedCircle = 0x25CC;

        /// <summary>One line breaking unit: a character and the combining marks and joiners that follow it (rule LB9).</summary>
        private struct Unit
        {
            /// <summary>The line breaking class after LB1, LB10 and the tailoring.</summary>
            internal LineBreakClass Class;

            /// <summary>The class after LB1 and LB10 but before the word-break tailoring, which the keep-all suppression reads.</summary>
            internal LineBreakClass Resolved;

            /// <summary>The scalar index of the unit's first character.</summary>
            internal int First;

            internal bool EndsWithJoiner;
            internal bool EastAsian;
            internal bool InitialQuote;
            internal bool FinalQuote;
            internal bool Pictographic;
            internal bool Unassigned;
            internal bool IsDottedCircle;
        }

        /// <summary>
        /// Finds the opportunities for every UTF-16 index of the text, and one past its end.
        /// </summary>
        internal static LineBreakOpportunity[] FindOpportunities(ReadOnlySpan<char> source, in LineBreakOptions options)
        {
            var text = new ScalarText(source);
            var result = new LineBreakOpportunity[source.Length + 1];
            result[source.Length] = LineBreakOpportunity.Mandatory;     // LB3 (and, for empty text, the only entry)
            if (text.Count == 0)
            {
                return result;
            }

            // LB2: never break at the start of text, which is the default, LineBreakOpportunity.Prohibited.
            var units = BuildUnits(text, options, out int unitCount);

            for (int i = 1; i < unitCount; i++)
            {
                var decision = Decide(units, unitCount, i);
                if (decision != LineBreakOpportunity.Prohibited)
                {
                    if (decision == LineBreakOpportunity.Allowed && options.WordBreak == WordBreakMode.KeepAll && IsKeepAllPair(units[i - 1].Resolved, units[i].Resolved))
                    {
                        decision = LineBreakOpportunity.Prohibited;
                    }

                    result[text.Offset[units[i].First]] = decision;
                }
            }

            if (options.Strictness == LineBreakStrictness.Anywhere)
            {
                ApplyAnywhere(text, result);
            }

            return result;
        }

        private static Unit[] BuildUnits(in ScalarText text, in LineBreakOptions options, out int unitCount)
        {
            var units = new Unit[text.Count];
            unitCount = 0;

            // CSS Text 3 leaves the sets of rules for line-break to the user agent, but requires some breaks: auto is normal.
            var strictness = options.Strictness is LineBreakStrictness.Auto or LineBreakStrictness.Anywhere ? LineBreakStrictness.Normal : options.Strictness;

            for (int k = 0; k < text.Count; k++)
            {
                int cp = text.Code[k];
                int value = SegmentationData.LineBreak(cp);
                var raw = (LineBreakClass)(value & ClassMask);

                // LB1: resolve the classes that depend on criteria outside the algorithm.
                var resolved = raw switch
                {
                    LineBreakClass.AI or LineBreakClass.SG or LineBreakClass.XX => LineBreakClass.AL,
                    LineBreakClass.SA => (value & SegmentationData.LineBreakMark) != 0 ? LineBreakClass.CM : LineBreakClass.AL,
                    LineBreakClass.CJ => strictness == LineBreakStrictness.Loose ? LineBreakClass.ID : LineBreakClass.NS,
                    _ => raw,
                };

                // The CJK hyphen-like characters may start a line in the normal and loose settings.
                if (strictness != LineBreakStrictness.Strict && cp is 0x301C or 0x30A0)
                {
                    resolved = LineBreakClass.ID;
                }

                if (strictness == LineBreakStrictness.Loose)
                {
                    // Iteration marks, centred punctuation and inseparable characters may start a line; a hyphen only after an ideograph.
                    if (IsLooseIdeographic(cp) || resolved == LineBreakClass.IN)
                    {
                        resolved = LineBreakClass.ID;
                    }
                    else if (cp is 0x2010 or 0x2013 && unitCount > 0 && units[unitCount - 1].Class == LineBreakClass.ID)
                    {
                        resolved = LineBreakClass.ID;
                    }
                }

                // LB9: a combining mark or joiner belongs to the character before it, unless that one is a hard break, a space or a
                // zero width space.
                if ((resolved == LineBreakClass.CM || resolved == LineBreakClass.ZWJ) && unitCount > 0
                    && units[unitCount - 1].Resolved is not (LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                        or LineBreakClass.SP or LineBreakClass.ZW))
                {
                    units[unitCount - 1].EndsWithJoiner = resolved == LineBreakClass.ZWJ;
                    continue;
                }

                var unit = new Unit { First = k, EndsWithJoiner = resolved == LineBreakClass.ZWJ };
                if (resolved == LineBreakClass.CM || resolved == LineBreakClass.ZWJ)
                {
                    // LB10: any remaining mark or joiner is a letter, in every respect U+0041.
                    unit.Resolved = LineBreakClass.AL;
                }
                else
                {
                    unit.Resolved = resolved;
                    unit.EastAsian = (value & SegmentationData.LineBreakEastAsian) != 0;
                    unit.InitialQuote = (value & SegmentationData.LineBreakPi) != 0;
                    unit.FinalQuote = (value & SegmentationData.LineBreakPf) != 0;
                    unit.Pictographic = (value & SegmentationData.LineBreakPictographic) != 0;
                    unit.Unassigned = (value & SegmentationData.LineBreakUnassigned) != 0;
                    unit.IsDottedCircle = cp == DottedCircle;
                }

                unit.Class = unit.Resolved;
                if (options.WordBreak == WordBreakMode.BreakAll && unit.Resolved is LineBreakClass.NU or LineBreakClass.AL)
                {
                    unit.Class = LineBreakClass.ID;
                }

                units[unitCount++] = unit;
            }

            return units;
        }

        private static LineBreakOpportunity Decide(Unit[] units, int count, int i)
        {
            var a = units[i - 1].Class;
            var b = units[i].Class;
            bool hasNext = i + 1 < count;
            var next = hasNext ? units[i + 1].Class : LineBreakClass.XX;

            // LB4, LB5, LB6
            if (a == LineBreakClass.BK)
            {
                return LineBreakOpportunity.Mandatory;
            }

            if (a == LineBreakClass.CR)
            {
                return b == LineBreakClass.LF ? LineBreakOpportunity.Prohibited : LineBreakOpportunity.Mandatory;
            }

            if (a is LineBreakClass.LF or LineBreakClass.NL)
            {
                return LineBreakOpportunity.Mandatory;
            }

            if (b is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB7
            if (b == LineBreakClass.SP || b == LineBreakClass.ZW)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB8
            int beforeSpaces = SkipSpacesBack(units, i - 1);
            if (beforeSpaces >= 0 && units[beforeSpaces].Class == LineBreakClass.ZW)
            {
                return LineBreakOpportunity.Allowed;
            }

            // LB8a
            if (units[i - 1].EndsWithJoiner)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB11
            if (a == LineBreakClass.WJ || b == LineBreakClass.WJ)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB12, LB12a
            if (a == LineBreakClass.GL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (b == LineBreakClass.GL && a is not (LineBreakClass.SP or LineBreakClass.HY or LineBreakClass.HH))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB13
            if (b is LineBreakClass.CL or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.SY)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB14
            if (beforeSpaces >= 0 && units[beforeSpaces].Class == LineBreakClass.OP)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB15a
            if (beforeSpaces >= 0 && units[beforeSpaces].Class == LineBreakClass.QU && units[beforeSpaces].InitialQuote)
            {
                int before = beforeSpaces - 1;
                if (before < 0 || units[before].Class is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                    or LineBreakClass.OP or LineBreakClass.QU or LineBreakClass.GL or LineBreakClass.SP or LineBreakClass.ZW)
                {
                    return LineBreakOpportunity.Prohibited;
                }
            }

            // LB15b
            if (b == LineBreakClass.QU && units[i].FinalQuote
                && (!hasNext || next is LineBreakClass.SP or LineBreakClass.GL or LineBreakClass.WJ or LineBreakClass.CL or LineBreakClass.QU
                    or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.IS or LineBreakClass.SY or LineBreakClass.BK or LineBreakClass.CR
                    or LineBreakClass.LF or LineBreakClass.NL or LineBreakClass.ZW))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB15c, LB15d
            if (a == LineBreakClass.SP && b == LineBreakClass.IS && hasNext && next == LineBreakClass.NU)
            {
                return LineBreakOpportunity.Allowed;
            }

            if (b == LineBreakClass.IS)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB16
            if (b == LineBreakClass.NS && beforeSpaces >= 0 && units[beforeSpaces].Class is LineBreakClass.CL or LineBreakClass.CP)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB17
            if (b == LineBreakClass.B2 && beforeSpaces >= 0 && units[beforeSpaces].Class == LineBreakClass.B2)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB18
            if (a == LineBreakClass.SP)
            {
                return LineBreakOpportunity.Allowed;
            }

            // LB19
            if (b == LineBreakClass.QU && !units[i].InitialQuote)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a == LineBreakClass.QU && !units[i - 1].FinalQuote)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB19a
            if (b == LineBreakClass.QU && !units[i - 1].EastAsian)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (b == LineBreakClass.QU && (!hasNext || !units[i + 1].EastAsian))
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a == LineBreakClass.QU && !units[i].EastAsian)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a == LineBreakClass.QU && (i - 2 < 0 || !units[i - 2].EastAsian))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB20
            if (a == LineBreakClass.CB || b == LineBreakClass.CB)
            {
                return LineBreakOpportunity.Allowed;
            }

            // LB20a
            if (a is LineBreakClass.HY or LineBreakClass.HH && b is LineBreakClass.AL or LineBreakClass.HL
                && (i - 2 < 0 || units[i - 2].Class is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                    or LineBreakClass.SP or LineBreakClass.ZW or LineBreakClass.CB or LineBreakClass.GL))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB21
            if (b is LineBreakClass.BA or LineBreakClass.HH or LineBreakClass.HY or LineBreakClass.NS || a == LineBreakClass.BB)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB21a
            if (a is LineBreakClass.HY or LineBreakClass.HH && i - 2 >= 0 && units[i - 2].Class == LineBreakClass.HL && b != LineBreakClass.HL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB21b
            if (a == LineBreakClass.SY && b == LineBreakClass.HL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB22
            if (b == LineBreakClass.IN)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB23
            if ((a is LineBreakClass.AL or LineBreakClass.HL && b == LineBreakClass.NU) || (a == LineBreakClass.NU && b is LineBreakClass.AL or LineBreakClass.HL))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB23a
            if ((a == LineBreakClass.PR && b is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM)
                || (a is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM && b == LineBreakClass.PO))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB24
            if ((a is LineBreakClass.PR or LineBreakClass.PO && b is LineBreakClass.AL or LineBreakClass.HL)
                || (a is LineBreakClass.AL or LineBreakClass.HL && b is LineBreakClass.PR or LineBreakClass.PO))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB25
            if (IsNumberContinuation(units, count, i))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB26, LB27
            if (a == LineBreakClass.JL && b is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.H2 or LineBreakClass.H3)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a is LineBreakClass.JV or LineBreakClass.H2 && b is LineBreakClass.JV or LineBreakClass.JT)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a is LineBreakClass.JT or LineBreakClass.H3 && b == LineBreakClass.JT)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.H2 or LineBreakClass.H3 && b == LineBreakClass.PO)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a == LineBreakClass.PR && b is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.H2 or LineBreakClass.H3)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB28
            if (a is LineBreakClass.AL or LineBreakClass.HL && b is LineBreakClass.AL or LineBreakClass.HL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB28a
            if (IsBrahmicSyllable(units, count, i))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB29
            if (a == LineBreakClass.IS && b is LineBreakClass.AL or LineBreakClass.HL)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB30
            if (a is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU && b == LineBreakClass.OP && !units[i].EastAsian)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (a == LineBreakClass.CP && !units[i - 1].EastAsian && b is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU)
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB30a
            if (a == LineBreakClass.RI && b == LineBreakClass.RI)
            {
                int run = 0;
                for (int j = i - 1; j >= 0 && units[j].Class == LineBreakClass.RI; j--)
                {
                    run++;
                }

                if ((run & 1) == 1)
                {
                    return LineBreakOpportunity.Prohibited;
                }
            }

            // LB30b
            if (b == LineBreakClass.EM && (a == LineBreakClass.EB || (units[i - 1].Pictographic && units[i - 1].Unassigned)))
            {
                return LineBreakOpportunity.Prohibited;
            }

            // LB31
            return LineBreakOpportunity.Allowed;
        }

        /// <summary>The index of the last unit at or before <paramref name="from"/> that is not a space, or -1.</summary>
        private static int SkipSpacesBack(Unit[] units, int from)
        {
            int j = from;
            while (j >= 0 && units[j].Class == LineBreakClass.SP)
            {
                j--;
            }

            return j;
        }

        /// <summary>LB25: do not break numbers.</summary>
        private static bool IsNumberContinuation(Unit[] units, int count, int i)
        {
            var a = units[i - 1].Class;
            var b = units[i].Class;
            bool hasNext = i + 1 < count;
            var next = hasNext ? units[i + 1].Class : LineBreakClass.XX;
            bool hasNext2 = i + 2 < count;
            var next2 = hasNext2 ? units[i + 2].Class : LineBreakClass.XX;

            // NU (SY | IS)* CL × PO|PR, NU (SY | IS)* CP × PO|PR
            if (a is LineBreakClass.CL or LineBreakClass.CP && b is LineBreakClass.PO or LineBreakClass.PR)
            {
                int j = i - 2;
                while (j >= 0 && units[j].Class is LineBreakClass.SY or LineBreakClass.IS)
                {
                    j--;
                }

                if (j >= 0 && units[j].Class == LineBreakClass.NU)
                {
                    return true;
                }
            }

            // NU (SY | IS)* × PO|PR
            if (a is LineBreakClass.NU or LineBreakClass.SY or LineBreakClass.IS && b is LineBreakClass.PO or LineBreakClass.PR)
            {
                int j = i - 1;
                while (j >= 0 && units[j].Class is LineBreakClass.SY or LineBreakClass.IS)
                {
                    j--;
                }

                if (j >= 0 && units[j].Class == LineBreakClass.NU)
                {
                    return true;
                }
            }

            // PO|PR × OP NU, PO|PR × OP IS NU, PO|PR × NU
            if (a is LineBreakClass.PO or LineBreakClass.PR)
            {
                if (b == LineBreakClass.OP && hasNext && (next == LineBreakClass.NU || (next == LineBreakClass.IS && hasNext2 && next2 == LineBreakClass.NU)))
                {
                    return true;
                }

                if (b == LineBreakClass.NU)
                {
                    return true;
                }
            }

            // HY × NU, IS × NU
            if (a is LineBreakClass.HY or LineBreakClass.IS && b == LineBreakClass.NU)
            {
                return true;
            }

            // NU (SY | IS)* × NU
            if (a is LineBreakClass.NU or LineBreakClass.SY or LineBreakClass.IS && b == LineBreakClass.NU)
            {
                int j = i - 1;
                while (j >= 0 && units[j].Class is LineBreakClass.SY or LineBreakClass.IS)
                {
                    j--;
                }

                if (j >= 0 && units[j].Class == LineBreakClass.NU)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>LB28a: do not break inside the orthographic syllables of Brahmic scripts.</summary>
        private static bool IsBrahmicSyllable(Unit[] units, int count, int i)
        {
            var a = units[i - 1];
            var b = units[i];
            bool hasNext = i + 1 < count;

            static bool AksaraOrCircleOrStart(in Unit u) => u.Class is LineBreakClass.AK or LineBreakClass.AS || u.IsDottedCircle;
            static bool AksaraOrCircle(in Unit u) => u.Class == LineBreakClass.AK || u.IsDottedCircle;

            // AP × (AK | ◌ | AS)
            if (a.Class == LineBreakClass.AP && AksaraOrCircleOrStart(b))
            {
                return true;
            }

            // (AK | ◌ | AS) × (VF | VI)
            if (AksaraOrCircleOrStart(a) && b.Class is LineBreakClass.VF or LineBreakClass.VI)
            {
                return true;
            }

            // (AK | ◌ | AS) VI × (AK | ◌)
            if (a.Class == LineBreakClass.VI && i - 2 >= 0 && AksaraOrCircleOrStart(units[i - 2]) && AksaraOrCircle(b))
            {
                return true;
            }

            // (AK | ◌ | AS) × (AK | ◌ | AS) VF
            if (AksaraOrCircleOrStart(a) && AksaraOrCircleOrStart(b) && hasNext && units[i + 1].Class == LineBreakClass.VF)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// The keep-all tailoring of CSS <c>word-break</c>: no break between two typographic letter units, which are the
        /// classes NU, AL, AI and ID (Hangul syllables and complex-context scripts count as letters too).
        /// </summary>
        private static bool IsKeepAllPair(LineBreakClass a, LineBreakClass b) => IsLetterUnit(a) && IsLetterUnit(b);

        private static bool IsLetterUnit(LineBreakClass value) =>
            value is LineBreakClass.NU or LineBreakClass.AL or LineBreakClass.ID or LineBreakClass.H2 or LineBreakClass.H3
                or LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.HL or LineBreakClass.SA;

        /// <summary>
        /// The characters CSS <c>line-break: loose</c> lets a line start with: iteration marks, centred punctuation and the like,
        /// which the default class rules bind to the character before them.
        /// </summary>
        private static bool IsLooseIdeographic(int codePoint) => codePoint switch
        {
            0x3005 or 0x303B or 0x309D or 0x309E or 0x30FD or 0x30FE => true,      // iteration marks
            0x203C or 0x2047 or 0x2048 or 0x2049 or 0xFF01 or 0xFF1F => true,      // exclamation and question mark combinations
            0x30FB or 0xFF1A or 0xFF1B or 0xFF65 => true,                          // centred punctuation
            _ => false,
        };

        /// <summary>CSS <c>line-break: anywhere</c>: a break at every grapheme boundary, and none inside a cluster.</summary>
        private static void ApplyAnywhere(in ScalarText text, LineBreakOpportunity[] result)
        {
            var graphemes = GraphemeBreaker.FindBoundaries(text);
            for (int k = 1; k < text.Count; k++)
            {
                int offset = text.Offset[k];
                if (result[offset] == LineBreakOpportunity.Mandatory)
                {
                    continue;
                }

                result[offset] = graphemes[k] ? LineBreakOpportunity.Allowed : LineBreakOpportunity.Prohibited;
            }
        }
    }
}
