namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// The default word boundaries of UAX #29, section 4, rules WB1 to WB999.
    /// </summary>
    internal static class WordBreaker
    {
        private const int ClassMask = 0x1F;

        /// <summary>
        /// Finds the boundaries, as scalar indices 0 to <see cref="ScalarText.Count"/>: entry <c>k</c> is <see langword="true"/>
        /// when there is a boundary before the scalar at <c>k</c>. Empty text has none.
        /// </summary>
        internal static bool[] FindBoundaries(in ScalarText text)
        {
            int count = text.Count;
            var boundaries = new bool[count + 1];
            if (count == 0)
            {
                return boundaries;
            }

            boundaries[0] = true;
            boundaries[count] = true;

            var classes = new WordBreakClass[count];
            var pictographic = new bool[count];
            for (int k = 0; k < count; k++)
            {
                int value = SegmentationData.Word(text.Code[k]);
                classes[k] = (WordBreakClass)(value & ClassMask);
                pictographic[k] = (value & SegmentationData.WordPictographic) != 0;
            }

            // WB4: an Extend, Format or ZWJ joins the character before it, except after a newline. What is left are the
            // characters the other rules see.
            var effective = new int[count];
            var absorbed = new bool[count];
            int effectiveCount = 0;
            for (int k = 0; k < count; k++)
            {
                if (k > 0 && IsIgnorable(classes[k]) && !IsNewline(classes[k - 1]))
                {
                    absorbed[k] = true;
                }
                else
                {
                    effective[effectiveCount++] = k;
                }
            }

            int regionalIndicators = 0;   // consecutive effective RI before the position
            for (int e = 0; e < effectiveCount; e++)
            {
                int k = effective[e];
                var b = classes[k];

                if (e == 0)
                {
                    regionalIndicators = b == WordBreakClass.RegionalIndicator ? 1 : 0;
                    continue;
                }

                var a = classes[effective[e - 1]];

                // The rules that read the original neighbour, before WB4 takes effect.
                bool decided = false;
                bool breaks = false;
                var originalPrevious = classes[k - 1];

                if (originalPrevious == WordBreakClass.CR && b == WordBreakClass.LF)
                {
                    decided = true;
                }
                else if (IsNewline(originalPrevious) || IsNewline(b))
                {
                    decided = true;
                    breaks = true;
                }
                else if (originalPrevious == WordBreakClass.ZWJ && pictographic[k])
                {
                    decided = true;
                }
                else if (originalPrevious == WordBreakClass.WSegSpace && b == WordBreakClass.WSegSpace)
                {
                    decided = true;
                }

                if (!decided)
                {
                    breaks = DecideEffective(classes, effective, effectiveCount, e, a, b, regionalIndicators);
                }

                boundaries[k] = breaks;
                regionalIndicators = b == WordBreakClass.RegionalIndicator ? regionalIndicators + 1 : 0;
            }

            return boundaries;
        }

        private static bool DecideEffective(WordBreakClass[] classes, int[] effective, int effectiveCount, int e, WordBreakClass a, WordBreakClass b, int regionalIndicators)
        {
            var a2 = e >= 2 ? classes[effective[e - 2]] : WordBreakClass.Other;
            bool hasA2 = e >= 2;
            var b2 = e + 1 < effectiveCount ? classes[effective[e + 1]] : WordBreakClass.Other;
            bool hasB2 = e + 1 < effectiveCount;

            // WB5
            if (IsLetter(a) && IsLetter(b))
            {
                return false;
            }

            // WB6, WB7
            if (IsLetter(a) && IsMidLetterOrQuote(b) && hasB2 && IsLetter(b2))
            {
                return false;
            }

            if (hasA2 && IsLetter(a2) && IsMidLetterOrQuote(a) && IsLetter(b))
            {
                return false;
            }

            // WB7a, WB7b, WB7c
            if (a == WordBreakClass.HebrewLetter && b == WordBreakClass.SingleQuote)
            {
                return false;
            }

            if (a == WordBreakClass.HebrewLetter && b == WordBreakClass.DoubleQuote && hasB2 && b2 == WordBreakClass.HebrewLetter)
            {
                return false;
            }

            if (hasA2 && a2 == WordBreakClass.HebrewLetter && a == WordBreakClass.DoubleQuote && b == WordBreakClass.HebrewLetter)
            {
                return false;
            }

            // WB8, WB9, WB10
            if (a == WordBreakClass.Numeric && b == WordBreakClass.Numeric)
            {
                return false;
            }

            if (IsLetter(a) && b == WordBreakClass.Numeric)
            {
                return false;
            }

            if (a == WordBreakClass.Numeric && IsLetter(b))
            {
                return false;
            }

            // WB11, WB12
            if (hasA2 && a2 == WordBreakClass.Numeric && IsMidNumOrQuote(a) && b == WordBreakClass.Numeric)
            {
                return false;
            }

            if (a == WordBreakClass.Numeric && IsMidNumOrQuote(b) && hasB2 && b2 == WordBreakClass.Numeric)
            {
                return false;
            }

            // WB13, WB13a, WB13b
            if (a == WordBreakClass.Katakana && b == WordBreakClass.Katakana)
            {
                return false;
            }

            if ((IsLetter(a) || a == WordBreakClass.Numeric || a == WordBreakClass.Katakana || a == WordBreakClass.ExtendNumLet) && b == WordBreakClass.ExtendNumLet)
            {
                return false;
            }

            if (a == WordBreakClass.ExtendNumLet && (IsLetter(b) || b == WordBreakClass.Numeric || b == WordBreakClass.Katakana))
            {
                return false;
            }

            // WB15, WB16
            if (a == WordBreakClass.RegionalIndicator && b == WordBreakClass.RegionalIndicator && (regionalIndicators & 1) == 1)
            {
                return false;
            }

            // WB999
            return true;
        }

        private static bool IsIgnorable(WordBreakClass value) => value is WordBreakClass.Extend or WordBreakClass.Format or WordBreakClass.ZWJ;

        private static bool IsNewline(WordBreakClass value) => value is WordBreakClass.Newline or WordBreakClass.CR or WordBreakClass.LF;

        private static bool IsLetter(WordBreakClass value) => value is WordBreakClass.ALetter or WordBreakClass.HebrewLetter;

        private static bool IsMidLetterOrQuote(WordBreakClass value) =>
            value is WordBreakClass.MidLetter or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;

        private static bool IsMidNumOrQuote(WordBreakClass value) =>
            value is WordBreakClass.MidNum or WordBreakClass.MidNumLet or WordBreakClass.SingleQuote;
    }
}
