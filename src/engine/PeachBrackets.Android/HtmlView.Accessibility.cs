using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using PeachBrackets.Android.Logic;
using AndroidUri = Android.Net.Uri;
using Action = Android.Views.Accessibility.Action;

// The Obtain factories and SetBoundsInParent are deprecated on recent API levels but are the only ones that exist from API 24, the lowest this
// package supports; the replacements (the AccessibilityNodeInfo constructors) need API 30.
#pragma warning disable CA1422

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    private IReadOnlyList<AccessibleNode> _accessibleNodes = [];
    private TextNodeProvider? _accessibility;
    private int _accessibilityFocus = AccessibilityNodeProvider.HostViewId;

    /// <inheritdoc />
    public override AccessibilityNodeProvider? AccessibilityNodeProvider => _accessibility ??= new TextNodeProvider(this);

    // Called when a document has laid out: the tree an accessibility service walks is rebuilt from it.
    private void RefreshAccessibility(HtmlDocument document)
    {
        _accessibleNodes = document.GetAccessibleNodes();
        _accessibilityFocus = AccessibilityNodeProvider.HostViewId;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        if (IsAccessibilityEnabled())
            NotifySubtreeAccessibilityStateChanged(this, this, ContentChangeTypes.Subtree);
    }

    private bool IsAccessibilityEnabled() =>
        Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true };

    // A node's rectangle in the view's own pixels, as it is drawn now.
    private global::Android.Graphics.Rect NodeRect(AccessibleNode node)
    {
        if ((uint)node.PageIndex >= (uint)_pageTops.Length)
            return new global::Android.Graphics.Rect();

        var scale = Scale;
        var left = (node.Bounds.X * PixelsPerPoint * scale) - _scrollX;
        var top = ((_pageTops[node.PageIndex] + (node.Bounds.Y * PixelsPerPoint)) * scale) - _scrollY;
        return new global::Android.Graphics.Rect((int)left, (int)top, (int)(left + (node.Bounds.Width * PixelsPerPoint * scale)), (int)(top + (node.Bounds.Height * PixelsPerPoint * scale)));
    }

    private void FollowLinkNode(AccessibleNode node)
    {
        if (node.Uri is not { } uri)
            return;

        var args = new HtmlLinkTappedEventArgs(uri.StartsWith('#') ? null : uri, uri.StartsWith('#') ? uri[1..] : null);
        LinkTapped?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (uri.StartsWith('#'))
        {
            ScrollToElement(uri[1..]);
        }
        else if (PageLayout.IsLaunchable(uri) && Context is { } context)
        {
            var intent = new Intent(Intent.ActionView, AndroidUri.Parse(uri));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
    }

    // Scrolls so that a node an accessibility service moved to is on screen.
    private void RevealNode(AccessibleNode node)
    {
        var rect = NodeRect(node);
        if (rect.Top < 0)
            ScrollTo(_scrollX, _scrollY + rect.Top - (Height / 8));
        else if (rect.Bottom > Height)
            ScrollTo(_scrollX, _scrollY + (rect.Bottom - Height) + (Height / 8));
    }

    private void SendNodeEvent(int virtualViewId, EventTypes type)
    {
        if (!IsAccessibilityEnabled() || Parent is null)
            return;

        var accessibilityEvent = AccessibilityEvent.Obtain(type);
        if (accessibilityEvent is null)
            return;

        accessibilityEvent.SetSource(this, virtualViewId);
        accessibilityEvent.PackageName = Context?.PackageName;
        accessibilityEvent.ClassName = "android.widget.TextView";
        if (virtualViewId > 0 && virtualViewId <= _accessibleNodes.Count)
            accessibilityEvent.Text?.Add(new Java.Lang.String(_accessibleNodes[virtualViewId - 1].Text));

        Parent.RequestSendAccessibilityEvent(this, accessibilityEvent);
    }

    // The blocks of text and the links of the document as virtual views: the host is -1 and node i is i + 1.
    private sealed class TextNodeProvider(HtmlView view) : AccessibilityNodeProvider
    {
        private const string TextClass = "android.widget.TextView";

        public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId)
        {
            return virtualViewId == HostViewId ? CreateHost() : CreateNode(virtualViewId);
        }

        private AccessibilityNodeInfo CreateHost()
        {
            var info = AccessibilityNodeInfo.Obtain(view)!;
            view.OnInitializeAccessibilityNodeInfo(info);
            info.ClassName = "android.webkit.WebView";
            info.ContentDescription = "HTML document";
            info.Scrollable = true;
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollForward);
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollBackward);
            for (var i = 0; i < view._accessibleNodes.Count; i++)
            {
                if (view._accessibleNodes[i].ParentIndex < 0)
                    info.AddChild(view, i + 1);
            }

            return info;
        }

        private AccessibilityNodeInfo? CreateNode(int virtualViewId)
        {
            var index = virtualViewId - 1;
            if ((uint)index >= (uint)view._accessibleNodes.Count)
                return null;

            var node = view._accessibleNodes[index];
            var info = AccessibilityNodeInfo.Obtain()!;
            info.SetSource(view, virtualViewId);
            info.SetParent(view, node.ParentIndex >= 0 ? node.ParentIndex + 1 : HostViewId);
            info.PackageName = view.Context?.PackageName;
            info.ClassName = TextClass;
            info.Text = node.Text;
            info.Enabled = true;
            info.VisibleToUser = true;

            var rect = view.NodeRect(node);
            info.SetBoundsInParent(rect);
            var location = new int[2];
            view.GetLocationOnScreen(location);
            rect.Offset(location[0], location[1]);
            info.SetBoundsInScreen(rect);

            if (node.Role == AccessibleRole.Heading && OperatingSystem.IsAndroidVersionAtLeast(28))
                info.Heading = true;

            if (node.Role == AccessibleRole.Link)
            {
                info.Clickable = true;
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
            }

            info.AccessibilityFocused = view._accessibilityFocus == virtualViewId;
            info.AddAction(info.AccessibilityFocused
                ? AccessibilityNodeInfo.AccessibilityAction.ActionClearAccessibilityFocus
                : AccessibilityNodeInfo.AccessibilityAction.ActionAccessibilityFocus);

            for (var i = 0; i < view._accessibleNodes.Count; i++)
            {
                if (view._accessibleNodes[i].ParentIndex == index)
                    info.AddChild(view, i + 1);
            }

            return info;
        }

        public override bool PerformAction(int virtualViewId, Action action, Bundle? arguments)
        {
            if (virtualViewId == HostViewId)
            {
                switch (action)
                {
                    case Action.ScrollForward:
                        view.ScrollTo(view._scrollX, view._scrollY + (view.Height * 0.8));
                        return true;
                    case Action.ScrollBackward:
                        view.ScrollTo(view._scrollX, view._scrollY - (view.Height * 0.8));
                        return true;
                    default:
                        return view.PerformAccessibilityAction(action, arguments);
                }
            }

            var index = virtualViewId - 1;
            if ((uint)index >= (uint)view._accessibleNodes.Count)
                return false;

            var node = view._accessibleNodes[index];
            switch (action)
            {
                case Action.AccessibilityFocus:
                    view._accessibilityFocus = virtualViewId;
                    view.RevealNode(node);
                    view._surface.Invalidate();
                    view.SendNodeEvent(virtualViewId, EventTypes.ViewAccessibilityFocused);
                    return true;
                case Action.ClearAccessibilityFocus:
                    if (view._accessibilityFocus == virtualViewId)
                        view._accessibilityFocus = HostViewId;

                    view.SendNodeEvent(virtualViewId, EventTypes.ViewAccessibilityFocusCleared);
                    return true;
                case Action.Click when node.Role == AccessibleRole.Link:
                    view.FollowLinkNode(node);
                    return true;
                default:
                    return false;
            }
        }

        public override IList<AccessibilityNodeInfo>? FindAccessibilityNodeInfosByText(string? text, int virtualViewId)
        {
            var found = new List<AccessibilityNodeInfo>();
            if (string.IsNullOrEmpty(text))
                return found;

            for (var i = 0; i < view._accessibleNodes.Count; i++)
            {
                if (view._accessibleNodes[i].Text.Contains(text, StringComparison.OrdinalIgnoreCase) && CreateNode(i + 1) is { } info)
                    found.Add(info);
            }

            return found;
        }

        public override AccessibilityNodeInfo? FindFocus(NodeFocus focus) =>
            view._accessibilityFocus != HostViewId ? CreateNode(view._accessibilityFocus) : null;
    }
}
