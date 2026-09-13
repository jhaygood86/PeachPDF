namespace PeachPDF.Tests.CSS
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using PeachPDF.CSS;
    using Xunit;

    // Regression coverage for Token.Data's memory-slice-backed design (Lexer.BeginContentAt/
    // AppendLiteral/EndContent): most tokens' Data is a zero-allocation slice of the shared TextSource
    // buffer rather than an eagerly materialized string, with escape sequences, CRLF/lone-CR
    // normalization, and escaped line continuations as the three documented exceptions that force an
    // owned-string fallback instead. These tests exercise exactly those exception boundaries - that
    // Data's *content* is correct in each case, not (there being only one accessor now - see
    // .claude/recent-fixes/2026-09-12-token-data-becomes-span-only.md) an equivalence between two.
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
            Assert.Equal("a\nb", token.Data.ToString());
        }

        [Fact]
        public void Comment_ContainingLoneCarriageReturn_NormalizesToLineFeed()
        {
            var token = FirstToken("/*a\rb*/");

            Assert.Equal(TokenType.Comment, token.Type);
            Assert.Equal("a\nb", token.Data.ToString());
        }

        [Fact]
        public void String_ContainingEscapeSequence_MaterializesDecodedContent()
        {
            // \41 is the hex escape for 'A' (CSS Syntax §4.3.7).
            var token = FirstToken("\"a\\41 b\"");

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("aAb", token.Data.ToString());
        }

        [Fact]
        public void String_ContainingEscapedLineContinuation_MaterializesWithoutTheEscapedNewline()
        {
            // A backslash immediately followed by a newline is a line continuation (CSS Syntax §4.3.7) -
            // the string spans the source line break without the escaped newline appearing in Data.
            var token = FirstToken("\"a\\\nb\"");

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("ab", token.Data.ToString());
        }

        [Fact]
        public void Ident_PlainAsciiContent_SlicesDirectlyFromSource()
        {
            var token = FirstToken("hello-world");

            Assert.Equal(TokenType.Ident, token.Type);
            Assert.Equal("hello-world", token.Data.ToString());
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
                        Assert.Equal($"str{i}", token.Data.ToString());
                        break;
                    case 4:
                        Assert.Equal(TokenType.Comment, token.Type);
                        Assert.False(token.IsValid);
                        Assert.Equal($"comment{i}", token.Data.ToString());
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
