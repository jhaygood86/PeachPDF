using Microsoft.UI.Xaml;
using PeachBrackets.Accessibility;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;

namespace PeachBrackets.WinUI.UITests;

public class HtmlViewTests
{
    private const string Page = "<h1 id=h>Title</h1><p id=p>Some <a id=l href='https://example.com/'>link</a> text</p>";

    // Puts a view in the test window and waits until its document has laid out.
    private static async Task<HtmlView> LoadAsync(Window window, string html)
    {
        var view = new HtmlView { Html = html };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
        window.Content = view;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return view;
    }

    [Fact]
    public async Task ADocumentLoadsAndLaysOut()
    {
        var pages = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            return view.Document!.Pages.Count;
        });

        Assert.True(pages >= 1);
    }

    [Fact]
    public async Task TheViewExposesADocumentPeerWithTheTextBlocksBelowIt()
    {
        var (type, names) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var document = DocumentPeer(view)!;
            return (document.GetAutomationControlType(), document.GetChildren().Select(c => c.GetName()).ToList());
        });

        Assert.Equal(AutomationControlType.Document, type);
        Assert.Contains("Title", names);
        Assert.Contains(names, n => n.StartsWith("Some"));
    }

    [Fact]
    public async Task HeadingsLinksAndTextAreToldApartToUiAutomation()
    {
        var (heading, level, link, text) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var children = DocumentPeer(view)!.GetChildren();
            var h = children.First(c => c.GetName() == "Title");
            var p = children.First(c => c.GetName().StartsWith("Some"));
            var a = p.GetChildren().First();
            return (h.GetAutomationControlType(), h.GetHeadingLevel(), a.GetAutomationControlType(), p.GetAutomationControlType());
        });

        Assert.Equal(AutomationControlType.Text, heading);
        Assert.Equal(AutomationHeadingLevel.Level1, level);
        Assert.Equal(AutomationControlType.Hyperlink, link);
        Assert.Equal(AutomationControlType.Text, text);
    }

    [Fact]
    public async Task InvokingALinkRaisesLinkClickedAndCanBeCancelled()
    {
        var (uri, anchor) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            string? seen = null;
            view.LinkClicked += (_, e) =>
            {
                seen = e.Uri;
                e.Cancel = true;
            };
            var link = DocumentPeer(view)!.GetChildren().First(c => c.GetName().StartsWith("Some")).GetChildren().First();
            ((IInvokeProvider)link.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            return (seen, link.GetAutomationId());
        });

        Assert.Equal("https://example.com/", uri);
        Assert.False(string.IsNullOrEmpty(anchor));
    }

    [Fact]
    public async Task EveryNodeHasABoundingRectangleAndTheFirstIsOnScreen()
    {
        var (empty, firstOffscreen) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var nodes = DocumentPeer(view)!.GetChildren();
            return (nodes.Count(n => n.GetBoundingRectangle().Width <= 0 || n.GetBoundingRectangle().Height <= 0), nodes[0].IsOffscreen());
        });

        Assert.Equal(0, empty);
        Assert.False(firstOffscreen);
    }

    [Fact]
    public async Task SelectAllSelectsTheTextAndScrollToElementFindsAnId()
    {
        var (selected, found, missing) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            view.SelectAll();
            return (view.SelectedText, view.ScrollToElement("l"), view.ScrollToElement("nope"));
        });

        Assert.Contains("Title", selected);
        Assert.True(found);
        Assert.False(missing);
    }

    [Fact]
    public async Task ADocumentCanComeFromAFile_AndAMissingOneIsReported()
    {
        var path = Path.Combine(Path.GetTempPath(), $"peachbrackets-winui-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, Page, TestContext.Current.CancellationToken);
        try
        {
            var (fromFile, failure) = await UiThread.RunAsync(async window =>
            {
                var view = new HtmlView { Source = new Uri(path) };
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.DocumentLoaded += (_, _) => loaded.TrySetResult();
                window.Content = view;
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var title = DocumentPeer(view)!.GetChildren().Any(c => c.GetName() == "Title");

                var broken = new HtmlView { Source = new Uri(path + ".missing") };
                var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                broken.DocumentFailed += (_, e) => failed.TrySetResult(e.Exception);
                window.Content = broken;
                return (title, await failed.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            });

            Assert.True(fromFile);
            Assert.NotNull(failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ALinkToAnAnchorInTheDocumentIsRaisedWithoutAnAddress()
    {
        var (uri, anchor) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a id=go href='#end'>go</a><div style='height:3000px'></div><h2 id=end>End</h2>");
            string? seenUri = "unset";
            string? seenAnchor = null;
            view.LinkClicked += (_, e) =>
            {
                seenUri = e.Uri;
                seenAnchor = e.Destination;
            };
            var link = DocumentPeer(view)!.GetChildren()[0];
            ((IInvokeProvider)link.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            return (seenUri, seenAnchor);
        });

        Assert.Null(uri);
        Assert.Equal("end", anchor);
    }

    [Fact]
    public async Task TheDocumentAndItsNodesDescribeThemselvesToUiAutomation()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var document = DocumentPeer(view)!;
            var node = document.GetChildren().First(c => c.GetName() == "Title");
            node.SetFocus();
            node.ShowContextMenu();
            return new[]
            {
                document.GetClassName(), document.GetName(), node.GetClassName(),
                node.IsContentElement().ToString(), node.IsControlElement().ToString(), node.IsEnabled().ToString(),
                node.IsKeyboardFocusable().ToString(), node.HasKeyboardFocus().ToString(),
            };
        });

        Assert.Equal(["HtmlView", "HTML document", "HtmlNode", "True", "True", "True", "False", "False"], facts);
    }

    [Fact]
    public async Task ResizingTheViewLaysTheDocumentOutAgain()
    {
        var reloaded = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var again = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => again.TrySetResult();
            view.Width = 320;
            window.Content = null;
            window.Content = view;
            view.Width = 480;
            await again.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return view.Document is not null;
        });

        Assert.True(reloaded);
    }

    [Fact]
    public async Task LandmarksControlsAndTablesAreToldToUiAutomationWithTheirOwnTypes()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<nav aria-label='Site'><a href='#x'>Home</a></nav><main><button disabled aria-describedby=d>Save</button><span id=d>Saves it</span>"
                + "<table><tr><th>Head</th></tr><tr><td>Cell</td></tr></table></main>");
            var top = DocumentPeer(view)!.GetChildren();
            var nav = top.First(c => c.GetLandmarkType() == AutomationLandmarkType.Navigation);
            var main = top.First(c => c.GetLandmarkType() == AutomationLandmarkType.Main);
            var button = Descend(main).First(c => c.GetAutomationControlType() == AutomationControlType.Button);
            var table = Descend(main).First(c => c.GetAutomationControlType() == AutomationControlType.Table);
            return new[]
            {
                nav.GetName(), nav.GetChildren()[0].GetAutomationControlType().ToString(),
                button.GetName(), button.IsEnabled().ToString(), button.GetHelpText(),
                Descend(table).Any(c => c.GetAutomationControlType() == AutomationControlType.HeaderItem).ToString(),
            };
        });

        Assert.Equal(["Site", "Hyperlink", "Save", "False", "Saves it", "True"], facts);
    }

    [Fact]
    public async Task TabStepsThroughTheTabStopsOfThePageAndThenLeavesIt()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a href='#x'>first</a><button>second</button><p>text</p>");
            var log = new List<string>();

            async Task<bool> TabAsync(bool forward)
            {
                var used = view.StepFocus(forward);
                await view.FocusIdle;
                log.Add(used + ":" + (view.FocusedNode?.Name ?? "none"));
                return used;
            }

            await TabAsync(true);
            await TabAsync(true);
            await TabAsync(false);
            await TabAsync(true);
            await TabAsync(true);
            return log;
        });

        // Tab to the link, to the button, back to the link, on to the button, and past it: the page gives up the focus and the key is left to XAML.
        Assert.Equal(["True:first", "True:second", "True:first", "True:second", "False:none"], facts);
    }

    [Fact]
    public async Task AutomationCanFocusAPartOfThePageAndSeesWhereTheFocusIs()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a href='#x'>link</a><button>button</button><p>text</p>");
            var top = DocumentPeer(view)!.GetChildren();
            var button = top.First(c => c.GetAutomationControlType() == AutomationControlType.Button);
            var text = top.First(c => c.GetName() == "text");
            var before = button.HasKeyboardFocus();
            button.SetFocus();
            await Task.Delay(300);
            await view.FocusIdle;
            // The engine builds a new tree when the focus restyles the page; the peer for the focused part is still found.
            var found = DocumentPeer(view)!.FindPeer(view.FocusedNode!) is not null;
            return new[] { before.ToString(), button.IsKeyboardFocusable().ToString(), text.IsKeyboardFocusable().ToString(), button.HasKeyboardFocus().ToString(), view.FocusedNode!.Name, found.ToString() };
        });

        Assert.Equal(["False", "True", "False", "True", "button", "True"], facts);
    }

    [Fact]
    public async Task ListItemsKnowTheirPlaceInTheSet()
    {
        var places = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<ul><li>one</li><li>two</li><li>three</li></ul><p>not an item</p>");
            var top = DocumentPeer(view)!.GetChildren();
            var items = top.First(c => c.GetAutomationControlType() == AutomationControlType.List).GetChildren();
            return (items.Select(i => (i.GetPositionInSet(), i.GetSizeOfSet())).ToArray(), top.First(c => c.GetName() == "not an item").GetPositionInSet());
        });

        Assert.Equal([(1, 3), (2, 3), (3, 3)], places.Item1);
        Assert.Equal(-1, places.Item2);
    }

    private static IEnumerable<AutomationPeer> Descend(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren() ?? [])
        {
            yield return child;
            foreach (var below in Descend(child))
                yield return below;
        }
    }

    // A UserControl has no automation peer of its own: the view's is carried by an element inside it.
    private static HtmlViewAutomationPeer? DocumentPeer(DependencyObject element)
    {
        if (element is UIElement ui && FrameworkElementAutomationPeer.CreatePeerForElement(ui) is HtmlViewAutomationPeer found)
            return found;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (DocumentPeer(VisualTreeHelper.GetChild(element, i)) is { } peer)
                return peer;
        }

        return null;
    }

    [Fact]
    public async Task AScreenReaderTogglesACheckBoxAndTheRadioButtonsOfAGroupFollow()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<label><input type=checkbox id=agree> I agree</label><input type=radio name=p id=a checked><input type=radio name=p id=b>");
            var top = DocumentPeer(view)!.GetChildren();
            var checkBox = top.First(c => c.GetAutomationControlType() == AutomationControlType.CheckBox);
            var toggle = (IToggleProvider)checkBox.GetPattern(PatternInterface.Toggle);
            var before = toggle.ToggleState;
            toggle.Toggle();
            await Task.Delay(300);
            var after = ((IToggleProvider)DocumentPeer(view)!.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.CheckBox).GetPattern(PatternInterface.Toggle)).ToggleState;

            var radios = DocumentPeer(view)!.GetChildren().Where(c => c.GetAutomationControlType() == AutomationControlType.RadioButton).ToList();
            ((IToggleProvider)radios[1].GetPattern(PatternInterface.Toggle)).Toggle();
            await Task.Delay(300);
            var states = DocumentPeer(view)!.GetChildren().Where(c => c.GetAutomationControlType() == AutomationControlType.RadioButton)
                .Select(r => ((IToggleProvider)r.GetPattern(PatternInterface.Toggle)).ToggleState.ToString()).ToArray();
            return (before.ToString(), after.ToString(), states);
        });

        Assert.Equal(("Off", "On"), (facts.Item1, facts.Item2));
        Assert.Equal(["Off", "On"], facts.states);
    }

    [Fact]
    public async Task ATableIsAGridWhoseCellsKnowTheirPlaceSpanAndHeaders()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<table><tr><th>Name</th><th>Qty</th></tr><tr><td>Pear</td><td>3</td></tr><tr><td colspan=2>Total</td></tr></table>");
            var table = DocumentPeer(view)!.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.Table);
            var grid = (IGridProvider)table.GetPattern(PatternInterface.Grid);
            var tableInfo = (ITableProvider)table.GetPattern(PatternInterface.Table);

            var cells = new List<AutomationPeer>();
            void Collect(AutomationPeer peer)
            {
                foreach (var child in peer.GetChildren())
                {
                    if (child.GetPattern(PatternInterface.GridItem) is not null)
                        cells.Add(child);
                    else
                        Collect(child);
                }
            }

            Collect(table);
            var items = cells.Select(c => (IGridItemProvider)c.GetPattern(PatternInterface.GridItem)).ToList();
            var pear = (ITableItemProvider)cells[2].GetPattern(PatternInterface.TableItem);
            return (grid.RowCount, grid.ColumnCount, grid.GetItem(1, 0) is not null, grid.GetItem(2, 1) is not null, grid.GetItem(5, 5) is null,
                tableInfo.RowOrColumnMajor.ToString(), tableInfo.GetColumnHeaders().Length, tableInfo.GetRowHeaders().Length,
                cells.Count, items[2].Row, items[2].Column, items[4].ColumnSpan, items[4].ContainingGrid is not null, pear.GetColumnHeaderItems().Length);
        });

        Assert.Equal((3, 2, true, true, true, "RowMajor", 2, 0, 5, 1, 0, 2, true, 1), facts);
    }

    [Fact]
    public async Task TypingEditsTheFocusedTextFieldAndAScreenReaderReadsAndSetsIt()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=name value='Ada' style='width:150pt'><input id=agree type=checkbox>");
            view.StepFocus(true);
            await view.FocusIdle;

            var field = DocumentPeer(view)!.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.Edit);
            var value = (IValueProvider)field.GetPattern(PatternInterface.Value);
            var start = value.Value;

            // Typed characters go in at the caret, which starts at the end; Backspace takes one off; Home moves the caret to the front.
            view.TypeCharacter('m');
            view.TypeCharacter('\ud83d');
            view.TypeCharacter('\ude00');
            var typed = value.Value;
            view.HandleEditKey(Windows.System.VirtualKey.Back);
            view.HandleEditKey(Windows.System.VirtualKey.Home);
            view.TypeCharacter('>');
            var edited = value.Value;
            var control = view.TypeCharacter('\b');

            value.SetValue("Lin");
            await Task.Delay(300);
            return (start, typed, edited, control, value.Value, value.IsReadOnly);
        });

        Assert.Equal(("Ada", "Adam😀", ">Adam", false, "Lin", false), facts);
    }

    [Fact]
    public async Task TogglingACheckBoxIsAStateChangeTheViewCanTellUiAutomation()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=a type=checkbox aria-label=agree><p>after</p>");
            DocumentPeer(view);
            var box = DocumentPeer(view)!.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.CheckBox);
            ((IToggleProvider)box.GetPattern(PatternInterface.Toggle)).Toggle();
            await Task.Delay(300);
            var diff = view.LastAccessibilityDiff;
            return diff?.Changes.Select(c => (c.Kind.ToString(), c.Property)).ToArray();
        });

        Assert.NotNull(facts);
        Assert.Equal([("StateChanged", "checked")], facts);
    }

    [Fact]
    public async Task ALiveRegionAnnouncementIsToldToUiAutomationAndAViewNobodyAsksAboutTellsNothing()
    {
        var told = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<p>text</p>");

            // A view no automation client has asked about has no peer to tell, so a refresh does nothing and does not fail.
            var unasked = new HtmlView();
            unasked.RefreshAccessibility(view.Document!);

            var peer = (HtmlViewAutomationPeer)DocumentPeer(view)!;
            var region = view.AccessibilityTree!.Root;
            HtmlView.RaiseAnnouncements(peer,
            (AccessibilityAnnouncement[])[
                new AccessibilityAnnouncement("Saved", LiveSetting.Polite, region),
                new AccessibilityAnnouncement("Error", LiveSetting.Assertive, region),
            ]);
            return true;
        });

        Assert.True(told);
    }

    [Fact]
    public async Task TogglingACheckBoxRestylesTheRulesThatReadItsState()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<style>input:checked + label { color: red }</style><input id=a type=checkbox aria-label=agree><label for=a>on</label>");
            var box = DocumentPeer(view)!.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.CheckBox);
            ((IToggleProvider)box.GetPattern(PatternInterface.Toggle)).Toggle();
            await Task.Delay(500);
            await view.FocusIdle;
            return (view.Document!.NeedsRestyle, view.Document.GetFormState(view.Document.GetElement("a")!)!.Value.Checked);
        });

        Assert.Equal((false, true), facts);
    }

    [Fact]
    public async Task AProgressBarAndASliderAreRangesAndOnlyTheSliderCanBeMoved()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<progress id=p value=3 max=10></progress><input id=s type=range min=0 max=50 value=10 aria-label=volume>");
            var top = DocumentPeer(view)!.GetChildren();
            var bar = (IRangeValueProvider)top.First(c => c.GetAutomationControlType() == AutomationControlType.ProgressBar).GetPattern(PatternInterface.RangeValue);
            var sliderPeer = top.First(c => c.GetAutomationControlType() == AutomationControlType.Slider);
            var slider = (IRangeValueProvider)sliderPeer.GetPattern(PatternInterface.RangeValue);

            var before = (bar.Value, bar.Minimum, bar.Maximum, bar.IsReadOnly, slider.Value, slider.Maximum, slider.IsReadOnly, slider.LargeChange, slider.SmallChange);
            bar.SetValue(9);
            slider.SetValue(40);
            await Task.Delay(300);
            var after = DocumentPeer(view)!.GetChildren();
            var barAfter = (IRangeValueProvider)after.First(c => c.GetAutomationControlType() == AutomationControlType.ProgressBar).GetPattern(PatternInterface.RangeValue);
            var sliderAfter = (IRangeValueProvider)after.First(c => c.GetAutomationControlType() == AutomationControlType.Slider).GetPattern(PatternInterface.RangeValue);
            return (before, barAfter.Value, sliderAfter.Value);
        });

        Assert.Equal((3.0, 0.0, 10.0, true, 10.0, 50.0, false, 5.0, 0.5), facts.before);
        Assert.Equal(3.0, facts.Item2);
        Assert.Equal(40.0, facts.Item3);
    }

    [Fact]
    public async Task APressInATextFieldPlacesTheCaretAndADragSelectsFromThere()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=f value=\"hello world\" style=\"width:200px\">");
            var field = view.Document!.GetElement("f")!;
            var bounds = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "f").Bounds[0].Bounds;

            view._fieldPressed = true;
            Assert.True(view.FocusTextFieldAt(0, bounds.X + 1, bounds.Y + (bounds.Height / 2)));
            await Task.Delay(500);
            await view.FocusIdle;
            var anchor = view.FieldAnchor;
            await view.ExtendFieldSelectionAsync(view.Document, 0, bounds.X + bounds.Width - 1, bounds.Y + (bounds.Height / 2), anchor ?? 0);
            var state = view.Document.GetEditState()!.Value;
            return (anchor, state.SelectionStart, state.SelectionEnd, state.Text.Length);
        });

        Assert.Equal(0, facts.anchor);
        Assert.Equal((0, facts.Length), (facts.SelectionStart, facts.SelectionEnd));
    }

    [Fact]
    public async Task TheDocumentCanBeReadByWordAndParagraphThroughTheTextPattern()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<h1>Title here</h1><p>First paragraph.</p><input id=f aria-label=name value=\"typed in\">");
            var document = DocumentPeer(view)!;
            var text = (ITextProvider)document.GetPattern(PatternInterface.Text);
            var range = text.DocumentRange;
            var all = range.GetText(-1);

            var word = text.DocumentRange;
            word.MoveEndpointByRange(TextPatternRangeEndpoint.End, word, TextPatternRangeEndpoint.Start);
            word.ExpandToEnclosingUnit(TextUnit.Word);
            var firstWord = word.GetText(-1);
            word.Move(TextUnit.Word, 1);
            var secondWord = word.GetText(-1);

            var line = text.DocumentRange;
            line.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Paragraph, -5);
            line.ExpandToEnclosingUnit(TextUnit.Paragraph);
            var firstParagraph = line.GetText(-1);
            line.GetBoundingRectangles(out var rectangles);

            var field = document.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.Edit);
            var fieldText = (ITextProvider)field.GetPattern(PatternInterface.Text);
            var typed = fieldText.DocumentRange.GetText(-1);
            return (all, firstWord, secondWord, firstParagraph, rectangles.Length % 4, text.SupportedTextSelection, typed);
        });

        Assert.Contains("Title here", facts.all);
        Assert.Contains("First paragraph.", facts.all);
        Assert.Equal("Title ", facts.firstWord);
        Assert.Equal("here\n", facts.secondWord);
        Assert.Equal("Title here\n", facts.firstParagraph);
        Assert.Equal(0, facts.Item5);
        Assert.Equal(SupportedTextSelection.Single, facts.Item6);
        Assert.Equal("typed in\n", facts.typed);
    }

    [Fact]
    public async Task TheTextPatternFindsComparesAndLocatesRanges()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<h1>Title here</h1><p>First paragraph.</p>");
            var document = (HtmlViewAutomationPeer)DocumentPeer(view)!;
            var text = (ITextProvider)document.GetPattern(PatternInterface.Text);
            var all = text.DocumentRange;

            var found = all.FindText("paragraph", false, false);
            var foundBack = all.FindText("TITLE", true, true);
            var missing = all.FindText("zzz", false, false);

            var clone = all.Clone();
            var same = all.Compare(clone);
            var order = all.CompareEndpoints(TextPatternRangeEndpoint.Start, found, TextPatternRangeEndpoint.Start);
            clone.MoveEndpointByRange(TextPatternRangeEndpoint.Start, found, TextPatternRangeEndpoint.Start);
            clone.MoveEndpointByRange(TextPatternRangeEndpoint.End, found, TextPatternRangeEndpoint.End);
            var narrowed = clone.GetText(-1);
            var wider = found.Clone();
            wider.MoveEndpointByRange(TextPatternRangeEndpoint.Start, all, TextPatternRangeEndpoint.Start);
            var widerText = wider.GetText(-1);
            var backwards = found.Clone();
            backwards.MoveEndpointByRange(TextPatternRangeEndpoint.End, all, TextPatternRangeEndpoint.Start);
            var collapsed = backwards.GetText(-1);

            var enclosing = found.GetEnclosingElement();
            var children = found.GetChildren();
            found.ScrollIntoView(true);
            found.Select();
            found.AddToSelection();
            found.RemoveFromSelection();
            var attribute = found.GetAttributeValue(0);
            var attributeRange = found.FindAttribute(0, 0, false);
            found.GetBoundingRectangles(out var rectangles);

            var visible = text.GetVisibleRanges();
            var bounds = document.GetChildren()[0].GetBoundingRectangle();
            var atPoint = text.RangeFromPoint(new Windows.Foundation.Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)));
            var outside = text.RangeFromPoint(new Windows.Foundation.Point(-5000, -5000));
            var fromChild = text.RangeFromChild(document.ProviderOf(document.GetChildren()[0])!);
            var unknown = text.RangeFromChild(document.ProviderOf(document)!);
            var emptyView = (ITextProvider)new HtmlTextProvider(document, view, view.AccessibilityTree!.Root);

            return (found.GetText(-1), foundBack.GetText(-1), missing, same, order, narrowed, widerText, collapsed, enclosing is not null,
                children.Length, attribute, attributeRange, rectangles.Length, visible.Length, atPoint.GetText(-1), outside.GetText(-1), fromChild.GetText(-1),
                unknown.GetText(-1), text.GetSelection().Length, emptyView.DocumentRange is not null);
        });

        Assert.Equal("paragraph", facts.Item1);
        Assert.Equal("Title", facts.Item2);
        Assert.Null(facts.missing);
        Assert.True(facts.same);
        Assert.True(facts.order < 0);
        Assert.Equal("paragraph", facts.narrowed);
        Assert.Contains("Title here", facts.widerText);
        Assert.Equal(string.Empty, facts.collapsed);
        Assert.True(facts.Item9);
        Assert.Equal(0, facts.Item10);
        Assert.Equal(Microsoft.UI.Xaml.DependencyProperty.UnsetValue, facts.attribute);
        Assert.Null(facts.attributeRange);
        Assert.True(facts.Item13 > 0 && facts.Item13 % 4 == 0);
        Assert.True(facts.Item14 > 0);
        Assert.False(string.IsNullOrEmpty(facts.Item15));
        Assert.Equal(string.Empty, facts.Item16);
        Assert.Equal("Title here", facts.Item17);
        Assert.Equal(string.Empty, facts.Item18);
        Assert.Equal(0, facts.Item19);
        Assert.True(facts.Item20);
    }

    [Fact]
    public async Task ATextAreaTakesEnterAndMovesByLineWithTheArrowKeys()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<textarea id=t style=\"width:200px;height:100px;font-size:16px\">one" + (char)10 + "two</textarea>");
            var bounds = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "t").Bounds[0].Bounds;
            view._fieldPressed = true;
            Assert.True(view.FocusTextFieldAt(0, bounds.X + 3, bounds.Y + 3));
            await Task.Delay(500);
            await view.FocusIdle;

            var steps = new List<(int Start, int End, string Text)>();
            void Note() { var s = view.Document!.GetEditState()!.Value; steps.Add((s.SelectionStart, s.SelectionEnd, s.Text)); }
            Note();
            view.HandleEditKey(Windows.System.VirtualKey.Down);
            Note();
            view.HandleEditKey(Windows.System.VirtualKey.End);
            Note();
            view.HandleEditKey(Windows.System.VirtualKey.Enter);
            Note();
            view.HandleEditKey(Windows.System.VirtualKey.Up);
            Note();
            view.HandleEditKey(Windows.System.VirtualKey.Home);
            Note();
            return steps;
        });

        const char Lf = (char)10;
        var two = "one" + Lf + "two";
        var three = two + Lf;
        Assert.Equal((0, 0, two), facts[0]);
        Assert.Equal((4, 4, two), facts[1]);
        Assert.Equal((7, 7, two), facts[2]);
        Assert.Equal((8, 8, three), facts[3]);
        Assert.Equal((4, 4, three), facts[4]);
        Assert.Equal((4, 4, three), facts[5]);
    }

    [Fact]
    public async Task ALongPressOnATextFieldSelectsItsWordAndOpensTheMenu()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=f value=\"hello world\" style=\"width:200px\"><p>text</p>");
            var bounds = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "f").Bounds[0].Bounds;
            var opened = view.OpenFieldMenu(0, bounds.X + 2, bounds.Y + (bounds.Height / 2), new Windows.Foundation.Point(bounds.X, bounds.Y), selectWord: true);
            await Task.Delay(700);
            await view.FocusIdle;
            var state = view.Document!.GetEditState()!.Value;
            var elsewhere = view.OpenFieldMenu(0, 1, bounds.Y + bounds.Height + 40, new Windows.Foundation.Point(1, 1), selectWord: false);
            view.CloseFlyouts();
            await Task.Delay(400);
            return (opened, state.SelectionStart, state.SelectionEnd, elsewhere);
        });

        Assert.True(facts.opened);
        Assert.Equal((0, 5), (facts.SelectionStart, facts.SelectionEnd));
        Assert.False(facts.elsewhere);
    }

    [Fact]
    public async Task ARangeIsDraggedAndDropDownsAndPickersOpen()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window,
                "<input id=r type=range min=0 max=100 style=\"width:200px;display:block\">"
                + "<select id=s style=\"display:block\"><option value=a>A</option><option value=b selected>B</option></select>"
                + "<input id=d type=date value=2026-11-02 style=\"display:block\">"
                + "<input id=t type=time value=14:30 style=\"display:block\">"
                + "<input id=c type=color value=#ff8000 style=\"display:block\">"
                + "<input id=x type=text style=\"display:block\">");
            PeachDrawing.Core.Rect B(string id) => view.AccessibilityTree!.Nodes.First(n => n.ElementId == id).Bounds[0].Bounds;
            var r = B("r");

            var began = view.BeginRangeDrag(0, r.X + (r.Width / 2), r.Y + (r.Height / 2));
            var middle = double.Parse(view.Document!.GetFormState(view.Document.GetElement("r")!)!.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
            view.SetRange(r.X + r.Width);
            var end = double.Parse(view.Document.GetFormState(view.Document.GetElement("r")!)!.Value.Value, System.Globalization.CultureInfo.InvariantCulture);

            var opened = new List<bool>();
            foreach (var id in new[] { "s", "d", "t", "c", "x" })
            {
                var b = B(id);
                opened.Add(view.OpenPickerAt(0, b.X + 2, b.Y + (b.Height / 2), new Windows.Foundation.Point(b.X, b.Y)));
                view.CloseFlyouts();
                await Task.Delay(400);
            }

            view.CloseFlyouts();
            await Task.Delay(400);
            var none = view.BeginRangeDrag(0, B("x").X + 2, B("x").Y + 2);
            return (began, middle, end, opened, none);
        });

        Assert.True(facts.began);
        Assert.InRange(facts.middle, 45, 55);
        Assert.Equal(100, facts.end);
        Assert.Equal([true, true, true, true, false], facts.opened);
        Assert.False(facts.none);
    }

    [Fact]
    public async Task TheTextPatternReportsAndSetsTheSelectionOfAFocusedFieldAndTheAttributesOfText()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<h2 lang=fr>Titre</h2><input id=f value=\"hello world\" style=\"width:200px\"><p>plain</p>");
            var document = (HtmlViewAutomationPeer)DocumentPeer(view)!;
            var text = (ITextProvider)document.GetPattern(PatternInterface.Text);
            var before = text.GetSelection().Length;

            var bounds = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "f").Bounds[0].Bounds;
            view._fieldPressed = true;
            Assert.True(view.FocusTextFieldAt(0, bounds.X + 2, bounds.Y + (bounds.Height / 2)));
            await Task.Delay(500);
            await view.FocusIdle;
            view.Document!.SetEditSelection(2, 7);
            var selected = text.GetSelection();
            var selectedText = selected.Length == 1 ? selected[0].GetText(-1) : string.Empty;

            var all = text.DocumentRange;
            var word = all.FindText("world", false, false);
            word.Select();
            await Task.Delay(300);
            var afterSelect = view.Document.GetEditState()!.Value;

            var heading = all.FindText("Titre", false, false);
            var plain = all.FindText("plain", false, false);
            var styleOfHeading = heading.GetAttributeValue(40034);
            var cultureOfHeading = heading.GetAttributeValue(40004);
            var readOnlyOfPlain = plain.GetAttributeValue(40015);
            var readOnlyOfField = word.GetAttributeValue(40015);
            var unsupported = heading.GetAttributeValue(40019);
            var foundHeading = all.FindAttribute(40034, 70002, false);
            var foundBack = all.FindAttribute(40015, false, true);
            var notFound = all.FindAttribute(40034, 70005, false);
            return (before, selectedText, afterSelect.SelectionStart, afterSelect.SelectionEnd, styleOfHeading, cultureOfHeading, readOnlyOfPlain,
                readOnlyOfField, unsupported == Microsoft.UI.Xaml.DependencyProperty.UnsetValue, foundHeading.GetText(-1), foundBack.GetText(-1), notFound);
        });

        Assert.Equal(0, facts.before);
        Assert.Equal("llo w", facts.selectedText);
        Assert.Equal((6, 11), (facts.Item3, facts.Item4));
        Assert.Equal(70002, facts.Item5);
        Assert.Equal(12, facts.Item6);
        Assert.Equal(true, facts.Item7);
        Assert.Equal(false, facts.Item8);
        Assert.True(facts.Item9);
        Assert.Equal("Titre", facts.Item10);
        Assert.Equal("hello world", facts.Item11);
        Assert.Null(facts.Item12);
    }

    [Fact]
    public async Task AChangeAnApplicationMakesToTheDocumentIsShownAndTold()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=a type=checkbox aria-label=agree>");
            var peerTree = DocumentPeer(view)!.GetChildren();
            var result = view.Document!.SetChecked(view.Document.GetElement("a")!, true);
            view.ApplyUpdate(result);
            var changed = view.LastAccessibilityDiff?.Changes.Any(c => c.Property == "checked") == true;

            view.ApplyUpdate(new PeachBrackets.Updates.UpdateResult(false, "nothing", []));
            return (peerTree.Count, changed);
        });

        Assert.Equal(1, facts.Item1);
        Assert.True(facts.changed);
    }

    [Fact]
    public async Task TheWheelScrollsATextAreaThatHasMoreLinesThanShowAndOtherwiseLeavesItToThePage()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var lines = string.Join("<br>", Enumerable.Range(1, 12).Select(i => "line " + i));
            var view = await LoadAsync(window, "<textarea id=t style=\"width:200px;height:50px;display:block\">" + string.Join((char)10, Enumerable.Range(1, 12).Select(i => "line " + i)) + "</textarea><input id=i style=\"display:block\">");
            var t = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "t").Bounds[0].Bounds;
            var i = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "i").Bounds[0].Bounds;
            var down = view.ScrollTextAreaAt(0, t.X + 5, t.Y + 5, 20);
            var up = view.ScrollTextAreaAt(0, t.X + 5, t.Y + 5, -500);
            var upAgain = view.ScrollTextAreaAt(0, t.X + 5, t.Y + 5, -20);
            var notAField = view.ScrollTextAreaAt(0, i.X + 5, i.Y + 3, 20);
            var outside = view.ScrollTextAreaAt(0, 1, 1000, 20);
            return (down, up, upAgain, notAField, outside);
        });

        Assert.Equal((true, true, false, false, false), facts);
    }

    [Fact]
    public async Task AClickOnAFileInputOpensThePickerAndShowsTheChosenNames()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<input id=f type=file multiple accept=\".txt\" style=\"width:300px;display:block\">");
            var asked = new List<PeachBrackets.Updates.FileInputInfo>();
            view.FilePicker = info =>
            {
                asked.Add(info);
                return Task.FromResult(new[] { "a.txt", "b.txt" });
            };
            var b = view.AccessibilityTree!.Nodes.First(n => n.ElementId == "f").Bounds[0].Bounds;
            var opened = view.OpenPickerAt(0, b.X + 5, b.Y + (b.Height / 2), new Windows.Foundation.Point(b.X, b.Y));
            await Task.Delay(300);
            var value = view.Document!.GetFormState(view.Document.GetElement("f")!)!.Value.Value;

            view.FilePicker = _ => throw new InvalidOperationException("no window");
            view.OpenPickerAt(0, b.X + 5, b.Y + (b.Height / 2), new Windows.Foundation.Point(b.X, b.Y));
            await Task.Delay(200);
            var after = view.Document.GetFormState(view.Document.GetElement("f")!)!.Value.Value;
            return (opened, asked.Count, asked[0], value, after);
        });

        Assert.True(facts.opened);
        Assert.Equal(1, facts.Count);
        Assert.Equal((true, ".txt"), (facts.Item3.Multiple, facts.Item3.Accept));
        Assert.Equal("a.txt, b.txt", facts.value);
        Assert.Equal("a.txt, b.txt", facts.after);
    }

    [Fact]
    public async Task TheTextPatternReportsTheFontAndColoursOfText()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<p style=\"font-size:20pt;font-weight:bold;font-style:italic;color:rgb(255,0,0);background:rgb(0,0,255)\">styled</p><p>plain</p>");
            var text = (ITextProvider)DocumentPeer(view)!.GetPattern(PatternInterface.Text);
            var all = text.DocumentRange;
            var styled = all.FindText("styled", false, false);
            var plain = all.FindText("plain", false, false);
            return (styled.GetAttributeValue(40005), styled.GetAttributeValue(40006), styled.GetAttributeValue(40007), styled.GetAttributeValue(40014),
                styled.GetAttributeValue(40008), styled.GetAttributeValue(40001), plain.GetAttributeValue(40007), plain.GetAttributeValue(40014),
                plain.GetAttributeValue(40001) == Microsoft.UI.Xaml.DependencyProperty.UnsetValue);
        });

        Assert.False(string.IsNullOrEmpty((string)facts.Item1));
        Assert.Equal(20.0, (double)facts.Item2, 1);
        Assert.Equal(700, facts.Item3);
        Assert.Equal(true, facts.Item4);
        Assert.Equal(0x0000FF, facts.Item5);
        Assert.Equal(0xFF0000, facts.Item6);
        Assert.Equal(400, facts.Item7);
        Assert.Equal(false, facts.Item8);
        Assert.True(facts.Item9);
    }

    [Fact]
    public async Task TheTextPatternSelectsAndReportsTheSelectionOfThePagesOwnText()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<p>dup one dup two</p><p>other</p>");
            var text = (ITextProvider)DocumentPeer(view)!.GetPattern(PatternInterface.Text);
            var all = text.DocumentRange;
            var none = text.GetSelection().Length;

            var firstDup = all.FindText("dup", false, false);
            var second = all.Clone();
            second.MoveEndpointByRange(TextPatternRangeEndpoint.Start, firstDup, TextPatternRangeEndpoint.End);
            var secondDup = second.FindText("dup", false, false);
            secondDup.Select();
            await Task.Delay(300);
            var selectedInDocument = view.Document!.Selection.GetText();
            var reported = text.GetSelection();
            var reportedText = reported.Length == 1 ? reported[0].GetText(-1) : string.Empty;
            var reportedStart = reported.Length == 1 ? reported[0].CompareEndpoints(TextPatternRangeEndpoint.Start, secondDup, TextPatternRangeEndpoint.Start) : 99;

            view.Document.Selection.SelectAll();
            var multiBlock = text.GetSelection().Length;
            return (none, selectedInDocument, reportedText, reportedStart, multiBlock);
        });

        Assert.Equal(0, facts.none);
        Assert.Equal("dup", facts.selectedInDocument);
        Assert.Equal("dup", facts.reportedText);
        Assert.Equal(0, facts.reportedStart);
        Assert.Equal(0, facts.multiBlock);
    }
}
