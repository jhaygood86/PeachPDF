namespace PeachPDF.CSS
{
    internal enum TokenType : byte
    {
        // First member so default(Token).Type reads as an obviously-uninitialized sentinel rather than
        // the misleading TokenType.String that used to occupy slot 0 - only ever compared symbolically
        // (never cast to/from its numeric value or serialized), so reordering is safe.
        None,
        String,
        Url,
        Color,
        Hash,
        Comment,
        AtKeyword,
        Ident,
        Function,
        Number,
        Percentage,
        Dimension,
        Range,
        Cdo,
        Cdc,
        Column,
        Delim,
        Match,
        RoundBracketOpen,
        RoundBracketClose,
        CurlyBracketOpen,
        CurlyBracketClose,
        SquareBracketOpen,
        SquareBracketClose,
        Colon,
        Comma,
        Semicolon,
        Whitespace,
        EndOfFile,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Equal
    }
}