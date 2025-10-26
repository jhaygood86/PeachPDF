namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssColumnsPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssColumnWidthLengthLegal()
        {
            var snippet = "column-width: 300px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnWidthProperty>(property);
            var concrete = (ColumnWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("300px", concrete.Value);
        }

        [Fact]
        public void CssColumnWidthPercentIllegal()
        {
            var snippet = "column-width: 30%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnWidthProperty>(property);
            var concrete = (ColumnWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssColumnWidthVwLegal()
        {
            var snippet = "column-width: 0.3vw";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnWidthProperty>(property);
            var concrete = (ColumnWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3vw", concrete.Value);
        }

        [Fact]
        public void CssColumnWidthAutoUppercaseLegal()
        {
            var snippet = "column-width: AUTO";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnWidthProperty>(property);
            var concrete = (ColumnWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void CssColumnCountAutoLowercaseLegal()
        {
            var snippet = "column-count: auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-count", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnCountProperty>(property);
            var concrete = (ColumnCountProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void CssColumnCountNumberLegal()
        {
            var snippet = "column-count: 3";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-count", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnCountProperty>(property);
            var concrete = (ColumnCountProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3", concrete.Value);
        }

        [Fact]
        public void CssColumnCountZeroLegal()
        {
            var snippet = "column-count: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-count", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnCountProperty>(property);
            var concrete = (ColumnCountProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssColumsZeroLegal()
        {
            var snippet = "columns: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssColumsLengthLegal()
        {
            var snippet = "columns: 10px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px", concrete.Value);
        }

        [Fact]
        public void CssColumsNumberLegal()
        {
            var snippet = "columns: 4";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("4", concrete.Value);
        }

        [Fact]
        public void CssColumsLengthNumberLegal()
        {
            var snippet = "columns: 25em 5";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25em 5", concrete.Value);
        }

        [Fact]
        public void CssColumsNumberLengthLegal()
        {
            var snippet = "columns : 5   25em  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25em 5", concrete.Value);
        }

        [Fact]
        public void CssColumsAutoAutoLegal()
        {
            var snippet = "columns : auto auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto auto", concrete.Value);
        }

        [Fact]
        public void CssColumsAutoLegal()
        {
            var snippet = "columns : auto  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void CssColumsNumberPercenIllegal()
        {
            var snippet = "columns : 5   25%  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("columns", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnsProperty>(property);
            var concrete = (ColumnsProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssColumSpanAllLegal()
        {
            var snippet = "column-span: all";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-span", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnSpanProperty>(property);
            var concrete = (ColumnSpanProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("all", concrete.Value);
        }

        [Fact]
        public void CssColumSpanNoneUppercaseLegal()
        {
            var snippet = "column-span: None";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-span", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnSpanProperty>(property);
            var concrete = (ColumnSpanProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssColumSpanLengthIllegal()
        {
            var snippet = "column-span: 10px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-span", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnSpanProperty>(property);
            var concrete = (ColumnSpanProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Theory]
        [MemberData(nameof(LengthOrPercentOrGlobalTestValues))]
        public void ColumnGapLegalValues(string value)
            => TestForLegalValue<ColumnGapProperty>(PropertyNames.ColumnGap, value);

        [Fact]
        public void CssColumFillBalanceLegal()
        {
            var snippet = "column-fill: balance;";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-fill", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnFillProperty>(property);
            var concrete = (ColumnFillProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("balance", concrete.Value);
        }

        [Fact]
        public void CssColumFillAutoLegal()
        {
            var snippet = "column-fill: auto;";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-fill", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnFillProperty>(property);
            var concrete = (ColumnFillProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void CssColumRuleColorTransparentLegal()
        {
            var snippet = "column-rule-color: transparent";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleColorProperty>(property);
            var concrete = (ColumnRuleColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgba(0, 0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void CssColumRuleColorRgbLegal()
        {
            var snippet = "column-rule-color: rgb(192, 56, 78)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleColorProperty>(property);
            var concrete = (ColumnRuleColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(192, 56, 78)", concrete.Value);
        }

        [Fact]
        public void CssColumRuleColorRedLegal()
        {
            var snippet = "column-rule-color: red";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleColorProperty>(property);
            var concrete = (ColumnRuleColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void CssColumRuleColorNoneIllegal()
        {
            var snippet = "column-rule-color: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleColorProperty>(property);
            var concrete = (ColumnRuleColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssColumRuleStyleInsetTailUpperLegal()
        {
            var snippet = "column-rule-style: inSET";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleStyleProperty>(property);
            var concrete = (ColumnRuleStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("inset", concrete.Value);
        }

        [Fact]
        public void CssColumRuleStyleNoneLegal()
        {
            var snippet = "column-rule-style: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleStyleProperty>(property);
            var concrete = (ColumnRuleStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssColumRuleStyleAutoIllegal()
        {
            var snippet = "column-rule-style: auto ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleStyleProperty>(property);
            var concrete = (ColumnRuleStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssColumRuleWidthLengthLegal()
        {
            var snippet = "column-rule-width: 2px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleWidthProperty>(property);
            var concrete = (ColumnRuleWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("2px", concrete.Value);
        }

        [Fact]
        public void CssColumRuleWidthThickLegal()
        {
            var snippet = "column-rule-width: thick";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleWidthProperty>(property);
            var concrete = (ColumnRuleWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("5px", concrete.Value);
        }

        [Fact]
        public void CssColumRuleWidthMediumLegal()
        {
            var snippet = "column-rule-width : medium !important ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-width", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<ColumnRuleWidthProperty>(property);
            var concrete = (ColumnRuleWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px", concrete.Value);
        }

        [Fact]
        public void CssColumRuleWidthThinUppercaseLegal()
        {
            var snippet = "column-rule-width: THIN";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleWidthProperty>(property);
            var concrete = (ColumnRuleWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px", concrete.Value);
        }

        [Fact]
        public void CssColumRuleDottedLegal()
        {
            var snippet = "column-rule: dotted";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleProperty>(property);
            var concrete = (ColumnRuleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("dotted", concrete.Value);
        }

        [Fact]
        public void CssColumRuleSolidBlueLegal()
        {
            var snippet = "column-rule: solid  blue";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleProperty>(property);
            var concrete = (ColumnRuleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(0, 0, 255) solid", concrete.Value);
        }

        [Fact]
        public void CssColumRuleSolidLengthLegal()
        {
            var snippet = "column-rule: solid 8px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleProperty>(property);
            var concrete = (ColumnRuleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("8px solid", concrete.Value);
        }

        [Fact]
        public void CssColumRuleThickInsetBlueLegal()
        {
            var snippet = "column-rule: thick inset blue";
            var property = ParseDeclaration(snippet);
            Assert.Equal("column-rule", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ColumnRuleProperty>(property);
            var concrete = (ColumnRuleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(0, 0, 255) 5px inset", concrete.Value);
        }
    }
}








