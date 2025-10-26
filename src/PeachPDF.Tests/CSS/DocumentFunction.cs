namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;
    public class CssDocumentFunctionTests : CssConstructionFunctions
    {
        [Fact]
        public void CssDocumentRuleSingleUrlFunction()
        {
            var snippet = "@document url(http://www.w3.org/) { }";
            var rule = ParseRule(snippet) as DocumentRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleType.Document, rule.Type);
            Assert.Single(rule.Conditions);
            var condition = rule.Conditions.First();
            Assert.Equal("url", condition.Name);
            Assert.Equal("http://www.w3.org/", condition.Data);
            //Assert.False(condition.Matches(Url.Create("https://www.w3.org/")));
            //Assert.True(condition.Matches(Url.Create("http://www.w3.org")));
        }

        [Fact]
        public void CssDocumentRuleSingleUrlPrefixFunction()
        {
            var snippet = "@document url-prefix(http://www.w3.org/Style/) { }";
            var rule = ParseRule(snippet) as DocumentRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleType.Document, rule.Type);
            Assert.Single(rule.Conditions);
            var condition = rule.Conditions.First();
            Assert.Equal("url-prefix", condition.Name);
            Assert.Equal("http://www.w3.org/Style/", condition.Data);
            //Assert.False(condition.Matches(Url.Create("https://www.w3.org/Style/")));
            //Assert.True(condition.Matches(Url.Create("http://www.w3.org/Style/foo/bar")));
        }

        [Fact]
        public void CssDocumentRuleSingleDomainFunction()
        {
            var snippet = "@document domain('mozilla.org') { }";
            var rule = ParseRule(snippet) as DocumentRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleType.Document, rule.Type);
            Assert.Single(rule.Conditions);
            var condition = rule.Conditions.First();
            Assert.Equal("domain", condition.Name);
            Assert.Equal("mozilla.org", condition.Data);
            //Assert.False(condition.Matches(Url.Create("https://www.w3.org/")));
            //Assert.True(condition.Matches(Url.Create("http://mozilla.org")));
            //Assert.True(condition.Matches(Url.Create("http://www.mozilla.org")));
            //Assert.True(condition.Matches(Url.Create("http://foo.mozilla.org")));
        }

        [Fact]
        public void CssDocumentRuleSingleRegexpFunction()
        {
            var snippet = "@document regexp(\"https:.*\") { }";
            var rule = ParseRule(snippet) as DocumentRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleType.Document, rule.Type);
            Assert.Single(rule.Conditions);
            var condition = rule.Conditions.First();
            Assert.Equal("regexp", condition.Name);
            Assert.Equal("https:.*", condition.Data);
            //Assert.False(condition.Matches(Url.Create("http://www.w3.org")));
            //Assert.True(condition.Matches(Url.Create("https://www.w3.org/")));
        }

        [Fact]
        public void CssDocumentRuleMultipleFunctions()
        {
            var snippet = "@document url(http://www.w3.org/), url-prefix(http://www.w3.org/Style/), domain(mozilla.org), regexp(\"https:.*\") { }";
            var rule = ParseRule(snippet) as DocumentRule;
            Assert.NotNull(rule);
            Assert.Equal(RuleType.Document, rule.Type);
            Assert.Equal(4, rule.Conditions.Count());
            //Assert.True(rule.IsValid(Url.Create("https://www.w3.org/")));
            //Assert.True(rule.IsValid(Url.Create("http://www.w3.org/")));
            //Assert.True(rule.IsValid(Url.Create("http://www.w3.org/Style/bar")));
            //Assert.True(rule.IsValid(Url.Create("https://test.mozilla.org/foo")));
            //Assert.False(rule.IsValid(Url.Create("http://localhost")));
        }
    }
}







