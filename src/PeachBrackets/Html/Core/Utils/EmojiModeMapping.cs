using PeachDrawing.Text.Unicode;
using PeachPDF.CSS;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>Maps the cascaded <c>font-variant-emoji</c> keyword onto the text engine's own <see cref="EmojiMode"/>.</summary>
    internal static class EmojiModeMapping
    {
        internal static EmojiMode ToEmojiMode(this FontVariantEmojiMode mode) => mode switch
        {
            FontVariantEmojiMode.Text => EmojiMode.Text,
            FontVariantEmojiMode.Emoji => EmojiMode.Emoji,
            FontVariantEmojiMode.Unicode => EmojiMode.Unicode,
            _ => EmojiMode.Normal,
        };
    }
}
