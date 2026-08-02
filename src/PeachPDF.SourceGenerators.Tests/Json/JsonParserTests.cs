using System.Collections.Generic;
using PeachPDF.SourceGenerators.Json;

namespace PeachPDF.SourceGenerators.Tests.Json
{
    public class JsonParserTests
    {
        [Fact]
        public void Parses_Object_With_Members()
        {
            var ok = JsonParser.TryParse("""{"a": 1, "b": "two", "c": true, "d": false, "e": null}""", out var root, out var errors);

            Assert.True(ok);
            Assert.Empty(errors);
            Assert.True(root!.IsObject);
            Assert.True(root.TryGetProperty("a", out var a));
            Assert.Equal(1.0, a.NumberValue);
            Assert.True(root.TryGetProperty("b", out var b));
            Assert.Equal("two", b.StringValue);
            Assert.True(root.TryGetProperty("c", out var c));
            Assert.True(c.BoolValue);
            Assert.True(root.TryGetProperty("d", out var d));
            Assert.False(d.BoolValue);
            Assert.True(root.TryGetProperty("e", out var e));
            Assert.True(e.IsNull);
        }

        [Fact]
        public void Parses_Nested_Arrays_And_Objects()
        {
            var ok = JsonParser.TryParse("""{"items": [1, 2, {"nested": [true, null]}]}""", out var root, out var errors);

            Assert.True(ok);
            Assert.Empty(errors);
            Assert.True(root!.TryGetProperty("items", out var items));
            Assert.True(items.IsArray);
            Assert.Equal(3, items.ArrayItems.Count);
            Assert.True(items.ArrayItems[2].IsObject);
            Assert.True(items.ArrayItems[2].TryGetProperty("nested", out var nested));
            Assert.Equal(2, nested.ArrayItems.Count);
        }

        [Theory]
        [InlineData("\"hello\\nworld\"", "hello\nworld")]
        [InlineData("\"tab\\there\"", "tab\there")]
        [InlineData("\"quote\\\"end\"", "quote\"end")]
        [InlineData("\"back\\\\slash\"", "back\\slash")]
        [InlineData("\"\\u0041\\u0042\"", "AB")]
        public void Parses_String_Escapes(string literal, string expected)
        {
            var ok = JsonParser.TryParse(literal, out var root, out var errors);

            Assert.True(ok);
            Assert.Empty(errors);
            Assert.Equal(expected, root!.StringValue);
        }

        [Theory]
        [InlineData("42", 42.0)]
        [InlineData("-42", -42.0)]
        [InlineData("3.14", 3.14)]
        [InlineData("1e3", 1000.0)]
        [InlineData("-1.5e-2", -0.015)]
        [InlineData("0", 0.0)]
        public void Parses_Numbers(string literal, double expected)
        {
            var ok = JsonParser.TryParse(literal, out var root, out var errors);

            Assert.True(ok);
            Assert.Empty(errors);
            Assert.Equal(expected, root!.NumberValue, precision: 10);
        }

        [Fact]
        public void Rejects_Trailing_Comma_In_Object()
        {
            var ok = JsonParser.TryParse("""{"a": 1,}""", out _, out var errors);

            Assert.False(ok);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Rejects_Trailing_Comma_In_Array()
        {
            var ok = JsonParser.TryParse("[1, 2,]", out _, out var errors);

            Assert.False(ok);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Rejects_Comments()
        {
            var ok = JsonParser.TryParse("""
                {
                    // a comment
                    "a": 1
                }
                """, out _, out var errors);

            Assert.False(ok);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Rejects_Unterminated_String()
        {
            var ok = JsonParser.TryParse("\"unterminated", out _, out var errors);

            Assert.False(ok);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Rejects_Trailing_Content_After_Root_Value()
        {
            var ok = JsonParser.TryParse("{} extra", out _, out var errors);

            Assert.False(ok);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Reports_Line_And_Column_For_A_Malformed_Document()
        {
            var ok = JsonParser.TryParse("{\n  \"a\": ,\n}", out _, out var errors);

            Assert.False(ok);
            var error = Assert.Single(errors);
            Assert.Equal(2, error.Line);
        }

        [Fact]
        public void Empty_Object_And_Array_Parse()
        {
            Assert.True(JsonParser.TryParse("{}", out var obj, out var e1));
            Assert.Empty(e1);
            Assert.Empty(obj!.ObjectMembers);

            Assert.True(JsonParser.TryParse("[]", out var arr, out var e2));
            Assert.Empty(e2);
            Assert.Empty(arr!.ArrayItems);
        }
    }
}
