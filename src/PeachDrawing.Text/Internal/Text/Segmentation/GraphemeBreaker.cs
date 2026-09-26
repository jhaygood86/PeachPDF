namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// The extended grapheme cluster boundaries of UAX #29, section 3, rules GB1 to GB999.
    /// </summary>
    internal static class GraphemeBreaker
    {
        private const int ClassMask = 0x1F;
        private const int ConjunctShift = 6;

        // Indic_Conjunct_Break values in the table (bits 6-7).
        private const int ConjunctLinker = 1;
        private const int ConjunctConsonant = 2;
        private const int ConjunctExtend = 3;

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

            // The state the multi-character rules carry from one scalar to the next.
            int regionalIndicators = 0;       // consecutive RI scalars before the current position (GB12, GB13)
            int pictographic = 0;             // 0 none, 1 after ExtPict Extend*, 2 after ExtPict Extend* ZWJ (GB11)
            bool afterLinker = false;         // after InCB=Linker InCB=Extend* (GB9c)

            int previousValue = SegmentationData.Grapheme(text.Code[0]);
            Advance(previousValue, ref regionalIndicators, ref pictographic, ref afterLinker);

            for (int k = 1; k < count; k++)
            {
                int value = SegmentationData.Grapheme(text.Code[k]);
                int before = previousValue & ClassMask;
                int after = value & ClassMask;

                bool breaks = Decide(before, after, value, regionalIndicators, pictographic, afterLinker);
                boundaries[k] = breaks;

                Advance(value, ref regionalIndicators, ref pictographic, ref afterLinker);
                previousValue = value;
            }

            return boundaries;
        }

        private static bool Decide(int before, int after, int afterValue, int regionalIndicators, int pictographic, bool afterLinker)
        {
            var a = (GraphemeBreakClass)before;
            var b = (GraphemeBreakClass)after;

            // GB3
            if (a == GraphemeBreakClass.CR && b == GraphemeBreakClass.LF)
            {
                return false;
            }

            // GB4, GB5
            if (a is GraphemeBreakClass.Control or GraphemeBreakClass.CR or GraphemeBreakClass.LF)
            {
                return true;
            }

            if (b is GraphemeBreakClass.Control or GraphemeBreakClass.CR or GraphemeBreakClass.LF)
            {
                return true;
            }

            // GB6, GB7, GB8
            if (a == GraphemeBreakClass.L && b is GraphemeBreakClass.L or GraphemeBreakClass.V or GraphemeBreakClass.LV or GraphemeBreakClass.LVT)
            {
                return false;
            }

            if (a is GraphemeBreakClass.LV or GraphemeBreakClass.V && b is GraphemeBreakClass.V or GraphemeBreakClass.T)
            {
                return false;
            }

            if (a is GraphemeBreakClass.LVT or GraphemeBreakClass.T && b == GraphemeBreakClass.T)
            {
                return false;
            }

            // GB9, GB9a, GB9b
            if (b is GraphemeBreakClass.Extend or GraphemeBreakClass.ZWJ or GraphemeBreakClass.SpacingMark)
            {
                return false;
            }

            if (a == GraphemeBreakClass.Prepend)
            {
                return false;
            }

            // GB9c
            if (afterLinker && ((afterValue >> ConjunctShift) & 3) == ConjunctConsonant)
            {
                return false;
            }

            // GB11
            if (pictographic == 2 && (afterValue & SegmentationData.GraphemePictographic) != 0)
            {
                return false;
            }

            // GB12, GB13
            if (a == GraphemeBreakClass.RegionalIndicator && b == GraphemeBreakClass.RegionalIndicator && (regionalIndicators & 1) == 1)
            {
                return false;
            }

            // GB999
            return true;
        }

        private static void Advance(int value, ref int regionalIndicators, ref int pictographic, ref bool afterLinker)
        {
            var cls = (GraphemeBreakClass)(value & ClassMask);

            regionalIndicators = cls == GraphemeBreakClass.RegionalIndicator ? regionalIndicators + 1 : 0;

            if ((value & SegmentationData.GraphemePictographic) != 0)
            {
                pictographic = 1;
            }
            else if (pictographic == 1 && cls == GraphemeBreakClass.Extend)
            {
                // ExtPict Extend* stays
            }
            else if (pictographic == 1 && cls == GraphemeBreakClass.ZWJ)
            {
                pictographic = 2;
            }
            else
            {
                pictographic = 0;
            }

            int conjunct = (value >> ConjunctShift) & 3;
            if (conjunct == ConjunctLinker)
            {
                afterLinker = true;
            }
            else if (conjunct != ConjunctExtend)
            {
                afterLinker = false;
            }
        }
    }
}
