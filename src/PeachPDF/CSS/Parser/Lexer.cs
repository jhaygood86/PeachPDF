#nullable disable

using System;
using System.Globalization;

namespace PeachPDF.CSS
{
    internal sealed class Lexer : LexerBase
    {
        public event EventHandler<TokenizerError> Error;
        private TextPosition _position;

        // Content-run tracking for Token.Data's zero-allocation slice path: _contentStart/_contentEnd
        // bound the region of the shared Source buffer a token's Data would occupy if nothing that
        // makes it diverge from a literal source range happened during the scan (see BeginContentAt/
        // AppendLiteral/EndContent below). _mustMaterialize is set by AppendEscape/AppendLineContinuation
        // whenever content genuinely differs from source text; CrossedCarriageReturn (on LexerBase) is
        // set by NormalizeForward whenever a raw '\r' was collapsed to '\n' - both force EndContent() to
        // fall back to an owned string via FlushBuffer(), exactly as before this design existed.
        private int _contentStart;
        private int _contentEnd;
        private bool _mustMaterialize;

        public Lexer(TextSource source) : base(source)
        {
            IsInValue = false;
        }

        public Lexer(string source) : base(new TextSource(source))
        {
            IsInValue = false;
        }

        public bool IsInValue { get; set; }

        public Token Get()
        {
            var current = GetNext();
            _position = GetCurrentPosition();
            return Data(current);
        }


        internal void RaiseErrorOccurred(ParseError error, TextPosition position)
        {
            var handler = Error;
            if (handler == null) return;

            var errorEvent = new TokenizerError(error, position);
            handler.Invoke(this, errorEvent);
        }

        // Starts tracking a new token-content run that may end up representable as a literal slice of
        // Source instead of an owned string materialized via FlushBuffer(). `start` is the source index
        // of the run's first character - callers pass Source.Index directly when nothing has been read
        // yet for this run, or an explicitly captured earlier position (e.g. Source.Index - 1 for a
        // character already consumed as a method parameter) when the first character was already read
        // before this call.
        private void BeginContentAt(int start)
        {
            _contentStart = start;
            _contentEnd = start;
            _mustMaterialize = false;
            CrossedCarriageReturn = false;
        }

        // Appends a character that is a direct, unmodified echo of one just read from Source (never an
        // escape-decoded or line-continuation substitute - AppendEscape/AppendLineContinuation handle
        // those, and set _mustMaterialize instead) and advances the tracked content-end to match.
        // Because this always runs with Source.Index sitting one past the character just appended,
        // _contentEnd correctly tracks "the position right after the last real content character" even
        // when the caller goes on to peek further ahead (and maybe back up, possibly more than once)
        // before finishing the token - no fragile "how many extra lookahead reads happened since"
        // reconstruction at the point a token completes is ever needed.
        private void AppendLiteral(char c)
        {
            StringBuffer.Append(c);
            _contentEnd = Source.Index;
        }

        // Same as AppendLiteral, but for a character read (and possibly followed by further lookahead
        // that didn't pan out) earlier than the live cursor position - e.g. NumberExponential's fallback
        // re-appends 'letter' ('e'/'E') as the first character of a dimension's unit after having peeked
        // one or two characters further ahead to check for a valid exponent. Trusting live Source.Index
        // there would wrongly include that lookahead in the tracked content-end, so the character's own
        // known position is passed explicitly instead.
        private void AppendLiteralAt(char c, int position)
        {
            StringBuffer.Append(c);
            _contentEnd = position + 1;
        }

        // Ends a content run started by BeginContentAt(): if nothing that would make the tracked
        // [_contentStart, _contentEnd) range diverge from the accumulated buffer happened (no escape, no
        // CRLF/lone-CR normalization, no escaped line continuation), slices Source directly and discards
        // the buffer - zero allocation. Otherwise falls back to the buffer's own already-decoded content
        // as an owned string, exactly as before this design existed.
        private ReadOnlyMemory<char> EndContent()
        {
            if (!_mustMaterialize && !CrossedCarriageReturn)
            {
                var length = _contentEnd - _contentStart;
                StringBuffer.Clear();
                return Source.Slice(_contentStart, length);
            }

            return FlushBuffer().AsMemory();
        }

        // CSS's string/url escaped-line-continuation feature ("\" followed by a newline consumes the
        // newline without adding it to the value) - StringBuilder.AppendLine() appends the platform
        // default line terminator, not the source's own newline character(s), so content that crosses
        // this can never be represented as a literal source slice.
        private void AppendLineContinuation()
        {
            StringBuffer.AppendLine();
            _mustMaterialize = true;
        }

        private Token Data(char current)
        {
            _position = GetCurrentPosition();
            switch (current)
            {
                case Symbols.FormFeed:
                case Symbols.LineFeed:
                case Symbols.CarriageReturn:
                case Symbols.Tab:
                case Symbols.Space:
                    return NewWhitespace(current);
                case Symbols.DoubleQuote:
                    return StringDoubleQuote();
                case Symbols.Num:
                    return IsInValue ? ColorLiteral() : HashStart();
                case Symbols.Dollar:
                    current = GetNext();
                    return current == Symbols.Equality ? NewMatch(Combinators.Ends) : NewDelimiter(GetPrevious());
                case Symbols.SingleQuote:
                    return StringSingleQuote();
                case Symbols.RoundBracketOpen:
                    return NewOpenRound();
                case Symbols.RoundBracketClose:
                    return NewCloseRound();
                case Symbols.Asterisk:
                    current = GetNext();
                    return current == Symbols.Equality ? NewMatch(Combinators.InText) : NewDelimiter(GetPrevious());
                case Symbols.Plus:
                    {
                        var c1 = GetNext();
                        if (c1 != Symbols.EndOfFile)
                        {
                            var c2 = GetNext();
                            Back(2);

                            if (c1.IsDigit() || c1 == Symbols.Dot && c2.IsDigit()) return NumberStart(current);
                        }
                        else
                        {
                            Back();
                        }

                        return NewDelimiter(current);
                    }
                case Symbols.Comma:
                    return NewComma();

                case Symbols.Dot:
                    {
                        var c = GetNext();
                        return c.IsDigit() ? NumberStart(GetPrevious()) : NewDelimiter(GetPrevious());
                    }
                case Symbols.Minus:
                    {
                        var c1 = GetNext();
                        if (c1 != Symbols.EndOfFile)
                        {
                            var c2 = GetNext();
                            Back(2);
                            if (c1.IsDigit() || c1 == Symbols.Dot && c2.IsDigit()) return NumberStart(current);
                            if (c1.IsNameStart()) return IdentStart(current);
                            if (c1 == Symbols.ReverseSolidus && !c2.IsLineBreak() && c2 != Symbols.EndOfFile)
                                return IdentStart(current);

                            if (c1 == Symbols.Minus)
                            {
                                if (c2 == Symbols.GreaterThan)
                                {
                                    Advance(2);
                                    return NewCloseComment();
                                }

                                return IdentStart(current);
                            }

                            return NewDelimiter(current);
                        }

                        Back();

                        return NewDelimiter(current);
                    }
                case Symbols.Solidus:
                    current = GetNext();
                    return current == Symbols.Asterisk
                        ? Comment()
                        : NewDelimiter(GetPrevious());

                case Symbols.ReverseSolidus:
                    current = GetNext();
                    if (current.IsLineBreak())
                    {
                        RaiseErrorOccurred(ParseError.LineBreakUnexpected);
                        return NewDelimiter(GetPrevious());
                    }

                    if (current != Symbols.EndOfFile) return IdentStart(GetPrevious());

                    RaiseErrorOccurred(ParseError.EOF);
                    return NewDelimiter(GetPrevious());

                case Symbols.Colon:
                    return NewColon();
                case Symbols.Semicolon:
                    return NewSemicolon();
                case Symbols.LessThan:
                    current = GetNext();
                    if (current == Symbols.ExclamationMark)
                    {
                        current = GetNext();
                        if (current == Symbols.Minus)
                        {
                            current = GetNext();
                            if (current == Symbols.Minus) return NewOpenComment();
                            // ReSharper disable once RedundantAssignment
                            current = GetPrevious();
                        }

                        GetPrevious();
                        return NewDelimiter(GetPrevious());
                    }
                    else if (current == Symbols.Equality)
                    {
                        Advance();
                        return NewLessThanOrEqual();
                    }
                    else
                    {
                        return NewLessThan();
                    }
                case Symbols.At:
                    return AtKeywordStart();
                case Symbols.SquareBracketOpen:
                    return NewOpenSquare();
                case Symbols.SquareBracketClose:
                    return NewCloseSquare();
                case Symbols.Accent:
                    current = GetNext();
                    return current == Symbols.Equality
                        ? NewMatch(Combinators.Begins)
                        : NewDelimiter(GetPrevious());
                case Symbols.CurlyBracketOpen:
                    return NewOpenCurly();
                case Symbols.CurlyBracketClose:
                    return NewCloseCurly();
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    return NumberStart(current);
                case 'U':
                case 'u':
                    current = GetNext();
                    if (current != Symbols.Plus) return IdentStart(GetPrevious());
                    current = GetNext();
                    if (current.IsHex() || current == Symbols.QuestionMark) return UnicodeRange(current);
                    // ReSharper disable once RedundantAssignment
                    current = GetPrevious();
                    return IdentStart(GetPrevious());

                case Symbols.Pipe:
                    current = GetNext();
                    if (current == Symbols.Equality)
                        return NewMatch(Combinators.InToken);
                    else if (current == Symbols.Pipe) return NewColumn();

                    return NewDelimiter(GetPrevious());

                case Symbols.Tilde:
                    current = GetNext();
                    return current == Symbols.Equality
                        ? NewMatch(Combinators.InList)
                        : NewDelimiter(GetPrevious());

                case Symbols.EndOfFile:
                    return NewEof();
                case Symbols.ExclamationMark:
                    current = GetNext();
                    return current == Symbols.Equality
                        ? NewMatch(Combinators.Unlike)
                        : NewDelimiter(GetPrevious());
                case Symbols.GreaterThan:
                    if (GetNext() == Symbols.Equality)
                    {
                        Advance();
                        return NewGreaterThanOrEqual();
                    }
                    GetPrevious();
                    return NewGreaterThan();
                default:
                    return current.IsNameStart() ? IdentStart(current) : NewDelimiter(current);
            }
        }

        private Token StringDoubleQuote()
        {
            BeginContentAt(Source.Index);
            while (true)
            {
                var current = GetNext();
                switch (current)
                {
                    case Symbols.DoubleQuote:
                    case Symbols.EndOfFile:
                        return NewString(EndContent(), Symbols.DoubleQuote);
                    case Symbols.FormFeed:
                    case Symbols.LineFeed:
                        RaiseErrorOccurred(ParseError.LineBreakUnexpected);
                        Back();
                        return NewString(EndContent(), Symbols.DoubleQuote, true);
                    case Symbols.ReverseSolidus:
                        current = GetNext();
                        if (current.IsLineBreak())
                        {
                            AppendLineContinuation();
                        }
                        else if (current != Symbols.EndOfFile)
                        {
                            AppendEscape(current);
                        }
                        else
                        {
                            RaiseErrorOccurred(ParseError.EOF);
                            Back();
                            return NewString(EndContent(), Symbols.DoubleQuote, true);
                        }

                        break;
                    default:
                        AppendLiteral(current);
                        break;
                }
            }
        }

        private Token StringSingleQuote()
        {
            BeginContentAt(Source.Index);
            while (true)
            {
                var current = GetNext();
                switch (current)
                {
                    case Symbols.SingleQuote:
                    case Symbols.EndOfFile:
                        return NewString(EndContent(), Symbols.SingleQuote);
                    case Symbols.FormFeed:
                    case Symbols.LineFeed:
                        RaiseErrorOccurred(ParseError.LineBreakUnexpected);
                        Back();
                        return NewString(EndContent(), Symbols.SingleQuote, true);
                    case Symbols.ReverseSolidus:
                        current = GetNext();
                        if (current.IsLineBreak())
                        {
                            AppendLineContinuation();
                        }
                        else if (current != Symbols.EndOfFile)
                        {
                            AppendEscape(current);
                        }
                        else
                        {
                            RaiseErrorOccurred(ParseError.EOF);
                            Back();
                            return NewString(EndContent(), Symbols.SingleQuote, true);
                        }

                        break;
                    default:
                        AppendLiteral(current);
                        break;
                }
            }
        }

        private Token ColorLiteral()
        {
            var current = GetNext();

            // '#' not followed by a name code point or a valid escape is a plain delimiter (CSS Syntax §4.3.4).
            if (!current.IsName() && !IsValidEscape(current))
            {
                Back();
                return NewDelimiter(Symbols.Num);
            }

            Back();
            BeginContentAt(Source.Index);

            // A '#' always begins a <hash-token>, consuming a whole <name> (CSS Syntax §4.3.4). Classify it as
            // a color literal only when the name is entirely hex digits (e.g. "#f00"); otherwise keep it as an
            // id hash-token (e.g. "#hero" — an id-selector inside element()), instead of truncating at the
            // first non-hex character (which turned "#hero" into an empty color plus a stray "hero" ident).
            var allHex = true;

            while (true)
            {
                current = GetNext();

                if (current.IsName())
                {
                    allHex = allHex && current.IsHex();
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                    allHex = false;
                }
                else
                {
                    Back();
                    var text = EndContent();
                    return allHex ? NewColor(text) : NewHash(text);
                }
            }
        }

        private Token HashStart()
        {
            BeginContentAt(Source.Index);
            var current = GetNext();
            if (current.IsNameStart())
            {
                AppendLiteral(current);
                return HashRest();
            }

            if (IsValidEscape(current))
            {
                current = GetNext();
                AppendEscape(current);
                return HashRest();
            }

            if (current == Symbols.ReverseSolidus)
            {
                RaiseErrorOccurred(ParseError.InvalidCharacter);
                Back();
                return NewDelimiter(Symbols.Num);
            }

            Back();
            return NewDelimiter(Symbols.Num);
        }

        private Token HashRest()
        {
            while (true)
            {
                var current = GetNext();
                if (current.IsName())
                {
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else if (current == Symbols.ReverseSolidus)
                {
                    RaiseErrorOccurred(ParseError.InvalidCharacter);
                    Back();
                    return NewHash(EndContent());
                }
                else
                {
                    Back();
                    return NewHash(EndContent());
                }
            }
        }

        private Token Comment()
        {
            BeginContentAt(Source.Index);
            var current = GetNext();
            while (current != Symbols.EndOfFile)
                if (current == Symbols.Asterisk)
                {
                    current = GetNext();
                    if (current == Symbols.Solidus) return NewComment(EndContent());
                    AppendLiteral(Symbols.Asterisk);
                }
                else
                {
                    AppendLiteral(current);
                    current = GetNext();
                }

            RaiseErrorOccurred(ParseError.EOF);
            return NewComment(EndContent(), true);
        }

        private Token AtKeywordStart()
        {
            BeginContentAt(Source.Index);
            var current = GetNext();
            if (current == Symbols.Minus)
            {
                current = GetNext();
                if (current.IsNameStart() || IsValidEscape(current))
                {
                    AppendLiteral(Symbols.Minus);
                    return AtKeywordRest(current);
                }

                Back(2);
                return NewDelimiter(Symbols.At);
            }

            if (current.IsNameStart())
            {
                AppendLiteral(current);
                return AtKeywordRest(GetNext());
            }

            if (IsValidEscape(current))
            {
                current = GetNext();
                AppendEscape(current);
                return AtKeywordRest(GetNext());
            }

            Back();
            return NewDelimiter(Symbols.At);
        }

        private Token AtKeywordRest(char current)
        {
            while (true)
            {
                if (current.IsName())
                {
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else
                {
                    Back();
                    return NewAtKeyword(EndContent());
                }

                current = GetNext();
            }
        }

        private Token IdentStart(char current)
        {
            BeginContentAt(Source.Index - 1);

            if (current == Symbols.Minus)
            {
                current = GetNext();
                if (current.IsNameStart() || current == Symbols.Minus || IsValidEscape(current))
                {
                    AppendLiteral(Symbols.Minus);
                    return IdentRest(current);
                }

                Back();
                return NewDelimiter(Symbols.Minus);
            }

            if (current.IsNameStart())
            {
                AppendLiteral(current);
                return IdentRest(GetNext());
            }

            if (current == Symbols.ReverseSolidus && IsValidEscape(current))
            {
                current = GetNext();
                AppendEscape(current);
                return IdentRest(GetNext());
            }

            return Data(current);
        }

        private Token IdentRest(char current)
        {
            while (true)
            {
                if (current.IsName())
                {
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else if (current == Symbols.RoundBracketOpen)
                {
                    var name = EndContent();
                    var type = name.Span.GetTypeFromName();
                    return type == TokenType.Function ? NewFunction(name) : UrlStart(name.ToString());
                }
                else
                {
                    Back();
                    return NewIdent(EndContent());
                }

                current = GetNext();
            }
        }
        //private Token TransformFunctionWhitespace(char current)
        //{
        //    while (true)
        //    {
        //        current = GetNext();
        //        if (current == Symbols.RoundBracketOpen)
        //        {
        //            Back();
        //            return NewFunction(FlushBuffer());
        //        }
        //        if (!current.IsSpaceCharacter())
        //        {
        //            Back(2);
        //            return NewIdent(FlushBuffer());
        //        }
        //    }
        //}

        private Token NumberStart(char current)
        {
            while (true)
            {
                BeginContentAt(Source.Index - 1);

                if (current.IsOneOf(Symbols.Plus, Symbols.Minus))
                {
                    AppendLiteral(current);
                    current = GetNext();
                    if (current == Symbols.Dot)
                    {
                        AppendLiteral(current);
                        AppendLiteral(GetNext());
                        return NumberFraction();
                    }

                    AppendLiteral(current);
                    return NumberRest();
                }

                if (current == Symbols.Dot)
                {
                    AppendLiteral(current);
                    AppendLiteral(GetNext());
                    return NumberFraction();
                }

                if (current.IsDigit())
                {
                    AppendLiteral(current);
                    return NumberRest();
                }

                current = GetNext();
            }
        }

        private Token NumberRest()
        {
            var current = GetNext();
            while (true)
            {
                if (current.IsDigit())
                {
                    AppendLiteral(current);
                }
                else if (current == 'e' || current == 'E')
                {
                    // Defer to the switch below: NumberExponential already re-derives (and correctly
                    // falls back to Dimension for) the same "digit, or sign then digit" decision, so
                    // there is nothing to disambiguate here - just stop claiming 'e'/'E' as a unit
                    // start before that dedicated exponent handling ever gets a chance to run.
                    break;
                }
                else
                {
                    var dimension = TryStartDimension(current);
                    if (dimension is not null) return dimension.Value;
                    break;
                }

                current = GetNext();
            }

            switch (current)
            {
                case Symbols.Dot:
                    current = GetNext();
                    if (current.IsDigit())
                    {
                        AppendLiteral(Symbols.Dot);
                        AppendLiteral(current);
                        return NumberFraction();
                    }

                    Back();
                    return NewNumber(EndContent());
                case 'e':
                case 'E':
                    return NumberExponential(current);
                case '%':
                case Symbols.Minus:
                default:
                    return FinishNumberOrPercentage(current);
            }
        }

        private Token NumberFraction()
        {
            var current = GetNext();
            while (true)
            {
                if (current.IsDigit())
                {
                    AppendLiteral(current);
                }
                else if (current == 'e' || current == 'E')
                {
                    break;
                }
                else
                {
                    var dimension = TryStartDimension(current);
                    if (dimension is not null) return dimension.Value;
                    break;
                }

                current = GetNext();
            }

            switch (current)
            {
                case 'e':
                case 'E':
                    return NumberExponential(current);
                case '%':
                case Symbols.Minus:
                default:
                    return FinishNumberOrPercentage(current);
            }
        }

        /// <summary>
        /// Shared by <see cref="NumberRest"/>/<see cref="NumberFraction"/>/<see cref="SciNotation"/>'s
        /// digit-scanning loops: once a number's digits stop, checks whether <paramref name="current"/>
        /// starts a unit (a name-start character, or a valid escape) and if so flushes the accumulated
        /// digits as the number and hands off to <see cref="Dimension"/> to consume it. Returns
        /// <see langword="null"/> when <paramref name="current"/> doesn't start a unit, leaving the
        /// caller's loop position and <see cref="LexerBase.StringBuffer"/> untouched so the caller can
        /// break out and dispatch on <paramref name="current"/> itself.
        /// </summary>
        private Token? TryStartDimension(char current)
        {
            // Captured once, up front: current was already read by the caller (Source.Index is one past
            // it), and neither branch below reads anything else before deciding whether current itself
            // starts the unit - so this is exactly current's own position regardless of which branch runs.
            var currentPosition = Source.Index - 1;

            if (current.IsNameStart())
            {
                var number = EndContent();
                BeginContentAt(currentPosition);
                AppendLiteral(current);
                return Dimension(number);
            }

            if (IsValidEscape(current))
            {
                current = GetNext();
                var number = EndContent();
                BeginContentAt(currentPosition);
                AppendEscape(current);
                return Dimension(number);
            }

            return null;
        }

        /// <summary>
        /// Shared terminal dispatch for <see cref="NumberRest"/>/<see cref="NumberFraction"/>/
        /// <see cref="SciNotation"/>: once a number is known to be neither a fraction continuation nor
        /// an exponent nor a unit-starting dimension, decides between a percentage, a dash-led dimension
        /// (<see cref="NumberDash"/>), or a plain number.
        /// </summary>
        private Token FinishNumberOrPercentage(char current)
        {
            if (current == '%')
            {
                return NewPercentage(EndContent());
            }

            if (current == Symbols.Minus)
            {
                return NumberDash();
            }

            Back();
            return NewNumber(EndContent());
        }

        private Token Dimension(ReadOnlyMemory<char> number)
        {
            while (true)
            {
                var current = GetNext();
                if (current.IsLetter())
                {
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else
                {
                    Back();
                    return NewDimension(number, EndContent().ToString());
                }
            }
        }

        private Token SciNotation()
        {
            var current = GetNext();
            while (true)
            {
                if (current.IsDigit())
                {
                    AppendLiteral(current);
                }
                else
                {
                    var dimension = TryStartDimension(current);
                    if (dimension is not null) return dimension.Value;
                    break;
                }

                current = GetNext();
            }

            return FinishNumberOrPercentage(current);
        }

        private Token UrlStart(string functionName)
        {
            var current = SkipSpaces();
            switch (current)
            {
                case Symbols.EndOfFile:
                    RaiseErrorOccurred(ParseError.EOF);
                    return NewUrl(functionName, ReadOnlyMemory<char>.Empty, true);
                case Symbols.DoubleQuote:
                    return UrlDoubleQuote(functionName);
                case Symbols.SingleQuote:
                    return UrlSingleQuote(functionName);
                case Symbols.RoundBracketClose:
                    return NewUrl(functionName, ReadOnlyMemory<char>.Empty);
                default:
                    return UrlUnquoted(current, functionName);
            }
        }

        private Token UrlDoubleQuote(string functionName)
        {
            BeginContentAt(Source.Index);
            while (true)
            {
                var current = GetNext();
                if (current.IsLineBreak())
                {
                    RaiseErrorOccurred(ParseError.LineBreakUnexpected);
                    return UrlBad(functionName);
                }

                switch (current)
                {
                    case Symbols.EndOfFile:
                        return NewUrl(functionName, EndContent());
                    case Symbols.DoubleQuote:
                        return UrlEnd(functionName);
                }

                if (current != Symbols.ReverseSolidus)
                {
                    AppendLiteral(current);
                }
                else
                {
                    current = GetNext();
                    if (current == Symbols.EndOfFile)
                    {
                        Back(2);
                        RaiseErrorOccurred(ParseError.EOF);
                        return NewUrl(functionName, EndContent(), true);
                    }

                    if (current.IsLineBreak())
                        AppendLineContinuation();
                    else
                        AppendEscape(current);
                }
            }
        }

        private Token UrlSingleQuote(string functionName)
        {
            BeginContentAt(Source.Index);
            while (true)
            {
                var current = GetNext();
                if (current.IsLineBreak())
                {
                    RaiseErrorOccurred(ParseError.LineBreakUnexpected);
                    return UrlBad(functionName);
                }

                switch (current)
                {
                    case Symbols.EndOfFile:
                        return NewUrl(functionName, EndContent());
                    case Symbols.SingleQuote:
                        return UrlEnd(functionName);
                }

                if (current != Symbols.ReverseSolidus)
                {
                    AppendLiteral(current);
                }
                else
                {
                    current = GetNext();
                    if (current == Symbols.EndOfFile)
                    {
                        Back(2);
                        RaiseErrorOccurred(ParseError.EOF);
                        return NewUrl(functionName, EndContent(), true);
                    }

                    if (current.IsLineBreak())
                        AppendLineContinuation();
                    else
                        AppendEscape(current);
                }
            }
        }

        private Token UrlUnquoted(char current, string functionName)
        {
            BeginContentAt(Source.Index - 1);
            while (true)
            {
                if (current.IsSpaceCharacter()) return UrlEnd(functionName);
                if (current.IsOneOf(Symbols.RoundBracketClose, Symbols.EndOfFile))
                    return NewUrl(functionName, EndContent());
                if (current.IsOneOf(Symbols.DoubleQuote, Symbols.SingleQuote, Symbols.RoundBracketOpen) ||
                    current.IsNonPrintable())
                {
                    RaiseErrorOccurred(ParseError.InvalidCharacter);
                    return UrlBad(functionName);
                }

                if (current != Symbols.ReverseSolidus)
                {
                    AppendLiteral(current);
                }
                else if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else
                {
                    RaiseErrorOccurred(ParseError.InvalidCharacter);
                    return UrlBad(functionName);
                }

                current = GetNext();
            }
        }

        private Token UrlEnd(string functionName)
        {
            while (true)
            {
                var current = GetNext();
                if (current == Symbols.RoundBracketClose) return NewUrl(functionName, EndContent());

                if (current.IsSpaceCharacter()) continue;

                RaiseErrorOccurred(ParseError.InvalidCharacter);
                Back();
                return UrlBad(functionName);
            }
        }

        private Token UrlBad(string functionName)
        {
            // A rare error-recovery path with non-trivial paren/brace-depth bookkeeping and multiple
            // possible entry points (mid-run from UrlDoubleQuote/SingleQuote/Unquoted/End) - always
            // materialize via FlushBuffer() rather than trying to verify slice-safety here; the buffer
            // was already being accumulated character-by-character by the run this continues anyway.
            _mustMaterialize = true;

            var current = Current;
            var curly = 0;
            var round = 1;
            while (current != Symbols.EndOfFile)
            {
                if (current == Symbols.Semicolon)
                {
                    Back();
                    return NewUrl(functionName, EndContent(), true);
                }

                if (current == Symbols.CurlyBracketClose && --curly == -1)
                {
                    Back();
                    return NewUrl(functionName, EndContent(), true);
                }

                if (current == Symbols.RoundBracketClose && --round == 0)
                    return NewUrl(functionName, EndContent(), true);
                if (IsValidEscape(current))
                {
                    current = GetNext();
                    AppendEscape(current);
                }
                else
                {
                    if (current == Symbols.RoundBracketOpen)
                        ++round;
                    else if (curly == Symbols.CurlyBracketOpen) ++curly;
                    StringBuffer.Append(current);
                }

                current = GetNext();
            }

            RaiseErrorOccurred(ParseError.EOF);
            return NewUrl(functionName, EndContent(), true);
        }

        private Token UnicodeRange(char current)
        {
            BeginContentAt(Source.Index - 1);

            for (var i = 0; i < 6 && current.IsHex(); i++)
            {
                AppendLiteral(current);
                current = GetNext();
            }

            // Fewer than 6 hex digits may be followed by '?' wildcards (which pad the value and are
            // mutually exclusive with the start-end range form) OR - like a full 6-digit start - by a
            // '-<hex>' range end (e.g. U+41-5A). Only the wildcard form is handled here; a '-' falls
            // through to the shared range handling below.
            if (StringBuffer.Length != 6 && current == Symbols.QuestionMark)
            {
                // The wildcard budget must be captured up front: appending to StringBuffer inside the loop
                // would otherwise shrink a "6 - StringBuffer.Length" bound on every iteration, so only half
                // the available wildcards were ever consumed (U+?????? stopped after three).
                var wildcards = 6 - StringBuffer.Length;

                for (var i = 0; i < wildcards; i++)
                {
                    if (current != Symbols.QuestionMark)
                    {
                        // ReSharper disable once RedundantAssignment
                        current = GetPrevious();
                        break;
                    }

                    AppendLiteral(current);
                    current = GetNext();
                }

                return NewRange(EndContent().ToString());
            }

            if (current == Symbols.Minus)
            {
                // current (the '-') was already read by the main hex loop above, so its own position is
                // one behind live Source.Index right now.
                var minusPosition = Source.Index - 1;
                current = GetNext();
                if (current.IsHex())
                {
                    var start = EndContent();
                    BeginContentAt(minusPosition + 1);
                    for (var i = 0; i < 6; i++)
                    {
                        if (!current.IsHex())
                        {
                            // ReSharper disable once RedundantAssignment
                            current = GetPrevious();
                            break;
                        }

                        AppendLiteral(current);
                        current = GetNext();
                    }

                    var end = EndContent();
                    return NewRange(start.ToString(), end.ToString());
                }

                Back(2);
                return NewRange(EndContent().ToString());
            }

            Back();
            return NewRange(EndContent().ToString());
        }

        private Token NewMatch(string match)
        {
            return new(TokenType.Match, match.AsMemory(), _position);
        }

        private Token NewColumn()
        {
            return new(TokenType.Column, Combinators.Column.AsMemory(), _position);
        }

        private Token NewCloseCurly()
        {
            return new(TokenType.CurlyBracketClose, "}".AsMemory(), _position);
        }

        private Token NewOpenCurly()
        {
            return new(TokenType.CurlyBracketOpen, "{".AsMemory(), _position);
        }

        private Token NewCloseSquare()
        {
            return new(TokenType.SquareBracketClose, "]".AsMemory(), _position);
        }

        private Token NewOpenSquare()
        {
            return new(TokenType.SquareBracketOpen, "[".AsMemory(), _position);
        }

        private Token NewOpenComment()
        {
            return new(TokenType.Cdo, "<!--".AsMemory(), _position);
        }

        private Token NewSemicolon()
        {
            return new(TokenType.Semicolon, ";".AsMemory(), _position);
        }

        private Token NewColon()
        {
            return new(TokenType.Colon, ":".AsMemory(), _position);
        }

        private Token NewCloseComment()
        {
            return new(TokenType.Cdc, "-->".AsMemory(), _position);
        }

        private Token NewComma()
        {
            return new(TokenType.Comma, ",".AsMemory(), _position);
        }

        private Token NewCloseRound()
        {
            return new(TokenType.RoundBracketClose, ")".AsMemory(), _position);
        }

        private Token NewOpenRound()
        {
            return new(TokenType.RoundBracketOpen, "(".AsMemory(), _position);
        }

        private Token NewString(ReadOnlyMemory<char> value, char quote, bool bad = false)
        {
            return Token.NewString(value, bad, quote, _position);
        }

        private Token NewHash(ReadOnlyMemory<char> value)
        {
            return Token.NewKeyword(TokenType.Hash, value, _position);
        }

        private Token NewComment(ReadOnlyMemory<char> value, bool bad = false)
        {
            return Token.NewComment(value, bad, _position);
        }

        private Token NewAtKeyword(ReadOnlyMemory<char> value)
        {
            return Token.NewKeyword(TokenType.AtKeyword, value, _position);
        }

        private Token NewIdent(ReadOnlyMemory<char> value)
        {
            return Token.NewKeyword(TokenType.Ident, value, _position);
        }

        private Token NewFunction(ReadOnlyMemory<char> value)
        {
            var function = Token.NewFunction(value, _position);

            // Tracks paren depth so a bare (non-function) parenthesized group nested inside the
            // function's arguments - e.g. calc((1px + 2px) * 3) - doesn't get mistaken for the end of
            // the function: only the closing paren that matches this function's own opening paren (depth
            // returning to 0) should terminate it. A nested function call's own parens are already fully
            // consumed by its own (recursive) call to this method before it's added as a single token
            // here, so they never surface as bare RoundBracketOpen/Close tokens at this level.
            var depth = 1;
            var token = Get();
            while (token.Type != TokenType.EndOfFile)
            {
                function.AddArgumentToken(token);

                if (token.Type == TokenType.RoundBracketOpen)
                {
                    depth++;
                }
                else if (token.Type == TokenType.RoundBracketClose)
                {
                    depth--;
                    if (depth == 0) break;
                }

                token = Get();
            }

            return function;
        }

        private Token NewPercentage(ReadOnlyMemory<char> value)
        {
            return Token.NewUnit(TokenType.Percentage, value, "%", _position);
        }

        private Token NewDimension(ReadOnlyMemory<char> value, string unit)
        {
            return Token.NewUnit(TokenType.Dimension, value, unit, _position);
        }

        private Token NewUrl(string functionName, ReadOnlyMemory<char> data, bool bad = false)
        {
            return Token.NewUrl(functionName, data, bad, _position);
        }

        private Token NewRange(string range)
        {
            return Token.NewRange(range, _position);
        }

        private Token NewRange(string start, string end)
        {
            return Token.NewRange(start, end, _position);
        }

        private Token NewWhitespace(char character)
        {
            return new(TokenType.Whitespace, SingleCharStrings.Of(character).AsMemory(), _position);
        }

        private Token NewNumber(ReadOnlyMemory<char> number)
        {
            return Token.NewNumber(number, _position);
        }

        private Token NewDelimiter(char c)
        {
            return new(TokenType.Delim, SingleCharStrings.Of(c).AsMemory(), _position);
        }

        private Token NewColor(ReadOnlyMemory<char> text)
        {
            return Token.NewColor(text, _position);
        }

        private Token NewEof()
        {
            return new(TokenType.EndOfFile, ReadOnlyMemory<char>.Empty, _position);
        }

        private Token NewGreaterThan() => new Token(TokenType.GreaterThan, ">".AsMemory(), _position);
        private Token NewGreaterThanOrEqual() => new Token(TokenType.GreaterThanOrEqual, ">=".AsMemory(), _position);
        private Token NewLessThan() => new Token(TokenType.LessThan, "<".AsMemory(), _position);
        private Token NewLessThanOrEqual() => new Token(TokenType.LessThanOrEqual, "<=".AsMemory(), _position);


        private Token NumberExponential(char letter)
        {
            // letter ('e'/'E') was already read by the caller, so its own position is one behind live
            // Source.Index right now - captured up front since the branches below read further ahead
            // (and sometimes back up again) before deciding where the number ends and the unit begins.
            var letterPosition = Source.Index - 1;
            var current = GetNext();
            if (current.IsDigit())
            {
                AppendLiteral(letter);
                AppendLiteral(current);
                return SciNotation();
            }

            if (current == Symbols.Plus || current == Symbols.Minus)
            {
                var op = current;
                current = GetNext();
                if (current.IsDigit())
                {
                    AppendLiteral(letter);
                    AppendLiteral(op);
                    AppendLiteral(current);
                    return SciNotation();
                }

                Back();
            }

            // Not actually a valid exponent (e.g. "3e" or "3e+" with no following digit) - the number is
            // just the digits scanned so far, and 'letter' becomes the first character of a dimension's
            // unit instead (e.g. "3e px" is nonsensical CSS, but "3epx" parses as the dimension "3" + "epx").
            var number = EndContent();
            BeginContentAt(letterPosition);
            AppendLiteralAt(letter, letterPosition);
            Back();
            return Dimension(number);
        }

        private Token NumberDash()
        {
            // The '-' was already read by the caller, so its own position is one behind live Source.Index.
            var minusPosition = Source.Index - 1;
            var current = GetNext();
            if (current.IsNameStart())
            {
                var number = EndContent();
                BeginContentAt(minusPosition);
                AppendLiteral(Symbols.Minus);
                AppendLiteral(current);
                return Dimension(number);
            }

            if (IsValidEscape(current))
            {
                current = GetNext();
                var number = EndContent();
                BeginContentAt(minusPosition);
                AppendLiteral(Symbols.Minus);
                AppendEscape(current);
                return Dimension(number);
            }

            Back(2);
            return NewNumber(EndContent());
        }

        // Appends a CSS escape sequence's decoded content directly to StringBuffer, without ever
        // allocating a scratch string for it - char.ConvertFromUtf32 exists only to hand back a
        // string, but nearly every real escape resolves to a single BMP codepoint appendable as one
        // char, and a rare supplementary-plane escape needs only the same manual surrogate-pair math
        // ConvertFromUtf32 performs internally, computed straight into the buffer.
        private void AppendEscape(char current)
        {
            // Escape-decoded content never matches the literal source bytes it came from (that's the
            // entire point of an escape), so a token whose scan crosses one can never be sliced.
            _mustMaterialize = true;

            if (!current.IsHex())
            {
                StringBuffer.Append(current);
                return;
            }

            Span<char> escape = stackalloc char[6];
            var length = 0;
            var isHex = true;
            while (isHex && length < escape.Length)
            {
                escape[length++] = current;
                current = GetNext();
                isHex = current.IsHex();
            }

            if (!current.IsSpaceCharacter()) Back();

            var code = int.Parse(escape[..length], NumberStyles.HexNumber);

            if (code.IsInvalid())
            {
                StringBuffer.Append(Symbols.Replacement);
                return;
            }

            if (code <= 0xFFFF)
            {
                StringBuffer.Append((char)code);
                return;
            }

            code -= 0x10000;
            StringBuffer.Append((char)((code >> 10) + 0xD800)).Append((char)((code & 0x3FF) + 0xDC00));
        }

        private bool IsValidEscape(char current)
        {
            if (current != Symbols.ReverseSolidus) return false;
            current = GetNext();
            Back();
            return current != Symbols.EndOfFile && !current.IsLineBreak();
        }

        private void RaiseErrorOccurred(ParseError code)
        {
            RaiseErrorOccurred(code, GetCurrentPosition());
        }
    }
}
