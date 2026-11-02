using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class AccessibilityTreeTests
{
    private static async Task<(HtmlDocument Document, AccessibilityTree Tree)> LoadAsync(string body, string head = "", HtmlDocumentOptions? options = null)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), options ?? new HtmlDocumentOptions { Media = "screen" });
        return (document, document.GetAccessibilityTree());
    }

    private static async Task<AccessibilityTree> TreeAsync(string body, string head = "") => (await LoadAsync(body, head)).Tree;

    private static AccessibilityNode Only(AccessibilityTree tree, AccessibilityRole role) => Assert.Single(tree.Nodes, n => n.Role == role);

    private static AccessibilityNode Named(AccessibilityTree tree, string name) => Assert.Single(tree.Nodes, n => n.Name == name && n.Role != AccessibilityRole.Text);

    [Fact]
    public async Task HeadingsParagraphsAndLinksFormATreeThatFollowsTheDocument()
    {
        var tree = await TreeAsync("<h1>Title</h1><p>Some <a href='https://example.com/'>link</a> text</p>");

        Assert.Equal(AccessibilityRole.Document, tree.Root.Role);
        var heading = Only(tree, AccessibilityRole.Heading);
        Assert.Equal("Title", heading.Name);
        Assert.Equal(1, heading.Level);

        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.Equal("Some link text", paragraph.Text);
        Assert.Equal([AccessibilityRole.Text, AccessibilityRole.Link, AccessibilityRole.Text], paragraph.Children.Select(c => c.Role).ToArray());

        var link = Only(tree, AccessibilityRole.Link);
        Assert.Equal("link", link.Name);
        Assert.Equal("https://example.com/", link.Uri);
        Assert.Same(paragraph, link.Parent);
        Assert.True(link.IsFocusable);
    }

    [Fact]
    public async Task ElementsThatMeanNothingToAReaderAreLeftOutButTheirContentIsNot()
    {
        var tree = await TreeAsync("<div><span><p>inside</p></span></div>");

        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.Same(tree.Root, paragraph.Parent);
        Assert.DoesNotContain(tree.Nodes, n => n.Role == AccessibilityRole.Generic);
    }

    [Theory]
    [InlineData("<header>H</header>", AccessibilityRole.Banner)]
    [InlineData("<footer>F</footer>", AccessibilityRole.ContentInfo)]
    [InlineData("<nav>N</nav>", AccessibilityRole.Navigation)]
    [InlineData("<main>M</main>", AccessibilityRole.Main)]
    [InlineData("<aside>A</aside>", AccessibilityRole.Complementary)]
    [InlineData("<section aria-label='Named'>S</section>", AccessibilityRole.Region)]
    [InlineData("<form aria-label='Named'>F</form>", AccessibilityRole.Form)]
    [InlineData("<article>A</article>", AccessibilityRole.Article)]
    public async Task LandmarksHaveTheirRoles(string html, AccessibilityRole role)
    {
        var tree = await TreeAsync(html);

        Assert.Contains(tree.Nodes, n => n.Role == role);
    }

    [Fact]
    public async Task ASectionOrFormWithNoNameIsNotALandmark()
    {
        var tree = await TreeAsync("<section>S</section><form>F</form>");

        Assert.DoesNotContain(tree.Nodes, n => n.Role is AccessibilityRole.Region or AccessibilityRole.Form);
    }

    [Fact]
    public async Task AHeaderOrFooterInsideASectioningElementIsNotAPageLandmark()
    {
        var tree = await TreeAsync("<article><header>H</header><footer>F</footer></article>");

        Assert.DoesNotContain(tree.Nodes, n => n.Role is AccessibilityRole.Banner or AccessibilityRole.ContentInfo);
    }

    [Fact]
    public async Task AnExplicitRoleReplacesTheNativeOne()
    {
        var tree = await TreeAsync("<div role='navigation'>x</div><h2 role='button'>Go</h2><a href='#t' role='bogus button'>Go2</a>");

        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Navigation);
        Assert.Equal("Go", Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Button && n.Name == "Go").Name);
        // The first token that is a role wins; "bogus" is skipped.
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Button && n.Name == "Go2");
    }

    [Fact]
    public async Task APresentationalRoleTakesTheElementOutOfTheTreeButNotItsContent()
    {
        var tree = await TreeAsync("<h1 role='presentation'>Plain</h1><ul role='none'><li>one</li></ul>");

        Assert.DoesNotContain(tree.Nodes, n => n.Role == AccessibilityRole.Heading);
        Assert.DoesNotContain(tree.Nodes, n => n.Role == AccessibilityRole.List);
        Assert.Contains(tree.Nodes, n => n.Role == AccessibilityRole.Text && n.Name == "Plain");
    }

    [Fact]
    public async Task APresentationalRoleIsIgnoredOnSomethingThatCanBeFocused()
    {
        var tree = await TreeAsync("<button role='presentation'>Keep</button>");

        Assert.Equal("Keep", Only(tree, AccessibilityRole.Button).Name);
    }

    [Fact]
    public async Task ListsTablesAndFormControlsHaveTheirRoles()
    {
        var tree = await TreeAsync(
            "<ul><li>a</li><li>b</li></ul><table><caption>Cap</caption><thead><tr><th>H</th></tr></thead><tbody><tr><td>c</td></tr></tbody></table>"
            + "<input type=checkbox checked><input type=radio><input type=range><input type=number><input type=search><input type=text><textarea></textarea>"
            + "<select><option>x</option></select><select multiple><option>y</option></select><progress></progress><meter></meter><hr>");

        Assert.Equal(2, tree.Nodes.Count(n => n.Role == AccessibilityRole.ListItem));
        Assert.Equal("Cap", Only(tree, AccessibilityRole.Table).Name);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ColumnHeader);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Cell);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.CheckBox);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Radio);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Slider);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.SpinButton);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.SearchBox);
        Assert.Equal(2, tree.Nodes.Count(n => n.Role == AccessibilityRole.TextBox));
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ComboBox);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ListBox);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ProgressBar);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Meter);
        Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Separator);
    }

    // ---- names ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANameComesFromLabelledByThenAriaLabelThenTheContent()
    {
        var tree = await TreeAsync(
            "<span id=a>Alpha</span> <span id=b>Beta</span>"
            + "<button aria-labelledby='a b' aria-label='ignored'>content</button>"
            + "<button aria-label='Label' title='tip'>content</button>"
            + "<button>content only</button>");

        var buttons = tree.Nodes.Where(n => n.Role == AccessibilityRole.Button).ToArray();
        Assert.Equal(["Alpha Beta", "Label", "content only"], buttons.Select(b => b.Name).ToArray());
        Assert.Equal("tip", buttons[1].Description);
    }

    [Fact]
    public async Task AControlIsNamedByItsLabel_WhetherForOrWrapping()
    {
        var tree = await TreeAsync(
            "<label for=first>First name</label><input id=first type=text>"
            + "<label>Second <input type=text id=second></label>"
            + "<input type=text placeholder='Only a placeholder'>");

        Assert.Equal(["First name", "Second", "Only a placeholder"], tree.Nodes.Where(n => n.Role == AccessibilityRole.TextBox).Select(n => n.Name).ToArray());
    }

    [Fact]
    public async Task ImagesFieldsetsFiguresAndButtonsAreNamedTheWayHtmlSays()
    {
        var tree = await TreeAsync(
            "<img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt='A picture'>"
            + "<fieldset><legend>Legend text</legend>x</fieldset>"
            + "<figure>f<figcaption>Figure caption</figcaption></figure>"
            + "<input type=submit><input type=reset value='Clear'>");

        Assert.Equal("A picture", Only(tree, AccessibilityRole.Image).Name);
        Assert.Equal("Legend text", Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Group).Name);
        Assert.Equal("Figure caption", Only(tree, AccessibilityRole.Figure).Name);
        Assert.Equal(["Submit", "Clear"], tree.Nodes.Where(n => n.Role == AccessibilityRole.Button).Select(n => n.Name).ToArray());
    }

    [Fact]
    public async Task AnImageWithAnEmptyAltIsDecorationUnlessItIsNamedSomeOtherWay()
    {
        var tree = await TreeAsync(
            "<img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt=''><img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt='' aria-label='Named'>");

        Assert.Equal("Named", Only(tree, AccessibilityRole.Image).Name);
    }

    [Fact]
    public async Task TheTitleNamesAnElementOnlyWhenNothingElseDoes()
    {
        var tree = await TreeAsync("<a href='#x' title='Go to x'><img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt=''></a>");

        var link = Only(tree, AccessibilityRole.Link);
        Assert.Equal("Go to x", link.Name);
        Assert.Equal(string.Empty, link.Description);
    }

    [Fact]
    public async Task ANameFromContentIncludesWhatIsInsideItWithoutRunningWordsTogether()
    {
        var tree = await TreeAsync("<h2>one<b>two</b> three <img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt='four'></h2>");

        Assert.Equal("onetwo three four", Only(tree, AccessibilityRole.Heading).Name);
    }

    [Fact]
    public async Task OtherNativeLabelsAreRead()
    {
        var tree = await TreeAsync(
            "<select><optgroup label='Group label'><option label='Option label'>x</option><option selected>Plain option</option></optgroup></select>"
            + "<input type=image alt='Image button' src='data:image/gif;base64,R0lGODlhAQABAAAAACw='>"
            + "<math alttext='x squared'><msup><mi>x</mi><mn>2</mn></msup></math>"
            + "<map name=m><area href='#a' alt='Area alt' shape=rect coords='0,0,5,5'></map>"
            + "<p aria-description='Extra' accesskey='k' aria-keyshortcuts='Alt+K'>Para</p>"
            + "<div contenteditable role=textbox aria-label='Edit'>x</div>");

        Assert.Equal("Group label", Only(tree, AccessibilityRole.Group).Name);
        var options = tree.Nodes.Where(n => n.Role == AccessibilityRole.Option).ToArray();
        Assert.Equal(["Option label", "Plain option"], options.Select(o => o.Name).ToArray());
        Assert.False(options[0].Selected);
        Assert.True(options[1].Selected);
        Assert.Contains(tree.Nodes, n => n.Role == AccessibilityRole.Button && n.Name == "Image button");
        Assert.Equal("x squared", Only(tree, AccessibilityRole.Math).Name);

        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.Equal("Extra", paragraph.Description);
        Assert.Equal("k", paragraph.AccessKey);
        Assert.Equal("Alt+K", paragraph.KeyShortcuts);
        Assert.True(Named(tree, "Edit").IsFocusable);
    }

    [Fact]
    public async Task ARepeatingTableHeaderIsInTheTreeOnceWhateverNumberOfPagesItIsOn()
    {
        var rows = string.Concat(Enumerable.Range(0, 220).Select(i => $"<tr><td>row {i}</td></tr>"));
        var (document, tree) = await LoadAsync($"<table><thead><tr><th>Repeated head</th></tr></thead><tbody>{rows}</tbody></table>");

        Assert.True(document.Pages.Count > 1);
        var header = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ColumnHeader);
        Assert.Equal("Repeated head", header.Name);
        Assert.Equal(1, tree.Nodes.Count(n => n.Role == AccessibilityRole.RowGroup && n.TagName == "thead"));
        // The copies on later pages are not in the tree; the header is reported where it first is.
        Assert.NotEmpty(header.Bounds);
    }

    [Fact]
    public async Task AFootnoteStyleLeafAndEmptyNodesDoNotBreakTheTree()
    {
        var tree = await TreeAsync("<p></p><div></div><hr><img src='data:image/gif;base64,R0lGODlhAQABAAAAACw=' alt='dot'><ul><li></li></ul>");

        Assert.Contains(tree.Nodes, n => n.Role == AccessibilityRole.Separator);
        Assert.Equal("dot", Only(tree, AccessibilityRole.Image).Name);
        Assert.All(tree.Nodes, n => Assert.NotNull(n.Id));
    }

    // ---- what a host reads ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ABlockIsReadAsTheTextItHoldsDirectly_AndItsElementsAreSteppedToOnTheirOwn()
    {
        var tree = await TreeAsync("<p>Some <a href='#t'>link</a> text</p><h2>Heading</h2><button aria-label='Named'>x</button>");

        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.Equal("Some text", paragraph.ReadingText);
        Assert.Equal([AccessibilityRole.Link], paragraph.ReadingChildren.Select(c => c.Role).ToArray());
        Assert.Equal("Heading", Only(tree, AccessibilityRole.Heading).ReadingText);
        Assert.Equal("Named", Only(tree, AccessibilityRole.Button).ReadingText);
    }

    [Fact]
    public async Task AnElementThatHoldsOnlyTextIsReadAsAllOfIt()
    {
        var tree = await TreeAsync("<p>just <b>bold</b> words</p>");

        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.Equal("just bold words", paragraph.ReadingText);
        Assert.DoesNotContain(paragraph.ReadingChildren, c => c.Role != AccessibilityRole.Strong);
    }

    [Fact]
    public async Task TextThatBelongsToTheDocumentItselfIsSteppedTo()
    {
        var tree = await TreeAsync("loose text<p>para</p>");

        Assert.Equal([AccessibilityRole.Text, AccessibilityRole.Paragraph], tree.Root.ReadingChildren.Select(c => c.Role).ToArray());
        Assert.Equal("loose text", tree.Root.ReadingChildren.First().ReadingText);
    }

    // ---- what is left out ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HiddenContentIsNotInTheTree()
    {
        var tree = await TreeAsync(
            "<p>shown</p><p aria-hidden='true'>aria-hidden</p><p hidden>hidden attribute</p><p style='display:none'>display none</p>"
            + "<div inert><p>inert</p></div><p style='visibility:hidden'>invisible</p>");

        Assert.Equal(["shown"], tree.Nodes.Where(n => n.Role == AccessibilityRole.Text).Select(n => n.Name).ToArray());
    }

    [Fact]
    public async Task AChildCanBeVisibleWhenItsParentIsNot()
    {
        var tree = await TreeAsync("<div style='visibility:hidden'>hidden <p style='visibility:visible'>shown</p></div>");

        Assert.Equal("shown", Only(tree, AccessibilityRole.Paragraph).Text);
    }

    // ---- states --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task StatesComeFromAttributesAndFromTheElementsAround()
    {
        var tree = await TreeAsync(
            "<input type=checkbox id=c checked required readonly aria-invalid=true>"
            + "<input type=checkbox aria-checked=mixed>"
            + "<button aria-pressed=true>P</button>"
            + "<fieldset disabled><button>In disabled</button><input type=text></fieldset>"
            + "<details open><summary>Sum</summary>body</details><details><summary>Closed</summary>body</details>"
            + "<div role=tab aria-selected=true aria-expanded=false aria-current=page>Tab</div>"
            + "<div role=alert>Alert</div><div role=status aria-live=assertive>Status</div>");

        var boxes = tree.Nodes.Where(n => n.Role == AccessibilityRole.CheckBox).ToArray();
        Assert.Equal(ToggleState.True, boxes[0].Checked);
        Assert.True(boxes[0].Required);
        Assert.True(boxes[0].ReadOnly);
        Assert.True(boxes[0].Invalid);
        Assert.Equal(ToggleState.Mixed, boxes[1].Checked);

        Assert.Equal(ToggleState.True, Named(tree, "P").Pressed);
        Assert.True(Named(tree, "In disabled").Disabled);
        Assert.True(Only(tree, AccessibilityRole.TextBox).Disabled);

        Assert.True(Named(tree, "Sum").Expanded);
        Assert.False(Named(tree, "Closed").Expanded);

        var tab = Only(tree, AccessibilityRole.Tab);
        Assert.True(tab.Selected);
        Assert.False(tab.Expanded);
        Assert.Equal("page", tab.Current);

        Assert.Equal(LiveSetting.Assertive, Only(tree, AccessibilityRole.Alert).Live);
        Assert.Equal(LiveSetting.Assertive, Only(tree, AccessibilityRole.Status).Live);
    }

    [Fact]
    public async Task FocusabilityFollowsTheElementAndTabindex()
    {
        var tree = await TreeAsync("<a href='#'>a</a><button disabled>b</button><div role=group tabindex=0>c</div><div role=group tabindex=-1>d</div><p>e</p>");

        Assert.True(Named(tree, "a").IsFocusable);
        Assert.False(Named(tree, "b").IsFocusable);
        var groups = tree.Nodes.Where(n => n.Role == AccessibilityRole.Group).ToArray();
        Assert.Equal([0, -1], groups.Select(g => g.TabIndex!.Value).ToArray());
        Assert.All(groups, g => Assert.True(g.IsFocusable));
        Assert.False(Only(tree, AccessibilityRole.Paragraph).IsFocusable);
    }

    // ---- language and identity -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LanguageIsInheritedFromTheNearestLangAttribute()
    {
        var (_, tree) = await LoadAsync("<p>one</p><div lang='fr'><p>deux</p><p lang='de'>drei</p></div>", options: new HtmlDocumentOptions { DefaultLanguage = "en", Media = "screen" });

        var paragraphs = tree.Nodes.Where(n => n.Role == AccessibilityRole.Paragraph).ToArray();
        Assert.Equal(["en", "fr", "de"], paragraphs.Select(p => p.Language).ToArray());
    }

    [Fact]
    public async Task NodeIdsAreTheSameEachTimeTheDocumentIsLoaded()
    {
        const string body = "<h1>Title</h1><p>Some <a href='#t'>link</a> text</p><ul><li>one</li></ul>";
        var first = (await TreeAsync(body)).Nodes.Select(n => n.Id).ToArray();
        var second = (await TreeAsync(body)).Nodes.Select(n => n.Id).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(first.Length, first.Distinct().Count());
    }

    [Fact]
    public async Task ANodeCanBeFoundByItsId()
    {
        var tree = await TreeAsync("<h1 id='main-title'>Title</h1>");
        var heading = Only(tree, AccessibilityRole.Heading);

        Assert.Equal("main-title", heading.ElementId);
        Assert.Same(heading, tree.FindById(heading.Id));
        Assert.Null(tree.FindById("nope"));
    }

    // ---- where things are ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NodesKnowWhereTheyAre_AndAPointFindsTheInnermostOne()
    {
        var (document, tree) = await LoadAsync("<h1>Title</h1><p>Some <a href='#t'>link</a> text</p>");

        var link = Only(tree, AccessibilityRole.Link);
        var bounds = Assert.Single(link.Bounds);
        Assert.Equal(0, bounds.PageIndex);
        Assert.True(bounds.Bounds.Width > 0 && bounds.Bounds.Height > 0);

        var centre = (X: bounds.Bounds.Left + (bounds.Bounds.Width / 2), Y: bounds.Bounds.Top + (bounds.Bounds.Height / 2));
        Assert.Same(link, document.GetAccessibilityNodeAt(0, centre.X, centre.Y));
        Assert.Null(document.GetAccessibilityNodeAt(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetAccessibilityNodeAt(5, 1, 1));
    }

    [Fact]
    public async Task ABlockThatCrossesAPageBreakIsOnBothPages()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("word", 2000));
        var (document, tree) = await LoadAsync($"<p>{long_}</p>");

        Assert.True(document.Pages.Count > 1);
        var paragraph = Only(tree, AccessibilityRole.Paragraph);
        Assert.True(paragraph.Bounds.Count > 1);
        Assert.Equal(paragraph.Bounds.Select(b => b.PageIndex).OrderBy(i => i).ToArray(), paragraph.Bounds.Select(b => b.PageIndex).ToArray());
    }

    [Fact]
    public async Task TheRootIsNamedByTheDocumentTitle()
    {
        var (_, document) = await TestDocument.LoadAsync("<html lang='en'><head><title>My page</title></head><body><p>x</p></body></html>", new HtmlDocumentOptions { Media = "screen" });
        var tree = document.GetAccessibilityTree();

        Assert.Equal("My page", tree.Root.Name);
        Assert.Equal("en", tree.Root.Language);
        Assert.Same(tree, document.GetAccessibilityTree());
    }
}
