namespace PeachPDF.Tests.CSS
{
    using System.Collections.Generic;
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using Xunit;

    // Coverage for the List<Token> pooling infrastructure (issue #922, part 3d) - Pool.NewTokenList/
    // ReturnTokenList and CssValueParser.GetCssTokensPooled's PooledTokenList wrapper.
    public class PooledTokenListTests
    {
        [Fact]
        public void GetCssTokensPooled_ProducesTheSameTokensAsGetCssTokens()
        {
            const string css = "1px solid red";

            var expected = CssValueParser.GetCssTokens(css);

            using var pooled = CssValueParser.GetCssTokensPooled(css);
            List<Token> actual = pooled;

            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Type, actual[i].Type);
                Assert.Equal(expected[i].Data, actual[i].Data);
            }
        }

        [Fact]
        public void GetCssTokensPooled_RespectsInValueContextAndPreserveWhitespace()
        {
            using var pooled = CssValueParser.GetCssTokensPooled("#f00 1px", inValueContext: true, preserveWhitespace: true);
            List<Token> tokens = pooled;

            Assert.Equal(TokenType.Color, tokens[0].Type);
            Assert.Contains(tokens, t => t.Type == TokenType.Whitespace);
        }

        [Fact]
        public void Pool_NewTokenList_ReturnsAClearedListAfterReturnTokenList()
        {
            var first = Pool.NewTokenList();
            first.Add(Token.Comma);
            first.Add(Token.Whitespace);
            Pool.ReturnTokenList(first);

            var second = Pool.NewTokenList();

            Assert.Same(first, second);
            Assert.Empty(second);
        }

        [Fact]
        public void PooledTokenList_Dispose_ReturnsTheUnderlyingListToThePool()
        {
            List<Token> captured;
            using (var pooled = CssValueParser.GetCssTokensPooled("a b c"))
            {
                captured = pooled;
            }

            var reused = Pool.NewTokenList();

            Assert.Same(captured, reused);
        }
    }
}
