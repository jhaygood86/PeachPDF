namespace PeachPDF.Text
{
    /// <summary>
    /// The default a caller asks for when a character has both a text and an emoji presentation and no
    /// variation selector says which (CSS Fonts 4's <c>font-variant-emoji</c> is the one caller today).
    /// </summary>
    internal enum EmojiMode : byte
    {
        /// <summary>No request: the font-family order decides.</summary>
        Normal,

        /// <summary>Prefer the text presentation of every emoji presentation participating code point.</summary>
        Text,

        /// <summary>Prefer the emoji presentation of every emoji presentation participating code point.</summary>
        Emoji,

        /// <summary>Emoji presentation for characters whose default is emoji, text presentation otherwise.</summary>
        Unicode
    }
}
