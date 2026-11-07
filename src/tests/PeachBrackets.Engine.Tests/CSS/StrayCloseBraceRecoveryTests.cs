using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS;

/// <summary>
/// A top-level qualified rule's prelude runs to the next <c>{</c>, a stray <c>}</c> included (CSS Syntax 3 section 5.5.3), so a rule written with its
/// <c>{</c> missing is one invalid rule and the rules after it still apply. Read the other way, the block of the next rule became the declarations of the
/// invalid one, and every rule after that a nested rule of it, to the end of the sheet: slashdot.org's style sheet lost about 70% of its rules that way.
/// </summary>
public class StrayCloseBraceRecoveryTests
{
    // slashdot.org's classic.ssl.css: the "{" after the selector list is missing.
    private const string Broken = "#editor .spinner span.tag-server-busy.spinner,\ndisplay:none !important;\n}\n";

    private static async Task<Stylesheet> ParseAsync(string css) =>
        await new StylesheetParser().ParseAsync(css, TestContext.Current.CancellationToken);

    private static List<string> SelectorsOf(Stylesheet sheet) => sheet.StyleRules.Select(r => r.SelectorText).ToList();

    [Fact]
    public async Task RulesAfterAMalformedRule_StillApply()
    {
        var sheet = await ParseAsync("#a { color: red }\n" + Broken + "#b { width: 98.5% }\n#c { color: blue }\n#d { color: green }");

        var selectors = SelectorsOf(sheet);
        Assert.Contains("#a", selectors);
        Assert.Contains("#c", selectors);
        Assert.Contains("#d", selectors);
    }

    [Fact]
    public async Task TheRuleWhoseBlockFollowsTheStrayBrace_IsInvalidWithTheMalformedOne()
    {
        // The prelude "#editor ..., display:none !important; } #b" is not a selector: that one rule is dropped, block and all.
        var sheet = await ParseAsync(Broken + "#b { width: 98.5% }\n#c { color: blue }");

        var selectors = SelectorsOf(sheet);
        Assert.DoesNotContain("#b", selectors);
        Assert.Equal(["#c"], selectors);
    }

    [Fact]
    public async Task AStrayBraceInsideAMediaBlock_StillClosesTheBlock()
    {
        // Inside a block the closing brace ends the block, as before: #b is a top-level rule after it, not part of its prelude.
        var sheet = await ParseAsync("@media screen { #a { color: red } }\n#b { color: blue }");

        Assert.Equal(["#b"], SelectorsOf(sheet));
        Assert.Single(sheet.Rules.OfType<MediaRule>());
    }
}
