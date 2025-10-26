namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    public class DebugTests
    {
        [Fact]
        public void Debug_stuff_here()
        {
            var sheet = new StylesheetParser().Parse("foo > bar {color: red; }");
            var _ = sheet.ToCss();
            Console.WriteLine(sheet.StyleRules.First().SelectorText);
        }
    }
}






