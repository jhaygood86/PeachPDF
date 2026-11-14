using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia.Tests;

public class FormsTests
{
    private const string Page = """
        <html><body style="margin:0;font-size:14px">
        <form id="f" action="/go">
          <input id="c" type="checkbox" name="c" style="width:20px;height:20px">
          <label id="lab" for="c">box</label><br>
          <input id="r1" type="radio" name="r" value="a" style="width:20px;height:20px">
          <input id="r2" type="radio" name="r" value="b" style="width:20px;height:20px"><br>
          <input id="rng" type="range" min="0" max="100" value="0" style="width:200px;height:20px"><br>
          <input id="req" type="text" name="req" required value="">
          <button id="sub" type="submit" style="width:80px;height:24px">Send</button>
          <button id="rst" type="reset" style="width:80px;height:24px">Reset</button>
        </form>
        <details id="d"><summary id="sum">More</summary><p id="inner">hidden text</p></details>
        <select id="sel"><option value="x">X</option><option value="y" selected>Y</option></select>
        </body></html>
        """;

    [Fact]
    public Task ClickingACheckboxTogglesIt() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        Assert.False(HtmlViewInput.State(view, "c").Checked);
        await HtmlViewInput.ClickAsync(window, view, "c");
        Assert.True(HtmlViewInput.State(view, "c").Checked);
        await HtmlViewInput.ClickAsync(window, view, "c");
        Assert.False(HtmlViewInput.State(view, "c").Checked);
        window.Close();
    });

    [Fact]
    public Task ClickingARadioButtonUnchecksItsGroup() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        await HtmlViewInput.ClickAsync(window, view, "r1");
        Assert.True(HtmlViewInput.State(view, "r1").Checked);
        await HtmlViewInput.ClickAsync(window, view, "r2");
        Assert.True(HtmlViewInput.State(view, "r2").Checked);
        Assert.False(HtmlViewInput.State(view, "r1").Checked);
        window.Close();
    });

    [Fact]
    public Task PressingOnARangeSetsItAndDraggingMovesIt() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        var bounds = view.Document!.GetElementBounds("rng")!.Value;
        var y = (bounds.Y + (bounds.Height / 2)) * HtmlViewInput.PixelsPerPoint;
        var left = bounds.X * HtmlViewInput.PixelsPerPoint;
        var width = bounds.Width * HtmlViewInput.PixelsPerPoint;

        window.MouseDown(new(left + (width * 0.5), y), MouseButton.Left);
        await HtmlViewInput.SettleAsync(view);
        var middle = double.Parse(HtmlViewInput.State(view, "rng").Value, CultureInfo.InvariantCulture);
        Assert.InRange(middle, 35, 65);

        window.MouseMove(new(left + (width * 0.9), y));
        await HtmlViewInput.SettleAsync(view);
        var dragged = double.Parse(HtmlViewInput.State(view, "rng").Value, CultureInfo.InvariantCulture);
        window.MouseUp(new(left + (width * 0.9), y), MouseButton.Left);
        Assert.True(dragged > middle + 20, $"dragged value {dragged} should be past {middle}");
        window.Close();
    });

    [Fact]
    public Task AValidFormRaisesFormSubmitting() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        FormSubmission? submitted = null;
        view.FormSubmitting += (_, e) => submitted = e.Submission;
        await HtmlViewInput.ClickAsync(window, view, "c");
        view.Document!.SetValue(view.Document.GetElement("req")!, "hello");
        await HtmlViewInput.ClickAsync(window, view, "sub");
        Assert.NotNull(submitted);
        Assert.Contains(submitted.Entries, e => e.Name == "req" && e.Value == "hello");
        Assert.Contains(submitted.Entries, e => e.Name == "c");
        window.Close();
    });

    [Fact]
    public Task AnInvalidFormRaisesFormInvalidAndFocusesTheField() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        var submitting = false;
        HtmlFormInvalidEventArgs? invalid = null;
        view.FormSubmitting += (_, _) => submitting = true;
        view.FormInvalid += (_, e) => invalid = e;
        await HtmlViewInput.ClickAsync(window, view, "sub");
        Assert.False(submitting);
        Assert.NotNull(invalid);
        Assert.NotEmpty(invalid.Problems);
        Assert.Equal("req", invalid.Problems[0].Element.Id);
        Assert.Equal("req", view.FocusedNode?.ElementId);
        window.Close();
    });

    [Fact]
    public Task ResetPutsTheControlsBack() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        await HtmlViewInput.ClickAsync(window, view, "c");
        Assert.True(HtmlViewInput.State(view, "c").Checked);
        await HtmlViewInput.ClickAsync(window, view, "rst");
        Assert.False(HtmlViewInput.State(view, "c").Checked);
        window.Close();
    });

    [Fact]
    public Task ClickingASummaryOpensTheDetailsAndMovesWhatFollows() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        var before = view.Document!.GetElementBounds("sel")!.Value.Y;
        await HtmlViewInput.ClickAsync(window, view, "sum");
        var after = view.Document.GetElementBounds("sel")!.Value.Y;
        Assert.True(after > before + 5, $"the select should move down when the details open: {before} -> {after}");
        window.Close();
    });

    [Fact]
    public Task ChoosingAnOptionOfTheSelectMenuSetsTheValue() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 600, 400);
        var document = view.Document!;
        var menu = view.BuildSelectMenu(document, document.GetElement("sel")!);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(2, items.Count);
        Assert.False(items[0].IsChecked);
        Assert.True(items[1].IsChecked);
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("x", HtmlViewInput.State(view, "sel").Value);
        window.Close();
    });

    [Fact]
    public Task TheDateFlyoutWritesTheChosenDay() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync("<input id='d' type='date' value='2024-02-10'>", 600, 200);
        var document = view.Document!;
        var flyout = view.BuildDateFlyout(document, document.GetElement("d")!, "2024-02-10", withTime: false);
        var calendar = Assert.IsType<global::Avalonia.Controls.Calendar>(flyout.Content);
        Assert.Equal(new DateTime(2024, 2, 10), calendar.SelectedDate);
        calendar.SelectedDate = new DateTime(2024, 3, 5);
        Assert.Equal("2024-03-05", HtmlViewInput.State(view, "d").Value);
        window.Close();
    });

    [Fact]
    public Task TheTimeFlyoutWritesTheChosenTime() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync("<input id='t' type='time' value='09:30'>", 600, 200);
        var document = view.Document!;
        var flyout = view.BuildTimeFlyout(document, document.GetElement("t")!, "09:30", null);
        var panel = Assert.IsType<StackPanel>(flyout.Content);
        var row = Assert.IsType<StackPanel>(panel.Children[0]);
        ((NumericUpDown)row.Children[0]).Value = 14;
        ((NumericUpDown)row.Children[1]).Value = 5;
        ((Button)panel.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("14:05", HtmlViewInput.State(view, "t").Value);
        window.Close();
    });

    [Fact]
    public Task TheColorFlyoutWritesTheChosenColor() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync("<input id='k' type='color' value='#102030'>", 600, 200);
        var document = view.Document!;
        var flyout = view.BuildColorFlyout(document, document.GetElement("k")!, "#102030");
        var panel = Assert.IsType<StackPanel>(flyout.Content);
        var sliders = panel.Children.OfType<Slider>().ToList();
        Assert.Equal([16.0, 32.0, 48.0], sliders.Select(s => s.Value));
        sliders[0].Value = 255;
        panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("#ff2030", HtmlViewInput.State(view, "k").Value);
        window.Close();
    });

    [Fact]
    public Task ChosenFilesAreWrittenIntoTheFileField() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync("<form><input id='f' type='file' name='up'></form>", 600, 200);
        var element = view.Document!.GetElement("f")!;
        view.SetChosenFiles(element, [new ChosenFile("a.txt", "text/plain", [1, 2, 3])]);
        Assert.Contains("a.txt", HtmlViewInput.State(view, "f").Value);
        window.Close();
    });

    [Fact]
    public void AcceptAttributesBecomeFileFilters()
    {
        Assert.Null(HtmlView.FileTypesFor(string.Empty));
        var filter = Assert.Single(HtmlView.FileTypesFor("image/png,.pdf")!);
        Assert.Contains("image/png", filter.MimeTypes!);
        Assert.Contains("*.pdf", filter.Patterns!);
        Assert.Equal("image/png", HtmlView.ContentTypeOf("x.png", string.Empty));
        Assert.Equal("application/octet-stream", HtmlView.ContentTypeOf("x.bin", string.Empty));
        Assert.Equal("application/x-foo", HtmlView.ContentTypeOf("x.bin", "application/x-foo"));
    }
}

public class RangeKeyTests
{
    private const string Page = """
        <html><body style="margin:0">
        <input id="r" type="range" min="10" max="50" step="5" value="20" style="width:200px;height:20px"><br>
        <input id="plain" type="range" style="width:200px;height:20px"><br>
        <input id="off" type="range" value="20" disabled style="width:200px;height:20px">
        <div style="height:1500px"></div>
        </body></html>
        """;

    private static double Value(HtmlView view, string id) =>
        double.Parse(HtmlViewInput.State(view, id).Value, CultureInfo.InvariantCulture);

    [Fact]
    public Task ArrowKeysMoveAFocusedRangeByItsStepWithinMinAndMax() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal("r", view.FocusedNode?.ElementId);
        var scrolled = view.ScrollOffsetY;

        await HtmlViewInput.KeyAsync(window, view, Key.Right);
        Assert.Equal(25, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.Up);
        Assert.Equal(30, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.Left);
        await HtmlViewInput.KeyAsync(window, view, Key.Down);
        Assert.Equal(20, Value(view, "r"));

        // A large step is a tenth of the range, kept a multiple of the step; Home and End go to the ends; nothing goes past them.
        await HtmlViewInput.KeyAsync(window, view, Key.PageUp);
        Assert.Equal(25, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.End);
        Assert.Equal(50, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.Right);
        Assert.Equal(50, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.Home);
        Assert.Equal(10, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.PageDown);
        Assert.Equal(10, Value(view, "r"));

        // The keys went to the control, not to the page's scrolling.
        Assert.Equal(scrolled, view.ScrollOffsetY);
        window.Close();
    });

    [Fact]
    public Task ARangeWithNoStepOrBoundsMovesByOneInZeroToOneHundred() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal("plain", view.FocusedNode?.ElementId);
        await HtmlViewInput.KeyAsync(window, view, Key.Right);
        Assert.Equal(51, Value(view, "plain"));
        await HtmlViewInput.KeyAsync(window, view, Key.End);
        Assert.Equal(100, Value(view, "plain"));
        window.Close();
    });

    [Fact]
    public Task ARangeWithAMaximumBelowItsMinimumAndNoUsableStepStillMovesByOne() => HeadlessSession.Run(async () =>
    {
        const string Odd = "<html><body style='margin:0'><input id='r' type='range' min='10' max='5' step='0' value='20' style='width:200px;height:20px'></body></html>";
        var (window, view) = await HeadlessSession.ShowAsync(Odd, 400, 300);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal("r", view.FocusedNode?.ElementId);
        var before = Value(view, "r");

        // The bounds fall back to the minimum and a hundred above it, and the step to one.
        await HtmlViewInput.KeyAsync(window, view, Key.Right);
        Assert.Equal(before + 1, Value(view, "r"));
        await HtmlViewInput.KeyAsync(window, view, Key.End);
        Assert.Equal(110, Value(view, "r"));
        window.Close();
    });

    [Fact]
    public Task ARangeWithStepAnyMovesByAHundredthOfItsRange() => HeadlessSession.Run(async () =>
    {
        const string Any = "<html><body style='margin:0'><input id='r' type='range' min='0' max='200' step='any' value='100' style='width:200px;height:20px'></body></html>";
        var (window, view) = await HeadlessSession.ShowAsync(Any, 400, 300);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        await HtmlViewInput.KeyAsync(window, view, Key.Right);
        Assert.Equal(102, Value(view, "r"), 3);
        window.Close();
    });
}

public class FormRestyleTests
{
    private const string Page = """
        <html><head><style>
          #lab { display:block; width:120px; height:40px; background:#ff0000; }
          #c:checked + #lab { background:#00ff00; }
        </style></head><body style="margin:0">
        <input id="c" type="checkbox" style="width:20px;height:20px">
        <label id="lab" for="c">box</label>
        </body></html>
        """;

    [Fact]
    public Task ARuleThatReadsTheStateOfAControlIsMatchedAgainAfterTheControlChanges() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 200);
        var bounds = view.Document!.GetElementBounds("lab")!.Value;
        int x = (int)(bounds.X + 30), y = (int)(bounds.Y + 20);
        var (r, g, _) = HeadlessSession.PixelAt(HeadlessSession.Capture(window), x, y);
        Assert.True(r > 200 && g < 60, $"the label starts red, got ({r}, {g})");

        await HtmlViewInput.ClickAsync(window, view, "c");
        Assert.True(HtmlViewInput.State(view, "c").Checked);
        Assert.True(await HeadlessSession.PumpUntilAsync(() =>
        {
            var (red, green, _) = HeadlessSession.PixelAt(HeadlessSession.Capture(window), x, y);
            return green > 200 && red < 60;
        }), "the label turns green once the checkbox is checked");
        window.Close();
    });

    [Fact]
    public Task AKeyThatIsNeitherSpaceNorEnterDoesNotActivateAFocusedCheckbox() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 200);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal("c", view.FocusedNode?.ElementId);
        await HtmlViewInput.KeyAsync(window, view, Key.X);
        Assert.False(HtmlViewInput.State(view, "c").Checked);
        await HtmlViewInput.KeyAsync(window, view, Key.Space);
        Assert.True(HtmlViewInput.State(view, "c").Checked);
        window.Close();
    });
}
