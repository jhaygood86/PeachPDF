using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Html.Core.Entities
{
    internal record CssLineBoxCoordinates
    {
        public required CssLineBox Line { get; set; }

        public required double CurrentX { get; set; }

        public required double CurrentY { get; set; }

        public required double MaxRight { get; set; }

        public required double MaxBottom { get; set; }

        /// <summary>
        /// The fragmentainer this flow is filling, or null when nothing may break — an unpaginated
        /// pass, or inline content inside monolithic content.
        /// </summary>
        public FragmentainerContext? Fragmentainer { get; init; }

        /// <summary>
        /// How many words the walk has visited so far. <see cref="CssLayoutEngine.FlowBox"/> descends
        /// the inline box tree in a fixed order, so this ordinal names a position in that walk without
        /// having to carry a path — which is what lets a resumed pass fast-forward to where the previous
        /// fragmentainer stopped.
        /// </summary>
        public int WordOrdinal { get; set; }

        /// <summary>
        /// Words below this ordinal were placed in an earlier fragmentainer and are skipped: their
        /// geometry and their line boxes already exist and must not be rebuilt.
        /// </summary>
        public int ResumeOrdinal { get; init; }

        /// <summary>
        /// Whether the content visited since <paramref name="fromOrdinal"/> is being placed by this
        /// pass rather than skipped past — true when it begins at or after the resume point, and true
        /// when the walk reached that point somewhere inside it (a box straddling the break).
        /// </summary>
        /// <remarks>
        /// The first disjunct is what makes a box that visits no words at all come out right, and it is
        /// what makes this inert when <see cref="ResumeOrdinal"/> is 0: a bare
        /// <c>WordOrdinal &gt; ResumeOrdinal</c> would read false for a block's first child on an
        /// ordinary pass, and quietly stop giving it its trailing spacing.
        /// </remarks>
        public bool PlacedSince(int fromOrdinal) => fromOrdinal >= ResumeOrdinal || WordOrdinal > ResumeOrdinal;

        /// <summary>
        /// The ordinal of the first word on the line being built. A line box is monolithic
        /// (<see href="https://www.w3.org/TR/css-break-3/#monolithic">css-break-3 §4.1</see>), so when
        /// content cannot fit, the break is taken here rather than part-way through the line.
        /// </summary>
        public int LineStartOrdinal { get; set; }

        /// <summary>
        /// Set while a resumed flow's opening line is still empty. The first word of such a flow may ask
        /// to wrap — it is a <c>&lt;br&gt;</c>, or it no longer fits — but there is nothing to wrap away
        /// from, and honouring it would consume a blank line's height at the top of the fragmentainer.
        /// </summary>
        /// <remarks>
        /// Only a resumed flow suppresses this. A flow starting from the top genuinely does produce a
        /// leading blank line for content beginning with <c>&lt;br&gt;</c>, which is what a browser
        /// shows.
        /// </remarks>
        public bool SuppressLeadingWrap { get; set; }

        /// <summary>
        /// Where this flow stopped, or null while it is still filling the current fragmentainer.
        /// </summary>
        public InlineBreakToken? Break { get; set; }

        /// <summary>
        /// Whether the line currently being built already ends in a hyphenation split — tracked so the
        /// wrap that closes it can fold that into <see cref="ConsecutiveHyphenatedLines"/> before starting
        /// the next line. Reset to false whenever a new line starts.
        /// </summary>
        public bool CurrentLineHyphenated { get; set; }

        /// <summary>
        /// How many lines immediately before the one currently being built ended in a hyphenation split —
        /// what <c>hyphenate-limit-lines</c> (CSS Text 4 §6.3.5) gates against. Resets to 0 the first time
        /// a line closes without a hyphen, and (like the rest of this per-pass state) restarts from 0 at
        /// the top of a resumed fragmentainer pass rather than carrying across the page/column boundary.
        /// </summary>
        public int ConsecutiveHyphenatedLines { get; set; }
    }
}
