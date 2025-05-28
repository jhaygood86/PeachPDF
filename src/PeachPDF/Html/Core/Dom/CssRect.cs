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
        /// Gets the actual width of whitespace between words.
        /// </summary>
        public double ActualWordSpacing => (HasSpaceAfter ? OwnerBox.ActualWordSpacing : 0) + (IsImage ? OwnerBox.ActualWordSpacing : 0);

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