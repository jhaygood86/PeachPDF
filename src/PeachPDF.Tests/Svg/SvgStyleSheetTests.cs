using PeachPDF.Svg;

namespace PeachPDF.Tests.Svg
{
    public class SvgStyleSheetTests
    {
        [Fact]
        public void Parse_MissingValue_ReturnsEmptySheet()
        {
            Assert.Empty(SvgStyleSheet.Parse(null).Rules);
            Assert.Empty(SvgStyleSheet.Parse("").Rules);
        }

        [Fact]
        public void Match_TypeSelector_MatchesByTagName()
        {
            var sheet = SvgStyleSheet.Parse("rect { fill: red; }");
            var matched = sheet.Match("rect", null, []);
            Assert.Equal("red", matched["fill"]);
            Assert.Empty(sheet.Match("circle", null, []));
        }

        [Fact]
        public void Match_ClassSelector_MatchesByClass()
        {
            var sheet = SvgStyleSheet.Parse(".highlight { fill: yellow; }");
            Assert.Equal("yellow", sheet.Match("rect", null, ["highlight"])["fill"]);
            Assert.Empty(sheet.Match("rect", null, ["other"]));
        }

        [Fact]
        public void Match_IdSelector_MatchesById()
        {
            var sheet = SvgStyleSheet.Parse("#hero { fill: blue; }");
            Assert.Equal("blue", sheet.Match("rect", "hero", [])["fill"]);
            Assert.Empty(sheet.Match("rect", "other", []));
        }

        [Fact]
        public void Match_CompoundSelector_RequiresAllComponents()
        {
            var sheet = SvgStyleSheet.Parse("rect.foo#bar { fill: green; }");
            Assert.Equal("green", sheet.Match("rect", "bar", ["foo"])["fill"]);
            Assert.Empty(sheet.Match("rect", "bar", [])); // missing class
            Assert.Empty(sheet.Match("circle", "bar", ["foo"])); // wrong type
        }

        [Fact]
        public void Match_MultipleClassesRequired_AllMustBePresent()
        {
            var sheet = SvgStyleSheet.Parse(".a.b { fill: purple; }");
            Assert.Equal("purple", sheet.Match("rect", null, ["a", "b", "c"])["fill"]);
            Assert.Empty(sheet.Match("rect", null, ["a"]));
        }

        [Fact]
        public void Match_CommaSeparatedSelectorList_EachMatchesIndependently()
        {
            var sheet = SvgStyleSheet.Parse("rect, .foo { fill: orange; }");
            Assert.Equal("orange", sheet.Match("rect", null, [])["fill"]);
            Assert.Equal("orange", sheet.Match("circle", null, ["foo"])["fill"]);
        }

        [Fact]
        public void Match_UniversalSelector_MatchesAnyType()
        {
            var sheet = SvgStyleSheet.Parse("* { opacity: 0.5; }");
            Assert.Equal("0.5", sheet.Match("rect", null, [])["opacity"]);
            Assert.Equal("0.5", sheet.Match("anything", "id", ["cls"])["opacity"]);
        }

        [Fact]
        public void Match_IdSelectorBeatsClassSelector_BySpecificity()
        {
            var sheet = SvgStyleSheet.Parse("""
                .foo { fill: red; }
                #bar { fill: blue; }
                """);
            // #bar has higher specificity than .foo, regardless of source order.
            Assert.Equal("blue", sheet.Match("rect", "bar", ["foo"])["fill"]);
        }

        [Fact]
        public void Match_EqualSpecificity_LastRuleInSourceOrderWins()
        {
            var sheet = SvgStyleSheet.Parse("""
                .foo { fill: red; }
                .foo { fill: blue; }
                """);
            Assert.Equal("blue", sheet.Match("rect", null, ["foo"])["fill"]);
        }

        [Fact]
        public void Match_MultipleRules_DeclarationsFromNonConflictingPropertiesAllApply()
        {
            var sheet = SvgStyleSheet.Parse("""
                rect { fill: red; }
                .foo { stroke: blue; }
                """);
            var matched = sheet.Match("rect", null, ["foo"]);
            Assert.Equal("red", matched["fill"]);
            Assert.Equal("blue", matched["stroke"]);
        }

        [Fact]
        public void Parse_DescendantCombinator_SelectorIsSkipped()
        {
            // Descendant combinators are explicitly unsupported (v1 scope) - the malformed selector
            // is dropped rather than mis-parsed as something it isn't.
            var sheet = SvgStyleSheet.Parse("g rect { fill: red; }");
            Assert.Empty(sheet.Rules);
        }

        [Fact]
        public void Parse_CommentsAreStripped()
        {
            var sheet = SvgStyleSheet.Parse("/* comment */ rect { fill: red; } /* trailing */");
            Assert.Equal("red", sheet.Match("rect", null, [])["fill"]);
        }
    }
}
