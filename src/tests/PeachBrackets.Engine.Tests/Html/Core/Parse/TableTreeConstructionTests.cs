using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// The table, caption, column group, table body, row, cell and select insertion modes of the HTML tree construction algorithm
    /// (<see href="https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-intable">HTML §13.2.6.4.9 "in table"</see> and the
    /// ones after it), and foster parenting: what a browser builds for markup that leaves out table parts or puts content where a table
    /// does not take it.
    /// </summary>
    public class TableTreeConstructionTests
    {
        private static string Body(string html) => AdoptionAgencyParsingTests.Body(html);

        [Theory]
        // Rows get the section and cells the row they leave out.
        [InlineData("<table><tr><td>a</td></tr></table>", "<table><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<table><td>a<td>b</table>", "<table><tbody><tr><td>a</td><td>b</td></tr></tbody></table>")]
        [InlineData("<table><th>a</table>", "<table><tbody><tr><th>a</th></tr></tbody></table>")]
        [InlineData("<table><col><tr><td>a</table>", "<table><colgroup><col></colgroup><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<table><caption>c<tr><td>a</table>", "<table><caption>c</caption><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<table><thead><tr><th>h<tbody><tr><td>d</table>",
            "<table><thead><tr><th>h</th></tr></thead><tbody><tr><td>d</td></tr></tbody></table>")]
        [InlineData("<table><tr><td>a<tr><td>b<tbody><tr><td>c</table>",
            "<table><tbody><tr><td>a</td></tr><tr><td>b</td></tr></tbody><tbody><tr><td>c</td></tr></tbody></table>")]
        [InlineData("<table><colgroup><col><col></colgroup><tr><td>a</table>",
            "<table><colgroup><col><col></colgroup><tbody><tr><td>a</td></tr></tbody></table>")]
        // Whitespace stays in the table.
        [InlineData("<table> <tr><td>y</td></tr></table>", "<table> <tbody><tr><td>y</td></tr></tbody></table>")]
        // A table in a cell is its own table; a table in a table ends it.
        [InlineData("<table><tr><td><table><tr><td>i</table></table>",
            "<table><tbody><tr><td><table><tbody><tr><td>i</td></tr></tbody></table></td></tr></tbody></table>")]
        [InlineData("<table><tr><td>a<table><tr><td>b</td></tr></table>c</td></tr></table>",
            "<table><tbody><tr><td>a<table><tbody><tr><td>b</td></tr></tbody></table>c</td></tr></tbody></table>")]
        [InlineData("<table><table><tr><td>a", "<table></table><table><tbody><tr><td>a</td></tr></tbody></table>")]
        public void TableParts_AreImpliedWhereTheSourceLeavesThemOut(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        // Content a table does not take goes in front of it.
        [InlineData("<table>x<tr><td>y</td></tr></table>", "x<table><tbody><tr><td>y</td></tr></tbody></table>")]
        [InlineData("<table><div>a</div><tr><td>b</td></tr></table>", "<div>a</div><table><tbody><tr><td>b</td></tr></tbody></table>")]
        [InlineData("<table><tr>x<td>y</td></tr></table>", "x<table><tbody><tr><td>y</td></tr></tbody></table>")]
        [InlineData("<table><b>x<tr><td>y</td></tr></table>", "<b>x</b><table><tbody><tr><td>y</td></tr></tbody></table>")]
        [InlineData("<table><colgroup>x</colgroup><tr><td>y</table>", "x<table><colgroup></colgroup><tbody><tr><td>y</td></tr></tbody></table>")]
        // Formatting reopens in fostered content and is bounded by the cell.
        [InlineData("<b><table><tr><td>x</td></tr></table>y", "<b><table><tbody><tr><td>x</td></tr></tbody></table>y</b>")]
        [InlineData("<table><tr><td><b>x</td></tr></table>y", "<table><tbody><tr><td><b>x</b></td></tr></tbody></table>y")]
        public void ContentInATable_IsFosterParented(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        // Table parts mean nothing outside a table.
        [InlineData("<td>x</td>", "x")]
        [InlineData("<tr><td>x</td></tr>y", "xy")]
        [InlineData("<caption>c</caption>d", "cd")]
        [InlineData("<div><tbody>a</tbody></div>", "<div>a</div>")]
        // Stray end tags inside a table are dropped.
        [InlineData("<table></td></tr></tbody><tr><td>a</table>", "<table><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<table><tr><td>a</tr>b</td></table>", "b<table><tbody><tr><td>a</td></tr></tbody></table>")]
        public void TablePartsOutsideATable_AreIgnored(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        // A form takes no content in a table, and only the first one counts.
        [InlineData("<table><form><tr><td>a</table>", "<table><form></form><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<form><form>a</form></form>", "<form>a</form>")]
        [InlineData("<table><input type=hidden><tr><td>a</table>", "<table><input><tbody><tr><td>a</td></tr></tbody></table>")]
        [InlineData("<table><input><tr><td>a</table>", "<input><table><tbody><tr><td>a</td></tr></tbody></table>")]
        // </form> takes the form off the stack without closing what is open inside it.
        [InlineData("<form><div>a</form>b</div>", "<form><div>ab</div></form>")]
        public void FormsInTables_FollowTheFormElementPointer(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        [InlineData("<select><option>a<option>b</select>", "<select><option>a</option><option>b</option></select>")]
        [InlineData("<select><optgroup><option>a<optgroup><option>b</select>",
            "<select><optgroup><option>a</option></optgroup><optgroup><option>b</option></optgroup></select>")]
        [InlineData("<select><option>a<select>", "<select><option>a</option></select>")]
        [InlineData("<select><option>a</option>x</select>", "<select><option>a</option>x</select>")]
        [InlineData("<table><tr><td><select><option>a<td>b</table>",
            "<table><tbody><tr><td><select><option>a</option></select></td><td>b</td></tr></tbody></table>")]
        [InlineData("<select><option>a</select>b", "<select><option>a</option></select>b")]
        public void SelectOptions_FollowTheSelectModes(string html, string expected) => Assert.Equal(expected, Body(html));

        [Fact]
        public void ACaptionClosesWithItsEndTag_AndTheTableGoesOn()
        {
            Assert.Equal("<table><caption><b>c</b></caption><tbody><tr><td>a</td></tr></tbody></table>",
                Body("<table><caption><b>c</caption><tr><td>a</table>"));
        }

        [Fact]
        public void AnEndTagForAnElementOutsideTheCell_DoesNotReachPastTheTable()
        {
            Assert.Equal("<div><table><tbody><tr><td>x</td></tr></tbody></table></div>", Body("<div><table><tr><td></div>x</td></tr></table></div>"));
        }
    }
}
