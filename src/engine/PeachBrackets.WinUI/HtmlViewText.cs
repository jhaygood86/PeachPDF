using System;
using System.Collections.Generic;
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

    // The selection is not mirrored into the pattern yet.
    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.None;

    public ITextRangeProvider[] GetSelection() => [];

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

    public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward) => null!;

    public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase)
    {
        var at = _model.Find(text, _start, _end, backward, ignoreCase);
        return at < 0 ? null! : new HtmlTextRange(_provider, _model, at, at + text.Length);
    }

    // No text attribute (font, colour, language) is reported yet.
    public object GetAttributeValue(int attributeId) => null!;

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

    // The selection is not mirrored into the pattern, so a range cannot be selected.
    public void Select() { }

    public void AddToSelection() { }

    public void RemoveFromSelection() { }

    private int Edge(TextPatternRangeEndpoint endpoint) => endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
}
