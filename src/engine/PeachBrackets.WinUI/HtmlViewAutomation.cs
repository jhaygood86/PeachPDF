using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using PeachBrackets.Accessibility;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    private AccessibilityTree? _accessibilityTree;

    internal AccessibilityTree? AccessibilityTree => _accessibilityTree;

    /// <summary>What changed in the last refresh of the tree, for the tests: null when there was no tree before it.</summary>
    internal AccessibilityTreeDiff? LastAccessibilityDiff { get; private set; }

    /// <summary>
    /// Shows a change an application made to the document itself - through its text-editing and style-setting methods - which the view cannot see: it repaints what changed, and tells a screen reader what a live region (<c>aria-live</c>) now says and what
    /// else about the page changed.
    /// </summary>
    /// <param name="result">what the document returned for the change</param>
    public void ApplyUpdate(PeachBrackets.Updates.UpdateResult result)
    {
        if (_document is not { } document)
            return;

        Repaint(result);
        if (result.Applied)
            RefreshAccessibility(document);
    }

    // Called when a document has laid out: the tree a screen reader walks is rebuilt from it.
    internal void RefreshAccessibility(HtmlDocument document)
    {
        var before = _accessibilityTree;
        var after = _accessibilityTree = document.GetAccessibilityTree();
        if (FrameworkElementAutomationPeer.FromElement(_host) is not HtmlViewAutomationPeer peer)
            return;

        // What changed decides what is told: nothing at all when the tree is the same, a structure change when parts came or went, a property change
        // for a check box that was toggled, and a notification for what a live region should now say.
        var diff = LastAccessibilityDiff = before is null ? null : AccessibilityTreeDiff.Compare(before, after);
        if (diff is { IsEmpty: true })
            return;

        peer.Invalidate();
        if (AutomationPeer.ListenerExists(AutomationEvents.StructureChanged))
            peer.RaiseAutomationEvent(AutomationEvents.StructureChanged);

        if (diff is null)
            return;

        foreach (var change in diff.Changes)
        {
            if (change is { Kind: AccessibilityChangeKind.StateChanged, Property: "checked" } && peer.FindPeer(change.Node) is { } nodePeer)
            {
                var previous = before!.FindById(change.Node.Id)?.Checked ?? PeachBrackets.Accessibility.ToggleState.None;
                nodePeer.RaisePropertyChangedEvent(
                    Microsoft.UI.Xaml.Automation.TogglePatternIdentifiers.ToggleStateProperty,
                    HtmlViewLogic.ToToggleState(previous), HtmlViewLogic.ToToggleState(change.Node.Checked));
            }
        }

        RaiseAnnouncements(peer, diff.Announcements);
    }

    // What a live region should now say, told to UI Automation as notifications.
    internal static void RaiseAnnouncements(HtmlViewAutomationPeer peer, IEnumerable<AccessibilityAnnouncement> announcements)
    {
        foreach (var announcement in announcements)
        {
            peer.RaiseNotificationEvent(
                Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.Other,
                HtmlViewLogic.ToNotificationProcessing(announcement.Politeness),
                announcement.Text,
                announcement.Region.Id);
        }
    }

    /// <summary>Where a node is on the screen, in the screen's own pixels, given where the view is.</summary>
    internal Rect NodeBounds(AccessibilityNode node, Rect viewOnScreen)
    {
        // A node that crosses a page break is on more than one page; it is reported where it starts.
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return default;

        var (pageIndex, bounds) = node.Bounds[0];
        var zoom = ViewZoom;
        var x = (bounds.X * PixelsPerPoint * zoom) - ViewHorizontalOffset;
        var y = ((_pageTops[pageIndex] + (bounds.Y * PixelsPerPoint)) * zoom) - ViewVerticalOffset;
        return new Rect(viewOnScreen.X + x, viewOnScreen.Y + y, bounds.Width * PixelsPerPoint * zoom, bounds.Height * PixelsPerPoint * zoom);
    }

    /// <summary>Whether a node lies outside what the view shows.</summary>
    internal bool IsOffscreen(AccessibilityNode node)
    {
        var rect = NodeBounds(node, new Rect(0, 0, 0, 0));
        // An auto-height view shows all of itself; whether the list it is in shows it is for the list to say.
        if (_scrollMode == HtmlViewScrollMode.Disabled)
            return false;

        return rect.Bottom < 0 || rect.Top > _scroller.ViewportHeight || rect.Right < 0 || rect.Left > _scroller.ViewportWidth;
    }

    /// <summary>Follows a link a screen reader activated, as a click on it would.</summary>
    internal void ActivateLink(AccessibilityNode node)
    {
        if (node.Uri is not { } uri)
            return;

        var args = new HtmlLinkClickedEventArgs(uri.StartsWith('#') ? null : uri, uri.StartsWith('#') ? uri[1..] : null);
        LinkClicked?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (uri.StartsWith('#'))
            ScrollToElement(uri[1..]);
        else if (HtmlViewLogic.IsLaunchable(uri))
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    /// <summary>Scrolls a node into view when a screen reader moves to it.</summary>
    internal void BringIntoView(AccessibilityNode node)
    {
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return;

        var (pageIndex, bounds) = node.Bounds[0];
        ScrollToY(Math.Max(0, _pageTops[pageIndex] + (bounds.Y * PixelsPerPoint) - 40 / ViewZoom));
    }
}

// A UserControl is not asked for an automation peer, so the one for the view is carried by the element it hosts its content in.
internal sealed class HtmlViewHost : ContentControl
{
    private readonly HtmlView _owner;

    public HtmlViewHost(HtmlView owner)
    {
        _owner = owner;
        // Shows its content and nothing else: the control exists to carry the automation peer.
        Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContentControl'><ContentPresenter/></ControlTemplate>");
        IsTabStop = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new HtmlViewAutomationPeer(_owner, this);
    }
}

/// <summary>
/// What UI Automation sees of an <see cref="HtmlView"/>: a document whose children are the blocks of text (paragraphs, headings, list items) with the
/// links inside them below, so a screen reader can read the page, jump between headings and follow links.
/// </summary>
public sealed class HtmlViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly HtmlView _view;
    private List<AutomationPeer>? _children;

    internal HtmlViewAutomationPeer(HtmlView owner, FrameworkElement host) : base(host) => _view = owner;

    internal void Invalidate() => _children = null;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(HtmlView);

    /// <inheritdoc />
    protected override string GetNameCore() =>
        base.GetNameCore() is { Length: > 0 } name ? name : "HTML document";

    /// <inheritdoc />
    protected override IList<AutomationPeer> GetChildrenCore()
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

    internal Rect ViewBounds => GetBoundingRectangle();

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Text && _view.AccessibilityTree is { } tree
            ? new HtmlTextProvider(this, _view, tree.Root)
            : base.GetPatternCore(patternInterface);

    /// <summary>The provider a peer of this tree is known to UI Automation by.</summary>
    internal IRawElementProviderSimple? ProviderOf(AutomationPeer peer) => ProviderFromPeer(peer);

    /// <summary>The node a provider handed out for this tree stands for, or null if it is not one of ours.</summary>
    internal AccessibilityNode? NodeOf(IRawElementProviderSimple provider) => (PeerFromProvider(provider) as HtmlNodeAutomationPeer)?.Node;

    /// <summary>The peer that stands for a node, if the tree of peers has been asked for.</summary>
    internal HtmlNodeAutomationPeer? FindPeer(AccessibilityNode node)
    {
        GetChildrenCore();
        return _children is null ? null : Find(_children, node);

        static HtmlNodeAutomationPeer? Find(IEnumerable<AutomationPeer> peers, AccessibilityNode node)
        {
            foreach (var peer in peers.OfType<HtmlNodeAutomationPeer>())
            {
                if (peer.Node.Id == node.Id)
                    return peer;

                if (Find(peer.Peers, node) is { } below)
                    return below;
            }

            return null;
        }
    }
}

/// <summary>One part of the page - a paragraph, a heading, a link, a control, a landmark - in the tree a screen reader walks.</summary>
internal sealed class HtmlNodeAutomationPeer : AutomationPeer, IInvokeProvider, IToggleProvider, IValueProvider, IRangeValueProvider, IGridProvider, ITableProvider, IGridItemProvider, ITableItemProvider
{
    private readonly HtmlViewAutomationPeer _root;
    private readonly HtmlView _view;
    private readonly AccessibilityNode _node;
    private readonly List<AutomationPeer> _children = [];

    public HtmlNodeAutomationPeer(HtmlViewAutomationPeer root, AutomationPeer parent, HtmlView view, AccessibilityNode node)
    {
        _root = root;
        _view = view;
        _node = node;
        SetParent(parent);
        foreach (var child in node.ReadingChildren)
            _children.Add(new HtmlNodeAutomationPeer(root, this, view, child));
    }

    internal AccessibilityNode Node => _node;

    internal IReadOnlyList<AutomationPeer> Peers => _children;

    protected override AutomationControlType GetAutomationControlTypeCore() => HtmlViewLogic.ToControlType(_node.Role);

    protected override string GetClassNameCore() => "HtmlNode";

    protected override string GetNameCore() => _node.ReadingText;

    protected override string GetHelpTextCore() => _node.Description;

    protected override string GetAutomationIdCore() => _node.Id;

    protected override AutomationHeadingLevel GetHeadingLevelCore() => _node.Role == AccessibilityRole.Heading
        ? (AutomationHeadingLevel)Math.Clamp(_node.Level, 1, 9)
        : AutomationHeadingLevel.None;

    protected override int GetPositionInSetCore() => _node.PositionInSet > 0 ? _node.PositionInSet : -1;

    protected override int GetSizeOfSetCore() => _node.SetSize > 0 ? _node.SetSize : -1;

    protected override AutomationLandmarkType GetLandmarkTypeCore() => HtmlViewLogic.ToLandmarkType(_node.Role);

    protected override string GetLocalizedLandmarkTypeCore() => HtmlViewLogic.LandmarkName(_node.Role) ?? base.GetLocalizedLandmarkTypeCore();

    protected override AutomationLiveSetting GetLiveSettingCore() => HtmlViewLogic.ToLiveSetting(_node.Live);

    protected override string GetAcceleratorKeyCore() => _node.KeyShortcuts ?? string.Empty;

    protected override string GetAccessKeyCore() => _node.AccessKey ?? string.Empty;

    protected override bool IsRequiredForFormCore() => _node.Required;

    protected override Rect GetBoundingRectangleCore() => _view.NodeBounds(_node, _root.ViewBounds);

    protected override bool IsOffscreenCore() => _view.IsOffscreen(_node);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => !_node.Disabled;

    protected override bool IsKeyboardFocusableCore() => _node.IsFocusable;

    protected override bool HasKeyboardFocusCore() => _view.FocusedNode?.Id == _node.Id;

    protected override IList<AutomationPeer> GetChildrenCore() => _children;

    protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Invoke when _node.Role is AccessibilityRole.Link or AccessibilityRole.Button => this,
        PatternInterface.Toggle when HtmlViewLogic.IsToggleControl(_node.Role) => this,
        PatternInterface.Value when _node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox => this,
        PatternInterface.Text when _node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox => new HtmlTextProvider(_root, _view, _node),
        PatternInterface.RangeValue when HtmlViewLogic.IsRangeRole(_node.Role) => this,
        PatternInterface.Grid or PatternInterface.Table when HtmlViewLogic.IsGridRole(_node.Role) => this,
        PatternInterface.GridItem or PatternInterface.TableItem when HtmlViewLogic.IsGridCellRole(_node.Role) && HtmlViewLogic.TableOf(_node) is not null => this,
        _ => null,
    };

    protected override void ShowContextMenuCore() { }

    protected override void SetFocusCore()
    {
        if (_node.IsFocusable)
            _view.DispatcherQueue.TryEnqueue(() => _view.FocusFromAutomation(_node));
        else
            _view.BringIntoView(_node);
    }

    public void Invoke() => _view.DispatcherQueue.TryEnqueue(() =>
    {
        if (_node.Role == AccessibilityRole.Button)
            _view.InvokeButton(_node);
        else
            _view.ActivateLink(_node);
    });

    // A check box or radio button, as a screen reader toggles it: the page does what a click on it does.
    public Microsoft.UI.Xaml.Automation.ToggleState ToggleState => HtmlViewLogic.ToToggleState(_view.CurrentNode(_node).Checked);

    public void Toggle() => _view.DispatcherQueue.TryEnqueue(() => _view.ActivateControl(_node));

    // A text field, as a screen reader reads and sets it.
    public string Value => _view.FieldValue(_view.CurrentNode(_node)) ?? string.Empty;

    public bool IsReadOnly => _view.CurrentNode(_node).ReadOnly || _node.Disabled;

    public void SetValue(string value) => _view.DispatcherQueue.TryEnqueue(() => _view.SetFieldValue(_view.CurrentNode(_node), value));

    // A progress bar, meter or slider, as a screen reader reads it and a slider is moved. These are explicit because a text field's Value and
    // IsReadOnly (above) have the same names with other types.
    double IRangeValueProvider.Value => _view.CurrentNode(_node).Value ?? _node.ValueMin;

    double IRangeValueProvider.Minimum => _view.CurrentNode(_node).ValueMin;

    double IRangeValueProvider.Maximum => _view.CurrentNode(_node).ValueMax;

    double IRangeValueProvider.SmallChange => HtmlViewLogic.RangeSteps(_view.CurrentNode(_node).ValueMin, _view.CurrentNode(_node).ValueMax).Small;

    double IRangeValueProvider.LargeChange => HtmlViewLogic.RangeSteps(_view.CurrentNode(_node).ValueMin, _view.CurrentNode(_node).ValueMax).Large;

    bool IRangeValueProvider.IsReadOnly => _node.Role != AccessibilityRole.Slider || _node.ReadOnly || _node.Disabled;

    void IRangeValueProvider.SetValue(double value)
    {
        if (_node.Role == AccessibilityRole.Slider && !_node.ReadOnly && !_node.Disabled)
            _view.DispatcherQueue.TryEnqueue(() => _view.SetFieldValue(_view.CurrentNode(_node), value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ---- tables: the table as a grid, a cell as an item of it --------------------------------------------------------------------------

    public int RowCount => _node.RowCount;

    public int ColumnCount => _node.ColumnCount;

    public IRawElementProviderSimple? GetItem(int row, int column) => ProviderOf(HtmlViewLogic.CellAt(_node, row, column));

    public RowOrColumnMajor RowOrColumnMajor => RowOrColumnMajor.RowMajor;

    public IRawElementProviderSimple[] GetColumnHeaders() => ProvidersOf(HtmlViewLogic.HeadersOf(_node, columns: true));

    public IRawElementProviderSimple[] GetRowHeaders() => ProvidersOf(HtmlViewLogic.HeadersOf(_node, columns: false));

    public int Row => Math.Max(_node.RowIndex, 0);

    public int Column => Math.Max(_node.ColumnIndex, 0);

    public int RowSpan => Math.Max(_node.RowSpan, 1);

    public int ColumnSpan => Math.Max(_node.ColumnSpan, 1);

    public IRawElementProviderSimple? ContainingGrid => ProviderOf(HtmlViewLogic.TableOf(_node));

    public IRawElementProviderSimple[] GetColumnHeaderItems() => ProvidersOf(_node.ColumnHeaders);

    public IRawElementProviderSimple[] GetRowHeaderItems() => ProvidersOf(_node.RowHeaders);

    private IRawElementProviderSimple? ProviderOf(AccessibilityNode? node) =>
        node is not null && _root.FindPeer(node) is { } peer ? ProviderFromPeer(peer) : null;

    private IRawElementProviderSimple[] ProvidersOf(IEnumerable<AccessibilityNode> nodes) =>
        [.. nodes.Select(ProviderOf).OfType<IRawElementProviderSimple>()];
}
