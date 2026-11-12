using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Updates;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PeachDrawing.Core;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    // What the host changed on each element since the source was loaded, by the element's place in the source: loading the document again (a change of
    // :hover that moves something does) applies it to the new boxes. Apart from _formChanges, which a form reset clears.
    private readonly Dictionary<int, Dictionary<string, string?>> _attributeChanges = [];

    // The styles the elements had before an in-place restyle that may have to give up and load the document again, for that load to start transitions from.
    private Dictionary<int, StyleSnapshot>? _styleSnapshot;

    // What an element looked like before a restyle: its style, and whether it was not displayed (a box that is shown again takes its starting style).
    private readonly record struct StyleSnapshot(ComputedStyle Style, bool Hidden);

    /// <summary>
    /// Sets an attribute of an element and styles the document again, so every rule that reads it (<c>[data-state=open]</c>, <c>.open</c>, <c>#id</c>) matches
    /// the new value. A property whose value differs afterwards and that the element's <c>transition-*</c> names runs to its new value instead of snapping
    /// (when <see cref="HtmlDocumentOptions.AnimationsEnabled"/> is set). The change is kept for as long as the document is: loading it again for a pointer
    /// or focus rule applies it too.
    /// </summary>
    /// <remarks>
    /// The document is loaded from its source again with the change applied, so what was selected stays only if the text did not change, and the element
    /// handles taken before are stale (a handle still names the same element; take new ones from <see cref="GetElement"/> to be safe).
    /// A document built with the declarative API, or one whose text was edited in place, has no source to load again and the call is not applied.
    /// </remarks>
    /// <param name="element">the element, from <see cref="GetElement"/> or <see cref="GetElementAt(int, double, double)"/></param>
    /// <param name="name">the attribute name</param>
    /// <param name="value">its new value; null removes the attribute</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or has white space in it</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public ValueTask<UpdateResult> SetAttributeAsync(DocumentElement element, string name, string? value, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        if (name.AsSpan().IndexOfAny(" \t\r\n\f") >= 0)
            throw new ArgumentException("An attribute name has no white space in it.", nameof(name));

        return MutateAsync(element, name, _ => value, measuringCanvas);
    }

    /// <summary>Removes an attribute of an element and styles the document again; see <see cref="SetAttributeAsync"/>.</summary>
    /// <param name="element">the element</param>
    /// <param name="name">the attribute name</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied (it is not when the element has no such attribute) and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or has white space in it</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public ValueTask<UpdateResult> RemoveAttributeAsync(DocumentElement element, string name, Canvas measuringCanvas) =>
        SetAttributeAsync(element, name, null, measuringCanvas);

    /// <summary>Adds a class to an element and styles the document again; see <see cref="SetAttributeAsync"/>.</summary>
    /// <param name="element">the element</param>
    /// <param name="className">the class, without a dot</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied (it is not when the element has the class) and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="ArgumentException"><paramref name="className"/> is empty or has white space in it</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public ValueTask<UpdateResult> AddClassAsync(DocumentElement element, string className, Canvas measuringCanvas) =>
        ClassesAsync(element, className, ClassChange.Add, measuringCanvas);

    /// <summary>Removes a class from an element and styles the document again; see <see cref="SetAttributeAsync"/>.</summary>
    /// <param name="element">the element</param>
    /// <param name="className">the class, without a dot</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied (it is not when the element does not have the class) and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="ArgumentException"><paramref name="className"/> is empty or has white space in it</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public ValueTask<UpdateResult> RemoveClassAsync(DocumentElement element, string className, Canvas measuringCanvas) =>
        ClassesAsync(element, className, ClassChange.Remove, measuringCanvas);

    /// <summary>Adds a class to an element that does not have it and removes it from one that does, and styles the document again; see <see cref="SetAttributeAsync"/>.</summary>
    /// <param name="element">the element</param>
    /// <param name="className">the class, without a dot</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="ArgumentException"><paramref name="className"/> is empty or has white space in it</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public ValueTask<UpdateResult> ToggleClassAsync(DocumentElement element, string className, Canvas measuringCanvas) =>
        ClassesAsync(element, className, ClassChange.Toggle, measuringCanvas);

    /// <summary>The value of an attribute of an element as the document has it now (with the changes made through this class), or null when it has none.</summary>
    /// <param name="element">the element</param>
    /// <param name="name">the attribute name, matched without regard to case</param>
    /// <returns>the value, or null</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    public string? GetAttribute(DocumentElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfDisposed();
        var tag = (LiveBoxOf(element) ?? element.Box).HtmlTag;
        return tag?.Attributes is { } attributes && attributes.TryGetValue(name, out var value) ? value : null;
    }

    private enum ClassChange { Add, Remove, Toggle }

    private ValueTask<UpdateResult> ClassesAsync(DocumentElement element, string className, ClassChange change, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        if (className.AsSpan().IndexOfAny(" \t\r\n\f") >= 0)
            throw new ArgumentException("A class name has no white space in it.", nameof(className));

        return MutateAsync(element, "class", current =>
        {
            var classes = new List<string>((current ?? string.Empty).Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries));
            var has = classes.Exists(c => c == className);
            if (change == ClassChange.Add && !has) classes.Add(className);
            else if (change == ClassChange.Remove) classes.RemoveAll(c => c == className);
            else if (change == ClassChange.Toggle && !has) classes.Add(className);
            else if (change == ClassChange.Toggle) classes.RemoveAll(c => c == className);
            else return current;

            return classes.Count == 0 ? null : string.Join(' ', classes);
        }, measuringCanvas);
    }

    private async ValueTask<UpdateResult> MutateAsync(DocumentElement element, string name, Func<string?, string?> next, Canvas measuringCanvas)
    {
        ThrowIfDisposed();
        _ = Pages;
        if (_source is null || _options is null)
            return UpdateResult.NotApplied("Only a document loaded from HTML can be styled again for a change of an attribute.");
        if (_textEdited)
            return UpdateResult.NotApplied("The text was edited in place, which loading the document again would undo.");
        if (LiveBoxOf(element) is not { HtmlTag: { SourceOrdinal: >= 0 } tag })
            return UpdateResult.NotApplied("The element is not one the document can find again.");

        var current = tag.Attributes is { } attributes && attributes.TryGetValue(name, out var existing) ? existing : null;
        var value = next(current);
        if (string.Equals(value, current, StringComparison.Ordinal) && (value is not null || !(tag.Attributes?.ContainsKey(name) ?? false)))
            return UpdateResult.NotApplied("The element is already so.");

        if (value is null) tag.RemoveAttribute(name);
        else tag.SetAttribute(name, value);

        if (!_attributeChanges.TryGetValue(tag.SourceOrdinal, out var changes))
            _attributeChanges[tag.SourceOrdinal] = changes = [with(StringComparer.OrdinalIgnoreCase)];

        changes[name] = value;
        return await RestyleAsync([tag.SourceOrdinal], measuringCanvas);
    }

    /// <summary>
    /// Changes what the document reports for <c>prefers-color-scheme</c>, <c>prefers-reduced-motion</c>, <c>forced-colors</c> and the other user preferences, and
    /// styles it again, so the <c>@media</c> rules that read them match the new values; properties that differ afterwards run to their new values when the
    /// element's <c>transition-*</c> names them. The document is loaded from its source again, with every page to repaint.
    /// </summary>
    /// <remarks>
    /// Style sheet rules are matched again, but what was chosen from the media when the document was first loaded (the keyframes in a media rule, a
    /// <c>&lt;picture&gt;</c> source) is chosen again by the load this does. A document with no source (the declarative API) or whose text was edited in place is not changed.
    /// </remarks>
    /// <param name="preferences">the preferences to report; null for <see cref="UserPreferences.Default"/></param>
    /// <param name="colorScheme">the colour scheme to report</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async ValueTask<UpdateResult> SetPreferencesAsync(UserPreferences? preferences, ColorSchemePreference colorScheme, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        ThrowIfDisposed();
        _ = Pages;
        if (_source is null || _options is null)
            return UpdateResult.NotApplied("Only a document loaded from HTML can be styled again for a change of preferences.");
        if (_textEdited)
            return UpdateResult.NotApplied("The text was edited in place, which loading the document again would undo.");

        var resolved = preferences ?? UserPreferences.Default;
        if (_container.Preferences == resolved && _container.PreferredColorScheme == colorScheme)
            return UpdateResult.NotApplied("The preferences are already so.");

        _container.Preferences = resolved;
        _container.PreferredColorScheme = colorScheme;

        // Any element may look different, so every page is painted again.
        var result = await RestyleAsync([], measuringCanvas);
        var regions = new List<DirtyRegion>(_pages!.Count);
        for (var page = 0; page < _pages.Count; page++)
            regions.Add(new DirtyRegion(page, new Rect(0, 0, _pages[page].WidthPoints, _pages[page].HeightPoints)));

        return result with { DirtyRegions = regions };
    }

    // The form state and the host's attribute changes, as the reload applies them (a change made through SetAttributeAsync wins over a form state of the same name).
    private IReadOnlyDictionary<int, Dictionary<string, string?>> AttributeOverridesForReload()
    {
        if (_attributeChanges.Count == 0)
            return _formChanges;

        var merged = new Dictionary<int, Dictionary<string, string?>>(_formChanges.Count + _attributeChanges.Count);
        foreach (var (ordinal, values) in _formChanges)
            merged[ordinal] = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);

        foreach (var (ordinal, values) in _attributeChanges)
        {
            if (!merged.TryGetValue(ordinal, out var into))
                merged[ordinal] = into = [with(StringComparer.OrdinalIgnoreCase)];

            foreach (var (name, value) in values)
                into[name] = value;
        }

        return merged;
    }

    // The computed style of each element, by its place in the source (the first box of an element that has several): immutable, so it can be read once the tree is gone.
    private static Dictionary<int, StyleSnapshot> SnapshotStyles(CssBox? root)
    {
        var styles = new Dictionary<int, StyleSnapshot>();
        void Walk(CssBox box)
        {
            if (box.HtmlTag is { SourceOrdinal: >= 0 } tag)
                styles.TryAdd(tag.SourceOrdinal, new StyleSnapshot(box.ComputedStyle, box.DerivedStyle.ActualDisplay == PeachBrackets.Css.Keywords.None));

            foreach (var child in box.Boxes)
                Walk(child);
        }

        if (root is not null)
            Walk(root);

        return styles;
    }

    // Starts a transition for each property of each element that has a different value than before and that the element's transition-* properties name.
    private void StartTransitionsFrom(Dictionary<int, StyleSnapshot>? before)
    {
        if (_container.Root is not { } root)
            return;

        // The style an element starts from when it is new, or is shown again after display: none (CSS Transitions 2 §4), when a rule says what that is.
        var starting = StartingStyles();

        void Walk(CssBox box)
        {
            if (box.HtmlTag is { SourceOrdinal: >= 0 } tag)
            {
                ComputedStyle? old = null;
                StyleSnapshot snapshot = default;
                var known = before is not null && before.TryGetValue(tag.SourceOrdinal, out snapshot);
                if (known) old = snapshot.Style;
                if ((!known || snapshot.Hidden) && starting is not null && box.DerivedStyle.ActualDisplay != PeachBrackets.Css.Keywords.None
                    && starting.TryGetValue(tag.SourceOrdinal, out var startingStyle))
                    old = startingStyle;

                var changes = old is null ? [] : InteractionRestyler.DiffStyles(box, old);
                if (changes.Count > 0 && !InteractionRestyler.PropertyFactoryHasNoTransition(box))
                {
                    _container.AnimationClock ??= new PeachBrackets.Html.Animation.AnimationClock(_container);
                    _container.AnimationClock.BeginTransitions(box, changes);
                }
            }

            foreach (var child in box.Boxes)
                Walk(child);
        }

        Walk(root);
    }

    // What each element's style would be with the @starting-style rules taking part, by its place in the source; null when there are none. The tree is cascaded
    // again with them in, which gives every box the starting style it would have, and then every box is given its own style back.
    private Dictionary<int, ComputedStyle>? StartingStyles()
    {
        if (_container.Root is not { } root || _container.CssData is not { HasStartingStyleRules: true })
            return null;

        var normal = new Dictionary<CssBox, ComputedStyle>(ReferenceEqualityComparer.Instance as IEqualityComparer<CssBox>);
        void Collect(CssBox box)
        {
            normal[box] = box.ComputedStyle;
            foreach (var child in box.Boxes) Collect(child);
        }

        Collect(root);

        void Restore(CssBox box)
        {
            if (normal.TryGetValue(box, out var style))
            {
                box.AdoptComputedStyle(style);
                box.ResetDerivedStyle();
            }

            foreach (var child in box.Boxes) Restore(child);
        }

        var starting = new Dictionary<int, ComputedStyle>();
        try
        {
            PeachBrackets.Html.CssData.WithStartingStyle(() => DomParser.RecascadeForState(_container, root));

            void Take(CssBox box)
            {
                if (box.HtmlTag is { SourceOrdinal: >= 0 } tag)
                    starting.TryAdd(tag.SourceOrdinal, box.ComputedStyle);

                foreach (var child in box.Boxes) Take(child);
            }

            Take(root);
        }
        finally
        {
            // Every box gets its own style back, even when the cascade with the starting styles in it threw.
            Restore(root);
        }

        return starting;
    }

    /// <summary>Starts the transitions an element takes from its <c>@starting-style</c> when the document is first styled. Called once a document has loaded.</summary>
    private void StartEntryTransitions()
    {
        if (_container.AnimationsEnabled)
            StartTransitionsFrom(null);
    }
}
