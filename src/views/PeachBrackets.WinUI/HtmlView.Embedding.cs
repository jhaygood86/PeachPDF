using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeachDrawing.Text;
using System;
using Windows.Foundation;
using Windows.UI;

namespace PeachBrackets.WinUI;

// Using the view as a block inside other layout - a row of a virtualized list - rather than as a viewer.
public sealed partial class HtmlView
{
    /// <summary>Identifies the <see cref="ScrollMode"/> property.</summary>
    public static readonly DependencyProperty ScrollModeProperty =
        DependencyProperty.Register(nameof(ScrollMode), typeof(HtmlViewScrollMode), typeof(HtmlView),
            new PropertyMetadata(HtmlViewScrollMode.Auto, (d, e) => ((HtmlView)d).OnScrollModeChanged((HtmlViewScrollMode)e.NewValue)));

    /// <summary>Identifies the <see cref="IsZoomEnabled"/> property.</summary>
    public static readonly DependencyProperty IsZoomEnabledProperty =
        DependencyProperty.Register(nameof(IsZoomEnabled), typeof(bool), typeof(HtmlView),
            new PropertyMetadata(true, (d, e) => ((HtmlView)d).OnZoomEnabledChanged((bool)e.NewValue)));

    /// <summary>Identifies the <see cref="IsLayoutSynchronous"/> property.</summary>
    public static readonly DependencyProperty IsLayoutSynchronousProperty =
        DependencyProperty.Register(nameof(IsLayoutSynchronous), typeof(bool), typeof(HtmlView),
            new PropertyMetadata(false, (d, e) => ((HtmlView)d)._layoutSynchronous = (bool)e.NewValue));

    /// <summary>Identifies the <see cref="ContentHeight"/> property.</summary>
    public static readonly DependencyProperty ContentHeightProperty =
        DependencyProperty.Register(nameof(ContentHeight), typeof(double), typeof(HtmlView), new PropertyMetadata(0d));

    /// <summary>Identifies the <see cref="SelectionColor"/> property.</summary>
    public static readonly DependencyProperty SelectionColorProperty =
        DependencyProperty.Register(nameof(SelectionColor), typeof(Color), typeof(HtmlView),
            new PropertyMetadata(Color.FromArgb(255, 173, 214, 255), (d, _) => ((HtmlView)d).OnOptionsChanged()));

    /// <summary>Identifies the <see cref="UserStyleSheet"/> property.</summary>
    public static readonly DependencyProperty UserStyleSheetProperty =
        DependencyProperty.Register(nameof(UserStyleSheet), typeof(string), typeof(HtmlView),
            new PropertyMetadata(null, (d, _) => ((HtmlView)d).OnOptionsChanged()));

    /// <summary>Identifies the <see cref="DocumentMargin"/> property.</summary>
    public static readonly DependencyProperty DocumentMarginProperty =
        DependencyProperty.Register(nameof(DocumentMargin), typeof(double), typeof(HtmlView),
            new PropertyMetadata(0d, (d, _) => ((HtmlView)d).OnOptionsChanged()));

    /// <summary>Identifies the <see cref="AllowNetworkAccess"/> property.</summary>
    public static readonly DependencyProperty AllowNetworkAccessProperty =
        DependencyProperty.Register(nameof(AllowNetworkAccess), typeof(bool), typeof(HtmlView),
            new PropertyMetadata(false, (d, e) => { ((HtmlView)d)._allowNetworkAccess = (bool)e.NewValue; ((HtmlView)d).OnOptionsChanged(); }));

    private volatile bool _allowNetworkAccess;

    /// <summary>
    /// Whether the document may load images, style sheets and fonts from <c>http</c> and <c>https</c> addresses. Off by default, so a document cannot make
    /// the application call out; <c>data:</c> URIs and files are always read. Changing it loads the document again.
    /// </summary>
    public bool AllowNetworkAccess
    {
        get => (bool)GetValue(AllowNetworkAccessProperty);
        set => SetValue(AllowNetworkAccessProperty, value);
    }

    /// <summary>Identifies the <see cref="DocumentBaseUri"/> property.</summary>
    public static readonly DependencyProperty DocumentBaseUriProperty =
        DependencyProperty.Register(nameof(DocumentBaseUri), typeof(Uri), typeof(HtmlView),
            new PropertyMetadata(null, (d, e) => { ((HtmlView)d)._baseUri = (Uri?)e.NewValue; ((HtmlView)d).OnOptionsChanged(); }));

    private volatile Uri? _baseUri;

    /// <summary>
    /// The address the document's relative references (style sheets, images, fonts) resolve against, unless it has a <c>&lt;base href&gt;</c> of its own.
    /// Defaults to <see cref="Source"/>; set it when the HTML came from somewhere else, such as a page the host fetched itself. Changing it loads the
    /// document again.
    /// </summary>
    public Uri? DocumentBaseUri
    {
        get => (Uri?)GetValue(DocumentBaseUriProperty);
        set => SetValue(DocumentBaseUriProperty, value);
    }

    private HtmlViewScrollMode _scrollMode = HtmlViewScrollMode.Auto;
    private bool _zoomEnabled = true;
    private bool _layoutSynchronous;
    private double _inFlightWidth = -1;
    private double? _offeredHeight;
    private double? _loadedViewport;
    private FontSet? _fontSet;

    /// <summary>
    /// Whether the view scrolls the document itself (<see cref="HtmlViewScrollMode.Auto"/>, the default) or is a block in other layout
    /// (<see cref="HtmlViewScrollMode.Disabled"/>): as tall as its document (<see cref="ContentHeight"/>), no scroll bars, and the wheel, touch panning
    /// and zoom left to the list or panel it sits in.
    /// </summary>
    public HtmlViewScrollMode ScrollMode
    {
        get => (HtmlViewScrollMode)GetValue(ScrollModeProperty);
        set => SetValue(ScrollModeProperty, value);
    }

    /// <summary>Whether the scroll wheel with Ctrl held and a pinch zoom the document. Only a view that scrolls itself (<see cref="HtmlViewScrollMode.Auto"/>) zooms.</summary>
    public bool IsZoomEnabled
    {
        get => (bool)GetValue(IsZoomEnabledProperty);
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>
    /// Says that this view's documents need nothing asynchronous: their images, style sheets and fonts are inline (<c>data:</c> URIs), already
    /// registered (<see cref="HtmlViewFonts"/>) or otherwise answered without waiting. The view then loads and lays the document out in the same call that
    /// needs its size, so an auto-height view reports the exact height from the layout pass instead of changing it a moment later.
    /// </summary>
    /// <remarks>
    /// If a document then tries to fetch something it would have to wait for, loading fails with an <see cref="InvalidOperationException"/> naming the
    /// resource, delivered through <see cref="DocumentFailed"/>. Off by default.
    /// </remarks>
    public bool IsLayoutSynchronous
    {
        get => (bool)GetValue(IsLayoutSynchronousProperty);
        set => SetValue(IsLayoutSynchronousProperty, value);
    }

    /// <summary>
    /// The height of the laid-out document in device-independent pixels at the width the view was last laid out for, or the last known height before
    /// the first layout. An auto-height view (<see cref="HtmlViewScrollMode.Disabled"/>) is this tall.
    /// </summary>
    public double ContentHeight => (double)GetValue(ContentHeightProperty);

    /// <summary>
    /// Raised when <see cref="ContentHeight"/> changes: the document finished laying out, was laid out again for another width, or changed (a form control
    /// opened, text was edited). A list that sized the row from an estimate uses this to correct it.
    /// </summary>
    public event EventHandler? ContentHeightChanged;

    /// <summary>
    /// The colour behind selected text (opaque or not; the text keeps its own colour). The default is a light blue; a design with its own accent sets it.
    /// </summary>
    public Color SelectionColor
    {
        get => (Color)GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }

    /// <summary>
    /// A style sheet applied after the browser defaults and before the document's own, as a reader's style sheet is: the way to give every document the
    /// application's typography without editing each one.
    /// </summary>
    public string? UserStyleSheet
    {
        get => (string?)GetValue(UserStyleSheetProperty);
        set => SetValue(UserStyleSheetProperty, value);
    }

    /// <summary>The space left around the document inside the view, in device-independent pixels. The default is none, so the document starts at the view's top-left corner.</summary>
    public double DocumentMargin
    {
        get => (double)GetValue(DocumentMarginProperty);
        set => SetValue(DocumentMarginProperty, value);
    }

    /// <summary>
    /// The fonts the view resolves families from. <see langword="null"/> (the default) uses <see cref="HtmlViewFonts.Shared"/>, which every view shares; give
    /// a view a set of its own to keep its fonts apart. Set it before the view loads: changing it later loads the document again.
    /// </summary>
    public FontSet? FontSet
    {
        get => _fontSet;
        set
        {
            if (ReferenceEquals(_fontSet, value))
                return;

            _fontSet = value;
            if (_context is null)
                return;

            // The context was made for the other set.
            _loadVersion++;
            _document?.Dispose();
            _document = null;
            _context.Dispose();
            _context = null;
            EnsureContext();
            OnOptionsChanged();
        }
    }

    private bool HasContent => Html is not null || Source is not null;

    // What the view is as a block in layout: as wide as it is given, as tall as the document is at that width. The width is also what the document is laid
    // out for, so the first measure of a new width is where it is loaded - at once when the layout is synchronous, otherwise in the background, the view
    // keeping the height it had and measuring again when the new layout is ready.
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_scrollMode != HtmlViewScrollMode.Disabled)
            return base.MeasureOverride(availableSize);

        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(_loadedWidth, 0);
        if (_offeredHeight is null && double.IsFinite(availableSize.Height))
            _offeredHeight = availableSize.Height;

        if (width >= 1 && HasContent && (_needsReload || (Math.Abs(width - _loadedWidth) > 0.5 && Math.Abs(width - _inFlightWidth) > 0.5)))
        {
            EnsureContext();
            _measuring = true;
            try
            {
                StartReload(width);
            }
            finally
            {
                _measuring = false;
            }
        }

        var height = ContentHeight;
        _host.Measure(new Size(width, height));
        return new Size(width, height);
    }

    private void OnScrollModeChanged(HtmlViewScrollMode mode)
    {
        _scrollMode = mode;
        if (mode == HtmlViewScrollMode.Disabled)
        {
            _scroller.Content = null;
            _layers.Children.Clear();
            _host.Content = _canvas;
        }
        else
        {
            _host.Content = null;
            _scroller.Content = _canvas;
            if (!_layers.Children.Contains(_scroller))
            {
                _layers.Children.Add(_scroller);
                _layers.Children.Add(_overlay);
            }

            _host.Content = _layers;
        }

        if (_document is not null)
            UpdateMetrics();

        ApplyRenderMode();
        InvalidateMeasure();
    }

    private void OnZoomEnabledChanged(bool enabled)
    {
        _zoomEnabled = enabled;
        _scroller.ZoomMode = enabled ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    // The height of a document in DIPs: how far down its content reaches, margins included.
    private double MeasureContentHeight(PeachBrackets.HtmlDocument document)
    {
        var pages = document.Pages;
        if (pages.Count == 0)
            return 0;

        // A screen document is one surface; its content ends where GetContentHeight says, with the margin below it.
        var margin = DocumentMargin / PixelsPerPoint;
        return Math.Min(pages[0].HeightPoints, document.GetContentHeight(0) + (2 * margin)) * PixelsPerPoint;
    }

    private void SetContentHeight(double height)
    {
        if (Math.Abs(height - ContentHeight) < 0.01)
            return;

        SetValue(ContentHeightProperty, height);
        if (_scrollMode == HtmlViewScrollMode.Disabled && !_measuring)
            InvalidateMeasure();

        RaiseOutsideMeasure(() => ContentHeightChanged?.Invoke(this, EventArgs.Empty));
    }

    // Events are not raised from inside a layout pass, where a handler that touches layout would be re-entering it.
    private void RaiseOutsideMeasure(Action raise)
    {
        if (_measuring)
            DispatcherQueue.TryEnqueue(() => raise());
        else
            raise();
    }

    private double ViewZoom => _scrollMode == HtmlViewScrollMode.Disabled ? 1 : _scroller.ZoomFactor;

    private double ViewHorizontalOffset => _scrollMode == HtmlViewScrollMode.Disabled ? 0 : _scroller.HorizontalOffset;

    internal double ViewVerticalOffset => _scrollMode == HtmlViewScrollMode.Disabled ? 0 : _scroller.VerticalOffset;

    // Brings a position of the document (device-independent pixels from its top, at no zoom) to the top of what is shown: by scrolling the view, or,
    // for a view that does not scroll, by asking what does.
    // An animated scroll takes a second or two over a long distance, and the scroller does not pass a click to the document until it has finished: a second
    // link clicked soon after the first went nowhere. A jump to a place in the document is instant, as in a browser without `scroll-behavior: smooth`.
    internal void ScrollToY(double y, bool animate = true)
    {
        if (_scrollMode == HtmlViewScrollMode.Disabled)
        {
            _canvas.StartBringIntoView(new BringIntoViewOptions { TargetRect = new Rect(0, y, 1, 1), VerticalAlignmentRatio = 0, AnimationDesired = animate });
            return;
        }

        _scroller.ChangeView(null, y * _scroller.ZoomFactor, null, disableAnimation: !animate);
    }
}
