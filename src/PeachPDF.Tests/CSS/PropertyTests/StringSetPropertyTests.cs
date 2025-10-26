using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class StringSetPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void StringSetNoneLegal()
        {
            var snippet = "string-set: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void StringSetSimpleTextLegal()
        {
            var snippet = "string-set: chapter content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("chapter content()", concrete.Value);
        }

        [Fact]
        public void StringSetWithStringLiteral()
        {
            var snippet = "string-set: header \"Page \" counter(page)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("header \"Page \" counter(page)", concrete.Value);
        }

        [Fact]
        public void StringSetWithAttrFunction()
        {
            var snippet = "string-set: title attr(title)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("title attr(title)", concrete.Value);
        }

        [Fact]
        public void StringSetContentBefore()
        {
            var snippet = "string-set: heading content(before) \":\" content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("heading content(before) \":\" content()", concrete.Value);
        }

        [Fact]
        public void StringSetContentAfter()
        {
            var snippet = "string-set: heading content(after)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("heading content(after)", concrete.Value);
        }

        [Fact]
        public void StringSetContentFirstLetter()
        {
            var snippet = "string-set: initial content(first-letter)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("initial content(first-letter)", concrete.Value);
        }

        [Fact]
        public void StringSetMultiplePairs()
        {
            var snippet = "string-set: header content(text), footer counter(page)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("header content(), footer counter(page)", concrete.Value);
        }

        [Fact]
        public void StringSetInvalidNoContentListIllegal()
        {
            var snippet = "string-set: header";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void StringSetInvalidNumberIllegal()
        {
            var snippet = "string-set: 123 content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }
    }
}
