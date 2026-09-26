namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// The default sentence boundaries of UAX #29, section 5, rules SB1 to SB998.
    /// </summary>
    internal static class SentenceBreaker
    {
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

            var classes = new SentenceBreakClass[count];
            for (int k = 0; k < count; k++)
            {
                classes[k] = (SentenceBreakClass)SegmentationData.Sentence(text.Code[k]);
            }

            // SB5: an Extend or Format joins the character before it, except after a paragraph separator.
            var effective = new int[count];
            int effectiveCount = 0;
            for (int k = 0; k < count; k++)
            {
                if (k > 0 && IsIgnorable(classes[k]) && !IsParagraphSeparator(classes[k - 1]))
                {
                    continue;
                }

                effective[effectiveCount++] = k;
            }

            for (int e = 1; e < effectiveCount; e++)
            {
                int k = effective[e];
                var originalPrevious = classes[k - 1];

                bool breaks;
                if (originalPrevious == SentenceBreakClass.CR && classes[k] == SentenceBreakClass.LF)
                {
                    breaks = false;                                       // SB3
                }
                else if (IsParagraphSeparator(originalPrevious))
                {
                    breaks = true;                                        // SB4
                }
                else
                {
                    breaks = Decide(classes, effective, effectiveCount, e);
                }

                boundaries[k] = breaks;
            }

            return boundaries;
        }

        private static bool Decide(SentenceBreakClass[] classes, int[] effective, int effectiveCount, int e)
        {
            var a = classes[effective[e - 1]];
            var b = classes[effective[e]];

            // SB6
            if (a == SentenceBreakClass.ATerm && b == SentenceBreakClass.Numeric)
            {
                return false;
            }

            // SB7
            if (a == SentenceBreakClass.ATerm && e >= 2 && b == SentenceBreakClass.Upper
                && classes[effective[e - 2]] is SentenceBreakClass.Upper or SentenceBreakClass.Lower)
            {
                return false;
            }

            // What SB8 to SB11 look back for: SATerm Close* Sp*
            int spaces = e - 1;
            while (spaces >= 0 && classes[effective[spaces]] == SentenceBreakClass.Sp)
            {
                spaces--;
            }

            int closes = spaces;
            while (closes >= 0 && classes[effective[closes]] == SentenceBreakClass.Close)
            {
                closes--;
            }

            bool afterTerminator = closes >= 0 && classes[effective[closes]] is SentenceBreakClass.ATerm or SentenceBreakClass.STerm;
            bool afterATerm = afterTerminator && classes[effective[closes]] == SentenceBreakClass.ATerm;
            bool onlyCloseAfterTerminator = afterTerminator && spaces == e - 1;       // SATerm Close*, no Sp
            bool noSpaceBeforeClose = afterTerminator;

            // SB8: ATerm Close* Sp* × ( ¬(OLetter | Upper | Lower | ParaSep | SATerm) )* Lower
            if (afterATerm)
            {
                for (int f = e; f < effectiveCount; f++)
                {
                    var c = classes[effective[f]];
                    if (c == SentenceBreakClass.Lower)
                    {
                        return false;
                    }

                    if (c is SentenceBreakClass.OLetter or SentenceBreakClass.Upper or SentenceBreakClass.Sep or SentenceBreakClass.CR
                        or SentenceBreakClass.LF or SentenceBreakClass.STerm or SentenceBreakClass.ATerm)
                    {
                        break;
                    }
                }
            }

            // SB8a: SATerm Close* Sp* × (SContinue | SATerm)
            if (afterTerminator && b is SentenceBreakClass.SContinue or SentenceBreakClass.STerm or SentenceBreakClass.ATerm)
            {
                return false;
            }

            // SB9: SATerm Close* × (Close | Sp | ParaSep)
            if (onlyCloseAfterTerminator && (b == SentenceBreakClass.Close || b == SentenceBreakClass.Sp || IsParagraphSeparator(b)))
            {
                return false;
            }

            // SB10: SATerm Close* Sp* × (Sp | ParaSep)
            if (noSpaceBeforeClose && (b == SentenceBreakClass.Sp || IsParagraphSeparator(b)))
            {
                return false;
            }

            // SB11: SATerm Close* Sp* ParaSep? ÷
            if (afterTerminator)
            {
                return true;
            }

            // SB998
            return false;
        }

        private static bool IsIgnorable(SentenceBreakClass value) => value is SentenceBreakClass.Extend or SentenceBreakClass.Format;

        private static bool IsParagraphSeparator(SentenceBreakClass value) =>
            value is SentenceBreakClass.Sep or SentenceBreakClass.CR or SentenceBreakClass.LF;
    }
}
