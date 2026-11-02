using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using PeachBrackets.Accessibility;
using Windows.Foundation;

namespace PeachBrackets.WinUI;

/// <summary>
/// The Text pattern of a document or a text field, so a screen reader can read it by character, word, line and paragraph and ask where a stretch of it
/// is on screen. The text is the one <see cref="HtmlTextModel"/> reads from the accessibility tree; ranges are offsets into it.
/// </summary>
internal sealed class HtmlTextProvider : ITextProvider
{
    private readonly HtmlViewAutomationPeer _root;
    private readonly HtmlView _view;
    private readonly AccessibilityNode _scope;

    public HtmlTextProvider(HtmlViewAutomationPeer root, HtmlView view, AccessibilityNode scope)
    {
        _root = root;
        _view = view;
        _scope = scope;
    }

    public ITextRangeProvider DocumentRange
    {
        get
        {
            var model = Model();
            return new HtmlTextRange(this, model, 0, model.Length);
        }
    }

    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;

    // The selection of the text field that has the focus; the selection of the page's own text is not reported.
    public ITextRangeProvider[] GetSelection()
    {
        if (_view.Document?.GetEditState() is not { } state || _view.FocusedNode is not { } focused)
            return PageSelection();

        var model = Model();
        foreach (var segment in model.Segments)
        {
            if (segment.Node?.Id == focused.Id)
            {
                var from = Math.Clamp(Math.Min(state.SelectionStart, state.SelectionEnd), 0, segment.Length);
                var to = Math.Clamp(Math.Max(state.SelectionStart, state.SelectionEnd), 0, segment.Length);
                return [new HtmlTextRange(this, model, segment.Start + from, segment.Start + to)];
            }
        }

        return [];
    }

    public ITextRangeProvider[] GetVisibleRanges()
    {
        var model = Model();
        var ranges = new List<ITextRangeProvider>();
        foreach (var segment in model.Segments)
        {
            if (segment.Node is { } node && !_view.IsOffscreen(node))
                ranges.Add(new HtmlTextRange(this, model, segment.Start, segment.End));
        }

        return [.. ranges];
    }

    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement)
    {
        var model = Model();
        if (_root.NodeOf(childElement) is { } child)
        {
            foreach (var segment in model.Segments)
            {
                if (segment.Node?.Id == child.Id)
                    return new HtmlTextRange(this, model, segment.Start, segment.End);
            }
        }

        return new HtmlTextRange(this, model, 0, 0);
    }

    public ITextRangeProvider RangeFromPoint(Point screenLocation)
    {
        var model = Model();
        var view = _root.ViewBounds;
        foreach (var segment in model.Segments)
        {
            if (segment.Node is { } node && _view.NodeBounds(node, view).Contains(screenLocation))
                return new HtmlTextRange(this, model, segment.Start, segment.End);
        }

        return new HtmlTextRange(this, model, 0, 0);
    }

    // The selection of the page's own text, when it lies inside one block: the selected phrase is found in the document's text the way the document finds
    // text, and in the reading text by its place among the occurrences, so the two agree on which one is selected.
    private ITextRangeProvider[] PageSelection()
    {
        if (_view.Document is not { Selection: { HasSelection: true } selection } document || selection.GetRange() is not { } range)
            return [];

        var phrase = selection.GetText();
        if (phrase.Length == 0 || phrase.Contains('\n', StringComparison.Ordinal))
            return [];

        var n = document.FindText(phrase).ToList().FindIndex(r => r.Equals(range));
        var model = Model();
        var at = n < 0 ? -1 : model.OffsetOfOccurrence(phrase, n);
        if (at < 0)
            return [];

        return [new HtmlTextRange(this, model, at, at + phrase.Length)];
    }

    internal HtmlTextModel Model()
    {
        var scope = _view.CurrentNode(_scope);
        return HtmlTextModel.Build(scope, node => node.Role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox ? _view.FieldValue(node) ?? string.Empty : null);
    }

    internal HtmlViewAutomationPeer Root => _root;

    internal HtmlView View => _view;
}

/// <summary>A stretch of a document's text.</summary>
internal sealed class HtmlTextRange : ITextRangeProvider
{
    private readonly HtmlTextProvider _provider;
    private readonly HtmlTextModel _model;
    private int _start;
    private int _end;

    public HtmlTextRange(HtmlTextProvider provider, HtmlTextModel model, int start, int end)
    {
        _provider = provider;
        _model = model;
        _start = start;
        _end = end;
    }

    internal int Start => _start;

    internal int End => _end;

    public ITextRangeProvider Clone() => new HtmlTextRange(_provider, _model, _start, _end);

    public bool Compare(ITextRangeProvider range) => range is HtmlTextRange other && other._model == _model && other._start == _start && other._end == _end;

    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        var target = (HtmlTextRange)targetRange;
        return Edge(endpoint).CompareTo(target.Edge(targetEndpoint));
    }

    public void ExpandToEnclosingUnit(TextUnit unit) => (_start, _end) = _model.Expand(_start, _end, HtmlViewLogic.ToUnitKind(unit));

    public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward)
    {
        // The first block, in the direction asked, whose text has the value; the range is that block's text.
        var segments = _model.SegmentsIn(_start, _end).ToList();
        if (backward)
            segments.Reverse();

        foreach (var segment in segments)
        {
            if (HtmlTextModel.AttributeValue(segment.Node, attributeId) is { } actual && actual.Equals(value))
                return new HtmlTextRange(_provider, _model, segment.Start, segment.End);
        }

        return null!;
    }

    public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase)
    {
        var at = _model.Find(text, _start, _end, backward, ignoreCase);
        return at < 0 ? null! : new HtmlTextRange(_provider, _model, at, at + text.Length);
    }

    // The language, the style (a heading's level) and whether the text is read-only, taken from the block the range starts in; a stretch across blocks
    // that differ is not reported as mixed. Anything else is not supported, which UI Automation spells as the unset value.
    public object GetAttributeValue(int attributeId) =>
        HtmlTextModel.AttributeValue(_model.SegmentAt(_start)?.Node, attributeId) ?? Microsoft.UI.Xaml.DependencyProperty.UnsetValue;

    public void GetBoundingRectangles(out double[] returnValue)
    {
        var rectangles = new List<double>();
        var view = _provider.Root.ViewBounds;
        foreach (var segment in _model.SegmentsIn(_start, _end))
        {
            if (segment.Node is not { } node)
                continue;

            var rect = _provider.View.NodeBounds(node, view);
            if (rect.Width > 0 && rect.Height > 0)
                { rectangles.Add(rect.X); rectangles.Add(rect.Y); rectangles.Add(rect.Width); rectangles.Add(rect.Height); }
        }

        returnValue = [.. rectangles];
    }

    public IRawElementProviderSimple[] GetChildren() => [];

    public IRawElementProviderSimple GetEnclosingElement()
    {
        if (_model.SegmentAt(_start) is { Node: { } node } && _provider.Root.FindPeer(node) is { } peer && _provider.Root.ProviderOf(peer) is { } provider)
            return provider;

        return _provider.Root.ProviderOf(_provider.Root)!;
    }

    public string GetText(int maxLength) => _model.Slice(_start, _end, maxLength);

    public int Move(TextUnit unit, int count)
    {
        var moved = _model.Move(_start, _end, HtmlViewLogic.ToUnitKind(unit), count);
        (_start, _end) = (moved.Start, moved.End);
        return moved.Moved;
    }

    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        var place = ((HtmlTextRange)targetRange).Edge(targetEndpoint);
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = place;
            _end = Math.Max(_end, _start);
        }
        else
        {
            _end = place;
            _start = Math.Min(_start, _end);
        }
    }

    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        var moved = _model.MoveEndpoint(_start, _end, endpoint == TextPatternRangeEndpoint.Start, HtmlViewLogic.ToUnitKind(unit), count);
        (_start, _end) = (moved.Start, moved.End);
        return moved.Moved;
    }

    public void ScrollIntoView(bool alignToTop)
    {
        if (_model.SegmentAt(_start) is { Node: { } node })
            _provider.View.BringIntoView(node);
    }

    // Selects the range in the text field that has the focus, when the range is inside that field's text.
    public void Select()
    {
        if (_model.SegmentAt(_start) is not { Node: { } node } segment || _end > segment.End)
            return;

        if (_provider.View.FocusedNode?.Id == node.Id)
        {
            _provider.View.SetFieldSelection(node, _start - segment.Start, _end - segment.Start);
            return;
        }

        // Text of the page: the same phrase, found the document's way, is what gets selected.
        var phrase = _model.Slice(_start, _end);
        if (phrase.Length > 0 && !phrase.Contains('\n', StringComparison.Ordinal))
            _provider.View.SelectPageText(phrase, _model.OccurrenceAt(phrase, _start));
    }

    public void AddToSelection() { }

    public void RemoveFromSelection() { }

    private int Edge(TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
}
