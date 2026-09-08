namespace PeachPDF.Tests.CSS
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using PeachPDF.CSS;
    using Xunit;

    // Regression coverage for Token.Data's memory-slice-backed design (Lexer.BeginContentAt/
    // AppendLiteral/EndContent): most tokens' Data is now a zero-allocation slice of the shared
    // TextSource buffer rather than an eagerly materialized string, with escape sequences, CRLF/lone-CR
    // normalization, and escaped line continuations as the three documented exceptions that force an
    // owned-string fallback instead. These tests exercise exactly those exception boundaries, plus the
    // DataSpan/Data equivalence invariant a range/length miscalculation in either path would violate.
    public class TokenDataSlicingTests
    {
        private static Token FirstToken(string css)
        {
            var tokenizer = new Lexer(new TextSource(css));
            return tokenizer.Get();
        }

        [Fact]
        public void Comment_ContainingCrLfPair_NormalizesToLoneLineFeed()
        {
            var token = FirstToken("/*a\r\nb*/");

            Assert.Equal(TokenType.Comment, token.Type);
            Assert.Equal("a\nb", token.Data);
            Assert.Equal(token.Data, token.DataSpan.ToString());
        }

        [Fact]
        public void Comment_ContainingLoneCarriageReturn_NormalizesToLineFeed()
        {
            var token = FirstToken("/*a\rb*/");

            Assert.Equal(TokenType.Comment, token.Type);
            Assert.Equal("a\nb", token.Data);
            Assert.Equal(token.Data, token.DataSpan.ToString());
        }

        [Fact]
        public void String_ContainingEscapeSequence_MaterializesDecodedContent()
        {
            // \41 is the hex escape for 'A' (CSS Syntax §4.3.7).
            var token = FirstToken("\"a\\41 b\"");

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("aAb", token.Data);
            Assert.Equal(token.Data, token.DataSpan.ToString());
        }

        [Fact]
        public void String_ContainingEscapedLineContinuation_MaterializesWithoutTheEscapedNewline()
        {
            // A backslash immediately followed by a newline is a line continuation (CSS Syntax §4.3.7) -
            // the string spans the source line break without the escaped newline appearing in Data.
            var token = FirstToken("\"a\\\nb\"");

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal(token.Data, token.DataSpan.ToString());
        }

        [Fact]
        public void Ident_PlainAsciiContent_SlicesDirectlyFromSource()
        {
            var token = FirstToken("hello-world");

            Assert.Equal(TokenType.Ident, token.Type);
            Assert.Equal("hello-world", token.Data);
            Assert.Equal(token.Data, token.DataSpan.ToString());
        }

        [Theory]
        [InlineData("div { color: red; margin: 1.5em -3px; }")]
        [InlineData("@media screen and (min-width: 10px) { .a::before { content: \"x\"; } }")]
        [InlineData("a[href^=\"http\"] { background: url(foo.png) no-repeat; }")]
        [InlineData(".b { color: rgba(1, 2, 3, 0.5); transform: calc((1px + 2px) * 3); }")]
        [InlineData("/* leading comment */ .c { unicode-range: U+41-5A, U+??; }")]
        [InlineData(".d { font: 1e2px/1.2 sans-serif; width: 3e; height: 4e+; }")]
        public void RepresentativeStylesheets_EveryTokensDataSpanMatchesData(string css)
        {
            var tokenizer = new Lexer(new TextSource(css));
            Token token;
            var count = 0;

            do
            {
                token = tokenizer.Get();
                Assert.Equal(token.Data, token.DataSpan.ToString());

                if (token.Type == TokenType.Function)
                {
                    foreach (var argument in token.Arguments!)
                    {
                        Assert.Equal(argument.Data, argument.DataSpan.ToString());
                    }
                }

                count++;
                Assert.True(count < 10_000, "Tokenizer did not reach EndOfFile - possible infinite loop.");
            } while (token.Type != TokenType.EndOfFile);
        }

        // Regression guard for Token's common/TokenExtra split (issue #922 follow-up): the struct holds
        // only Type/Position/Data/a single TokenExtra reference (~5.71% of tokens ever allocate one - see
        // TokenExtra.cs's own class comment), shrinking it from the earlier 48-byte explicit-layout union
        // design to 40 bytes. A future field addition to Token itself (as opposed to TokenExtra) would
        // silently regrow the common struct without any other test noticing, since every *behavioral*
        // test would still pass.
        [Fact]
        public void Token_CommonStructSize_Is40Bytes()
        {
            Assert.Equal(40, Unsafe.SizeOf<Token>());
        }

        // Correctness coverage for the common/TokenExtra split: every TokenExtra-using kind (Range,
        // Dimension, Url, Function with nested-argument tokens, String, Comment) must keep reading back
        // correctly through a moving/compacting GC, including when the Token values themselves are
        // relocated by a List<Token> resize - ordinary reference fields have no special GC risk the way
        // the earlier [FieldOffset]-aliased design did, but this keeps the same "mixed token kinds
        // survive list churn" coverage the prior test provided.
        [Fact]
        public void TokenExtraFields_SurviveGcAndListResize_ForMixedTokenKinds()
        {
            var tokens = new List<Token>();

            for (var i = 0; i < 150; i++)
            {
                switch (i % 6)
                {
                    case 0:
                        tokens.Add(Token.NewRange($"{i:X2}", $"{i + 1:X2}", TextPosition.Empty));
                        break;
                    case 1:
                        tokens.Add(Token.NewUnit(TokenType.Dimension, i.ToString().AsMemory(), $"unit{i}",
                            TextPosition.Empty));
                        break;
                    case 2:
                        tokens.Add(Token.NewUrl($"url-fn-{i}", i.ToString().AsMemory(), false, TextPosition.Empty));
                        break;
                    case 3:
                        tokens.Add(Token.NewString($"str{i}".AsMemory(), true, '"', TextPosition.Empty));
                        break;
                    case 4:
                        tokens.Add(Token.NewComment($"comment{i}".AsMemory(), false, TextPosition.Empty));
                        break;
                    default:
                        var function = Token.NewFunction($"fn{i}".AsMemory(), TextPosition.Empty);
                        function.AddArgumentToken(Token.NewUnit(TokenType.Dimension, "1".AsMemory(), $"nested{i}",
                            TextPosition.Empty));
                        function.AddArgumentToken(Token.NewRange($"{i:X2}", $"{i + 2:X2}", TextPosition.Empty));
                        tokens.Add(function);
                        break;
                }
            }

            // Encourage the GC to actually move/compact objects, not just mark-and-sweep in place.
            for (var i = 0; i < 20; i++)
            {
                var junk = new byte[5_000_000];
                GC.KeepAlive(junk);
            }
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                switch (i % 6)
                {
                    case 0:
                        Assert.Equal(TokenType.Range, token.Type);
                        Assert.Equal($"{i:X2}", token.RangeStart);
                        Assert.Equal($"{i + 1:X2}", token.RangeEnd);
                        break;
                    case 1:
                        Assert.Equal(TokenType.Dimension, token.Type);
                        Assert.Equal($"unit{i}", token.Unit);
                        break;
                    case 2:
                        Assert.Equal(TokenType.Url, token.Type);
                        Assert.Equal($"url-fn-{i}", token.FunctionName);
                        break;
                    case 3:
                        Assert.Equal(TokenType.String, token.Type);
                        Assert.True(token.IsValid);
                        Assert.Equal('"', token.Quote);
                        Assert.Equal($"str{i}", token.Data);
                        break;
                    case 4:
                        Assert.Equal(TokenType.Comment, token.Type);
                        Assert.False(token.IsValid);
                        Assert.Equal($"comment{i}", token.Data);
                        break;
                    default:
                        Assert.Equal(TokenType.Function, token.Type);
                        Assert.Equal(2, token.Arguments!.Count);
                        Assert.Equal($"nested{i}", token.Arguments[0].Unit);
                        Assert.Equal($"{i:X2}", token.Arguments[1].RangeStart);
                        Assert.Equal($"{i + 2:X2}", token.Arguments[1].RangeEnd);
                        break;
                }
            }
        }
    }
}
