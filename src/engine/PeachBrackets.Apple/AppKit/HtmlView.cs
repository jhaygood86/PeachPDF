using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using PeachBrackets.Accessibility;
using PeachBrackets.Apple.Logic;
using PeachBrackets.Updates;
using UniformTypeIdentifiers;
using PeachDrawing.Core;

namespace PeachBrackets.Apple;

/// <summary>
/// A view that shows an HTML document laid out by PeachBrackets as one scrolling, zoomable surface. It is an <see cref="NSScrollView"/>, so scrollbars,
/// momentum, rubber banding and pinch-to-zoom are the system's, and the page is painted into tiles that are composited by the graphics hardware.
/// It follows what the document asks for - the CSS <c>cursor</c>, the <c>:hover</c> and <c>:active</c> rules, <c>pointer-events</c> and
/// <c>user-select</c> - and turns clicks, drags and key commands into the behaviour a reader expects.
/// </summary>
/// <remarks>
/// Set <see cref="Html"/> and the view loads it (off the main thread), lays it out for its own width and paints only the part in view. One CSS pixel is
/// one point, so a page looks as it does in a browser. The document is laid out again when the view is resized or the appearance changes.
/// </remarks>
[Register("PeachBracketsHtmlView")]
public sealed class HtmlView : NSScrollView, IHtmlViewHost
{
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    private readonly HtmlViewEngine _engine;
    private readonly PageView _content;
    private CGColor _pageBackground = NSColor.White.CGColor;
    private NSObject? _scrollObserver;
    private NSObject? _magnifyObserver;
    private NSObject? _backingObserver;
    private NSObject? _scrollerStyleObserver;
    private double _settledZoom = 1;

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
        _content = new PageView(this, _engine);
        HasVerticalScroller = true;
        HasHorizontalScroller = true;
        AutohidesScrollers = true;
        // Overlay scrollers take no room, so the width the document is laid out for is the width the view has whether or not it scrolls; a legacy scroller
        // that appeared once the document proved tall made the visible width narrower than the layout and brought a horizontal scroller with it.
        ScrollerStyle = NSScrollerStyle.Overlay;
        AllowsMagnification = true;
        MinMagnification = (nfloat)MinZoom;
        MaxMagnification = (nfloat)MaxZoom;
        DrawsBackground = false;
        ContentView.PostsBoundsChangedNotifications = true;
        DocumentView = _content;
    }

    /// <summary>The HTML to show. Setting it loads and lays out the document.</summary>
    public string? Html
    {
        get => _engine.Html;
        set
        {
            _engine.Html = value;
            if (Window is not null)
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
    public bool AnimationsEnabled { get; set; }

    /// <summary>
    /// Raised, on the main thread, with the animation and transition events that happened since the last frame, in order. The animations themselves can be
    /// looked at and controlled through <c>Document.GetAnimations()</c>.
    /// </summary>
    public event EventHandler<HtmlAnimationEventsEventArgs>? AnimationEvents;

    /// <summary>
    /// Whether the page is composited and its effects (blur, shadow, filter, mask) run on the graphics hardware. On by default; turn it off on a machine
    /// without a working Metal driver. Takes effect before the view is first shown.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>The colour behind the page.</summary>
    public CGColor PageBackground
    {
        get => _pageBackground;
        set
        {
            _pageBackground = value;
            _engine.Invalidate();
        }
    }

    /// <summary>
    /// The pointing devices the document's <c>hover</c> and <c>pointer</c> media queries report. <see langword="null"/> (the default) reports a mouse or
    /// trackpad.
    /// </summary>
    public InputCapabilities? Input { get; set; }

    /// <summary>The loaded document, or <see langword="null"/> before one has loaded. It is replaced when the view reloads.</summary>
    public HtmlDocument? Document => _engine.Document;

    /// <summary>How far the page is zoomed: 1 is one CSS pixel to one point.</summary>
    public double Zoom => Magnification;

    /// <summary>The text that is selected, or an empty string.</summary>
    public string SelectedText => _engine.SelectedText;

    /// <summary>Raised when a document has loaded and laid out.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised when a document could not be loaded.</summary>
    public event EventHandler<HtmlLoadFailedEventArgs>? DocumentFailed;

    /// <summary>Raised before a link is followed. Set <see cref="HtmlLinkActivatedEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<HtmlLinkActivatedEventArgs>? LinkActivated;

    /// <summary>Raised when the page is clicked somewhere that is not a link.</summary>
    public event EventHandler<HtmlTappedEventArgs>? Clicked;

    /// <summary>Raised when a form is submitted (a submit button was clicked, or Enter was pressed in a field), with the form as it would be sent. The view sends nothing itself.</summary>
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
        if (text.Length == 0)
            return;

        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.WriteObjects([new NSString(text)]);
    }

    /// <summary>Selects all the text.</summary>
    public void SelectAll()
    {
        _engine.Document?.Selection.SelectAll();
    }

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
    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();
        if (Window is null)
        {
            RemoveObservers();
            _engine.Detach();
            return;
        }

        RemoveObservers();
        _scrollObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSView.BoundsChangedNotification, _ => OnScrolled(), ContentView);
        _magnifyObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSScrollView.DidEndLiveMagnifyNotification, _ => OnMagnified(), this);
        _backingObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.DidChangeBackingPropertiesNotification, _ => _engine.Invalidate(), Window);
        // The system puts its preferred scroller style back when the window is joined and whenever a mouse is attached or removed.
        ScrollerStyle = NSScrollerStyle.Overlay;
        _scrollerStyleObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSScroller.PreferredStyleChangedNotification, _ => ScrollerStyle = NSScrollerStyle.Overlay);
        _engine.Attach();
    }

    private void RemoveObservers()
    {
        if (_scrollObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_scrollObserver);

        if (_magnifyObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_magnifyObserver);

        if (_backingObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_backingObserver);

        if (_scrollerStyleObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_scrollerStyleObserver);

        if (_colorObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_colorObserver);

        if (_colorCloseObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_colorCloseObserver);

        _scrollObserver = _magnifyObserver = _backingObserver = _colorObserver = _colorCloseObserver = null;
    }

    /// <inheritdoc />
    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        _engine.QueueReflow();
    }

    /// <inheritdoc />
    public override void ResizeSubviewsWithOldSize(CGSize oldSize)
    {
        base.ResizeSubviewsWithOldSize(oldSize);
        if (_engine.NeedsReflow())
            _engine.QueueReflow();
    }

    private void OnScrolled()
    {
        UpdateTiles();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // A pinch scales the layers on the hardware while it lasts; when it ends the tiles are painted again at the new zoom.
    private void OnMagnified()
    {
        _settledZoom = Magnification;
        _engine.Invalidate();
        _engine.QueueImageUpgrade(_settledZoom);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTiles()
    {
        _engine.SetViewport(DocumentVisibleRect);
        _engine.UpdateTiles(DocumentVisibleRect, _settledZoom);
    }

    internal void ScrollToContentY(double y)
    {
        var maxY = Math.Max(0, _content.Frame.Height - DocumentVisibleRect.Height);
        ContentView.ScrollToPoint(new CGPoint(DocumentVisibleRect.X, Math.Clamp(y, 0, maxY)));
        ReflectScrolledClipView(ContentView);
    }

    internal void Raise(HtmlTappedEventArgs args) => Clicked?.Invoke(this, args);

    internal bool RaiseLink(HtmlLinkActivatedEventArgs args)
    {
        LinkActivated?.Invoke(this, args);
        return args.Cancel;
    }

    // ---- IHtmlViewHost ----

    double IHtmlViewHost.BackingScale => Window?.BackingScaleFactor ?? NSScreen.MainScreen?.BackingScaleFactor ?? 2;

    double IHtmlViewHost.ViewportWidth => ContentSize.Width / Math.Max(Magnification, 0.01);

    double IHtmlViewHost.ViewportHeight => ContentSize.Height / Math.Max(Magnification, 0.01);

    bool IHtmlViewHost.IsDark =>
        EffectiveAppearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]) == NSAppearance.NameDarkAqua;

    bool IHtmlViewHost.AllowNetworkAccess => AllowNetworkAccess;

    Uri? IHtmlViewHost.DocumentBaseUri => _documentBaseUri;

    bool IHtmlViewHost.AnimationsEnabled => AnimationsEnabled;

    bool IHtmlViewHost.SystemAllowsMotion => !NSWorkspace.SharedWorkspace.AccessibilityDisplayShouldReduceMotion;

    // The display's refresh is not exposed to a Mac view before macOS 14, so a timer at the usual rate in the common modes stands in (it keeps running while a
    // scroll or a menu tracks); the time of a tick is the clock's own, so a late tick is a longer step and not a slower animation.
    IDisposable IHtmlViewHost.StartFrameClock(Action<TimeSpan> tick)
    {
        var timer = NSTimer.CreateRepeatingTimer(1.0 / 60, _ => tick(FrameClock.Now));
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
        return new TimerClock(timer);
    }

    void IHtmlViewHost.AnimationEvents(IReadOnlyList<PeachBrackets.Updates.AnimationEventInfo> events) => AnimationEvents?.Invoke(this, new HtmlAnimationEventsEventArgs(events));

    void IHtmlViewHost.ContentResized() => _content.SetFrameSize(new CGSize(Math.Max(_engine.ContentWidth, ContentSize.Width / Math.Max(Magnification, 0.01)), _engine.ContentHeight));

    private sealed class TimerClock(NSTimer timer) : IDisposable
    {
        public void Dispose() => timer.Invalidate();
    }

    InputCapabilities? IHtmlViewHost.Input => Input;

    bool IHtmlViewHost.UseGpu => UseGpu;

    CGColor IHtmlViewHost.PageBackground => _pageBackground;

    InputCapabilities IHtmlViewHost.DetectInput() => AppleInput.Capabilities(mouse: true, stylus: false, touch: false);

    void IHtmlViewHost.DocumentAdopted()
    {
        _content.SetFrameSize(new CGSize(Math.Max(_engine.ContentWidth, ContentSize.Width / Math.Max(Magnification, 0.01)), _engine.ContentHeight));
        UpdateTiles();
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
    }

    void IHtmlViewHost.ContentInvalidated()
    {
        if (_engine.Document is not null && Window is not null)
            UpdateTiles();
    }

    void IHtmlViewHost.AccessibilityRefreshed(IReadOnlyList<AccessibilityEntry> entries, IReadOnlyList<string> announcements)
    {
        _content.SetAccessibilityElements(entries.Select(e => (NSObject)new PartElement(this, _content, e.Node, e.Frame)).ToArray());
        foreach (var text in announcements)
            NSAccessibility.PostNotification(this, new NSString("AXAnnouncementRequested"),
                NSDictionary.FromObjectsAndKeys([new NSString(text), NSNumber.FromInt32(50)], [new NSString("AXAnnouncementKey"), new NSString("AXPriority")]));
    }

    // What a link does when VoiceOver activates it: the same as a click.
    internal void ActivateNode(PeachBrackets.Accessibility.AccessibilityNode node)
    {
        if (node.Uri is not { } uri)
            return;

        var (target, destination) = AppleAccessibility.LinkTarget(uri);
        if (RaiseLink(new HtmlLinkActivatedEventArgs(target, destination)))
            return;

        if (destination is not null)
            ScrollToElement(destination);
        else if (PageLayout.IsLaunchable(uri) && NSUrl.FromString(uri) is { } url)
            NSWorkspace.SharedWorkspace.OpenUrl(url);
    }

    void IHtmlViewHost.FormSubmitting(FormSubmission submission) => FormSubmitting?.Invoke(this, new HtmlFormSubmittingEventArgs(submission));

    void IHtmlViewHost.FormInvalid(IReadOnlyList<ValidationProblem> problems) => FormInvalid?.Invoke(this, new HtmlFormInvalidEventArgs(problems));

    void IHtmlViewHost.FocusChanged() => FocusChanged?.Invoke(this, EventArgs.Empty);

    void IHtmlViewHost.Reveal(CGRect frame) => _content.ScrollRectToVisible(frame);

    void IHtmlViewHost.EditingChanged(bool editing)
    {
        if (editing)
        {
            Window?.MakeFirstResponder(_content);
            _content.InputContext?.Activate();
        }
        else
        {
            _content.InputContext?.DiscardMarkedText();
        }
    }

    // A drop-down's options as a menu at the point the click was.
    void IHtmlViewHost.PickOption(IReadOnlyList<SelectOption> options, CGRect anchor, Action<int> chosen)
    {
        var menu = new NSMenu();
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var item = new NSMenuItem(options[i].Label, (_, _) => chosen(index)) { Enabled = !options[i].Disabled, State = options[i].Selected ? NSCellStateValue.On : NSCellStateValue.Off };
            menu.AddItem(item);
        }

        menu.PopUpMenu(null, new CGPoint(anchor.X, anchor.Y + anchor.Height), _content);
    }

    // A date, a time or both in a small alert, the way a Mac asks: a date picker with the field's value in it.
    void IHtmlViewHost.PickValue(string kind, string current, CGRect anchor, Action<string> chosen)
    {
        if (kind == "color")
        {
            PickColor(current, chosen);
            return;
        }

        var hasDate = PickerValues.TryParseDate(current.Split('T')[0], out var day);
        var time = current.Contains('T') ? current[(current.IndexOf('T') + 1)..] : current;
        var hasTime = PickerValues.TryParseTime(time, out var clock);
        var initial = (kind == "time" ? DateTime.Today : hasDate ? day : DateTime.Today).Add(hasTime ? clock : TimeSpan.Zero);
        var picker = new NSDatePicker(new CGRect(0, 0, kind == "datetime" ? 280 : 140, 28))
        {
            DatePickerStyle = NSDatePickerStyle.TextFieldAndStepper,
            DatePickerElements = kind switch
            {
                "time" => NSDatePickerElementFlags.HourMinute,
                "datetime" => NSDatePickerElementFlags.YearMonthDateDay | NSDatePickerElementFlags.HourMinute,
                _ => NSDatePickerElementFlags.YearMonthDateDay,
            },
            DateValue = (NSDate)DateTime.SpecifyKind(initial, DateTimeKind.Local),
        };
        var alert = new NSAlert { MessageText = kind switch { "time" => "Choose a time", "datetime" => "Choose a date and time", _ => "Choose a date" }, AccessoryView = picker };
        alert.AddButton("OK");
        alert.AddButton("Cancel");
        if (alert.RunModal() != (nint)(long)NSAlertButtonReturn.First)
            return;

        var picked = ((DateTime)picker.DateValue).ToLocalTime();
        chosen(kind switch
        {
            "time" => PickerValues.FormatTime(picked.TimeOfDay),
            "datetime" => PickerValues.FormatDateTime(current, picked.Date, picked.TimeOfDay),
            _ => PickerValues.FormatDate(picked),
        });
    }

    // The system colour panel: the field follows the colour as it is chosen.
    private NSObject? _colorObserver;
    private NSObject? _colorCloseObserver;

    private void PickColor(string current, Action<string> chosen)
    {
        var (r, g, b) = PickerValues.ParseColor(current);
        var panel = NSColorPanel.SharedColorPanel;
        panel.ShowsAlpha = false;
        panel.Color = NSColor.FromRgb(r / 255f, g / 255f, b / 255f);
        if (_colorObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_colorObserver);

        _colorObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSColorPanel.ColorChangedNotification, _ =>
        {
            var rgb = panel.Color.UsingColorSpace(NSColorSpace.SRGBColorSpace);
            if (rgb is not null)
                chosen(PickerValues.FormatColor((byte)Math.Round(rgb.RedComponent * 255), (byte)Math.Round(rgb.GreenComponent * 255), (byte)Math.Round(rgb.BlueComponent * 255)));
        }, panel);
        // The panel is the application's, shared: the page stops following it when it closes, or the view goes.
        if (_colorCloseObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_colorCloseObserver);

        _colorCloseObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillCloseNotification, _ =>
        {
            if (_colorObserver is not null)
                NSNotificationCenter.DefaultCenter.RemoveObserver(_colorObserver);

            _colorObserver = null;
        }, panel);
        panel.OrderFront(null);
    }

    void IHtmlViewHost.PickFiles(bool multiple, IReadOnlyList<string> mimeTypes, IReadOnlyList<string> extensions, Action<IReadOnlyList<ChosenFile>> chosen)
    {
        var types = new List<UTType>();
        foreach (var mime in mimeTypes)
        {
            if (!mime.EndsWith("/*", StringComparison.Ordinal) && UTType.CreateFromMimeType(mime) is { } type)
                types.Add(type);
            else if (mime == "image/*")
                types.Add(UTTypes.Image);
            else if (mime == "audio/*")
                types.Add(UTTypes.Audio);
            else if (mime == "video/*")
                types.Add(UTTypes.Movie);
        }

        foreach (var extension in extensions)
        {
            if (UTType.CreateFromExtension(extension.TrimStart('.')) is { } type)
                types.Add(type);
        }

        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = multiple;
        panel.CanChooseDirectories = false;
        panel.CanChooseFiles = true;
        if (types.Count > 0)
            panel.AllowedContentTypes = types.ToArray();

        if (panel.RunModal() != (nint)(long)NSModalResponse.OK)
            return;

        var files = new List<ChosenFile>();
        foreach (var url in panel.Urls)
        {
            if (url.Path is not { } path || !File.Exists(path))
                continue;

            var mime = UTType.CreateFromExtension(Path.GetExtension(path).TrimStart('.'))?.PreferredMimeType ?? string.Empty;
            files.Add(new ChosenFile(Path.GetFileName(path), mime, File.ReadAllBytes(path)));
        }

        chosen(files);
    }

    void IHtmlViewHost.LoadFailed(Exception exception) => DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));

    /// <summary>The flipped view the pages sit in; it takes the mouse, the cursor and the key commands for the document.</summary>
    private sealed class PageView : NSView, INSTextInputClient
    {
        private readonly WeakReference<HtmlView> _ownerRef;

        // Weak: the page view is native-retained by the scroll view that the owner is, and a strong managed reference back would keep the whole view alive.
        private HtmlView _owner => _ownerRef.TryGetTarget(out var owner) ? owner : null!;
        private readonly HtmlViewEngine _engine;
        private NSTrackingArea? _tracking;
        private bool _dragSelecting;
        private bool _dragged;
        private (int Page, double X, double Y)? _downAt;
        private CGPoint _downPoint;
        private HtmlViewEngine.DragKind _engineDrag;

        public PageView(HtmlView owner, HtmlViewEngine engine) : base(CGRect.Empty)
        {
            _ownerRef = new WeakReference<HtmlView>(owner);
            _engine = engine;
            WantsLayer = true;
            AccessibilityElement = false;
            AccessibilityRole = "AXGroup";
            Layer!.AddSublayer(engine.ContentLayer);
            Layer.AddSublayer(engine.OverlayLayer);
        }

        public void SetAccessibilityElements(NSObject[] elements)
        {
            AccessibilityChildren = elements;
            NSAccessibility.PostNotification(this, new NSString("AXLayoutChanged"));
        }

        public override bool IsFlipped => true;

        public override bool AcceptsFirstResponder() => true;

        // A click on a window that is not key still acts on the page (a link, a control), as it does in a browser.
        public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

        // ---- the keyboard: Tab through the page, Enter on a link, and key input in a text field through the input context ----

        public override void KeyDown(NSEvent theEvent)
        {
            if (_engine.IsEditing)
            {
                var chars = theEvent.Characters ?? string.Empty;
                if (chars != "\t" && !theEvent.ModifierFlags.HasFlag(NSEventModifierMask.CommandKeyMask))
                {
                    InterpretKeyEvents([theEvent]);
                    return;
                }
            }

            var shift = theEvent.ModifierFlags.HasFlag(NSEventModifierMask.ShiftKeyMask);
            switch (theEvent.Characters)
            {
                case "\t" or "\u0019": // Tab, and Shift+Tab (which arrives as back-tab)
                    if (!_engine.StepFocus(theEvent.Characters == "\t" && !shift))
                        base.KeyDown(theEvent);

                    return;
                case "\r" or " ":
                    var (handled, link) = _engine.ActivateFocused(theEvent.Characters == " ");
                    if (link is not null)
                        _owner.ActivateNode(link);

                    if (!handled)
                        base.KeyDown(theEvent);

                    return;
                default:
                    base.KeyDown(theEvent);
                    return;
            }
        }

        // ---- NSTextInputClient ----

        public bool HasMarkedText => _engine.EditState is { CompositionStart: >= 0 };

        public NSRange MarkedRange =>
            _engine.EditState is { CompositionStart: >= 0 } s ? new NSRange(s.CompositionStart, s.CompositionEnd - s.CompositionStart) : new NSRange(NSRange.NotFound, 0);

        public NSRange SelectedRange =>
            _engine.EditState is { } s ? new NSRange(Math.Min(s.SelectionStart, s.SelectionEnd), Math.Abs(s.SelectionEnd - s.SelectionStart)) : new NSRange(NSRange.NotFound, 0);

        [Export("insertText:replacementRange:")]
        public void InsertText(NSObject text, NSRange replacementRange)
        {
            var value = (text is NSAttributedString attributed ? attributed.Value : text.ToString()) ?? string.Empty;
            if (_engine.EditState is { CompositionStart: >= 0 } composing && replacementRange.Location == NSRange.NotFound)
            {
                // Committing what was being composed: the final text takes the place of the marked text.
                _engine.InsertText(value, composing.CompositionStart, composing.CompositionEnd - composing.CompositionStart);
            }
            else if (replacementRange.Location != NSRange.NotFound)
            {
                _engine.InsertText(value, (int)replacementRange.Location, (int)replacementRange.Length);
            }
            else
            {
                _engine.InsertText(value);
            }

            _engine.EndComposition();
        }

        [Export("setMarkedText:selectedRange:replacementRange:")]
        public void SetMarkedText(NSObject text, NSRange selectedRange, NSRange replacementRange)
        {
            var value = (text is NSAttributedString attributed ? attributed.Value : text.ToString()) ?? string.Empty;
            _engine.SetComposition(value, selectedRange.Location == NSRange.NotFound ? value.Length : (int)selectedRange.Location);
        }

        public void UnmarkText() => _engine.EndComposition();

        public NSString[] ValidAttributesForMarkedText => [];

        public NSAttributedString? AttributedSubstringForProposedRange(NSRange range, out NSRange actualRange)
        {
            actualRange = range;
            var text = _engine.CurrentFieldText();
            if (range.Location == NSRange.NotFound || (int)range.Location > text.Length)
                return null;

            var length = Math.Min((int)range.Length, text.Length - (int)range.Location);
            return new NSAttributedString(text.Substring((int)range.Location, length));
        }

        public nuint CharacterIndex(CGPoint point) => (nuint)Math.Max(0, _engine.EditState?.SelectionEnd ?? 0);

        // Where the input method puts its candidate window: under the field with the focus, in screen coordinates.
        public CGRect FirstRectForCharacterRange(NSRange range, out NSRange actualRange)
        {
            actualRange = range;
            var frame = _engine.FocusedFrame ?? new CGRect(0, 0, 1, 1);
            var inWindow = ConvertRectToView(frame, null);
            return Window?.ConvertRectToScreen(inWindow) ?? inWindow;
        }

        // The commands the key bindings make of a key (Delete, the arrows, Return...), and of a command like Select All.
        [Export("doCommandBySelector:")]
        public void DoCommandBySelector(Selector selector)
        {
            switch (selector.Name)
            {
                case "deleteBackward:": _engine.Delete(backward: true, word: false); break;
                case "deleteForward:": _engine.Delete(backward: false, word: false); break;
                case "deleteWordBackward:": _engine.Delete(backward: true, word: true); break;
                case "deleteWordForward:": _engine.Delete(backward: false, word: true); break;
                case "moveLeft:": _engine.MoveCaret(EditMove.Left, false); break;
                case "moveRight:": _engine.MoveCaret(EditMove.Right, false); break;
                case "moveLeftAndModifySelection:": _engine.MoveCaret(EditMove.Left, true); break;
                case "moveRightAndModifySelection:": _engine.MoveCaret(EditMove.Right, true); break;
                case "moveWordLeft:": _engine.MoveCaret(EditMove.WordLeft, false); break;
                case "moveWordRight:": _engine.MoveCaret(EditMove.WordRight, false); break;
                case "moveWordLeftAndModifySelection:": _engine.MoveCaret(EditMove.WordLeft, true); break;
                case "moveWordRightAndModifySelection:": _engine.MoveCaret(EditMove.WordRight, true); break;
                case "moveToBeginningOfLine:" or "moveToLeftEndOfLine:": if (!_engine.MoveLine("Home", false)) _engine.MoveCaret(EditMove.Home, false); break;
                case "moveToEndOfLine:" or "moveToRightEndOfLine:": if (!_engine.MoveLine("End", false)) _engine.MoveCaret(EditMove.End, false); break;
                case "moveToBeginningOfLineAndModifySelection:": if (!_engine.MoveLine("Home", true)) _engine.MoveCaret(EditMove.Home, true); break;
                case "moveToEndOfLineAndModifySelection:": if (!_engine.MoveLine("End", true)) _engine.MoveCaret(EditMove.End, true); break;
                case "moveUp:": _engine.MoveLine("Up", false); break;
                case "moveDown:": _engine.MoveLine("Down", false); break;
                case "moveUpAndModifySelection:": _engine.MoveLine("Up", true); break;
                case "moveDownAndModifySelection:": _engine.MoveLine("Down", true); break;
                case "insertNewline:": _engine.EnterInField(); break;
                case "insertTab:": _engine.StepFocus(true); break;
                case "insertBacktab:": _engine.StepFocus(false); break;
                case "selectAll:": _engine.SelectAllInField(); break;
                default: break;
            }
        }

        [Export("cut:")]
        public void Cut(NSObject? sender)
        {
            if (_engine.FieldSelection().Length == 0)
                return;

            _owner.CopySelection();
            _engine.Delete(backward: true, word: false);
        }

        [Export("paste:")]
        public void Paste(NSObject? sender)
        {
            if (_engine.IsEditing && NSPasteboard.GeneralPasteboard.ReadObjectsForClasses([new ObjCRuntime.Class(typeof(NSString))], null)?.FirstOrDefault()?.ToString() is { } text)
                _engine.InsertText(CaretMoves.ForField(_engine.EditState?.Type ?? "text", text));
        }


        public override bool IsOpaque => false;

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            if (_tracking is not null)
                RemoveTrackingArea(_tracking);

            _tracking = new NSTrackingArea(Bounds,
                NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow
                | NSTrackingAreaOptions.CursorUpdate | NSTrackingAreaOptions.InVisibleRect, this, null);
            AddTrackingArea(_tracking);
        }

        private CGPoint Point(NSEvent e) => ConvertPointFromView(e.LocationInWindow, null);

        private bool Locate(NSEvent e, out int page, out double x, out double y)
        {
            var p = Point(e);
            return _engine.TryLocate(p.X, p.Y, out page, out x, out y);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            if (Locate(theEvent, out var page, out var x, out var y))
            {
                _engine.RequestState(page, x, y, pressed: false);
                ShowCursor(page, x, y);
            }
            else
            {
                _engine.RequestClear();
            }
        }

        public override void MouseEntered(NSEvent theEvent) => MouseMoved(theEvent);

        public override void MouseExited(NSEvent theEvent)
        {
            _engine.RequestClear();
            NSCursor.ArrowCursor.Set();
        }

        public override void CursorUpdate(NSEvent theEvent) => MouseMoved(theEvent);

        private void ShowCursor(int page, double x, double y)
        {
            var shape = _engine.Document!.GetCursorAt(page, x, y).Shape;
            Cursors.For(AppleCursors.ToKind(shape)).Set();
        }

        public override void MouseDown(NSEvent theEvent)
        {
            Window?.MakeFirstResponder(this);
            _dragged = false;
            _dragSelecting = false;
            if (!Locate(theEvent, out var page, out var x, out var y))
            {
                _downAt = null;
                return;
            }

            _downAt = (page, x, y);
            _downPoint = Point(theEvent);
            _engineDrag = _engine.BeginDrag(page, x, y);
            if (_engineDrag != HtmlViewEngine.DragKind.None)
            {
                // A range sets at the press and follows the mouse; a text field takes the focus at the press and the drag extends its selection.
                _engine.TapForm(page, x, y, new CGRect(_downPoint.X, _downPoint.Y, 1, 1));
                return;
            }

            _engine.RequestState(page, x, y, pressed: true);
            var selection = _engine.Document!.Selection;
            if (theEvent.ClickCount >= 2 && _engine.Document.GetInteractionAt(page, x, y).CanSelectText)
            {
                selection.SelectWordAt(page, x, y);
                return;
            }

            _dragSelecting = _engine.Document.GetInteractionAt(page, x, y).CanSelectText && selection.SelectAt(page, x, y);
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            if (_engineDrag != HtmlViewEngine.DragKind.None)
            {
                if (Locate(theEvent, out var dragPage, out var dragX, out var dragY))
                {
                    _dragged = true;
                    _engine.Drag(dragPage, dragX, dragY);
                }

                return;
            }

            if (!_dragSelecting || !Locate(theEvent, out var page, out var x, out var y))
                return;

            _dragged = true;
            _engine.Document!.Selection.ExtendTo(page, x, y);
        }

        public override void MouseUp(NSEvent theEvent)
        {
            if (Locate(theEvent, out var page, out var x, out var y))
                _engine.RequestState(page, x, y, pressed: false);

            if (_downAt is { } down && !_dragged && _engineDrag == HtmlViewEngine.DragKind.None && theEvent.ClickCount == 1)
                Click(down.Page, down.X, down.Y);

            _downAt = null;
            _dragSelecting = false;
            _engineDrag = HtmlViewEngine.DragKind.None;
            _engine.EndDrag();
        }

        private void Click(int page, double x, double y)
        {
            var at = _downPoint;
            if (_engine.TapForm(page, x, y, new CGRect(at.X, at.Y, 1, 1)))
                return;

            // A click on anything that is not a control takes the focus out of the field it was in.
            _engine.LoseFocus();

            if (_engine.LinkAt(page, x, y) is not { } link)
            {
                _engine.Document!.Selection.RemoveAllRanges();
                _owner.Raise(new HtmlTappedEventArgs(page, x, y));
                return;
            }

            if (_owner.RaiseLink(new HtmlLinkActivatedEventArgs(link.Uri, link.Anchor)))
                return;

            if (link.Uri is { } uri)
            {
                if (PageLayout.IsLaunchable(uri) && NSUrl.FromString(uri) is { } url)
                    NSWorkspace.SharedWorkspace.OpenUrl(url);
            }
            else if (_engine.LinkTargetTop(link) is { } top)
            {
                _owner.ScrollToContentY(top);
            }
        }

        public override NSMenu? MenuForEvent(NSEvent theEvent)
        {
            var menu = new NSMenu();
            var copy = new NSMenuItem("Copy", new Selector("copy:"), "") { Enabled = _engine.SelectedText.Length > 0 };
            menu.AddItem(copy);
            menu.AddItem(new NSMenuItem("Select All", new Selector("selectAll:"), ""));
            return menu;
        }

        [Export("copy:")]
        public void Copy(NSObject? sender) => _owner.CopySelection();

        [Export("selectAll:")]
        public void SelectAllText(NSObject? sender)
        {
            if (!_engine.IsEditing || !_engine.SelectAllInField())
                _owner.SelectAll();
        }

        [Export("validateMenuItem:")]
        public bool ValidateMenuItem(NSMenuItem item) =>
            item.Action?.Name switch
            {
                "copy:" => _engine.SelectedText.Length > 0,
                _ => true,
            };
    }

    private static class Cursors
    {
        public static NSCursor For(AppleCursorKind kind) => kind switch
        {
            AppleCursorKind.IBeam => NSCursor.IBeamCursor,
            AppleCursorKind.IBeamVertical => NSCursor.IBeamCursorForVerticalLayout,
            AppleCursorKind.Crosshair => NSCursor.CrosshairCursor,
            AppleCursorKind.PointingHand => NSCursor.PointingHandCursor,
            AppleCursorKind.OpenHand => NSCursor.OpenHandCursor,
            AppleCursorKind.ClosedHand => NSCursor.ClosedHandCursor,
            AppleCursorKind.ResizeLeftRight => NSCursor.ResizeLeftRightCursor,
            AppleCursorKind.ResizeUpDown => NSCursor.ResizeUpDownCursor,
            AppleCursorKind.ContextualMenu => NSCursor.ContextualMenuCursor,
            AppleCursorKind.NotAllowed => NSCursor.OperationNotAllowedCursor,
            AppleCursorKind.DragCopy => NSCursor.DragCopyCursor,
            AppleCursorKind.DragLink => NSCursor.DragLinkCursor,
            AppleCursorKind.Hidden => HiddenCursor,
            _ => NSCursor.ArrowCursor,
        };

        // `cursor: none`: a one-pixel transparent image.
        private static readonly NSCursor HiddenCursor = new(new NSImage(new CGSize(1, 1)), CGPoint.Empty);
    }

    /// <summary>One part of the page for VoiceOver: a heading, a link, a control or a piece of text.</summary>
    private sealed class PartElement : NSAccessibilityElement
    {
        private readonly WeakReference<HtmlView> _ownerRef;

        // Weak: the page view is native-retained by the scroll view that the owner is, and a strong managed reference back would keep the whole view alive.
        private HtmlView _owner => _ownerRef.TryGetTarget(out var owner) ? owner : null!;
        private readonly AccessibilityNode _node;

        public PartElement(HtmlView owner, NSView parent, AccessibilityNode node, CGRect frame)
        {
            _ownerRef = new WeakReference<HtmlView>(owner);
            _node = node;
            AccessibilityParent = parent;
            AccessibilityFrameInParentSpace = frame;
        }

        public override string AccessibilityRole => AppleAccessibility.MacRole(_node.Role);

        public override string? AccessibilityLabel => _node.ReadingText;

        public override NSObject? AccessibilityValue => _node.Value is { } v ? NSNumber.FromDouble(v) : null;

        public override bool AccessibilityEnabled => !_node.Disabled;

        public override bool AccessibilityPerformPress()
        {
            if (!AppleAccessibility.IsActivatable(_node.Role))
                return false;

            _owner.ActivateNode(_node);
            return true;
        }
    }
}
