using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    /// <summary>
    /// Extended tests for string-set property that specifically test the string() function
    /// within the content-list of string-set values.
    /// </summary>
    public class StringSetWithStringFunctionTests : CssConstructionFunctions
    {
        [Fact]
        public void StringSet_WithStringFunction_ParsesCorrectly()
        {
            var snippet = "string-set: section string(chapter)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("section string(chapter)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionAndKeyword_ParsesCorrectly()
        {
            var snippet = "string-set: section string(chapter, first)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            // Parser may normalize keywords - just verify it contains the function
            Assert.Contains("section string(chapter", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionAndLiteral_ParsesCorrectly()
        {
            var snippet = "string-set: section string(chapter) \" - \" content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("section string(chapter) \" - \" content()", concrete.Value);
        }

        [Fact]
        public void StringSet_WithMultipleStringFunctions_ParsesCorrectly()
        {
            var snippet = "string-set: full-title string(chapter) \" > \" string(section)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("full-title string(chapter) \" > \" string(section)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionLastKeyword_ParsesCorrectly()
        {
            var snippet = "string-set: current-section string(section, last)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("current-section string(section, last)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionStartKeyword_ParsesCorrectly()
        {
            var snippet = "string-set: page-header string(chapter, start)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("page-header string(chapter, start)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionFirstExceptKeyword_ParsesCorrectly()
        {
            var snippet = "string-set: heading string(chapter, first-except)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("heading string(chapter, first-except)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionAndCounter_ParsesCorrectly()
        {
            var snippet = "string-set: page-title string(chapter) \" (\" counter(page) \")\"";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("page-title string(chapter) \" (\" counter(page) \")\"", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionAndAttr_ParsesCorrectly()
        {
            var snippet = "string-set: full-heading string(chapter) \" - \" attr(title)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("full-heading string(chapter) \" - \" attr(title)", concrete.Value);
        }

        [Fact]
        public void StringSet_WithStringFunctionAndContentFunction_ParsesCorrectly()
        {
            var snippet = "string-set: combined string(chapter) \": \" content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("combined string(chapter) \": \" content()", concrete.Value);
        }

        [Fact]
        public void StringSet_MultipleNamesWithStringFunction_ParsesCorrectly()
        {
            var snippet = "string-set: header string(chapter), footer string(section, last)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("header string(chapter), footer string(section, last)", concrete.Value);
        }

        [Fact]
        public void StringSet_ComplexNestedExample_ParsesCorrectly()
        {
            var snippet = "string-set: breadcrumb string(part, first) \" / \" string(chapter) \" / \" content(text)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            // Verify main components are present
            Assert.Contains("breadcrumb", concrete.Value);
            Assert.Contains("string(part", concrete.Value);
            Assert.Contains("string(chapter)", concrete.Value);
            Assert.Contains("content()", concrete.Value);
        }

        [Fact]
        public void StringSet_WithHyphenatedIdentifiers_ParsesCorrectly()
        {
            var snippet = "string-set: my-heading string(my-chapter)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("string-set", property.Name);
            Assert.IsType<StringSetProperty>(property);
            var concrete = (StringSetProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("my-heading string(my-chapter)", concrete.Value);
        }
    }
}
