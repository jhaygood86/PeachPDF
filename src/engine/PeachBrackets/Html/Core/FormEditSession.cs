namespace PeachBrackets.Html
{
    /// <summary>
    /// Where the caret and selection of the text field being edited are, which the painter draws and <see cref="HtmlDocument"/> changes. It belongs to
    /// the field with the source ordinal <see cref="Ordinal"/>; when the focus moves to another field it starts over at the end of the new text.
    /// </summary>
    internal sealed class FormEditSession
    {
        /// <summary>The source ordinal of the field this describes, or -1.</summary>
        public int Ordinal { get; set; } = -1;

        public int Start { get; set; }

        public int End { get; set; }

        /// <summary>Where the text an input method is composing starts, or -1.</summary>
        public int CompositionStart { get; set; } = -1;

        public int CompositionEnd { get; set; } = -1;

        /// <summary>Whether the caret is drawn; a host turns it off and on to make it blink.</summary>
        public bool CaretVisible { get; set; } = true;
    }
}
