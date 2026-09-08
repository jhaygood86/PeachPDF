using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The fields only <see cref="TokenType.String"/>/<see cref="TokenType.Url"/>/
    /// <see cref="TokenType.Comment"/>/<see cref="TokenType.Function"/>/<see cref="TokenType.Range"/>/
    /// <see cref="TokenType.Dimension"/> tokens need - measured at 5.71% of all tokens across the full
    /// showcase corpus (issue #922 follow-up). <see cref="Token"/> holds a single, usually-null
    /// <see cref="TokenExtra"/> field pointing at one of these instead of carrying every former subclass's
    /// fields on every token, so the other ~94% of tokens (idents, numbers, whitespace, punctuation,
    /// percentages, EOF, ...) never pay for storage they don't use. Each field's owning <see
    /// cref="TokenType"/> is documented where it's read on <see cref="Token"/> itself; reading the wrong
    /// one for a token's actual <see cref="Token.Type"/> is a caller bug, same contract as before this
    /// split existed.
    /// </summary>
    internal sealed class TokenExtra
    {
        public bool IsValid { get; init; }
        public char Quote { get; init; }
        public string? ExtraOrRangeStart { get; init; }
        public string? RangeEnd { get; init; }
        public List<Token>? Arguments { get; init; }
    }
}
