using System;
using System.Collections.Generic;

namespace PeachBrackets.Accessibility;

/// <summary>What kind of thing changed between two accessibility trees (<see cref="AccessibilityChange"/>).</summary>
public enum AccessibilityChangeKind
{
    /// <summary>The part is in the new tree and was not in the old one.</summary>
    Added,

    /// <summary>The part was in the old tree and is not in the new one.</summary>
    Removed,

    /// <summary>The part's accessible name changed.</summary>
    NameChanged,

    /// <summary>The part's description changed.</summary>
    DescriptionChanged,

    /// <summary>A progress bar, meter, slider or spin button changed its number or its value text.</summary>
    ValueChanged,

    /// <summary>A state of the part changed; <see cref="AccessibilityChange.Property"/> says which.</summary>
    StateChanged,
}

/// <summary>One difference between two accessibility trees: a part that came, went or changed.</summary>
/// <param name="Kind">what happened</param>
/// <param name="Node">the part: its node in the new tree, or for <see cref="AccessibilityChangeKind.Removed"/> its node in the old one</param>
/// <param name="Property">for <see cref="AccessibilityChangeKind.StateChanged"/>, the state that changed (<c>checked</c>, <c>pressed</c>, <c>expanded</c>,
/// <c>selected</c>, <c>disabled</c>, <c>required</c>, <c>readonly</c>, <c>invalid</c>, <c>busy</c>, <c>current</c>, <c>modal</c>); otherwise null</param>
public sealed record AccessibilityChange(AccessibilityChangeKind Kind, AccessibilityNode Node, string? Property = null);

/// <summary>Something a screen reader should say because a live region changed.</summary>
/// <param name="Text">what to say</param>
/// <param name="Politeness"><see cref="LiveSetting.Polite"/> to say it when the reader is idle, <see cref="LiveSetting.Assertive"/> to interrupt</param>
/// <param name="Region">the live region it belongs to, in the new tree</param>
public sealed record AccessibilityAnnouncement(string Text, LiveSetting Politeness, AccessibilityNode Region);

/// <summary>
/// What changed between two accessibility trees of the same document, so a host can tell the platform's accessibility service about it - raise the
/// right change events, and say what a live region now contains - instead of replacing the whole tree. Parts are matched by <see cref="AccessibilityNode.Id"/>,
/// which survives a change of layout.
/// </summary>
public sealed class AccessibilityTreeDiff
{
    private AccessibilityTreeDiff(IReadOnlyList<AccessibilityChange> changes, IReadOnlyList<AccessibilityAnnouncement> announcements)
    {
        Changes = changes;
        Announcements = announcements;
    }

    /// <summary>Every difference, in the order of the new tree (removed parts last, in the order of the old one).</summary>
    public IReadOnlyList<AccessibilityChange> Changes { get; }

    /// <summary>What the live regions that changed should say, in the order of the new tree.</summary>
    public IReadOnlyList<AccessibilityAnnouncement> Announcements { get; }

    /// <summary>Whether nothing differs.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>Compares two trees of the same document.</summary>
    /// <param name="before">the tree as it was</param>
    /// <param name="after">the tree as it is now</param>
    /// <returns>the differences and what the live regions that changed should say</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    public static AccessibilityTreeDiff Compare(AccessibilityTree before, AccessibilityTree after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<AccessibilityChange>();
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in after.Nodes)
        {
            if (before.FindById(node.Id) is not { } old)
            {
                changes.Add(new AccessibilityChange(AccessibilityChangeKind.Added, node));
                added.Add(node.Id);
                continue;
            }

            Compare(old, node, changes);
        }

        foreach (var old in before.Nodes)
        {
            if (after.FindById(old.Id) is null)
                changes.Add(new AccessibilityChange(AccessibilityChangeKind.Removed, old));
        }

        return new AccessibilityTreeDiff(changes, Announce(before, after, changes, added));
    }

    private static void Compare(AccessibilityNode old, AccessibilityNode node, List<AccessibilityChange> changes)
    {
        if (old.Name != node.Name)
            changes.Add(new AccessibilityChange(AccessibilityChangeKind.NameChanged, node));
        if (old.Description != node.Description)
            changes.Add(new AccessibilityChange(AccessibilityChangeKind.DescriptionChanged, node));
        if (old.Value != node.Value || old.ValueText != node.ValueText)
            changes.Add(new AccessibilityChange(AccessibilityChangeKind.ValueChanged, node));

        void State(string property, bool differs)
        {
            if (differs)
                changes.Add(new AccessibilityChange(AccessibilityChangeKind.StateChanged, node, property));
        }

        State("checked", old.Checked != node.Checked);
        State("pressed", old.Pressed != node.Pressed);
        State("expanded", old.Expanded != node.Expanded);
        State("selected", old.Selected != node.Selected);
        State("disabled", old.Disabled != node.Disabled);
        State("required", old.Required != node.Required);
        State("readonly", old.ReadOnly != node.ReadOnly);
        State("invalid", old.Invalid != node.Invalid);
        State("busy", old.Busy != node.Busy);
        State("current", old.Current != node.Current);
        State("modal", old.Modal != node.Modal);
    }

    // A live region that was there before says what is new in it - or, when it is atomic, all it holds - unless it is busy.
    private static List<AccessibilityAnnouncement> Announce(
        AccessibilityTree before, AccessibilityTree after, List<AccessibilityChange> changes, HashSet<string> added)
    {
        var announcements = new List<AccessibilityAnnouncement>();
        var changedText = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (change.Kind is AccessibilityChangeKind.NameChanged)
                changedText.Add(change.Node.Id);
        }

        foreach (var region in after.Nodes)
        {
            if (region.Live == LiveSetting.Off || region.Busy || added.Contains(region.Id) || before.FindById(region.Id) is null)
                continue;

            // A region inside another live region is spoken with it.
            if (HasLiveAncestor(region))
                continue;

            string text;
            if (region.Atomic)
            {
                var oldText = before.FindById(region.Id)!.Text;
                text = region.Text;
                if (text == oldText && !HasChangeBelow(region, added, changedText))
                    continue;
            }
            else
            {
                var parts = new List<string>();
                CollectNew(region, added, changedText, parts);
                text = string.Join(' ', parts);
            }

            if (text.Trim().Length > 0)
                announcements.Add(new AccessibilityAnnouncement(text.Trim(), region.Live, region));
        }

        return announcements;
    }

    private static bool HasLiveAncestor(AccessibilityNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Live != LiveSetting.Off)
                return true;
        }

        return false;
    }

    private static bool HasChangeBelow(AccessibilityNode node, HashSet<string> added, HashSet<string> changedText)
    {
        foreach (var child in node.Children)
        {
            if (added.Contains(child.Id) || changedText.Contains(child.Id) || HasChangeBelow(child, added, changedText))
                return true;
        }

        return false;
    }

    // The text of what is new inside a region: a part that was added (all of it, once, however deep), and the new name of a part that was renamed.
    private static void CollectNew(AccessibilityNode node, HashSet<string> added, HashSet<string> changedText, List<string> parts)
    {
        foreach (var child in node.Children)
        {
            if (added.Contains(child.Id))
            {
                if (child.Text is { Length: > 0 } text)
                    parts.Add(text);
            }
            else if (changedText.Contains(child.Id) && child.Role == AccessibilityRole.Text)
            {
                parts.Add(child.Name);
            }
            else
            {
                CollectNew(child, added, changedText, parts);
            }
        }
    }
}
