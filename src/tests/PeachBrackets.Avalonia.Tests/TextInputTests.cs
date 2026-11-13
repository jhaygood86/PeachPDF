using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia.Tests;

public class TextInputTests
{
    private const string Page =
        "<html><body style='margin:0'>" +
        "<form action='/go'><input id='a' name='a' style='position:absolute;left:10px;top:10px;width:200px;height:24px;font-size:14px'>" +
        "<textarea id='t' style='position:absolute;left:10px;top:60px;width:200px;height:80px;font-size:14px'></textarea></form>" +
        "<input id='p' type='password' style='position:absolute;left:10px;top:160px;width:200px;height:24px'>" +
        "</body></html>";

    private static readonly Point FieldPoint = new(20, 22);
    private static readonly Point AreaPoint = new(20, 70);
    private static readonly Point PasswordPoint = new(20, 172);

    private static FormEditState State(HtmlView view) => view.Document!.GetEditState() ?? throw new InvalidOperationException("No field has the focus.");

    private static async Task ClickAsync(Window window, HtmlView view, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        await view.PlacementTask;
    }

    private static async Task<(Window Window, HtmlView View)> FocusFieldAsync(Point at, string? initial = null)
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        await ClickAsync(window, view, at);
        if (initial is not null)
            window.KeyTextInput(initial);

        return (window, view);
    }

    [Fact]
    public async Task AClickFocusesTheFieldAndTypedTextGoesIn()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "abc");
            Assert.Equal("abc", State(view).Text);
            Assert.Equal(3, State(view).SelectionEnd);
            window.Close();
        });
    }

    [Fact]
    public async Task TypingWithNoFieldFocusedDoesNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            window.KeyTextInput("abc");
            Assert.Null(view.Document!.GetEditState());
            Assert.False(view.TypeText("abc"));
            window.Close();
        });
    }

    [Fact]
    public async Task ArrowKeysBackspaceAndDeleteEditTheText()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "abcd");
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("abd", State(view).Text);
            Assert.Equal(2, State(view).SelectionEnd);

            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Equal("ab", State(view).Text);

            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(0, State(view).SelectionEnd);
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Assert.Equal(2, State(view).SelectionEnd);
            window.Close();
        });
    }

    [Fact]
    public async Task ShiftExtendsTheSelectionAndControlMovesByWord()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "one two three");
            window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            Assert.Equal((11, 13), (State(view).SelectionStart, State(view).SelectionEnd) switch { var (s, e) => (Math.Min(s, e), Math.Max(s, e)) });

            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            window.KeyPress(Key.Right, RawInputModifiers.Control, PhysicalKey.ArrowRight, null);
            Assert.Equal(4, State(view).SelectionEnd);

            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            window.KeyPress(Key.Back, RawInputModifiers.Control, PhysicalKey.Backspace, null);
            Assert.Equal("one two ", State(view).Text);

            window.KeyPress(Key.Left, RawInputModifiers.Shift | RawInputModifiers.Control, PhysicalKey.ArrowLeft, null);
            Assert.Equal(4, Math.Min(State(view).SelectionStart, State(view).SelectionEnd));
            window.Close();
        });
    }

    [Fact]
    public async Task ControlASelectsEverythingInTheFieldNotThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello");
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            Assert.Equal((0, 5), (State(view).SelectionStart, State(view).SelectionEnd));
            Assert.Equal(string.Empty, view.SelectedText);
            window.KeyTextInput("X");
            Assert.Equal("X", State(view).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task CopyCutAndPasteUseTheClipboard()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello world");
            var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;

            view.Document!.SetEditSelection(6, 11);
            await view.CopyFieldAsync();
            Assert.Equal("world", await ClipboardText(clipboard));
            Assert.Equal("hello world", State(view).Text);

            await view.CutFieldAsync();
            Assert.Equal("hello ", State(view).Text);

            await view.PasteIntoFieldAsync();
            Assert.Equal("hello world", State(view).Text);

            // A paste into a one-line field is one line.
            await SetClipboard(clipboard, "a\r\nb");
            await view.PasteIntoFieldAsync();
            Assert.Equal("hello worlda b", State(view).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task APasswordIsNeverCopied()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(PasswordPoint, "secret");
            var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
            await SetClipboard(clipboard, "before");
            view.Document!.SetEditSelection(0, 6);
            await view.CopyFieldAsync();
            await view.CutFieldAsync();
            Assert.Equal("before", await ClipboardText(clipboard));
            Assert.Equal("secret", State(view).Text);
            Assert.Equal(TextInputContentType.Password, TextInputOptions.GetContentType(view));
            Assert.True(TextInputOptions.GetIsSensitive(view));
            window.Close();
        });
    }

    [Fact]
    public async Task ADoubleClickSelectsAWordAndATripleClickAllTheText()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "alpha beta");
            // The control point (20, 22) at zoom 1 is 15, 16.5 points of the first page.
            const double x = 20 * 0.75;
            const double y = 22 * 0.75;
            Assert.True(view.PressInTextField(0, x, y, 2));
            await view.PlacementTask;
            Assert.Equal((0, 5), (State(view).SelectionStart, State(view).SelectionEnd));

            Assert.True(view.PressInTextField(0, x, y, 3));
            await view.PlacementTask;
            Assert.Equal((0, 10), (State(view).SelectionStart, State(view).SelectionEnd));
            window.Close();
        });
    }

    [Fact]
    public async Task DraggingInsideAFieldSelectsBetweenThePressAndThePointer()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello");
            window.MouseDown(new Point(11, 22), MouseButton.Left);
            await view.PlacementTask;
            Assert.Equal((0, 0), (State(view).SelectionStart, State(view).SelectionEnd));
            window.MouseMove(new Point(205, 22));
            Assert.Equal((0, 5), (State(view).SelectionStart, State(view).SelectionEnd));
            window.MouseUp(new Point(205, 22), MouseButton.Left);
            Assert.Equal(string.Empty, view.SelectedText);
            window.Close();
        });
    }

    [Fact]
    public async Task EnterInAOneLineFieldSubmitsItsForm()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "q");
            HtmlFormSubmittingEventArgs? submitted = null;
            view.FormSubmitting += (_, e) => submitted = e;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.NotNull(submitted);
            Assert.Equal("q", State(view).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task EnterInATextAreaInsertsALineAndUpAndDownMoveByLine()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(AreaPoint, "ab");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyTextInput("cd");
            Assert.Equal("ab\ncd", State(view).Text);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.True(State(view).SelectionEnd <= 2);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.True(State(view).SelectionEnd >= 3);

            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(3, State(view).SelectionEnd);
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Assert.Equal(5, State(view).SelectionEnd);
            window.Close();
        });
    }

    [Fact]
    public async Task TabAndEscapeAreLeftToTheFocusNavigation()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "a");
            var tab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab };
            Assert.False(view.HandleEditKey(tab));
            Assert.False(view.HandleEditKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }));
            window.Close();
        });
    }

    [Fact]
    public async Task AControlKeyWithNoFieldFocusedStillCopiesThePageSelection()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            Assert.False(view.HandleEditKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control }));
            window.Close();
        });
    }

    [Fact]
    public async Task ThePreeditStringIsComposedInTheFieldAndReplacedByTheCommit()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "x");
            view.SetPreedit("n", null);
            Assert.Equal("xn", State(view).Text);
            Assert.Equal((1, 2), (State(view).CompositionStart, State(view).CompositionEnd));

            view.SetPreedit("ni", 2);
            Assert.Equal("xni", State(view).Text);
            Assert.Equal((1, 3), (State(view).CompositionStart, State(view).CompositionEnd));

            // The commit replaces what was composed.
            window.KeyTextInput("你");
            Assert.Equal("x你", State(view).Text);
            Assert.Equal(-1, State(view).CompositionStart);

            // A cancelled composition takes its text out again.
            view.SetPreedit("zz", null);
            view.SetPreedit(null, null);
            Assert.Equal("x你", State(view).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task AnEmptyPreeditDoesNotDeleteTheSelection()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello");
            view.Document!.SetEditSelection(0, 5);
            view.SetPreedit(null, null);
            Assert.Equal("hello", State(view).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task TheInputMethodClientReportsTheFieldAndTheCaret()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello");
            var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            view.RaiseEvent(args);
            var client = args.Client;
            Assert.NotNull(client);
            Assert.Same(view, client.TextViewVisual);
            Assert.True(client.SupportsPreedit);
            Assert.Equal("hello", client.SurroundingText);
            Assert.Equal(new TextSelection(5, 5), client.Selection);

            // The caret is inside the field, which is at 10,10 and 200 wide.
            var caret = client.CursorRectangle;
            Assert.InRange(caret.X, 10, 210);
            Assert.InRange(caret.Y, 8, 36);
            Assert.True(caret.Height > 5);

            client.Selection = new TextSelection(1, 3);
            Assert.Equal((1, 3), (State(view).SelectionStart, State(view).SelectionEnd));
            window.Close();
        });
    }

    [Fact]
    public async Task TheClientIsNotOfferedBeforeADocumentLoads()
    {
        await HeadlessSession.Run(async () =>
        {
            var view = new HtmlView();
            var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            view.RaiseEvent(args);
            Assert.Null(args.Client);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheFieldMenuOffersCutCopyPasteAndSelectAllAsTheSelectionAllows()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await FocusFieldAsync(FieldPoint, "hello");
            var menu = view.BuildFieldMenu();
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["Cut", "Copy", "Paste", "Select all"], items.Select(i => (string)i.Header!));
            Assert.Equal([false, false, true, true], items.Select(i => i.IsEnabled));

            view.Document!.SetEditSelection(0, 5);
            items = view.BuildFieldMenu().Items.OfType<MenuItem>().ToList();
            Assert.Equal([true, true, true, true], items.Select(i => i.IsEnabled));

            // Select all through the menu.
            view.Document.SetEditSelection(2, 2);
            items = view.BuildFieldMenu().Items.OfType<MenuItem>().ToList();
            items[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal((0, 5), (State(view).SelectionStart, State(view).SelectionEnd));
            window.Close();
        });
    }

    [Fact]
    public async Task ARightClickOnAFieldFocusesItAndMakesTheMenu()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            window.MouseDown(FieldPoint, MouseButton.Right);
            await view.PlacementTask;
            Assert.NotNull(view.Document!.GetEditState());
            window.Close();
        });
    }

    [Fact]
    public async Task TheCaretStaysInViewWhileTyping()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(
                "<html><body style='margin:0'><div style='height:1500px'></div><input id='a' style='width:200px'></body></html>", 400, 300);
            // Scroll to the end of the page, where the field is at the bottom of the view, and type in it.
            window.MouseWheel(new Point(300, 100), new Vector(0, -60));
            await ClickAsync(window, view, new Point(20, 285));
            window.KeyTextInput("abc");
            Assert.Equal("abc", State(view).Text);
            var caret = view.CaretRectangle();
            Assert.NotNull(caret);
            Assert.InRange(caret.Value.Top, 0, 300);
            window.Close();
        });
    }

    private static async Task<string?> ClipboardText(global::Avalonia.Input.Platform.IClipboard clipboard) =>
        await global::Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);

    private static Task SetClipboard(global::Avalonia.Input.Platform.IClipboard clipboard, string text) =>
        global::Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
}

public class TextInputLogicTests
{
    [Fact]
    public void APreeditReplacesTheSelectionWhenNothingIsComposed()
    {
        var edit = TextInputLogic.Preedit("ab", null, "hello", 1, 3, -1, -1);
        Assert.Equal(new PreeditEdit(1, 3, "ab", 3, 1, 3), edit);
    }

    [Fact]
    public void APreeditReplacesTheCompositionAndPlacesTheCursorInIt()
    {
        var edit = TextInputLogic.Preedit("abc", 1, "xyz12", 4, 4, 1, 3);
        Assert.Equal(new PreeditEdit(1, 3, "abc", 2, 1, 4), edit);
    }

    [Fact]
    public void AnEmptyPreeditEndsTheCompositionAndIsNothingWithoutOne()
    {
        Assert.Equal(new PreeditEdit(1, 3, string.Empty, 1, -1, -1), TextInputLogic.Preedit(null, null, "xyz12", 3, 3, 1, 3));
        Assert.Null(TextInputLogic.Preedit(string.Empty, null, "xyz12", 0, 5, -1, -1));
    }

    [Fact]
    public void TypedTextFollowsTheKindOfTheFieldAndDropsControlCharacters()
    {
        Assert.Equal("a b", TextInputLogic.TypedText("text", "a\r\nb"));
        Assert.Equal("a\nb", TextInputLogic.TypedText("textarea", "a\r\nb"));
        Assert.Equal("ab", TextInputLogic.TypedText("text", "a\tb\u001b"));
        Assert.Equal(string.Empty, TextInputLogic.TypedText("text", "\t"));
    }

    [Fact]
    public void ShortcutAndWordModifiersDependOnThePlatform()
    {
        Assert.True(TextInputLogic.IsShortcut(KeyModifiers.Control, mac: false));
        Assert.False(TextInputLogic.IsShortcut(KeyModifiers.Control, mac: true));
        Assert.True(TextInputLogic.IsShortcut(KeyModifiers.Meta, mac: true));
        Assert.True(TextInputLogic.IsWordMove(KeyModifiers.Alt, mac: true));
        Assert.False(TextInputLogic.IsWordMove(KeyModifiers.Alt, mac: false));
    }

    [Fact]
    public void ARectangleOfAPageIsConvertedForZoomScrollAndDpi()
    {
        // 10 pt at 96/72 px per pt, zoom 2, dpi 2 -> scale 4: 53.33 px; less 20 px of scroll, over dpi.
        var rect = TextInputLogic.ToControl(100, 10, 3, 1, 12, 96.0 / 72.0, 4, 20, 40, 2);
        Assert.Equal((((10 * 96.0 / 72.0 * 4) - 20) / 2), rect.X, 6);
        Assert.Equal((((100 + (3 * 96.0 / 72.0)) * 4) - 40) / 2, rect.Y, 6);
        Assert.Equal(12 * 96.0 / 72.0 * 4 / 2, rect.Height, 6);
        Assert.True(rect.Width >= 1);
    }

    [Fact]
    public void ARectangleOutOfViewScrollsJustFarEnough()
    {
        Assert.Equal((0d, 0d), TextInputLogic.ScrollToReveal(new Rect(10, 10, 1, 10), 100, 100, 1));
        Assert.Equal((0d, 20d), TextInputLogic.ScrollToReveal(new Rect(10, 100, 1, 20), 100, 100, 1));
        Assert.Equal((-5d * 2, 0d), TextInputLogic.ScrollToReveal(new Rect(-5, 10, 1, 10), 100, 100, 2));
    }

    [Fact]
    public void TheContentTypeFollowsTheInputType()
    {
        Assert.Equal(TextInputContentType.Email, TextInputLogic.ContentTypeFor("email"));
        Assert.Equal(TextInputContentType.Normal, TextInputLogic.ContentTypeFor("text"));
        Assert.True(TextInputLogic.IsSensitive("password"));
        Assert.False(TextInputLogic.IsSensitive("text"));
        Assert.True(TextInputLogic.IsTextField("textarea"));
        Assert.False(TextInputLogic.IsTextField("checkbox"));
    }
}
