using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary>CSS Fonts 4 §2.1: <c>&lt;family-name&gt; = &lt;string&gt; | &lt;custom-ident&gt;+</c>.</summary>
    public class FontFamilyListTests
    {
        [Theory]
        [InlineData("Lato", "Lato")]
        [InlineData("\"Lato\"", "Lato")]
        [InlineData("'Lato'", "Lato")]
        [InlineData("  \"Lato\"  ", "Lato")]
        [InlineData("Open   Sans", "Open Sans")]
        [InlineData("  Open \t Sans  ", "Open Sans")]
        [InlineData("\"Open   Sans\"", "Open   Sans")]
        [InlineData("\"La\\74o\"", "Lato")]
        [InlineData("La\\74 o", "Lato")]
        [InlineData("\"Say \\\"Hi\\\"\"", "Say \"Hi\"")]
        [InlineData("'It\\'s'", "It's")]
        [InlineData("\"Back\\\\slash\"", "Back\\slash")]
        [InlineData("\"A\\\nB\"", "AB")]
        [InlineData("\"Bad\\0 Name\"", "Bad\uFFFDName")]
        [InlineData("\"Emoji\\1F600 \"", "Emoji\U0001F600")]
        [InlineData("\"Unterminated", "Unterminated")]
        public void SingleFamily_IsDecoded(string value, string expected)
        {
            var entries = FontFamilyList.Parse(value);

            var entry = Assert.Single(entries);
            Assert.Equal(expected, entry.Name);
        }

        [Fact]
        public void CommaInsideAQuotedName_DoesNotSplitTheList()
        {
            var entries = FontFamilyList.Parse("\"Foo, Bar\", 'Baz', Qux  Quux");

            Assert.Equal(["Foo, Bar", "Baz", "Qux Quux"], entries.Select(e => e.Name));
            Assert.Equal([true, true, false], entries.Select(e => e.IsQuoted));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(",")]
        [InlineData(" , ,, ")]
        [InlineData("\"\"")]
        public void EmptyOrBlank_HasNoFamilies(string value) => Assert.Empty(FontFamilyList.Parse(value));

        [Fact]
        public void Null_HasNoFamilies() => Assert.Empty(FontFamilyList.Parse(null));

        [Fact]
        public void StrayCommas_AreSkipped()
        {
            Assert.Equal(["A", "B"], FontFamilyList.Parse(",A,,B,").Select(e => e.Name));
        }

        [Theory]
        [InlineData("serif", true, false)]
        [InlineData("SANS-SERIF", true, false)]
        [InlineData("ui-rounded", true, false)]
        [InlineData("\"serif\"", false, true)]
        [InlineData("'monospace'", false, true)]
        [InlineData("Lato", false, false)]
        [InlineData("\"Lato\"", false, false)]
        public void QuotedGenericSpelling_IsNotTheGenericKeyword(string value, bool isGeneric, bool isQuotedGenericSpelling)
        {
            var entry = Assert.Single(FontFamilyList.Parse(value));

            Assert.Equal(isGeneric, entry.IsGeneric);
            Assert.Equal(isQuotedGenericSpelling, entry.IsQuotedGenericSpelling);
        }

        [Fact]
        public void StringMixedWithIdent_KeepsTheFamily()
        {
            var entry = Assert.Single(FontFamilyList.Parse("\"A\" B"));

            Assert.Equal("A B", entry.Name);
        }

        [Fact]
        public void IdentFollowedByString_IsNotQuoted()
        {
            var entry = Assert.Single(FontFamilyList.Parse("A \"B\""));

            Assert.Equal("A B", entry.Name);
            Assert.False(entry.IsQuoted);
        }
    }
}
