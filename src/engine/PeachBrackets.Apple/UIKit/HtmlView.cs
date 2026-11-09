using CoreAnimation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using PeachBrackets.Accessibility;
using PeachBrackets.Apple.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using UIKit;
using UniformTypeIdentifiers;

namespace PeachBrackets.Apple;

/// <summary>
/// A view that shows an HTML document laid out by PeachBrackets as one scrolling, zoomable surface. It is a <see cref="UIScrollView"/>, so panning,
/// deceleration, rubber banding and pinch-to-zoom are the system's, and the page is painted into tiles that are composited by the graphics hardware.
/// It follows what the document asks for - the CSS <c>cursor</c> (with a pointing device), the <c>:hover</c> and <c>:active</c> rules,
/// <c>pointer-events</c>, <c>touch-action</c> and <c>user-select</c> - and turns taps, long presses and drags into the behaviour a reader expects,
/// exposing each as an event an application can hook.
/// </summary>
/// <remarks>
/// Set <see cref="Html"/> and the view loads it (off the main thread), lays it out for its own width and paints only the part in view. One CSS pixel is
/// one point, so a page looks as it does in a mobile browser. The document is laid out again when the view is resized or the appearance changes.
/// </remarks>
[Register("PeachBracketsHtmlView")]
public sealed class HtmlView : UIScrollView, IHtmlViewHost, IUIKeyInput, IUIGestureRecognizerDelegate, IUIPointerInteractionDelegate, IUIEditMenuInteractionDelegate
{
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    private readonly HtmlViewEngine _engine;
    private readonly PageView _page;
    private CGColor _pageBackground = UIColor.White.CGColor;
    private double _settledZoom = 1;
    private bool _selectingAfterLongPress;
    private UIEditMenuInteraction? _menu;
    private CGPoint _menuPoint;
    private bool _touchUsedLast = true;
    private bool _attached;
    private NSObject? _keyboard;
    private NSObject? _keyboardHide;
    private nfloat _baseBottomInset;
    private bool _keyboardUp;
    private CGPoint _hoverPoint;

    /// <summary>Creates a view with nothing in it.</summary>
    public HtmlView() : this(CGRect.Empty)
    {
    }

    /// <summary>Creates a view with nothing in it.</summary>
    /// <param name="frame">where the view is, in its superview</param>
    [Export("initWithFrame:")]
    public HtmlView(CGRect frame) : base(frame)
    {
        _engine = new HtmlViewEngine(this);
        _page = new PageView(_engine);
        AddSubview(_page);
        MinimumZoomScale = (nfloat)MinZoom;
        MaximumZoomScale = (nfloat)MaxZoom;
        Scrolled += (_, _) => OnScrolled();
        ZoomingEnded += (_, e) => OnZoomingEnded(e.AtScale);
        ViewForZoomingInScrollView = _ => _page;
        BackgroundColor = UIColor.White;
        AlwaysBounceVertical = true;
        ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never;

        var tap = new UITapGestureRecognizer(OnTap) { Delegate = this };
        var longPress = new UILongPressGestureRecognizer(OnLongPress) { MinimumPressDuration = 0.4, Delegate = this };
        var hover = new UIHoverGestureRecognizer(OnHover);
        var touchGate = new TouchGate(this);
        AddGestureRecognizer(tap);
        AddGestureRecognizer(longPress);
        AddGestureRecognizer(hover);
        AddGestureRecognizer(touchGate);
        AddInteraction(new UIPointerInteraction(this));
        // The edit menu interaction is iOS 16; before that a selection is copied with a hardware keyboard's Cmd+C.
        if (OperatingSystem.IsIOSVersionAtLeast(16))
        {
            _menu = new UIEditMenuInteraction(this);
            AddInteraction(_menu);
        }
    }

    /// <summary>The HTML to show. Setting it loads and lays out the document.</summary>
    public string? Html
    {
        get => _engine.Html;
        set
        {
            _engine.Html = value;
            if (_attached)
                _ = _engine.ReloadAsync();
        }
    }

    /// <summary>
    /// Whether the document may load images, style sheets and fonts from <c>http</c> and <c>https</c> addresses. Off by default, so a document cannot make
    /// the application call out; <c>data:</c> URIs and files are always read. Takes effect on the next load.
    /// </summary>
    public bool AllowNetworkAccess { get; set; }

    /// <summary>
    /// The address the document's relative references (style sheets, images, fonts) resolve against, unless it has a <c>&lt;base href&gt;</c> of its own
    /// (which does not reach the style sheets, as they load before the document is known). Set it when the HTML came from somewhere else, such as a page
    /// the host fetched itself. Takes effect on the next load.
    /// </summary>
    public Uri? DocumentBaseUri { get => _documentBaseUri; set => _documentBaseUri = value; }

    private volatile Uri? _documentBaseUri;

    /// <summary>
    /// Whether the document plays its CSS animations, transitions and animated images (GIF, WebP). Off by default. A property that moves or resizes boxes
    /// animates by laying the document out again each frame, so its cost follows the size of the document; the rest repaint only the tiles they touch. Nothing
    /// plays when the system asks for reduced motion, and the document's <c>prefers-reduced-motion</c> reports it. Takes effect on the next load.
    /// </summary>
    // Named as on the other views; it hides UIView's static switch for UIKit's own view animations, which this property has nothing to do with.
    public new bool AnimationsEnabled { get; set; }

    /// <summary>
    /// Raised, on the main thread, with the animation and transition events that happened since the last frame, in order. The animations themselves can be
    /// looked at and controlled through <c>Document.GetAnimations()</c>.
    /// </summary>
    public event EventHandler<HtmlAnimationEventsEventArgs>? AnimationEvents;

    /// <summary>
    /// Whether the page is composited and its effects (blur, shadow, filter, mask) run on the graphics hardware. On by default. Takes effect before the
    /// view is first shown.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>The colour behind the page.</summary>
    public CGColor PageBackground
    {
        get => _pageBackground;
        set
        {
            _pageBackground = value;
            BackgroundColor = new UIColor(value);
            _engine.Invalidate();
        }
    }

    /// <summary>
    /// The pointing devices the document's <c>hover</c> and <c>pointer</c> media queries report. <see langword="null"/> (the default) reports a touch
    /// screen.
    /// </summary>
    public InputCapabilities? Input { get; set; }

    /// <summary>The loaded document, or <see langword="null"/> before one has loaded. It is replaced when the view reloads.</summary>
    public HtmlDocument? Document => _engine.Document;

    /// <summary>How far the page is zoomed: 1 is one CSS pixel to one point.</summary>
    public double Zoom => ZoomScale;

    /// <summary>The text that is selected, or an empty string.</summary>
    public string SelectedText => _engine.SelectedText;

    /// <summary>Raised when a document has loaded and laid out.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised when a document could not be loaded.</summary>
    public event EventHandler<HtmlLoadFailedEventArgs>? DocumentFailed;

    /// <summary>Raised before a link is followed. Set <see cref="HtmlLinkActivatedEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<HtmlLinkActivatedEventArgs>? LinkActivated;

    /// <summary>Raised when a touch begins, after the document has said which gestures it allows there; the application may take gestures away.</summary>
    public event EventHandler<HtmlTouchStartedEventArgs>? TouchStarted;

    /// <summary>Raised when the page is tapped somewhere that is not a link.</summary>
    public event EventHandler<HtmlTappedEventArgs>? Tapped;

    /// <summary>Raised when the page is long-pressed, before the word under the finger is selected.</summary>
    public event EventHandler<HtmlTappedEventArgs>? LongPressed;

    /// <summary>Raised when a form is submitted (a submit button was tapped, or Return was pressed in a field), with the form as it would be sent. The view sends nothing itself.</summary>
    public event EventHandler<HtmlFormSubmittingEventArgs>? FormSubmitting;

    /// <summary>Raised instead of <see cref="FormSubmitting"/> when a submit was tried and a control's value does not satisfy its constraints; the first such control has the focus.</summary>
    public event EventHandler<HtmlFormInvalidEventArgs>? FormInvalid;

    /// <summary>Raised when the part of the page that has the keyboard focus changes (see <see cref="FocusedNode"/>).</summary>
    public event EventHandler? FocusChanged;

    /// <summary>The part of the page that has the keyboard focus (a link, a button, a form control, anything with a <c>tabindex</c>), or <see langword="null"/> if none has.</summary>
    public AccessibilityNode? FocusedNode => _engine.FocusedNode;

    /// <summary>Raised when the scroll position changes.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Raised when the zoom has changed and the page has been painted again at it.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Puts the selected text on the general pasteboard.</summary>
    public void CopySelection()
    {
        var text = _engine.IsEditing && _engine.FieldSelection() is { Length: > 0 } inField ? inField : SelectedText;
        if (text.Length > 0)
            UIPasteboard.General.String = text;
    }

    /// <summary>Selects all the text.</summary>
    public void SelectAll() => _engine.Document?.Selection.SelectAll();

    /// <summary>Scrolls so that the element with the given <c>id</c> is at the top.</summary>
    /// <param name="elementId">the element's <c>id</c></param>
    /// <returns><see langword="true"/> when there is such an element</returns>
    public bool ScrollToElement(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        if (_engine.ElementTop(elementId) is not { } top)
            return false;

        ScrollToContentY(top);
        return true;
    }

    /// <summary>Takes in a change the document made to itself (a restyle, a form value) and paints it.</summary>
    /// <param name="result">what the document reported</param>
    public void ApplyUpdate(PeachBrackets.Updates.UpdateResult result) => _engine.ApplyUpdate(result);

    // ---- the view's life ----

    /// <inheritdoc />
    public override void MovedToWindow()
    {
        base.MovedToWindow();
        if (Window is null)
        {
            _attached = false;
            _keyboard?.Dispose();
            _keyboardHide?.Dispose();
            _keyboard = _keyboardHide = null;
            _engine.Detach();
            return;
        }

        _attached = true;
        _keyboard?.Dispose();
        _keyboardHide?.Dispose();
        _keyboard = UIKeyboard.Notifications.ObserveWillChangeFrame((_, e) => KeyboardChanged(e.FrameEnd));
        _keyboardHide = UIKeyboard.Notifications.ObserveWillHide((_, _) => KeyboardChanged(CGRect.Empty));
        _engine.Attach();
    }

    /// <inheritdoc />
    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (_engine.NeedsReflow())
            _engine.QueueReflow();

        UpdateTiles();
    }

    /// <inheritdoc />
#pragma warning disable CA1422 // the replacement (UITraitChangeObservable) is iOS 17 and later; this runs on every supported version
    public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
    {
        base.TraitCollectionDidChange(previousTraitCollection);
        if (previousTraitCollection is null || previousTraitCollection.UserInterfaceStyle != TraitCollection.UserInterfaceStyle)
            _engine.QueueReflow();
        else if (previousTraitCollection.DisplayScale != TraitCollection.DisplayScale)
            _engine.Invalidate();
    }
#pragma warning restore CA1422

    private void OnScrolled()
    {
        UpdateTiles();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // A pinch scales the page's layers on the hardware while it lasts; when it ends the tiles are painted again at the new zoom.
    private void OnZoomingEnded(nfloat scale)
    {
        _settledZoom = scale;
        _engine.Invalidate();
        _engine.QueueImageUpgrade(_settledZoom);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // The part of the page in view, in CSS pixels, which are the points of the page view.
    private CGRect VisibleRect
    {
        get
        {
            var zoom = Math.Max(ZoomScale, 0.01);
            return new CGRect(ContentOffset.X / zoom, ContentOffset.Y / zoom, Bounds.Width / zoom, Bounds.Height / zoom);
        }
    }

    private void UpdateTiles()
    {
        if (_engine.Document is null)
            return;

        var visible = VisibleRect;
        _engine.SetViewport(visible);
        _engine.UpdateTiles(visible, _settledZoom);
    }

    internal void ScrollToContentY(double y)
    {
        var maxY = Math.Max(0, (_page.Frame.Height * ZoomScale) - Bounds.Height);
        SetContentOffset(new CGPoint(ContentOffset.X, Math.Clamp(y * ZoomScale, 0, maxY)), true);
    }

    // ---- gestures ----

    private bool Locate(CGPoint inPage, out int page, out double x, out double y) => _engine.TryLocate(inPage.X, inPage.Y, out page, out x, out y);

    private void OnTap(UITapGestureRecognizer recognizer)
    {
        if (recognizer.State != UIGestureRecognizerState.Ended || !Locate(recognizer.LocationInView(_page), out var page, out var x, out var y))
            return;

        _touchUsedLast = true;
        var document = _engine.Document!;
        var at = recognizer.LocationInView(_page);

        // The view takes the keyboard (Tab, Return, the arrows) as the first responder; no soft keyboard comes up unless a text field has the focus.
        if (!IsFirstResponder)
            BecomeFirstResponder();

        if (_engine.TapForm(page, x, y, new CGRect(at.X, at.Y, 1, 1)))
            return;

        // A tap on anything that is not a control takes the focus out of the field it was in.
        _engine.LoseFocus();

        if (_engine.LinkAt(page, x, y) is not { } link)
        {
            document.Selection.RemoveAllRanges();
            Tapped?.Invoke(this, new HtmlTappedEventArgs(page, x, y));
            return;
        }

        var args = new HtmlLinkActivatedEventArgs(link.Uri, link.Anchor);
        LinkActivated?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (link.Uri is { } uri)
        {
            if (PageLayout.IsLaunchable(uri) && NSUrl.FromString(uri) is { } url)
                UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
        }
        else if (_engine.LinkTargetTop(link) is { } top)
        {
            ScrollToContentY(top);
        }
    }

    private void OnLongPress(UILongPressGestureRecognizer recognizer)
    {
        var point = recognizer.LocationInView(_page);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                if (!Locate(point, out var page, out var x, out var y))
                    return;

                LongPressed?.Invoke(this, new HtmlTappedEventArgs(page, x, y));
                var document = _engine.Document!;
                if (document.GetInteractionAt(page, x, y).CanSelectText && document.Selection.SelectWordAt(page, x, y))
                {
                    _selectingAfterLongPress = true;
                    _menuPoint = recognizer.LocationInView(this);
                }

                break;
            case UIGestureRecognizerState.Changed when _selectingAfterLongPress:
                if (Locate(point, out var movedPage, out var movedX, out var movedY))
                    _engine.Document!.Selection.ExtendTo(movedPage, movedX, movedY);

                _menuPoint = recognizer.LocationInView(this);
                break;
            case UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled:
                if (_selectingAfterLongPress && SelectedText.Length > 0 && _menu is not null && OperatingSystem.IsIOSVersionAtLeast(16))
                    _menu.PresentEditMenu(UIEditMenuConfiguration.Create(null, _menuPoint));

                _selectingAfterLongPress = false;
                break;
        }
    }

    // A pointing device hovering (an iPad's trackpad or mouse): the :hover state follows it.
    private void OnHover(UIHoverGestureRecognizer recognizer)
    {
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed:
                _touchUsedLast = false;
                _hoverPoint = recognizer.LocationInView(_page);
                if (Locate(_hoverPoint, out var page, out var x, out var y))
                    _engine.RequestState(page, x, y, pressed: false);
                else
                    _engine.RequestClear();

                break;
            default:
                _engine.RequestClear();
                break;
        }
    }

    // What a pointing device shows over the page: the I-beam over text, nothing for cursor: none, the system's own pointer otherwise.
    [Export("pointerInteraction:styleForRegion:")]
    private UIPointerStyle? PointerStyle(UIPointerInteraction interaction, UIPointerRegion region)
    {
        if (_engine.Document is not { } document || !Locate(_hoverPoint, out var page, out var x, out var y))
            return null;

        return AppleCursors.ToKind(document.GetCursorAt(page, x, y).Shape) switch
        {
            AppleCursorKind.IBeam => UIPointerStyle.Create(UIPointerShape.CreateBeam(16, UIAxis.Vertical), UIAxis.Vertical),
            AppleCursorKind.Hidden => UIPointerStyle.CreateHiddenPointerStyle(),
            _ => null,
        };
    }

    // The edit menu after a long press: Copy and Select All.
    [Export("editMenuInteraction:menuForConfiguration:suggestedActions:")]
    private UIMenu? MenuFor(UIEditMenuInteraction interaction, UIEditMenuConfiguration configuration, UIMenuElement[] suggestedActions) =>
        UIMenu.Create(
        [
            UIAction.Create("Copy", null, null, _ => CopySelection()),
            UIAction.Create("Select All", null, null, _ => SelectAll()),
        ]);

    // ---- responder chain: the keyboard commands for a hardware keyboard ----

    /// <inheritdoc />
    public override bool CanBecomeFirstResponder => true;

    /// <inheritdoc />
    public override bool CanPerform(Selector action, NSObject? withSender) =>
        action.Name switch
        {
            "copy:" => SelectedText.Length > 0 || (_engine.IsEditing && _engine.FieldSelection().Length > 0),
            "cut:" => _engine.IsEditing && _engine.FieldSelection().Length > 0,
            "paste:" => _engine.IsEditing && UIPasteboard.General.HasStrings,
            "selectAll:" => true,
            _ => base.CanPerform(action, withSender),
        };

    /// <inheritdoc />
    public override void Copy(NSObject? sender) => CopySelection();

    /// <inheritdoc />
    public override void SelectAll(NSObject? sender)
    {
        if (!_engine.IsEditing || !_engine.SelectAllInField())
            SelectAll();
    }

    // The on-screen keyboard covers the bottom of the view: the page is given room under it (so it can scroll up out of the way), and the field being edited is brought into view.
    private void KeyboardChanged(CGRect frame)
    {
        var covered = frame.IsEmpty || Window is null ? 0 : (nfloat)Math.Max(0, Bounds.Bottom - ConvertRectFromView(frame, null).Y);
        if (!_keyboardUp)
            _baseBottomInset = ContentInset.Bottom;

        _keyboardUp = covered > 0;
        var inset = ContentInset;
        inset.Bottom = _keyboardUp ? covered : _baseBottomInset;
        ContentInset = inset;
        var indicators = VerticalScrollIndicatorInsets;
        indicators.Bottom = inset.Bottom;
        VerticalScrollIndicatorInsets = indicators;
        if (_keyboardUp && _engine.FocusedFrame is { } focused)
            ((IHtmlViewHost)this).Reveal(focused);
    }

    // ---- the keyboard: key input in a text field (UIKeyInput), and Tab, Return and the arrows from a hardware keyboard ----

    private readonly UIView _noKeyboard = new(new CGRect(0, 0, 0, 0));

    /// <summary>The keyboard shows only while a text field has the focus; otherwise the view takes hardware keys without one.</summary>
    public override UIView InputView => _engine.IsEditing ? null! : _noKeyboard;

    // What the keyboard is told about the field being edited: the keys to show, whether to hide what is typed, the label of Return, and whether to correct.
    private AppleInputTraits FieldTraits => AppleInputTraits.For(_engine.EditState?.Type ?? "text");

    /// <summary>The keyboard the field being edited asks for.</summary>
    [Export("keyboardType")]
    public UIKeyboardType KeyboardType => FieldTraits.Keyboard switch
    {
        AppleKeyboard.EmailAddress => UIKeyboardType.EmailAddress,
        AppleKeyboard.Url => UIKeyboardType.Url,
        AppleKeyboard.PhonePad => UIKeyboardType.PhonePad,
        AppleKeyboard.DecimalPad => UIKeyboardType.DecimalPad,
        _ => UIKeyboardType.Default,
    };

    /// <summary>Whether what is typed is hidden (a password field).</summary>
    [Export("isSecureTextEntry")]
    public bool SecureTextEntry => FieldTraits.Secure;

    /// <summary>The label of the Return key.</summary>
    [Export("returnKeyType")]
    public UIReturnKeyType ReturnKeyType => FieldTraits.ReturnKey switch
    {
        "search" => UIReturnKeyType.Search,
        "go" => UIReturnKeyType.Go,
        "done" => UIReturnKeyType.Done,
        _ => UIReturnKeyType.Default,
    };

    /// <summary>Whether the system corrects what is typed.</summary>
    [Export("autocorrectionType")]
    public UITextAutocorrectionType AutocorrectionType => FieldTraits.Autocorrect ? UITextAutocorrectionType.Default : UITextAutocorrectionType.No;

    /// <summary>Whether the system capitalizes what is typed.</summary>
    [Export("autocapitalizationType")]
    public UITextAutocapitalizationType AutocapitalizationType => FieldTraits.Autocorrect ? UITextAutocapitalizationType.Sentences : UITextAutocapitalizationType.None;

    /// <inheritdoc />
    public bool HasText => _engine.CurrentFieldText().Length > 0;

    /// <inheritdoc />
    public void InsertText(string text)
    {
        if (text == "\n")
            _engine.EnterInField();
        else
            _engine.InsertText(text);
    }

    /// <inheritdoc />
    public void DeleteBackward() => _engine.Delete(backward: true, word: false);

    /// <inheritdoc />
    public override UIKeyCommand[] KeyCommands => _engine.IsEditing ? _editingKeyCommands ??= BuildKeyCommands(editing: true) : _keyCommands ??= BuildKeyCommands(editing: false);

    private UIKeyCommand[]? _keyCommands;
    private UIKeyCommand[]? _editingKeyCommands;

    // While a field is being edited, Space and Return are left to the text input (insertText), which also carries the letters typed just before them: a key command
    // is delivered at once, ahead of text still on its way, so a space typed after "ab" would land in front of them. The keys UIKeyInput has no word for (the arrows)
    // stay commands, and run after what is already queued.
    private UIKeyCommand[] BuildKeyCommands(bool editing)
    {
        UIKeyCommand Make(string input, UIKeyModifierFlags flags, string selector) =>
            UIKeyCommand.Create(new NSString(input), flags, new Selector(selector));

        var commands = new List<UIKeyCommand>
        {
            Make("\t", 0, "peachTab:"), Make("\t", UIKeyModifierFlags.Shift, "peachBackTab:"),
            Make(UIKeyCommand.LeftArrow, 0, "peachLeft:"), Make(UIKeyCommand.RightArrow, 0, "peachRight:"),
            Make(UIKeyCommand.UpArrow, 0, "peachUp:"), Make(UIKeyCommand.DownArrow, 0, "peachDown:"),
            Make(UIKeyCommand.LeftArrow, UIKeyModifierFlags.Shift, "peachLeftSelect:"), Make(UIKeyCommand.RightArrow, UIKeyModifierFlags.Shift, "peachRightSelect:"),
            Make(UIKeyCommand.LeftArrow, UIKeyModifierFlags.Alternate, "peachWordLeft:"), Make(UIKeyCommand.RightArrow, UIKeyModifierFlags.Alternate, "peachWordRight:"),
        };
        if (!editing)
        {
            commands.Add(Make("\r", 0, "peachReturn:"));
            commands.Add(Make(" ", 0, "peachSpace:"));
        }

        foreach (var command in commands)
            command.WantsPriorityOverSystemBehavior = true;

        return [.. commands];
    }

    // Runs after the text already queued by the input system, so an arrow pressed right after typing acts on what was typed.
    private static void AfterQueuedText(Action action) => NSRunLoop.Main.BeginInvokeOnMainThread(() => NSRunLoop.Main.BeginInvokeOnMainThread(action));

    [Export("peachTab:")] private void KeyTab(UIKeyCommand c) => _engine.StepFocus(true);

    [Export("peachBackTab:")] private void KeyBackTab(UIKeyCommand c) => _engine.StepFocus(false);

    [Export("peachReturn:")]
    private void KeyReturn(UIKeyCommand c)
    {
        if (_engine.IsEditing)
        {
            _engine.EnterInField();
            return;
        }

        var (_, link) = _engine.ActivateFocused(space: false);
        if (link is not null)
            ActivateNode(link);
    }

    [Export("peachSpace:")]
    private void KeySpace(UIKeyCommand c)
    {
        if (_engine.IsEditing)
            _engine.InsertText(" ");
        else
            _engine.ActivateFocused(space: true);
    }

    [Export("peachLeft:")] private void KeyLeft(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.Left, false));

    [Export("peachRight:")] private void KeyRight(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.Right, false));

    [Export("peachUp:")] private void KeyUp(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveLine("Up", false));

    [Export("peachDown:")] private void KeyDown(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveLine("Down", false));

    [Export("peachLeftSelect:")] private void KeyLeftSelect(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.Left, true));

    [Export("peachRightSelect:")] private void KeyRightSelect(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.Right, true));

    [Export("peachWordLeft:")] private void KeyWordLeft(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.WordLeft, false));

    [Export("peachWordRight:")] private void KeyWordRight(UIKeyCommand c) => AfterQueuedText(() => _engine.MoveCaret(EditMove.WordRight, false));

    /// <inheritdoc />
    public override void Cut(NSObject? sender)
    {
        if (_engine.FieldSelection().Length == 0)
            return;

        CopySelection();
        _engine.Delete(backward: true, word: false);
    }

    /// <inheritdoc />
    public override void Paste(NSObject? sender)
    {
        if (_engine.IsEditing && UIPasteboard.General.String is { } text)
            _engine.InsertText(CaretMoves.ForField(_engine.EditState?.Type ?? "text", text));
    }

    // ---- IHtmlViewHost ----

    double IHtmlViewHost.BackingScale => Math.Max(TraitCollection.DisplayScale, 1);

    double IHtmlViewHost.ViewportWidth => Bounds.Width;

    double IHtmlViewHost.ViewportHeight => Bounds.Height;

    bool IHtmlViewHost.IsDark => TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;

    bool IHtmlViewHost.AllowNetworkAccess => AllowNetworkAccess;

    Uri? IHtmlViewHost.DocumentBaseUri => _documentBaseUri;

    bool IHtmlViewHost.AnimationsEnabled => AnimationsEnabled;

    bool IHtmlViewHost.SystemAllowsMotion => !UIAccessibility.IsReduceMotionEnabled;

    IDisposable IHtmlViewHost.StartFrameClock(Action<TimeSpan> tick)
    {
        // The display link fires with the screen's refresh (and ProMotion's variable one), in the common modes so a scroll does not pause it.
        var link = CADisplayLink.Create(() => tick(FrameClock.Now));
        link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        return new DisplayLinkClock(link);
    }

    void IHtmlViewHost.AnimationEvents(IReadOnlyList<PeachBrackets.Updates.AnimationEventInfo> events) => AnimationEvents?.Invoke(this, new HtmlAnimationEventsEventArgs(events));

    // A zoomed page keeps the size the zoom gave it; the next settle takes the new content size.
    void IHtmlViewHost.ContentResized()
    {
        if (Math.Abs(ZoomScale - 1) > 0.001)
            return;

        _page.Frame = new CGRect(0, 0, Math.Max(_engine.ContentWidth, Bounds.Width), _engine.ContentHeight);
        ContentSize = _page.Frame.Size;
    }

    private sealed class DisplayLinkClock(CADisplayLink link) : IDisposable
    {
        public void Dispose() => link.Invalidate();
    }

    InputCapabilities? IHtmlViewHost.Input => Input;

    bool IHtmlViewHost.UseGpu => UseGpu;

    CGColor IHtmlViewHost.PageBackground => _pageBackground;

    InputCapabilities IHtmlViewHost.DetectInput() => AppleInput.Capabilities(mouse: false, stylus: false, touch: true, touchUsedLast: _touchUsedLast);

    void IHtmlViewHost.DocumentAdopted()
    {
        ZoomScale = 1;
        _settledZoom = 1;
        _page.Frame = new CGRect(0, 0, Math.Max(_engine.ContentWidth, Bounds.Width), _engine.ContentHeight);
        ContentSize = _page.Frame.Size;
        UpdateTiles();
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
    }

    void IHtmlViewHost.ContentInvalidated()
    {
        if (_engine.Document is not null && _attached)
            UpdateTiles();
    }

    void IHtmlViewHost.AccessibilityRefreshed(IReadOnlyList<AccessibilityEntry> entries, IReadOnlyList<string> announcements)
    {
        _page.IsAccessibilityElement = false;
        _page.Parts = entries.Select(e => (NSObject)new AccessibilityElement(this, _page, e.Node, e.Frame)).ToArray();
        UIAccessibility.PostNotification(UIAccessibilityPostNotification.LayoutChanged, null);
        foreach (var text in announcements)
            UIAccessibility.PostNotification(UIAccessibilityPostNotification.Announcement, new NSString(text));
    }

    // What a link does when VoiceOver activates it: the same as a tap.
    internal void ActivateNode(AccessibilityNode node)
    {
        if (node.Uri is not { } uri)
            return;

        var (target, destination) = AppleAccessibility.LinkTarget(uri);
        var args = new HtmlLinkActivatedEventArgs(target, destination);
        LinkActivated?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (destination is not null)
            ScrollToElement(destination);
        else if (PageLayout.IsLaunchable(uri) && NSUrl.FromString(uri) is { } url)
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
    }

    /// <summary>One part of the page for VoiceOver: a heading, a link, a control or a piece of text.</summary>
    private sealed class AccessibilityElement : UIAccessibilityElement
    {
        private readonly WeakReference<HtmlView> _ownerRef;

        private HtmlView _owner => _ownerRef.TryGetTarget(out var owner) ? owner : null!;
        private readonly AccessibilityNode _node;

        public AccessibilityElement(HtmlView owner, UIView container, AccessibilityNode node, CGRect frame) : base(container)
        {
            _ownerRef = new WeakReference<HtmlView>(owner);
            _node = node;
            AccessibilityFrameInContainerSpace = frame;
            AccessibilityLabel = node.ReadingText;
            var traits = AppleAccessibility.Traits(node.Role, node.Selected == true, node.Disabled);
            var result = UIAccessibilityTrait.None;
            if (traits.HasFlag(AppleTraits.Button)) result |= UIAccessibilityTrait.Button;
            if (traits.HasFlag(AppleTraits.Link)) result |= UIAccessibilityTrait.Link;
            if (traits.HasFlag(AppleTraits.Header)) result |= UIAccessibilityTrait.Header;
            if (traits.HasFlag(AppleTraits.Image)) result |= UIAccessibilityTrait.Image;
            if (traits.HasFlag(AppleTraits.StaticText)) result |= UIAccessibilityTrait.StaticText;
            if (traits.HasFlag(AppleTraits.Adjustable)) result |= UIAccessibilityTrait.Adjustable;
            if (traits.HasFlag(AppleTraits.Selected)) result |= UIAccessibilityTrait.Selected;
            if (traits.HasFlag(AppleTraits.NotEnabled)) result |= UIAccessibilityTrait.NotEnabled;
            if (traits.HasFlag(AppleTraits.SearchField)) result |= UIAccessibilityTrait.SearchField;
            AccessibilityTraits = (ulong)result;
            if (node.Value is { } value)
                AccessibilityValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        [Export("accessibilityActivate")]
        public bool Activate()
        {
            if (!AppleAccessibility.IsActivatable(_node.Role))
                return false;

            _owner.ActivateNode(_node);
            return true;
        }
    }

    void IHtmlViewHost.FormSubmitting(FormSubmission submission) => FormSubmitting?.Invoke(this, new HtmlFormSubmittingEventArgs(submission));

    void IHtmlViewHost.FormInvalid(IReadOnlyList<ValidationProblem> problems) => FormInvalid?.Invoke(this, new HtmlFormInvalidEventArgs(problems));

    void IHtmlViewHost.FocusChanged() => FocusChanged?.Invoke(this, EventArgs.Empty);

    void IHtmlViewHost.Reveal(CGRect frame)
    {
        var zoom = ZoomScale;
        ScrollRectToVisible(new CGRect(frame.X * zoom, frame.Y * zoom, frame.Width * zoom, frame.Height * zoom), true);
    }

    void IHtmlViewHost.ScrollTop(double y)
    {
        var maxY = Math.Max(0, (_page.Frame.Height * ZoomScale) - Bounds.Height);
        SetContentOffset(new CGPoint(ContentOffset.X, Math.Clamp(y * ZoomScale, 0, maxY)), false);
    }

    // The keyboard comes and goes with the text field's focus: the view is the first responder while one has it.
    void IHtmlViewHost.EditingChanged(bool editing)
    {
        if (editing)
        {
            ReloadInputViews();
            BecomeFirstResponder();
        }
        else
        {
            // The view stays the first responder (Tab, Return and the arrows still reach it); the keyboard goes because its input view is empty again.
            ReloadInputViews();
        }
    }

    // A drop-down's options as an action sheet (a popover on an iPad).
    void IHtmlViewHost.PickOption(IReadOnlyList<SelectOption> options, CGRect anchor, Action<int> chosen)
    {
        if (TopController() is not { } controller)
            return;

        var sheet = UIAlertController.Create(null, null, UIAlertControllerStyle.ActionSheet);
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var action = UIAlertAction.Create(options[i].Label, UIAlertActionStyle.Default, _ => chosen(index));
            action.Enabled = !options[i].Disabled;
            sheet.AddAction(action);
        }

        sheet.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = _page;
            popover.SourceRect = anchor;
        }

        controller.PresentViewController(sheet, true, null);
    }

    private UIViewController? TopController()
    {
        var controller = Window?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
            controller = presented;

        return controller;
    }

    // A date, a time or both in a sheet over the page, with Cancel and Done; a colour in the system colour picker.
    void IHtmlViewHost.PickValue(string kind, string current, CGRect anchor, Action<string> chosen)
    {
        if (TopController() is not { } controller)
            return;

        if (kind == "color")
        {
            var (r, g, b) = PickerValues.ParseColor(current);
            var colors = new UIColorPickerViewController { SupportsAlpha = false, SelectedColor = UIColor.FromRGB(r / 255f, g / 255f, b / 255f) };
            colors.Delegate = new ColorPickerDelegate(chosen);
            controller.PresentViewController(colors, true, null);
            return;
        }

        var hasDate = PickerValues.TryParseDate(current.Split('T')[0], out var day);
        var time = current.Contains('T') ? current[(current.IndexOf('T') + 1)..] : current;
        var hasTime = PickerValues.TryParseTime(time, out var clock);
        var initial = (kind == "time" ? DateTime.Today : hasDate ? day : DateTime.Today).Add(hasTime ? clock : TimeSpan.Zero);
        var picker = new UIDatePicker
        {
            Mode = kind switch { "time" => UIDatePickerMode.Time, "datetime" => UIDatePickerMode.DateAndTime, _ => UIDatePickerMode.Date },
            PreferredDatePickerStyle = kind == "time" ? UIDatePickerStyle.Wheels : UIDatePickerStyle.Inline,
            Date = (NSDate)DateTime.SpecifyKind(initial, DateTimeKind.Local),
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        var content = new UIViewController { Title = kind switch { "time" => "Time", "datetime" => "Date and time", _ => "Date" } };
        content.View!.BackgroundColor = UIColor.SystemBackground;
        content.View.AddSubview(picker);
        NSLayoutConstraint.ActivateConstraints(
        [
            picker.CenterXAnchor.ConstraintEqualTo(content.View.CenterXAnchor),
            picker.TopAnchor.ConstraintEqualTo(content.View.SafeAreaLayoutGuide.TopAnchor, 8),
        ]);
        var navigation = new UINavigationController(content) { ModalPresentationStyle = UIModalPresentationStyle.PageSheet };
        content.NavigationItem.LeftBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Cancel, (_, _) => navigation.DismissViewController(true, null));
        content.NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Done, (_, _) =>
        {
            var picked = ((DateTime)picker.Date).ToLocalTime();
            navigation.DismissViewController(true, null);
            chosen(kind switch
            {
                "time" => PickerValues.FormatTime(picked.TimeOfDay),
                "datetime" => PickerValues.FormatDateTime(current, picked.Date, picked.TimeOfDay),
                _ => PickerValues.FormatDate(picked),
            });
        });
        if (navigation.SheetPresentationController is { } sheet)
            sheet.Detents = [UISheetPresentationControllerDetent.CreateMediumDetent(), UISheetPresentationControllerDetent.CreateLargeDetent()];

        controller.PresentViewController(navigation, true, null);
    }

    private sealed class ColorPickerDelegate(Action<string> chosen) : UIColorPickerViewControllerDelegate
    {
        public override void DidSelectColor(UIColorPickerViewController viewController, UIColor color, bool continuously) => Report(color);

        public override void DidFinish(UIColorPickerViewController viewController) => Report(viewController.SelectedColor);

        private void Report(UIColor color)
        {
            color.GetRGBA(out var r, out var g, out var b, out _);
            chosen(PickerValues.FormatColor((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));
        }
    }

    // The system's document picker, limited to the kinds the control's accept attribute names; the chosen files are read at once.
    void IHtmlViewHost.PickFiles(bool multiple, IReadOnlyList<string> mimeTypes, IReadOnlyList<string> extensions, Action<IReadOnlyList<ChosenFile>> chosen)
    {
        if (TopController() is not { } controller)
            return;

        var types = new List<UTType>();
        foreach (var mime in mimeTypes)
        {
            if (mime == "image/*") types.Add(UTTypes.Image);
            else if (mime == "audio/*") types.Add(UTTypes.Audio);
            else if (mime == "video/*") types.Add(UTTypes.Movie);
            else if (!mime.EndsWith("/*", StringComparison.Ordinal) && UTType.CreateFromMimeType(mime) is { } type) types.Add(type);
        }

        foreach (var extension in extensions)
        {
            if (UTType.CreateFromExtension(extension.TrimStart('.')) is { } type)
                types.Add(type);
        }

        if (types.Count == 0)
            types.Add(UTTypes.Item);

        var picker = new UIDocumentPickerViewController(types.ToArray(), true) { AllowsMultipleSelection = multiple };
        picker.Delegate = new FilesPickerDelegate(chosen);
        controller.PresentViewController(picker, true, null);
    }

    private sealed class FilesPickerDelegate(Action<IReadOnlyList<ChosenFile>> chosen) : UIDocumentPickerDelegate
    {
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            var files = new List<ChosenFile>();
            foreach (var url in urls)
            {
                var scoped = url.StartAccessingSecurityScopedResource();
                try
                {
                    if (url.Path is not { } path || !File.Exists(path))
                        continue;

                    var mime = UTType.CreateFromExtension(Path.GetExtension(path).TrimStart('.'))?.PreferredMimeType ?? string.Empty;
                    files.Add(new ChosenFile(Path.GetFileName(path), mime, File.ReadAllBytes(path)));
                }
                finally
                {
                    if (scoped)
                        url.StopAccessingSecurityScopedResource();
                }
            }

            chosen(files);
        }
    }

    void IHtmlViewHost.LoadFailed(Exception exception) => DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));

    /// <summary>The view the pages sit in, zoomed by the scroll view. Its layers are the tiles.</summary>
    private sealed class PageView : UIView
    {
        /// <summary>The parts of the page VoiceOver stops on, which are what this view is a container of.</summary>
        public NSObject[] Parts { get; set; } = [];

        [Export("accessibilityElements")]
        public NSObject[] GetAccessibilityElements() => Parts;

        public PageView(HtmlViewEngine engine) : base(CGRect.Empty)
        {
            Layer.AddSublayer(engine.ContentLayer);
            Layer.AddSublayer(engine.OverlayLayer);
        }
    }

    /// <summary>
    /// Sees a touch begin before the scroll view decides what it is, asks the document which gestures it allows there (<c>touch-action</c>), lets the
    /// application take some away, and gives the scroll view what is left; it also sets the <c>:active</c> state while the finger is down.
    /// </summary>
    private sealed class TouchGate(HtmlView view) : UIGestureRecognizer
    {
        // Weak: the view owns its gesture recognizers, natively.
        private readonly WeakReference<HtmlView> _view = new(view);

        private HtmlView? Owner => _view.TryGetTarget(out var target) ? target : null;

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            base.TouchesBegan(touches, evt);
            if (touches.AnyObject is UITouch touch)
                if (Owner is { } owner) owner.BeginTouch(touch.LocationInView(owner._page), touch.Type);
        }

        // It watches the touch from first contact to last and is never recognized, so it never takes a gesture from the others.
        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            base.TouchesMoved(touches, evt);
            if (touches.AnyObject is UITouch touch)
                if (Owner is { } owner) owner.MoveTouch(touch.LocationInView(owner._page));
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            Owner?.EndTouch();
            State = UIGestureRecognizerState.Failed;
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            Owner?.EndTouch();
            State = UIGestureRecognizerState.Failed;
        }
    }

    private void BeginTouch(CGPoint inPage, UITouchType type)
    {
        _touchUsedLast = type != UITouchType.IndirectPointer;
        if (!Locate(inPage, out var page, out var x, out var y))
            return;

        var args = new HtmlTouchStartedEventArgs(page, x, y, _engine.Document!.GetInteractionAt(page, x, y));
        TouchStarted?.Invoke(this, args);
        var allowed = args.Allowed;
        ScrollEnabled = allowed.HasFlag(TouchActions.PanUp) || allowed.HasFlag(TouchActions.PanDown) || allowed.HasFlag(TouchActions.PanLeft)
            || allowed.HasFlag(TouchActions.PanRight);
        PinchGestureRecognizer!.Enabled = allowed.HasFlag(TouchActions.PinchZoom);
        _engine.RequestState(page, x, y, pressed: true);

        // A press on a range control or in a text field: moving on drags the thumb or extends the selection, and does not scroll the page.
        _touchDrag = _engine.BeginDrag(page, x, y);
        if (_touchDrag != HtmlViewEngine.DragKind.None)
            ScrollEnabled = false;
    }

    private HtmlViewEngine.DragKind _touchDrag;

    private void MoveTouch(CGPoint inPage)
    {
        if (_touchDrag != HtmlViewEngine.DragKind.None && Locate(inPage, out var page, out var x, out var y))
            _engine.Drag(page, x, y);
    }

    private void EndTouch()
    {
        _touchDrag = HtmlViewEngine.DragKind.None;
        _engine.EndDrag();
        ScrollEnabled = true;
        PinchGestureRecognizer!.Enabled = true;
        _engine.RequestClear();
    }
}
