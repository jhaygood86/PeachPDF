using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using PeachBrackets.Accessibility;
using PeachBrackets.Android.Logic;
using AndroidUri = Android.Net.Uri;
using Action = Android.Views.Accessibility.Action;

// The Obtain factories and SetBoundsInParent are deprecated on recent API levels but are the only ones that exist from API 24, the lowest this
// package supports; the replacements (the AccessibilityNodeInfo constructors) need API 30.
#pragma warning disable CA1422

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    // The parts of the page an accessibility service steps to, in reading order: virtual view i + 1 is _a11yNodes[i], and the host is -1.
    private AccessibilityNode[] _a11yNodes = [];
    private int[] _a11yParents = [];
    private Dictionary<AccessibilityNode, int> _a11yIds = new(ReferenceEqualityComparer.Instance);
    private TextNodeProvider? _accessibility;
    private int _accessibilityFocus = AccessibilityNodeProvider.HostViewId;

    /// <inheritdoc/>
    public override AccessibilityNodeProvider? AccessibilityNodeProvider => _accessibility ??= new TextNodeProvider(this);

    // Called when a document has laid out: the tree an accessibility service walks is rebuilt from it.
    private void RefreshAccessibility(HtmlDocument document)
    {
        var before = _accessibilityTree;
        var after = _accessibilityTree = document.GetAccessibilityTree();
        var diff = before is null ? null : AccessibilityTreeDiff.Compare(before, after);

        // Accessibility focus stays on the same part of the page when the tree is taken again (the page was restyled, say), not on the same number.
        var focusedId = _accessibilityFocus > 0 && _accessibilityFocus <= _a11yNodes.Length ? _a11yNodes[_accessibilityFocus - 1].Id : null;
        var nodes = new List<AccessibilityNode>();
        var parents = new List<int>();
        var ids = new Dictionary<AccessibilityNode, int>(ReferenceEqualityComparer.Instance);

        void Add(AccessibilityNode node, int parentId)
        {
            nodes.Add(node);
            parents.Add(parentId);
            var id = nodes.Count;
            ids[node] = id;
            foreach (var child in node.ReadingChildren)
                Add(child, id);
        }

        foreach (var child in after.Root.ReadingChildren)
            Add(child, AccessibilityNodeProvider.HostViewId);

        _a11yNodes = [.. nodes];
        _textCursor.Clear();
        _a11yParents = [.. parents];
        _a11yIds = ids;
        _accessibilityFocus = focusedId is not null && nodes.FindIndex(n => n.Id == focusedId) is >= 0 and var kept ? kept + 1 : AccessibilityNodeProvider.HostViewId;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        if (!IsAccessibilityEnabled() || diff is { IsEmpty: true })
            return;

        // What changed decides what is told: the whole subtree for a first tree, a part that came or went, or else only the kinds of change that
        // happened, and for a live region that changed an event from the region itself, which is what makes TalkBack read it.
        var changeTypes = diff is null ? ContentChangeTypes.Subtree : (ContentChangeTypes)AccessibilityMapping.ContentChangeTypes(diff.Changes.Select(c => c.Kind));
        NotifySubtreeAccessibilityStateChanged(this, this, changeTypes);
        if (diff is null)
            return;

        foreach (var announcement in diff.Announcements)
        {
            if (nodes.FindIndex(n => n.Id == announcement.Region.Id) is >= 0 and var index)
                SendContentChanged(index + 1, ContentChangeTypes.Text);
        }
    }

    private AccessibilityTree? _accessibilityTree;

    // A window-content-changed event from one part of the page: for a live region, the text it now holds.
    private void SendContentChanged(int virtualViewId, ContentChangeTypes types)
    {
        if (Parent is null || AccessibilityEvent.Obtain(EventTypes.WindowContentChanged) is not { } accessibilityEvent)
            return;

        accessibilityEvent.SetSource(this, virtualViewId);
        accessibilityEvent.PackageName = Context?.PackageName;
        accessibilityEvent.ContentChangeTypes = types;
        if (virtualViewId <= _a11yNodes.Length)
        {
            var node = _a11yNodes[virtualViewId - 1];
            accessibilityEvent.ClassName = AccessibilityMapping.ClassName(node.Role);
            accessibilityEvent.Text?.Add(new Java.Lang.String(node.Text));
        }

        Parent.RequestSendAccessibilityEvent(this, accessibilityEvent);
    }

    private bool IsAccessibilityEnabled() =>
        Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true };

    // A node's rectangle in the view's own pixels, as it is drawn now. A node that crosses a page break is reported where it starts.
    private global::Android.Graphics.Rect NodeRect(AccessibilityNode node)
    {
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return new global::Android.Graphics.Rect();

        var (pageIndex, bounds) = node.Bounds[0];
        var scale = Scale;
        var left = (bounds.X * PixelsPerPoint * scale) - _scrollX;
        var top = ((_pageTops[pageIndex] + (bounds.Y * PixelsPerPoint)) * scale) - _scrollY;
        return new global::Android.Graphics.Rect((int)left, (int)top, (int)(left + (bounds.Width * PixelsPerPoint * scale)), (int)(top + (bounds.Height * PixelsPerPoint * scale)));
    }

    private void FollowLinkNode(AccessibilityNode node)
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
    private void RevealNode(AccessibilityNode node)
    {
        var rect = NodeRect(node);
        if (rect.Top < 0)
            ScrollTo(_scrollX, _scrollY + rect.Top - (Height / 8));
        else if (rect.Bottom > Height)
            ScrollTo(_scrollX, _scrollY + (rect.Bottom - Height) + (Height / 8));
    }

    // Where a service last stopped in a node's text, by node.
    private readonly Dictionary<int, int> _textCursor = [];

    // Moves through a node's text by the size of step the service asked for, and tells it where it landed (TalkBack reads that piece).
    private bool TraverseText(int virtualViewId, AccessibilityNode node, bool forward, Bundle? arguments)
    {
        var granularity = (TextGranularity)(arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentMovementGranularityInt) ?? 0);
        var text = node.ReadingText;
        var from = _textCursor.TryGetValue(virtualViewId, out var at) ? at : (forward ? 0 : text.Length);
        var step = forward ? TextTraversal.Next(text, from, granularity) : TextTraversal.Previous(text, from, granularity);
        if (step is not { } move)
            return false;

        _textCursor[virtualViewId] = forward ? move.End : move.Start;
        if (IsAccessibilityEnabled() && Parent is not null && AccessibilityEvent.Obtain(EventTypes.ViewTextTraversedAtMovementGranularity) is { } traversed)
        {
            traversed.SetSource(this, virtualViewId);
            traversed.PackageName = Context?.PackageName;
            traversed.ClassName = AccessibilityMapping.ClassName(node.Role);
            traversed.Text?.Add(new Java.Lang.String(text));
            traversed.FromIndex = move.Start;
            traversed.ToIndex = move.End;
            traversed.MovementGranularity = (MovementGranularity)(int)granularity;
            Parent.RequestSendAccessibilityEvent(this, traversed);
        }

        return true;
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
        if (virtualViewId > 0 && virtualViewId <= _a11yNodes.Length)
        {
            var node = _a11yNodes[virtualViewId - 1];
            accessibilityEvent.ClassName = AccessibilityMapping.ClassName(node.Role);
            accessibilityEvent.Text?.Add(new Java.Lang.String(node.ReadingText));
        }

        Parent.RequestSendAccessibilityEvent(this, accessibilityEvent);
    }

    // The parts of the document as virtual views: the host is -1 and node i is i + 1.
    private sealed class TextNodeProvider(HtmlView view) : AccessibilityNodeProvider
    {
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
            for (var i = 0; i < view._a11yNodes.Length; i++)
            {
                if (view._a11yParents[i] == HostViewId)
                    info.AddChild(view, i + 1);
            }

            return info;
        }

        private AccessibilityNodeInfo? CreateNode(int virtualViewId)
        {
            var index = virtualViewId - 1;
            if ((uint)index >= (uint)view._a11yNodes.Length)
                return null;

            var node = view._a11yNodes[index];
            var info = AccessibilityNodeInfo.Obtain()!;
            info.SetSource(view, virtualViewId);
            info.SetParent(view, view._a11yParents[index]);
            info.PackageName = view.Context?.PackageName;
            info.ClassName = AccessibilityMapping.ClassName(node.Role);
            info.Enabled = !node.Disabled;
            info.VisibleToUser = true;
            info.ContentInvalid = node.Invalid;
            info.LiveRegion = (AccessibilityLiveRegion)AccessibilityMapping.LiveRegion(node.Live);

            // An image has no text to read, only what it is described as; everything else is read as its text.
            if (node.Role is AccessibilityRole.Image or AccessibilityRole.GraphicsDocument)
                info.ContentDescription = node.ReadingText;
            else
                info.Text = node.ReadingText;

            // A service can read a node with text by character, word or paragraph; lines and pages depend on layout the node does not carry.
            if (node.ReadingText.Length > 0 && node.Role is not (AccessibilityRole.Image or AccessibilityRole.GraphicsDocument))
                info.MovementGranularities = (MovementGranularity)(int)TextTraversal.Supported;

            if (node.Description.Length > 0 && OperatingSystem.IsAndroidVersionAtLeast(28))
                info.TooltipText = node.Description;

            var rect = view.NodeRect(node);
            info.SetBoundsInParent(rect);
            var location = new int[2];
            view.GetLocationOnScreen(location);
            rect.Offset(location[0], location[1]);
            info.SetBoundsInScreen(rect);

            if (node.Role == AccessibilityRole.Heading && OperatingSystem.IsAndroidVersionAtLeast(28))
                info.Heading = true;

            // Where it is in a list or a table, so TalkBack can say "item 2 of 5" or "row 3, column 2" and step through cells.
            if (node.Role is AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid)
            {
                info.SetCollectionInfo(AccessibilityNodeInfo.CollectionInfo.Obtain(node.RowCount, node.ColumnCount, false));
            }
            else if (node.Role is AccessibilityRole.List or AccessibilityRole.ListBox)
            {
                info.SetCollectionInfo(AccessibilityNodeInfo.CollectionInfo.Obtain(node.ReadingChildren.Count(c => c.SetSize > 0), 1, false));
            }

            if (node.RowIndex >= 0 && node.ColumnIndex >= 0)
            {
                var heading = node.Role is AccessibilityRole.ColumnHeader or AccessibilityRole.RowHeader;
                info.SetCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.Obtain(node.RowIndex, node.RowSpan, node.ColumnIndex, node.ColumnSpan, heading, node.Selected ?? false));
            }
            else if (node.PositionInSet > 0)
            {
                info.SetCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.Obtain(node.PositionInSet - 1, 1, 0, 1, false, node.Selected ?? false));
            }

            // A text field: an accessibility service can replace its text, move its selection and paste into it.
            if (node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox)
            {
                info.Editable = true;
                info.InputType = (global::Android.Text.InputTypes)1;
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionSetSelection);
                if (!node.ReadOnly && !node.Disabled)
                {
                    info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionSetText);
                    info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionPaste);
                }
            }

            // A progress bar, meter or slider: where it stands in its range, which TalkBack says as a percentage and a slider can be moved through.
            if (node.Role is AccessibilityRole.ProgressBar or AccessibilityRole.Meter or AccessibilityRole.Slider or AccessibilityRole.ScrollBar or AccessibilityRole.SpinButton)
            {
                if (node.Value is { } current)
                {
                    info.SetRangeInfo(AccessibilityNodeInfo.RangeInfo.Obtain(RangeType.Float, (float)node.ValueMin, (float)node.ValueMax, (float)current));
                    if (node.ValueText.Length > 0 && OperatingSystem.IsAndroidVersionAtLeast(30))
                        info.StateDescription = node.ValueText;
                }

                if (node.Role == AccessibilityRole.Slider && !node.ReadOnly && !node.Disabled)
                    info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionSetProgress);
            }

            if (AccessibilityMapping.IsCheckable(node.Role))
            {
                info.Checkable = true;
                info.Checked = node.Checked == ToggleState.True;
            }

            if (node.Selected is { } selected)
                info.Selected = selected;

            if (node.Expanded is { } expanded && OperatingSystem.IsAndroidVersionAtLeast(30))
                info.StateDescription = expanded ? "expanded" : "collapsed";

            if (AccessibilityMapping.IsClickable(node.Role))
            {
                info.Clickable = true;
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
            }

            // Input focus is the page's keyboard focus; accessibility focus is where the screen reader is.
            if (node.IsFocusable)
            {
                info.Focusable = true;
                info.Focused = view.FocusedNode?.Id == node.Id;
                if (!info.Focused)
                    info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionFocus);
            }

            info.AccessibilityFocused = view._accessibilityFocus == virtualViewId;
            info.AddAction(info.AccessibilityFocused
                ? AccessibilityNodeInfo.AccessibilityAction.ActionClearAccessibilityFocus
                : AccessibilityNodeInfo.AccessibilityAction.ActionAccessibilityFocus);

            foreach (var child in node.ReadingChildren)
            {
                if (view._a11yIds.TryGetValue(child, out var childId))
                    info.AddChild(view, childId);
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
            if ((uint)index >= (uint)view._a11yNodes.Length)
                return false;

            var node = view._a11yNodes[index];
            if ((int)action == AccessibilityNodeInfo.AccessibilityAction.ActionSetProgress?.Id && node.Role == AccessibilityRole.Slider && !node.ReadOnly && arguments is not null)
                return view.SetRangeValue(node, arguments.GetFloat(AccessibilityNodeInfo.ActionArgumentProgressValue));

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
                case Action.Focus when node.IsFocusable:
                    view.FocusFromAccessibility(node);
                    return true;
                case Action.Click when node.Role == AccessibilityRole.Link:
                    view.FollowLinkNode(node);
                    return true;
                case Action.Click when AccessibilityMapping.IsCheckable(node.Role):
                    return view.ActivateControl(node);
                case Action.SetText when node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox && !node.ReadOnly:
                    return view.SetFieldText(node, arguments?.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence)?.ToString() ?? string.Empty);
                case Action.SetSelection when node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox:
                    return view.SetFieldSelection(node, arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionStartInt, 0) ?? 0,
                        arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionEndInt, 0) ?? 0);
                case Action.Paste when node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox && !node.ReadOnly && view.IsEditing:
                    view.PasteIntoField();
                    return true;
                case Action.NextAtMovementGranularity or Action.PreviousAtMovementGranularity:
                    return view.TraverseText(virtualViewId, node, forward: action == Action.NextAtMovementGranularity, arguments);
                default:
                    return false;
            }
        }

        public override IList<AccessibilityNodeInfo>? FindAccessibilityNodeInfosByText(string? text, int virtualViewId)
        {
            var found = new List<AccessibilityNodeInfo>();
            if (string.IsNullOrEmpty(text))
                return found;

            for (var i = 0; i < view._a11yNodes.Length; i++)
            {
                if (view._a11yNodes[i].ReadingText.Contains(text, StringComparison.OrdinalIgnoreCase) && CreateNode(i + 1) is { } info)
                    found.Add(info);
            }

            return found;
        }

        public override AccessibilityNodeInfo? FindFocus(NodeFocus focus) =>
            view._accessibilityFocus != HostViewId ? CreateNode(view._accessibilityFocus) : null;
    }
}
