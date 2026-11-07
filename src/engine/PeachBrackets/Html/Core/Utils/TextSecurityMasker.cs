using PeachBrackets.Css;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Applies <c>-webkit-text-security</c>: every non-whitespace UTF-16 code unit becomes the bullet, so the result is
    /// always the same length as the input and word/whitespace boundary indices computed against it stay valid
    /// (the contract <see cref="TextTransformer"/> keeps too).
    /// </summary>
    internal static class TextSecurityMasker
    {
        internal static string Apply(string text, TextSecurity security)
        {
            if (security == TextSecurity.None || string.IsNullOrEmpty(text)) return text;

            var bullet = security switch
            {
                TextSecurity.Circle => '◦',
                TextSecurity.Square => '▪',
                _ => '•',
            };

            return string.Create(text.Length, (text, bullet), static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = char.IsWhiteSpace(state.text[i]) ? state.text[i] : state.bullet;
            });
        }
    }
}
