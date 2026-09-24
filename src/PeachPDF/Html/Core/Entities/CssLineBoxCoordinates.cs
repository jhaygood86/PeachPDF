using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using System.Collections.Generic;

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
        /// Set once <c>line-clamp</c> (CSS Overflow 4 §line-clamp) has appended its generated ellipsis
        /// word to the block's last visible line and the walk must stop right there. Deliberately a
        /// separate flag from <see cref="Break"/> rather than reusing it: <see cref="Break"/> means "this
        /// fragmentainer is full, a later pass resumes the remaining content elsewhere" and round-trips
        /// through an <see cref="InlineBreakToken"/> a future pass expects to consume — a clamped block's
        /// content is not paused, it is permanently done, so <c>CreateLineBoxes</c> must take its
        /// ordinary non-paginated finish (as if the content had simply ended) once this is set, the same
        /// way it already does whenever <see cref="Break"/> is null. Every recursive <see cref="Break"/>-
        /// propagation checkpoint in <see cref="CssLayoutEngine.FlowBox"/> also checks this flag, for
        /// exactly the same "stop walking now" reason, without taking <see cref="Break"/>'s "resume later"
        /// meaning.
        /// </summary>
        public bool ClampedStop { get; set; }

        /// <summary>
        /// Whether the line currently being built already ends in a hyphenation split — tracked so the
        /// wrap that closes it can fold that into <see cref="ConsecutiveHyphenatedLines"/> before starting
        /// the next line. Reset to false whenever a new line starts.
        /// </summary>
        public bool CurrentLineHyphenated { get; set; }

        /// <summary>
        /// Number of consecutive regional indicators at the end of the current line. UAX #14 LB30a
        /// uses its parity to keep each flag pair together while permitting a break between pairs.
        /// </summary>
        public int TrailingRegionalIndicatorCount { get; set; }

        /// <summary>
        /// The final extended grapheme cluster, possibly incomplete at an inline-owner boundary.
        /// Retained so UAX #29 rules such as extended-pictographic + ZWJ can span several owners.
        /// </summary>
        public string TrailingGraphemeContext { get; set; } = string.Empty;

        /// <summary>
        /// How many lines immediately before the one currently being built ended in a hyphenation split —
        /// what <c>hyphenate-limit-lines</c> (CSS Text 4 §6.3.5) gates against. Resets to 0 the first time
        /// a line closes without a hyphen. Unlike the rest of this per-pass state, this does <b>not</b>
        /// restart from 0 at the top of a resumed fragmentainer pass: <c>CreateLineBoxes</c> seeds it from
        /// <see cref="Fragmentation.InlineBreakToken.ConsecutiveHyphenatedLines"/>, so a run of consecutive
        /// hyphenated lines keeps counting across the page/column boundary instead.
        /// </summary>
        public int ConsecutiveHyphenatedLines { get; set; }

        /// <summary>
        /// Whether the cursor has advanced past a word separator since the last word was placed - the
        /// running state behind <see cref="CssRect.PrecededByWordSeparator"/>. Carried here rather than
        /// read back off the previous word because one of its sources belongs to no word at all: an
        /// inline box holding only white space (<c>&lt;span&gt;AA&lt;/span&gt; &lt;span&gt;BB&lt;/span&gt;</c>)
        /// advances the cursor by a word space of its own.
        /// </summary>
        public bool PendingWordSeparator { get; set; }

        /// <summary>
        /// Floats <see cref="CssLayoutEngine.FlowBox"/> has itself placed directly among the block's own
        /// inline content (issue #1038) - a float that is a sibling of ordinary inline content in the
        /// same box rather than a separate block-level box that merely precedes one. Not discoverable
        /// through <see cref="Utils.DomUtils.GetLastLeftIntersectingFloatBox"/>/
        /// <see cref="Utils.DomUtils.GetLastRightIntersectingFloatBox"/>: those only look at floats that
        /// precede their own <c>box</c> argument as a SIBLING of it (or of one of its ancestors), which is
        /// exactly the shape a float living among <c>box</c>'s own children never has. Appended, in flow
        /// order, both the moment such a float is placed and - on a resumed pass - the moment the walk
        /// structurally passes back through one placed on an earlier fragmentainer (so its real, already-
        /// committed geometry re-enters this fresh pass's list rather than the list starting empty and
        /// silently forgetting it). Consulted alongside the two ordinary queries wherever this flow asks
        /// for the nearest intersecting float, so whichever of the two is more restrictive wins.
        /// </summary>
        public List<CssBox>? InlineFloats { get; set; }

        /// <summary>
        /// Absolutely positioned boxes <see cref="CssLayoutEngine.FlowBox"/> passed among the block's
        /// inline content, each with where the walk had reached there. They take no part in the line
        /// boxes, so they are laid out once the lines are final, which is also when an inline containing
        /// block's fragments all exist (<see cref="CssLayoutEngine.CreateLineBoxes"/>).
        /// </summary>
        public List<SetAsideBox>? AbsolutelyPositioned { get; set; }

        /// <summary>
        /// The union of the extents of the empty inline boxes - an empty <c>&lt;span&gt;</c>, or one holding
        /// only out-of-flow content - the walk has passed after a wrap opportunity or at a line's start,
        /// which still take part in a line's height (CSS 2.1 §9.4.2, §10.8.1). Held for the next word, and
        /// folded into whichever line that word lands on, since an empty inline after a wrap opportunity
        /// goes to the next line with it. <see cref="CssLayoutEngine.CreateLineBoxes"/> folds what is left at
        /// the end of the flow into the last line, unless that line holds no content, which §9.4.2 keeps at
        /// zero height.
        /// </summary>
        public LineBoxExtent? PendingEmptyInlineExtent { get; set; }

        /// <summary>
        /// The empty inlines whose extent <see cref="PendingEmptyInlineExtent"/> holds, recorded on the line
        /// that takes them (<see cref="CssLineBox.EmptyInlines"/>) so a pass resuming after that line does not
        /// place them again.
        /// </summary>
        public List<CssBox>? PendingEmptyInlines { get; set; }

        /// <summary>
        /// The empty inlines the line before this flow's resume point already holds
        /// (<see cref="CssLineBox.EmptyInlines"/>), or null on a flow that is not resuming. An empty inline
        /// places no word, so one placed with the word before a break sits at the ordinal the flow resumes
        /// at, and the flow reaches it again; these must not be placed a second time. The others it reaches
        /// there belonged to the line the break discarded, and are placed as usual.
        /// </summary>
        public IReadOnlyList<CssBox>? EmptyInlinesBeforeResume { get; init; }
    }

    /// <summary>
    /// An absolutely positioned box <see cref="CssLayoutEngine.FlowBox"/> set aside, and the place on the
    /// line it was passed at.
    /// </summary>
    /// <param name="Box">the absolutely positioned box</param>
    /// <param name="Ordinal">the word ordinal the walk had reached</param>
    /// <param name="Line">the line being built when the walk reached it</param>
    /// <param name="X">the cursor's inline position there</param>
    /// <param name="Y">the cursor's block position there, the line's top while it holds no word</param>
    /// <param name="WordIndex">how many words <paramref name="Line"/> held then</param>
    /// <param name="Anchor">
    /// a word on <paramref name="Line"/> next to the place - the one before it, or failing that the one after
    /// it - whose later move by alignment and bidi reordering the place moves by; null until one is known
    /// </param>
    /// <param name="AnchorLeft">where <paramref name="Anchor"/> sat before that move</param>
    internal readonly record struct SetAsideBox(
        CssBox Box, int Ordinal, CssLineBox Line, double X, double Y, int WordIndex, CssRect? Anchor, double AnchorLeft);
}
