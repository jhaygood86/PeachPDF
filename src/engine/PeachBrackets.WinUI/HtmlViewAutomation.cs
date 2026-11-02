using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    private IReadOnlyList<AccessibleNode> _accessibleNodes = [];

    internal IReadOnlyList<AccessibleNode> AccessibleNodes => _accessibleNodes;

    // Called when a document has laid out: the tree a screen reader walks is rebuilt from it.
    private void RefreshAccessibility(HtmlDocument document)
    {
        _accessibleNodes = document.GetAccessibleNodes();
        if (AutomationPeer.ListenerExists(AutomationEvents.StructureChanged) && FrameworkElementAutomationPeer.FromElement(_host) is HtmlViewAutomationPeer peer)
        {
            peer.Invalidate();
            peer.RaiseAutomationEvent(AutomationEvents.StructureChanged);
        }
    }

    /// <summary>Where a node is on the screen, in the screen's own pixels, given where the view is.</summary>
    internal Rect NodeBounds(AccessibleNode node, Rect viewOnScreen)
    {
        if ((uint)node.PageIndex >= (uint)_pageTops.Length)
            return default;

        var zoom = _scroller.ZoomFactor;
        var x = (node.Bounds.X * PixelsPerPoint * zoom) - _scroller.HorizontalOffset;
        var y = ((_pageTops[node.PageIndex] + (node.Bounds.Y * PixelsPerPoint)) * zoom) - _scroller.VerticalOffset;
        return new Rect(viewOnScreen.X + x, viewOnScreen.Y + y, node.Bounds.Width * PixelsPerPoint * zoom, node.Bounds.Height * PixelsPerPoint * zoom);
    }

    /// <summary>Whether a node lies outside what the view shows.</summary>
    internal bool IsOffscreen(AccessibleNode node)
    {
        var rect = NodeBounds(node, new Rect(0, 0, 0, 0));
        return rect.Bottom < 0 || rect.Top > _scroller.ViewportHeight || rect.Right < 0 || rect.Left > _scroller.ViewportWidth;
    }

    /// <summary>Follows a link a screen reader activated, as a click on it would.</summary>
    internal void ActivateLink(AccessibleNode node)
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
    internal void BringIntoView(AccessibleNode node)
    {
        if ((uint)node.PageIndex >= (uint)_pageTops.Length)
            return;

        var y = (_pageTops[node.PageIndex] + (node.Bounds.Y * PixelsPerPoint)) * _scroller.ZoomFactor;
        _scroller.ChangeView(null, Math.Max(0, y - 40), null);
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
            var nodes = _view.AccessibleNodes;
            var peers = new List<AutomationPeer>();
            var blocks = new Dictionary<int, HtmlNodeAutomationPeer>();
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].ParentIndex >= 0 && blocks.TryGetValue(nodes[i].ParentIndex, out var block))
                {
                    block.AddChild(new HtmlNodeAutomationPeer(this, block, _view, nodes[i], i));
                    continue;
                }

                var peer = new HtmlNodeAutomationPeer(this, this, _view, nodes[i], i);
                peers.Add(peer);
                blocks[i] = peer;
            }

            _children = peers;
        }

        return _children;
    }

    internal Rect ViewBounds => GetBoundingRectangle();
}

/// <summary>One block of text, or one link, in the tree a screen reader walks.</summary>
internal sealed class HtmlNodeAutomationPeer : AutomationPeer, IInvokeProvider
{
    private readonly HtmlViewAutomationPeer _root;
    private readonly HtmlView _view;
    private readonly AccessibleNode _node;
    private readonly int _index;
    private readonly List<AutomationPeer> _children = [];

    public HtmlNodeAutomationPeer(HtmlViewAutomationPeer root, AutomationPeer parent, HtmlView view, AccessibleNode node, int index)
    {
        _root = root;
        _view = view;
        _node = node;
        _index = index;
        SetParent(parent);
    }

    public void AddChild(AutomationPeer child) => _children.Add(child);

    protected override AutomationControlType GetAutomationControlTypeCore() => _node.Role switch
    {
        AccessibleRole.Link => AutomationControlType.Hyperlink,
        AccessibleRole.ListItem => AutomationControlType.ListItem,
        _ => AutomationControlType.Text,
    };

    protected override string GetClassNameCore() => "HtmlNode";

    protected override string GetNameCore() => _node.Text;

    protected override string GetAutomationIdCore() => "node" + _index;

    protected override AutomationHeadingLevel GetHeadingLevelCore() => _node.Role == AccessibleRole.Heading
        ? (AutomationHeadingLevel)Math.Clamp(_node.HeadingLevel, 1, 9)
        : AutomationHeadingLevel.None;

    protected override Rect GetBoundingRectangleCore() => _view.NodeBounds(_node, _root.ViewBounds);

    protected override bool IsOffscreenCore() => _view.IsOffscreen(_node);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool HasKeyboardFocusCore() => false;

    protected override IList<AutomationPeer> GetChildrenCore() => _children;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke && _node.Role == AccessibleRole.Link ? this : null;

    protected override void ShowContextMenuCore() { }

    protected override void SetFocusCore() => _view.BringIntoView(_node);

    public void Invoke() => _view.DispatcherQueue.TryEnqueue(() => _view.ActivateLink(_node));
}
