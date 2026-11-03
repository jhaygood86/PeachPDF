using PeachBrackets.Css;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Parse;
using System;

namespace PeachBrackets.Html.Pages
{
    /// <summary>The printer's marks an <c>@page</c> asks for with <c>marks</c> (css-page-3 §12).</summary>
    [Flags]
    internal enum PageMarkKinds
    {
        None = 0,
        Crop = 1,
        Cross = 2,
    }

    /// <summary>
    /// What an <c>@page</c> rule's <c>bleed</c> and <c>marks</c> add around the page box (css-page-3 §12-13): a bleed area that
    /// extends the page's artwork past the trim edge, and, when marks are asked for, a further strip outside it that holds them.
    /// </summary>
    /// <param name="BleedPt">the bleed width in points (0 for none)</param>
    /// <param name="Marks">the marks to draw outside the bleed area</param>
    internal readonly record struct PagePrintExtras(double BleedPt, PageMarkKinds Marks)
    {
        /// <summary>Space left between the bleed area and a crop mark, so the mark never touches the artwork, in points.</summary>
        internal const double MarkGapPt = 3;

        /// <summary>The length of a crop mark's stroke, in points.</summary>
        internal const double CropLengthPt = 12;

        /// <summary>The width of the strip outside the bleed area that holds the marks, in points: the gap and the longest mark.</summary>
        internal const double MarkAreaPt = MarkGapPt + CropLengthPt;

        public static PagePrintExtras None => default;

        /// <summary>How far the sheet extends beyond the bleed area, in points: the strip the marks are drawn in.</summary>
        public double OutsidePt => Marks == PageMarkKinds.None ? 0 : MarkAreaPt;

        public bool IsNone => BleedPt <= 0 && Marks == PageMarkKinds.None;

        /// <summary>
        /// Resolves the declared <paramref name="bleed"/> and <paramref name="marks"/> text. <c>bleed: auto</c> (and anything that is not a
        /// non-negative length) is 6pt when crop marks are asked for and 0 otherwise (§13); <c>marks</c> that is not <c>none</c> or
        /// <c>crop</c>/<c>cross</c> in any order is <c>none</c>.
        /// </summary>
        public static PagePrintExtras Resolve(string? bleed, string? marks, PageLengthContext? context)
        {
            var kinds = ParseMarks(marks);

            double? declared = null;
            if (!string.IsNullOrWhiteSpace(bleed) && !bleed.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var length = context is { } c ? DomParser.ParseLengthToPdfPoints(bleed, c) : DomParser.ParseLengthToPdfPoints(bleed);
                if (length is >= 0) declared = length;
            }

            var autoBleed = (kinds & PageMarkKinds.Crop) != 0 ? 6 : 0;
            return new PagePrintExtras(declared ?? autoBleed, kinds);
        }

        private static PageMarkKinds ParseMarks(string? marks)
        {
            if (string.IsNullOrWhiteSpace(marks)) return PageMarkKinds.None;

            var kinds = PageMarkKinds.None;
            foreach (var word in marks.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Equals("crop", StringComparison.OrdinalIgnoreCase)) kinds |= PageMarkKinds.Crop;
                else if (word.Equals("cross", StringComparison.OrdinalIgnoreCase)) kinds |= PageMarkKinds.Cross;
                else return PageMarkKinds.None;
            }

            return kinds;
        }
    }
}
