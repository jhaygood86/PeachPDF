namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// Which presentation of an <see cref="Emoji.IsPresentationParticipant">emoji presentation
    /// participating code point</see> a character is asked to be drawn in (CSS Fonts 4
    /// <c>font-variant-emoji</c>, UTS #51 emoji presentation sequences).
    /// </summary>
    public enum EmojiPresentation : byte
    {
        /// <summary>No request: the character is not a participant, or the author left the choice to the user agent.</summary>
        NoPreference = 0,

        /// <summary>Text presentation, as if U+FE0E VARIATION SELECTOR-15 followed the character.</summary>
        Text,

        /// <summary>Emoji presentation, as if U+FE0F VARIATION SELECTOR-16 followed the character.</summary>
        Emoji
    }
}
