using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Logic;
using AvaloniaToggleState = Avalonia.Automation.Provider.ToggleState;

namespace PeachBrackets.Avalonia;

/// <summary>
/// What assistive technology sees of an <see cref="HtmlView"/>: a document whose children are the parts of the page (landmarks, headings, paragraphs, lists,
/// tables, links, controls) with what is inside each below it, so a screen reader can read the page, jump between headings and follow links.
/// </summary>
internal sealed class HtmlViewAutomationPeer : ControlAutomationPeer, IScrollProvider
{
    private readonly HtmlView _view;
    private List<AutomationPeer>? _children;

    public HtmlViewAutomationPeer(HtmlView owner) : base(owner) => _view = owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(HtmlView);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "HTML document";

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => GetChildrenCore();

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        if (_children is null)
        {
            var peers = new List<AutomationPeer>();
            if (_view.AccessibilityTree is { } tree)
            {
                foreach (var node in tree.Root.ReadingChildren)
                    peers.Add(new HtmlNodeAutomationPeer(this, this, _view, node));
            }

            _children = peers;
        }

        return _children;
    }

    protected override object? GetProviderCore(Type providerType) =>
        providerType == typeof(IScrollProvider) ? this : base.GetProviderCore(providerType);

    /// <summary>Where the view is, in the coordinates its nodes are told in.</summary>
    internal Rect ViewBounds => GetBoundingRectangle();

    /// <summary>The peer that stands for a node of the current tree, or null.</summary>
    internal HtmlNodeAutomationPeer? FindPeer(string nodeId)
    {
        GetChildrenCore();
        return _children is null ? null : Find(_children, nodeId);

        static HtmlNodeAutomationPeer? Find(IEnumerable<AutomationPeer> peers, string id)
        {
            foreach (var peer in peers.OfType<HtmlNodeAutomationPeer>())
            {
                if (peer.Node.Id == id)
                    return peer;

                if (Find(peer.Peers, id) is { } below)
                    return below;
            }

            return null;
        }
    }

    /// <summary>The tree was built again: the peers are built again from it, and what changed is told.</summary>
    internal void Refresh(AccessibilityTree? before, AccessibilityTree after, AccessibilityTreeDiff? diff)
    {
        _children = null;
        InvalidateChildren();
        RaiseChildrenChangedEvent();
        if (diff is null)
            return;

        foreach (var change in diff.Changes)
        {
            if (change.Kind == AccessibilityChangeKind.Removed || FindPeer(change.Node.Id) is not { } peer)
                continue;

            var old = before?.FindById(change.Node.Id);
            switch (change)
            {
                case { Kind: AccessibilityChangeKind.StateChanged, Property: "checked" }:
                    peer.RaisePropertyChangedEvent(
                        TogglePatternIdentifiers.ToggleStateProperty,
                        AccessibilityMapping.ToToggleState(old?.Checked ?? PeachBrackets.Accessibility.ToggleState.None),
                        AccessibilityMapping.ToToggleState(change.Node.Checked));
                    break;
                case { Kind: AccessibilityChangeKind.NameChanged }:
                    peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old?.ReadingText ?? string.Empty, change.Node.ReadingText);
                    break;
                case { Kind: AccessibilityChangeKind.DescriptionChanged }:
                    peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, old?.Description ?? string.Empty, change.Node.Description);
                    break;
                case { Kind: AccessibilityChangeKind.ValueChanged }:
                    peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old?.Value ?? 0d, change.Node.Value ?? 0d);
                    break;
            }
        }

        // Avalonia has no notification event: what a live region now says reaches a reader as a change of the region's name, which readers announce for a live region.
        foreach (var announcement in diff.Announcements)
        {
            if (FindPeer(announcement.Region.Id) is { } region)
                region.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, announcement.Text);
        }
    }

    /// <summary>The view scrolled or zoomed: where each node is changed.</summary>
    internal void RaiseBoundsChanged()
    {
        if (_children is null)
            return;

        foreach (var peer in _children.OfType<HtmlNodeAutomationPeer>())
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.BoundingRectangleProperty, default(Rect), peer.GetBoundingRectangle());
    }

    // IScrollProvider: the whole document, as a scroll viewer.
    public bool HorizontallyScrollable => _view.DocumentScroll().CanX;

    public double HorizontalScrollPercent => _view.DocumentScroll().X;

    public double HorizontalViewSize => _view.DocumentScroll().ViewWidth;

    public bool VerticallyScrollable => _view.DocumentScroll().CanY;

    public double VerticalScrollPercent => _view.DocumentScroll().Y;

    public double VerticalViewSize => _view.DocumentScroll().ViewHeight;

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount) => _view.ScrollDocumentFromAutomation(horizontalAmount, verticalAmount);

    public void SetScrollPercent(double horizontalPercent, double verticalPercent) => _view.ScrollDocumentToPercentFromAutomation(horizontalPercent, verticalPercent);
}

/// <summary>One part of the page - a paragraph, a heading, a link, a control, a landmark - in the tree a screen reader walks.</summary>
internal sealed class HtmlNodeAutomationPeer : AutomationPeer, IInvokeProvider, IToggleProvider, IValueProvider, IRangeValueProvider, IScrollProvider
{
    private readonly HtmlViewAutomationPeer _root;
    private readonly HtmlView _view;
    private readonly List<AutomationPeer> _children = [];
    private AutomationPeer _parent;

    public HtmlNodeAutomationPeer(HtmlViewAutomationPeer root, AutomationPeer parent, HtmlView view, AccessibilityNode node)
    {
        _root = root;
        _view = view;
        _parent = parent;
        Node = node;
        foreach (var child in node.ReadingChildren)
            _children.Add(new HtmlNodeAutomationPeer(root, this, view, child));
    }

    /// <summary>The node of the accessibility tree this peer stands for.</summary>
    internal AccessibilityNode Node { get; }

    /// <summary>The peers of the node's children.</summary>
    internal IReadOnlyList<AutomationPeer> Peers => _children;

    private AccessibilityNode Current => _view.CurrentNode(Node);

    protected override AutomationControlType GetAutomationControlTypeCore() => AccessibilityMapping.ToControlType(Node.Role);

    protected override string GetClassNameCore() => "HtmlNode";

    protected override string GetNameCore() => Node.ReadingText;

    protected override string GetHelpTextCore() => Node.Description;

    protected override string GetAutomationIdCore() => Node.Id;

    protected override int GetHeadingLevelCore() => Node.Role == AccessibilityRole.Heading ? Math.Clamp(Node.Level, 1, 9) : 0;

    protected override AutomationLandmarkType? GetLandmarkTypeCore() => AccessibilityMapping.ToLandmarkType(Node.Role);

    protected override AutomationLiveSetting GetLiveSettingCore() => AccessibilityMapping.ToLiveSetting(Node.Live);

    protected override string GetAcceleratorKeyCore() => Node.KeyShortcuts ?? string.Empty;

    protected override string GetAccessKeyCore() => Node.AccessKey ?? string.Empty;

    protected override string GetItemStatusCore() => Node.Invalid ? "invalid" : string.Empty;

    protected override string GetItemTypeCore() => Node.Role.ToString();

    protected override string GetPlaceholderTextCore() => string.Empty;

    protected override Rect GetBoundingRectangleCore() => _view.NodeBounds(Node, _root.ViewBounds);

    protected override bool IsOffscreenCore() => _view.IsOffscreen(Node);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => !Node.Disabled;

    protected override bool IsKeyboardFocusableCore() => Node.IsFocusable;

    protected override bool HasKeyboardFocusCore() => _view.Document?.FocusedNode?.Id == Node.Id;

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => _children;

    protected override AutomationPeer GetParentCore() => _parent;

    protected override AutomationPeer? GetLabeledByCore() => Node.LabelledBy.Count > 0 ? _root.FindPeer(Node.LabelledBy[0].Id) : null;

    protected override bool TrySetParent(AutomationPeer? parent)
    {
        if (parent is null)
            return false;

        _parent = parent;
        return true;
    }

    protected override void BringIntoViewCore() => _view.BringIntoView(Node);

    protected override bool ShowContextMenuCore() => false;

    protected override void SetFocusCore()
    {
        if (Node.IsFocusable)
            _view.FocusFromAutomation(Node);
        else
            _view.BringIntoView(Node);
    }

    protected override object? GetProviderCore(Type providerType)
    {
        if (providerType == typeof(IInvokeProvider))
            return Node.Role is AccessibilityRole.Link or AccessibilityRole.Button ? this : null;

        if (providerType == typeof(IToggleProvider))
            return AccessibilityMapping.IsToggleControl(Node.Role) ? this : null;

        if (providerType == typeof(IValueProvider))
            return AccessibilityMapping.IsTextField(Node.Role) ? this : null;

        if (providerType == typeof(IRangeValueProvider))
            return AccessibilityMapping.IsRangeRole(Node.Role) ? this : null;

        if (providerType == typeof(IScrollProvider))
            return _view.ScrollMetricsOf(Node) is not null ? this : null;

        return null;
    }

    // IInvokeProvider: a link is followed and a button pressed, as a click would.
    public void Invoke() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.InvokeFromAutomation(Node));

    // IToggleProvider: a check box or radio button, as a screen reader toggles it: the page does what a click on it does.
    public AvaloniaToggleState ToggleState => AccessibilityMapping.ToToggleState(Current.Checked);

    public void Toggle() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.ToggleFromAutomation(Node));

    // IValueProvider and IRangeValueProvider share names, so the range ones are explicit.
    public string? Value => _view.FieldValue(Current) ?? string.Empty;

    public bool IsReadOnly => Current.ReadOnly || Node.Disabled;

    public void SetValue(string? value) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.SetValueFromAutomation(Node, value ?? string.Empty));

    double IRangeValueProvider.Value => Current.Value ?? Node.ValueMin;

    double IRangeValueProvider.Minimum => Current.ValueMin;

    double IRangeValueProvider.Maximum => Current.ValueMax;

    double IRangeValueProvider.SmallChange => AccessibilityMapping.RangeSteps(Current.ValueMin, Current.ValueMax).Small;

    double IRangeValueProvider.LargeChange => AccessibilityMapping.RangeSteps(Current.ValueMin, Current.ValueMax).Large;

    bool IRangeValueProvider.IsReadOnly => Node.Role != AccessibilityRole.Slider || Node.ReadOnly || Node.Disabled;

    void IRangeValueProvider.SetValue(double value)
    {
        if (Node.Role == AccessibilityRole.Slider && !Node.ReadOnly && !Node.Disabled)
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.SetValueFromAutomation(Node, value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // IScrollProvider: a node whose element is a scroll container of the document (a list, a region, a table with overflow: auto) scrolls as it does from the wheel.
    private PeachBrackets.ScrollMetrics? Scrolled => _view.ScrollMetricsOf(Node);

    public bool HorizontallyScrollable => Scrolled is { } m && m.MaxX - m.MinX > 0;

    public double HorizontalScrollPercent => Scrolled is { } m && m.MaxX - m.MinX > 0 ? (m.X - m.MinX) / (m.MaxX - m.MinX) * 100 : -1;

    public double HorizontalViewSize => Scrolled is { ContentWidth: > 0 } m ? Math.Min(100, m.Viewport.Width / m.ContentWidth * 100) : 100;

    public bool VerticallyScrollable => Scrolled is { } m && m.MaxY - m.MinY > 0;

    public double VerticalScrollPercent => Scrolled is { } m && m.MaxY - m.MinY > 0 ? (m.Y - m.MinY) / (m.MaxY - m.MinY) * 100 : -1;

    public double VerticalViewSize => Scrolled is { ContentHeight: > 0 } m ? Math.Min(100, m.Viewport.Height / m.ContentHeight * 100) : 100;

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount) =>
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.ScrollFromAutomation(Node, horizontalAmount, verticalAmount));

    public void SetScrollPercent(double horizontalPercent, double verticalPercent) =>
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.ScrollToPercentFromAutomation(Node, horizontalPercent, verticalPercent));
}
