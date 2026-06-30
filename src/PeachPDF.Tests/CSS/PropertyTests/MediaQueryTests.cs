using PeachPDF.CSS;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class MediaQueryTests : CssConstructionFunctions
    {
        // ── media type parsing ─────────────────────────────────────────────────

        [Fact]
        public void AtMedia_Print_HasTypePrint()
        {
            var sheet = ParseStyleSheet("@media print { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var medium = rule.Media.Media.FirstOrDefault();
            Assert.NotNull(medium);
            Assert.Equal("print", medium.Type);
            Assert.False(medium.IsInverse);
            Assert.False(medium.IsExclusive);
        }

        [Fact]
        public void AtMedia_Screen_HasTypeScreen()
        {
            var sheet = ParseStyleSheet("@media screen { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var medium = rule.Media.Media.FirstOrDefault();
            Assert.NotNull(medium);
            Assert.Equal("screen", medium.Type);
        }

        [Fact]
        public void AtMedia_All_HasTypeAll()
        {
            var sheet = ParseStyleSheet("@media all { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var medium = rule.Media.Media.FirstOrDefault();
            Assert.NotNull(medium);
            Assert.Equal("all", medium.Type);
        }

        // ── not / only modifiers ───────────────────────────────────────────────

        [Fact]
        public void AtMedia_NotPrint_HasIsInverseTrue()
        {
            var sheet = ParseStyleSheet("@media not print { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var medium = rule.Media.Media.FirstOrDefault();
            Assert.NotNull(medium);
            Assert.Equal("print", medium.Type);
            Assert.True(medium.IsInverse);
        }

        [Fact]
        public void AtMedia_OnlyPrint_HasIsExclusiveTrue()
        {
            var sheet = ParseStyleSheet("@media only print { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var medium = rule.Media.Media.FirstOrDefault();
            Assert.NotNull(medium);
            Assert.Equal("print", medium.Type);
            Assert.True(medium.IsExclusive);
        }

        // ── comma-separated media list ─────────────────────────────────────────

        [Fact]
        public void AtMedia_PrintCommaScreen_HasTwoMediaEntries()
        {
            var sheet = ParseStyleSheet("@media print, screen { }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var media = rule.Media.Media.ToList();
            Assert.Equal(2, media.Count);
            Assert.Equal("print", media[0].Type);
            Assert.Equal("screen", media[1].Type);
        }

        // ── nested style rules ─────────────────────────────────────────────────

        [Fact]
        public void AtMedia_Print_NestedStyleRuleIsAccessible()
        {
            var sheet = ParseStyleSheet("@media print { p { color: red; } }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            var nested = rule.Rules.OfType<StyleRule>().FirstOrDefault();
            Assert.NotNull(nested);
            Assert.Equal("p", nested.SelectorText);
        }

        [Fact]
        public void AtMedia_Print_MultipleNestedRulesAreParsed()
        {
            var sheet = ParseStyleSheet("@media print { p { color: red; } div { font-size: 12pt; } }");
            var rule = sheet.Rules.OfType<MediaRule>().FirstOrDefault();
            Assert.NotNull(rule);
            Assert.Equal(2, rule.Rules.OfType<StyleRule>().Count());
        }
    }
}
