namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssTokenizationTests
    {
        [Fact]
        public void CssParserIdentifier()
        {
            var teststring = "h1 { background: blue; }";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(TokenType.Ident, token.Type);
        }

        [Fact]
        public void CssParserAtRule()
        {
            var teststring = "@media { background: blue; }";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(TokenType.AtKeyword, token.Type);
        }

        [Fact]
        public void CssParserUrlUnquoted()
        {
            var url = "http://someurl";
            var teststring = "url(" + url + ")";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        [Fact]
        public void CssParserUrlDoubleQuoted()
        {
            var url = "http://someurl";
            var teststring = "url(\"" + url + "\")";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        [Fact]
        public void CssParserUrlSingleQuoted()
        {
            var url = "http://someurl";
            var teststring = "url('" + url + "')";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        [Fact]
        public void LexerOnlyCarriageReturn()
        {
            var teststring = "\r";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }

        [Fact]
        public void LexerCarriageReturnLineFeed()
        {
            var teststring = "\r\n";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }

        [Fact]
        public void LexerOnlyLineFeed()
        {
            var teststring = "\n";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }
    }
}







