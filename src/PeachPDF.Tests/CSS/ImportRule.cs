namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssImportRuleTests
    {
        static ImportRule NewImportRule(string cssText)
        {
            var parser = new StylesheetParser();
            var rule = new ImportRule(parser) { Text = cssText };
            return rule;
        }

        [Fact]
        public void CssImportWithNonQuotedUrl()
        {
            var source = "@import url(button.css);";
            var rule = NewImportRule(source);
            Assert.Equal("button.css", rule.Href);
            Assert.Equal("", rule.Media.MediaText);
        }

        [Fact]
        public void CssImportWithDoubleQuotedUrl()
        {
            var source = "@import url(\"button.css\");";
            var rule = NewImportRule(source);
            Assert.Equal("button.css", rule.Href);
            Assert.Equal("", rule.Media.MediaText);
        }

        [Fact]
        public void CssImportWithSingleQuotedUrl()
        {
            var source = "@import url('button.css');";
            var rule = NewImportRule(source);
            Assert.Equal("button.css", rule.Href);
            Assert.Equal("", rule.Media.MediaText);
        }

        [Fact]
        public void CssImportWithDoubleQuotedStringAsUrl()
        {
            var source = "@import \"button.css\";";
            var rule = NewImportRule(source);
            Assert.Equal("button.css", rule.Href);
            Assert.Equal("", rule.Media.MediaText);
        }

        [Fact]
        public void CssImportWithSingleQuotedStringAsUrl()
        {
            var source = "@import 'button.css';";
            var rule = NewImportRule(source);
            Assert.Equal("button.css", rule.Href);
            Assert.Equal("", rule.Media.MediaText);
        }

        [Fact]
        public void CssImportWithUrlAndAllMedia()
        {
            var media = "all";
            var source = "@import url(size/medium.css) " + media + ";";
            var rule = NewImportRule(source);
            Assert.Equal("size/medium.css", rule.Href);
            Assert.Equal(media, rule.Media.MediaText);
            Assert.Equal(1, rule.Media.Length);
        }

        [Fact]
        public void CssImportWithUrlAndComplicatedMedia()
        {
            var media = "screen and (color), projection and (min-color: 256)";
            var source = "@import url(old.css) " + media + ";";
            var rule = NewImportRule(source);
            Assert.Equal("old.css", rule.Href);
            Assert.Equal(media, rule.Media.MediaText);
            Assert.Equal(2, rule.Media.Length);
        }
    }
}







