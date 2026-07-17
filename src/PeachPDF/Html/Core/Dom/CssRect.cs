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
            set => _rect.Y = value;
        }

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
        /// and a <c>::first-line</c> rule overrides <c>word-spacing</c>.
        /// </summary>
        public double ActualWordSpacing =>
            (HasSpaceAfter ? (FirstLineStyle?.ActualWordSpacing ?? OwnerBox.ActualWordSpacing) : 0) +
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
        /// The pre-<see cref="CssBoxProperties.TextTransform"/> source text this word was produced from
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
        /// Multiplier applied to the owner box's <see cref="CssBoxProperties.ActualFont"/> size when
        /// measuring/painting this specific fragment. Used to synthesize <c>font-variant: small-caps</c>
        /// (an upper-cased, originally-lowercase run is drawn smaller than the rest of its word) — 1.0
        /// (no-op) for every other <see cref="CssRect"/>. See <see cref="CssBox.ParseToWords"/>.
        /// </summary>
        public double FontSizeScale { get; set; } = 1.0;

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

        public bool BreakPage()
        {
            var container = OwnerBox.HtmlContainer;

            if (Height >= container!.PageSize.Height)
                return false;

            var remTop = (Top - container.MarginTop) % container.PageSize.Height;
            var remBottom = (Bottom - container.MarginTop) % container.PageSize.Height;

            if (!(remTop > remBottom)) return false;
            Top += container.PageSize.Height - remTop + 1;
            return true;

        }
    }
}