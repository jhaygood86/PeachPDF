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

    // Called when a document has laid out: the tree a screen reader walks is rebuilt from it.
    private void RefreshAccessibility(HtmlDocument document)
    {
        _accessibilityTree = document.GetAccessibilityTree();
        if (AutomationPeer.ListenerExists(AutomationEvents.StructureChanged) && FrameworkElementAutomationPeer.FromElement(_host) is HtmlViewAutomationPeer peer)
        {
            peer.Invalidate();
            peer.RaiseAutomationEvent(AutomationEvents.StructureChanged);
        }
    }

    /// <summary>Where a node is on the screen, in the screen's own pixels, given where the view is.</summary>
    internal Rect NodeBounds(AccessibilityNode node, Rect viewOnScreen)
    {
        // A node that crosses a page break is on more than one page; it is reported where it starts.
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return default;

        var (pageIndex, bounds) = node.Bounds[0];
        var zoom = _scroller.ZoomFactor;
        var x = (bounds.X * PixelsPerPoint * zoom) - _scroller.HorizontalOffset;
        var y = ((_pageTops[pageIndex] + (bounds.Y * PixelsPerPoint)) * zoom) - _scroller.VerticalOffset;
        return new Rect(viewOnScreen.X + x, viewOnScreen.Y + y, bounds.Width * PixelsPerPoint * zoom, bounds.Height * PixelsPerPoint * zoom);
    }

    /// <summary>Whether a node lies outside what the view shows.</summary>
    internal bool IsOffscreen(AccessibilityNode node)
    {
        var rect = NodeBounds(node, new Rect(0, 0, 0, 0));
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
        var y = (_pageTops[pageIndex] + (bounds.Y * PixelsPerPoint)) * _scroller.ZoomFactor;
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
}

/// <summary>One part of the page - a paragraph, a heading, a link, a control, a landmark - in the tree a screen reader walks.</summary>
internal sealed class HtmlNodeAutomationPeer : AutomationPeer, IInvokeProvider
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

    // The view does not move keyboard focus between the parts of a page yet, so none of them claims to take it.
    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool HasKeyboardFocusCore() => false;

    protected override IList<AutomationPeer> GetChildrenCore() => _children;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke && _node.Role == AccessibilityRole.Link ? this : null;

    protected override void ShowContextMenuCore() { }

    protected override void SetFocusCore() => _view.BringIntoView(_node);

    public void Invoke() => _view.DispatcherQueue.TryEnqueue(() => _view.ActivateLink(_node));
}
