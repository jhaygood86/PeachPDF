// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a word inside an inline box
    /// </summary>
    /// <remarks>
    /// Because of performance, words of text are the most atomic 
    /// element in the project. It should be characters, but come on,
    /// imagine the performance when drawing char by char on the device.<br/>
    /// It may change for future versions of the library.
    /// </remarks>
    internal abstract class CssRect
    {
        #region Fields and Consts

        /// <summary>
        /// Rectangle
        /// </summary>
        private RRect _rect;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="owner">the CSS box owner of the word</param>
        protected CssRect(CssBox owner)
        {
            OwnerBox = owner;
        }

        /// <summary>
        /// Gets the Box where this word belongs.
        /// </summary>
        public CssBox OwnerBox { get; }

        /// <summary>
        /// Gets or sets the bounds of the rectangle
        /// </summary>
        public RRect Rectangle
        {
            get => _rect;
            set => _rect = value;
        }

        /// <summary>
        /// Left of the rectangle
        /// </summary>
        public double Left
        {
            get => _rect.X;
            set => _rect.X = value;
        }

        /// <summary>
        /// Top of the rectangle
        /// </summary>
        public double Top
        {
            get => _rect.Y;
            set
            {
                // Being positioned is what makes a word this fragmentainer's again.
                AwaitsTheNextFragmentainer = false;

                // And it is what can make the owning box's subtree non-empty in a fragmentainer the
                // emitter had already observed it to hold nothing in. A word is the one piece of
                // content whose position lives nowhere on the box itself, so no box-level write would
                // report this (see CssBox.DiscardEmittedNothing).
                if (_rect.Y != value) OwnerBox.DiscardEmittedNothing();

                _rect.Y = value;
            }
        }

        /// <summary>
        /// Whether this word is on a line the current fragmentainer pass discarded, so it belongs to the
        /// <i>next</i> fragmentainer and no fragment of this one may claim it
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.1</see>: a line box
        /// never straddles a fragmentainer).
        /// </summary>
        /// <remarks>
        /// It is a flag rather than a parked coordinate because the position the word will end up at is not
        /// knowable yet — a resumed pass may place it at the next fragmentainer's content top, or, where the
        /// break is a directional one that steps over a page, several bands further on. Parking it at the band
        /// edge put it at the top of a page it never appeared on.
        /// </remarks>
        internal bool AwaitsTheNextFragmentainer { get; set; }

        /// <summary>
        /// Width of the rectangle
        /// </summary>
        public double Width
        {
            get => _rect.Width;
            set => _rect.Width = value;
        }

        /// <summary>
        /// Get the full width of the word including the spacing.
        /// </summary>
        public double FullWidth => _rect.Width + ActualWordSpacing;

        /// <summary>
        /// Gets the actual width of whitespace between words - consults <see cref="FirstLineStyle"/>
        /// instead of <see cref="OwnerBox"/> when this word is on the target's first formatted line
        /// and a <c>::first-line</c> rule overrides <c>word-spacing</c>/<c>letter-spacing</c>.
        /// </summary>
        /// <remarks>
        /// Includes one <c>letter-spacing</c> unit alongside <c>word-spacing</c>: a real UA applies
        /// letter-spacing at every adjacent-character transition in a run, including the space
        /// character's own leading/trailing edges - since this engine never paints the space character
        /// as its own glyph (it's purely this numeric gap between independently-painted word boxes),
        /// that extra unit has to be folded in here for the inter-word gap to widen proportionally with
        /// letter-spacing the same way a real browser's does, instead of staying pinned to plain
        /// word-spacing regardless of how large letter-spacing gets.
        /// </remarks>
        public double ActualWordSpacing =>
            (HasSpaceAfter ? (FirstLineStyle?.ActualWordSpacing ?? OwnerBox.ActualWordSpacing) + (FirstLineStyle?.ActualLetterSpacing ?? OwnerBox.ActualLetterSpacing) : 0) +
            (IsImage ? (FirstLineStyle?.ActualWordSpacing ?? OwnerBox.ActualWordSpacing) : 0);

        /// <summary>
        /// When set, this word lands on its block's first formatted line and a <c>::first-line</c>
        /// rule applies - measurement/painting must use this (a fully-cascaded, detached shadow
        /// <see cref="CssBox"/> - see <c>CssBox.ResolvedFirstLineStyle</c>) instead of
        /// <see cref="OwnerBox"/>'s own font/color/spacing/etc for this specific word. Null for every
        /// ordinary word. Set in <see cref="CssLayoutEngine.FlowBox"/>, and cleared again there for
        /// any word that turns out to actually land on a later line once wrapping is known (see the
        /// boundary re-measurement it performs when a box's content straddles the line-1/2 boundary).
        /// </summary>
        public CssBox? FirstLineStyle { get; set; }

        /// <summary>
        /// This word's natural (pre-rotation) size, cached once by
        /// <see cref="CssLayoutEngine.CreateVerticalLineBoxes"/>'s <c>NaturalWordSize</c> helper - kept
        /// independent of <see cref="Width"/>/<see cref="Height"/>, which that same layout overwrites with
        /// the word's physical (rotated) footprint once placed, so a repeated layout pass (a flex/table
        /// ancestor's provisional sizing, a monolithic relocation to a later page) reads the real natural
        /// size back instead of re-shaping the text - or, absent this cache, instead of compounding an
        /// already-rotated value. Null until first computed; never cleared, on the same assumption
        /// <see cref="CssBox.MeasureWordsSize"/>'s own once-per-layout guard already makes.
        /// </summary>
        internal (double Width, double Height)? NaturalSize { get; set; }

        /// <summary>
        /// Height of the rectangle
        /// </summary>
        public double Height
        {
            get => _rect.Height;
            set => _rect.Height = value;
        }

        /// <summary>
        /// Gets or sets the right of the rectangle. When setting, it only affects the Width of the rectangle.
        /// </summary>
        public double Right
        {
            get => Rectangle.Right;
            set => Width = value - Left;
        }

        /// <summary>
        /// Gets or sets the bottom of the rectangle. When setting, it only affects the Height of the rectangle.
        /// </summary>
        public double Bottom
        {
            get => Rectangle.Bottom;
            set => Height = value - Top;
        }

        /// <summary>
        /// was there a whitespace before the word chars (before trim)
        /// </summary>
        public virtual bool HasSpaceBefore => false;

        /// <summary>
        /// was there a whitespace after the word chars (before trim)
        /// </summary>
        public virtual bool HasSpaceAfter => false;

        /// <summary>
        /// Gets the image this words represents (if one exists)
        /// </summary>
        public virtual RImage? Image
        {
            get => null;
            // ReSharper disable ValueParameterNotUsed
            set { }
            // ReSharper restore ValueParameterNotUsed
        }

        /// <summary>
        /// Gets if the word represents an image.
        /// </summary>
        public virtual bool IsImage => false;

        /// <summary>
        /// Gets a bool indicating if this word is composed only by spaces.
        /// Spaces include tabs and line breaks
        /// </summary>
        public virtual bool IsSpaces => true;

        /// <summary>
        /// Gets if the word is composed by only a line break
        /// </summary>
        public virtual bool IsLineBreak => false;

        /// <summary>
        /// Gets the text of the word
        /// </summary>
        public virtual string? Text => null;

        /// <summary>
        /// This word's own resolved UAX#9 embedding level - uniform across the whole word by construction
        /// (<see cref="CssBox.ParseToWords"/> additionally splits at a level-boundary, so a single word
        /// never straddles two levels). Consumed by <see cref="CssLayoutEngine"/>'s per-line L2
        /// reorder/L4 mirroring step, which treats each word as one homogeneous-level UAX#9 unit.
        /// </summary>
        public byte BidiLevel { get; set; }

        /// <summary>
        /// The pre-<see cref="TextArea.TextTransform"/> source text this word was produced from
        /// (still HTML-decoded/soft-hyphen-stripped, just not case-transformed) - null for words that
        /// never carry real text (e.g. line breaks). All 3 CSS1 <c>text-transform</c> values are
        /// character-by-character and length-preserving, so this lets <see cref="FirstLineText"/> be
        /// derived independently under a <c>::first-line</c> rule's own <c>text-transform</c>, which may
        /// differ from <see cref="OwnerBox"/>'s own - re-deriving from the box's own already-transformed
        /// <see cref="Text"/> would lose information a transform like <c>uppercase</c> destroys (e.g. which
        /// letters were originally lowercase, needed to redo <c>capitalize</c> correctly).
        /// </summary>
        public string? OriginalText { get; set; }

        /// <summary>
        /// When set, overrides <see cref="Text"/> for measurement/painting - the result of re-running
        /// <see cref="OriginalText"/> through a <c>::first-line</c> rule's own <c>text-transform</c> value
        /// (see <see cref="CssBox.ApplyFirstLineStyleOverride"/>), only when that differs from
        /// <see cref="OwnerBox"/>'s own. Null for every ordinary word, and cleared again (see
        /// <see cref="CssBox.RemeasureWordsTail"/>) for any word that turns out to land on a later line
        /// once wrapping is known.
        /// </summary>
        public string? FirstLineText { get; set; }

        /// <summary>
        /// Multiplier applied to the owner box's <see cref="DerivedStyle.ActualFont"/> size when
        /// measuring/painting this specific fragment. Used to synthesize <c>font-variant: small-caps</c>
        /// (an upper-cased, originally-lowercase run is drawn smaller than the rest of its word) — 1.0
        /// (no-op) for every other <see cref="CssRect"/>. See <see cref="CssBox.ParseToWords"/>.
        /// </summary>
        public double FontSizeScale { get; set; } = 1.0;

        /// <summary>
        /// When true, this fragment's font is resolved per-codepoint (its <see cref="Text"/>'s first
        /// <see cref="System.Text.Rune"/> against <see cref="DerivedStyle.ActualFontForCodepoint"/>)
        /// rather than from the owner box's single <see cref="DerivedStyle.ActualFont"/> - the basis of
        /// <c>@font-face</c> <c>unicode-range</c> selection and glyph-coverage fallback. Set in
        /// <see cref="CssBox.ParseToWords"/> only for boxes whose text actually needs it; every fragment in
        /// such a split shares one resolved face by construction. <c>false</c> for ordinary words.
        /// </summary>
        public bool UsesPerCodepointFont { get; set; }

        /// <summary>
        /// When true, this fragment must never be treated as a line-break opportunity even if it would
        /// otherwise overflow — used to glue synthesized small-caps case-run fragments (which together
        /// make up what was originally one word) back together so splitting a word into runs never
        /// introduces a spurious new wrap point. See <see cref="CssLayoutEngine.FlowBox"/>.
        /// </summary>
        public bool SuppressWrapBefore { get; set; } = false;

        /// <summary>
        /// Candidate hyphenation break indices into <see cref="Text"/> — index <c>i</c> means a hyphen
        /// may be inserted between <c>Text[i-1]</c> and <c>Text[i]</c>. Populated by
        /// <see cref="CssBox.ParseToWords"/> from either an explicit soft hyphen (<c>&amp;shy;</c>) or,
        /// for <c>hyphens: auto</c> with a known document language, <c>PeachPDF.Text.HyphenationEngine</c>.
        /// Null/empty for every word that isn't a hyphenation candidate. Consulted only at layout time,
        /// in <see cref="CssLayoutEngine.FlowBox"/>, when a word would otherwise overflow the line.
        /// </summary>
        public IReadOnlyList<int>? HyphenationCandidates { get; set; }

        /// <summary>
        /// Represents this word for debugging purposes
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return
                $"{Text!.Replace(' ', '-').Replace("\n", "\\n")} ({Text.Length} char{(Text.Length != 1 ? "s" : string.Empty)})";
        }

        /// <summary>
        /// Whether this word, at its current <see cref="Top"/>, crosses the fragmentainer the pass
        /// <i>is filling</i> — the break decision the resumable inline flow makes: a straddle here ends
        /// the pass with an <see cref="Fragmentation.InlineBreakToken"/> rather than moving the word and
        /// carrying on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A word taller than a whole band never counts as straddling: there is no fragmentainer it
        /// could fit in, so moving it would only repeat the question on the next one. That makes it
        /// monolithic content in the sense of
        /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">css-break-3 §2</see> — it overflows
        /// rather than being split.
        /// </para>
        /// <para>
        /// Where an enclosing box asks for <c>box-decoration-break: clone</c>, the fragment being left behind
        /// closes with its own bottom border and padding, and §6.2 requires room to be reserved for them. So
        /// the word has to clear that much more than its own depth to still count as fitting. A table
        /// repeating a <c>&lt;tfoot&gt;</c> claims the foot of the band the same way
        /// (<see cref="Fragmentation.FragmentainerContext.BandEndInsetOf"/>), and the two compose.
        /// </para>
        /// <para>
        /// Asks <see cref="HtmlContainerInt.BandBeingFilled"/> — the fragmentainer the pass is actually
        /// filling, not merely the band this word's own top happens to fall in. Safe now that
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>'s stage 1 makes every
        /// mechanism that can spill flow past that fragmentainer step the pass cursor to match.
        /// </para>
        /// </remarks>
        public bool WouldStraddleFragmentainer()
        {
            var container = OwnerBox.HtmlContainer!;

            var (clonedTop, reservedEnd) = ClonedInsets(container);

            // The reserved insets count towards "too tall to fit anywhere": a resumed pass re-opens with the top
            // set and still has to clear the bottom one, so if the word cannot fit between them it never will,
            // and calling it a straddle would break to a fresh fragmentainer for every fragmentainer there is.
            if (MonolithicContent.FitsNoFragmentainer(Height, clonedTop, reservedEnd, container))
                return false;

            // Inside a multi-column column the question is about that column's own band, not the page grid's:
            // every column shares one page band, so the grid cannot say a word has left one column for the
            // next. The same "fits nowhere" exemption applies one size down — a word too tall for any column
            // overflows the one it is in rather than breaking to a fresh column for every column there is.
            if (container.CurrentFragmentainer is { HasOwnBand: true } columnBand)
            {
                return MonolithicContent.FitsInBand(Height, clonedTop, reservedEnd, columnBand.BandHeight)
                       && HtmlContainerInt.FallsPast(Bottom + reservedEnd, columnBand.Band);
            }

            // The band this word's own top falls in, asked of the fragmentainer the pass is actually
            // filling rather than merely of the page grid.
            var gridBand = container.BandStartingAt(Top);
            var band = container.BandBeingFilled(Top, gridBand);

            return HtmlContainerInt.FallsPast(Bottom + reservedEnd, band);
        }

        /// <summary>
        /// The two insets <see cref="WouldStraddleFragmentainer"/> and <see cref="OverflowsEveryFragmentainer"/>
        /// both measure against: how much of this word's own top is already claimed by a
        /// <c>box-decoration-break: clone</c> ancestor's opening edge, and how much of its bottom a
        /// repeating <c>&lt;tfoot&gt;</c> (or a clone ancestor's own closing edge) has claimed at the far
        /// end of the band. Shared so the two questions can never drift onto different reservations.
        /// </summary>
        private (double ClonedTop, double ReservedEnd) ClonedInsets(HtmlContainerInt container)
        {
            var (clonedTop, clonedBottom) = MonolithicContent.ClonedBlockInsets(OwnerBox, container);

            // Asked with the same slot the band below comes from - BandStartingAt(y) is
            // BandOfSlot(SlotStartingAt(y)) - so the reservation and the band it is taken out of cannot
            // name different fragmentainers, which they could if this read the context's own SlotIndex, a
            // cursor StepOverTo moves.
            var reservedEnd = clonedBottom
                              + (container.CurrentFragmentainer?.BandEndInsetOf(container.SlotStartingAt(Top)) ?? 0);

            return (clonedTop, reservedEnd);
        }

        /// <summary>
        /// The fragmentainer a break taken before this word resumes in: the band its own top begins, and
        /// the one after that only where it cannot fit there either.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Not <c>cursor.SlotIndex + 1</c>: a resume target must come from where the break actually fell,
        /// never assumed to be "the pass after this one" (see the invariant of that name) - a box can be
        /// placed far down the document, and after <see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>'s
        /// stage 1 the two happen to coincide (proven, not assumed, by <c>CursorSpills == 0</c> in the
        /// fixtures that exercise this), but the moment a future mechanism moves flow without stepping the
        /// cursor, "the pass after this one" is silently wrong again while this expression is not.
        /// </para>
        /// <para>
        /// In the ordinary straddle - this word's own band is the one the pass is filling - this is
        /// byte-identical to the retired <c>SlotStartingAt(word.Top) + 1</c> expression. It only differs
        /// in the spill case the conversion above exists to close: a word whose top already begins a
        /// later band than the one the pass opened with resumes in <i>that</i> band, not the one after
        /// it - the band the line was trying to sit in, per #435's own words, not a further one.
        /// </para>
        /// </remarks>
        internal int ResumeSlotForBreakBefore()
        {
            var container = OwnerBox.HtmlContainer!;
            var slot = container.SlotStartingAt(Top);
            var (_, reservedEnd) = ClonedInsets(container);

            return HtmlContainerInt.FallsPast(Bottom + reservedEnd, container.BandOfSlot(slot)) ? slot + 1 : slot;
        }

        /// <summary>
        /// Whether this word is too tall to fit in any fragmentainer at all - the <c>css-break-3 §2</c>
        /// monolithic-overflow case <see cref="WouldStraddleFragmentainer"/> exempts from being called a
        /// straddle, since moving it would only repeat the question on the next fragmentainer forever.
        /// </summary>
        /// <remarks>
        /// A word answering this <c>true</c> overflows the fragmentainer it is in rather than breaking,
        /// so the content <i>after</i> it flows into the following band without a break ever having been
        /// recorded for the crossing - one of the handful of mechanisms
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see> names as putting flow
        /// past the fragmentainer a pass says it is filling. The caller is expected to step the pass's
        /// cursor over to match once this word's own bottom is known, mirroring how a forced break already
        /// does the same thing by placement.
        /// </remarks>
        internal bool OverflowsEveryFragmentainer()
        {
            var container = OwnerBox.HtmlContainer!;
            var (clonedTop, reservedEnd) = ClonedInsets(container);

            return MonolithicContent.FitsNoFragmentainer(Height, clonedTop, reservedEnd, container);
        }
    }
}