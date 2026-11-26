using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>TextMetrics</c> (what <c>measureText</c> returns) or a <c>ShapedText</c> (what <c>shapeText</c> returns): a run of text laid out with the context's settings of the moment.</summary>
internal sealed class MeasuredTextObject : ObjectInstance
{
    public MeasuredTextObject(Engine engine, ObjectInstance prototype, ContextObject context, string layoutText, object layout) : base(engine)
    {
        Prototype = prototype;
        Context = context;
        LayoutText = layoutText;
        Layout = layout;
    }

    public ContextObject Context { get; }

    public string LayoutText { get; }

    /// <summary>The <c>TextLayout</c> of the run (a private type of the runtime).</summary>
    public object Layout { get; }
}

/// <summary>A cluster or a run of a <c>ShapedText</c>, which <c>fillGlyphRun</c> draws.</summary>
internal sealed class GlyphRunObject : ObjectInstance
{
    public GlyphRunObject(Engine engine, ObjectInstance prototype, string text, string baseline, double x, double y) : base(engine)
    {
        Prototype = prototype;
        Text = text;
        Baseline = baseline;
        X = x;
        Y = y;
    }

    public string Text { get; }

    public string Baseline { get; }

    public double X { get; }

    public double Y { get; }
}

// measureText and shapeText: the metrics of a run of text, and the questions about stretches of it (its clusters, boxes and positions).
internal sealed partial class ScriptRuntime
{
    private ObjectInstance TextMetricsPrototype = null!;
    private ObjectInstance ShapedTextPrototype = null!;
    private ObjectInstance GlyphRunPrototype = null!;

    private JsValue Rect(double x, double y, double width, double height) =>
        Engine.Evaluate("(function (x, y, w, h) { return new DOMRect(x, y, w, h); })").Call(JsValue.Undefined, [Js.Num(x), Js.Num(y), Js.Num(width), Js.Num(height)]);

    // A Web IDL [EnforceRange] unsigned long: a number that does not fit one is a TypeError.
    private uint ULong(JsValue value, string name)
    {
        var number = TypeConverter.ToNumber(value);
        if (!double.IsFinite(number) || Math.Truncate(number) is < 0 or > uint.MaxValue)
            throw TypeError($"The {name} is outside the range of an unsigned long.");

        return (uint)Math.Truncate(number);
    }

    private void InstallTextMetrics(ObjectInstance contextPrototype)
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;
        TextMetricsPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        ShapedTextPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        GlyphRunPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        Tag(TextMetricsPrototype, "TextMetrics");
        Tag(ShapedTextPrototype, "ShapedText");
        Tag(GlyphRunPrototype, "GlyphRun");
        Define(Engine.Global, "TextMetrics", Js.Constructor(Engine, "TextMetrics", TextMetricsPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "ShapedText", Js.Constructor(Engine, "ShapedText", ShapedTextPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

        Js.StrictMethod(Engine, contextPrototype, "measureText", 1, (self, args) => Measure(Cx(self), Js.AsText(Js.Arg(args, 0)), shaped: false));
        Js.StrictMethod(Engine, contextPrototype, "shapeText", 1, (self, args) => Measure(Cx(self), Js.AsText(Js.Arg(args, 0)), shaped: true));

        InstallRangeMethods(TextMetricsPrototype, shaped: false);
        InstallRangeMethods(ShapedTextPrototype, shaped: true);

        foreach (var stroke in new[] { false, true })
        {
            var isStroke = stroke;
            Js.StrictMethod(Engine, contextPrototype, isStroke ? "strokeTextCluster" : "fillTextCluster", 3, (self, args) =>
            {
                if (Js.Arg(args, 0) is not ObjectInstance cluster)
                    throw TypeError("The cluster is not a TextCluster.");

                var (x, y) = (Num(args, 1), Num(args, 2));
                var options = Js.Arg(args, 3) is ObjectInstance dictionary ? dictionary : null;
                var (align, baseline) = (ClusterWord(options, "align", AlignWords), ClusterWord(options, "baseline", BaselineWords));
                var offsetX = options?.Get("x") is { } ox && !ox.IsUndefined() ? TypeConverter.ToNumber(ox) : TypeConverter.ToNumber(cluster.Get("x"));
                var offsetY = options?.Get("y") is { } oy && !oy.IsUndefined() ? TypeConverter.ToNumber(oy) : TypeConverter.ToNumber(cluster.Get("y"));
                var text = cluster.Get("text") is JsString textValue ? textValue.ToString() : throw TypeError("The cluster has no text.");
                align ??= cluster.Get("align") is JsString a ? a.ToString() : null;
                baseline ??= cluster.Get("baseline") is JsString b ? b.ToString() : null;
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(offsetX) || !double.IsFinite(offsetY))
                    return JsValue.Undefined;

                DrawRun(Cx(self), text, x + offsetX, y + offsetY, align, baseline, isStroke);
                return JsValue.Undefined;
            });

            Js.StrictMethod(Engine, contextPrototype, isStroke ? "strokeGlyphRun" : "fillGlyphRun", 3, (self, args) =>
            {
                var (x, y) = (Num(args, 1), Num(args, 2));
                if (Js.Arg(args, 0) is not GlyphRunObject run)
                    throw TypeError("The argument is not a GlyphRun.");

                if (double.IsFinite(x) && double.IsFinite(y))
                    DrawRun(Cx(self), run.Text, x + run.X, y + run.Y, "left", run.Baseline, isStroke);

                return JsValue.Undefined;
            });

            Js.StrictMethod(Engine, contextPrototype, isStroke ? "strokeGlyphRuns" : "fillGlyphRuns", 3, (self, args) =>
            {
                var (x, y) = (Num(args, 1), Num(args, 2));
                if (Js.Arg(args, 0) is not ObjectInstance list || !list.IsArray())
                    throw TypeError("The argument is not a list of GlyphRuns.");

                var runs = new List<GlyphRunObject>();
                var count = (int)TypeConverter.ToNumber(list.Get("length"));
                for (var i = 0; i < count; i++)
                    runs.Add(list.Get(i.ToString(CultureInfo.InvariantCulture)) as GlyphRunObject ?? throw TypeError("An item of the list is not a GlyphRun."));

                if (double.IsFinite(x) && double.IsFinite(y))
                {
                    foreach (var run in runs)
                        DrawRun(Cx(self), run.Text, x + run.X, y + run.Y, "left", run.Baseline, isStroke);
                }

                return JsValue.Undefined;
            });
        }
    }

    private static readonly string[] AlignWords = ["start", "end", "left", "right", "center"];

    private static readonly string[] BaselineWords = ["top", "hanging", "middle", "alphabetic", "ideographic", "bottom"];

    // An enumerated member of an options dictionary: nothing when it is not there, a TypeError when it is not one of the words.
    private string? ClusterWord(ObjectInstance? options, string name, string[] words)
    {
        if (options is null || !options.HasProperty(name) || options.Get(name).IsUndefined())
            return null;

        var text = TypeConverter.ToString(options.Get(name));
        return words.Contains(text, StringComparer.Ordinal) ? text : throw TypeError($"The {name} '{text}' is not one of the allowed values.");
    }

    // Draws a piece of text with the context's font and styles, its alignment point at (x, y).
    private void DrawRun(ContextObject context, string text, double x, double y, string? align, string? baseline, bool stroke)
    {
        if (FontFor(context) is not { } font)
            return;

        var layout = LayoutText(context, font, text, align, baseline);
        var state = context.Bitmap.State;
        context.Bitmap.DrawText(layout.Text, x, y + layout.LineOffset, layout.Anchor, stroke, null, font, layout.Metrics, layout.Width, layout.LetterSpacing, layout.WordSpacing, layout.Shape,
            stroke ? state.StrokeStyle : state.FillStyle, stroke ? context.Bitmap.MakePen(state) : null);
    }

    // The box the ink of a stretch of the run covers, from the point the text is aligned on and the line it is on, y up as a canvas gives them: left and right
    // from the alignment point, ascent above the line and descent below it. A stretch with no ink is a box with no size, on the point.
    private static (double Left, double Right, double Ascent, double Descent) InkBox(TextLayout layout, int start, int end)
    {
        double left, right, top, bottom;
        bool any;
        if (layout.Run is { } run)
        {
            any = run.TryGetInk(start, end, out left, out right, out top, out bottom);
        }
        else if (start == 0 && end >= layout.Text.Length && layout.Metrics.InkRight > layout.Metrics.InkLeft)
        {
            (left, right, top, bottom, any) = (layout.Metrics.InkLeft, layout.Metrics.InkRight, layout.Metrics.InkAscent, layout.Metrics.InkDescent, true);
        }
        else
        {
            (left, right, top, bottom, any) = (0, 0, 0, 0, false);
        }

        if (!any)
            return (0, 0, 0, 0);

        return (layout.Anchor - left, right - layout.Anchor, top - layout.LineOffset, bottom + layout.LineOffset);
    }

    private JsValue Measure(ContextObject context, string text, bool shaped)
    {
        var font = FontFor(context);
        var layout = font is null
            ? new TextLayout(text, new TextRunMetrics(), null, ShapeSettings.Default, 0, 0, 0, 0, 0, null!, context.Bitmap.State.TextAlign, context.Bitmap.State.TextBaseline)
            : LayoutText(context, font, text);
        var measured = new MeasuredTextObject(Engine, shaped ? ShapedTextPrototype : TextMetricsPrototype, context, text, layout);

        var metrics = layout.Metrics;
        var line = layout.LineOffset;
        var (left, right, ascent, descent) = InkBox(layout, 0, layout.Text.Length);

        if (shaped)
        {
            Js.Value(measured, "width", Js.Num(layout.Width));
            return measured;
        }

        foreach (var (name, value) in new[]
        {
            ("width", layout.Width),
            ("actualBoundingBoxLeft", left), ("actualBoundingBoxRight", right),
            ("fontBoundingBoxAscent", metrics.FontAscent - line), ("fontBoundingBoxDescent", metrics.FontDescent + line),
            ("actualBoundingBoxAscent", ascent), ("actualBoundingBoxDescent", descent),
            ("emHeightAscent", metrics.EmAscent - line), ("emHeightDescent", metrics.EmDescent + line),
            ("hangingBaseline", metrics.HangingBaseline - line), ("alphabeticBaseline", -line), ("ideographicBaseline", metrics.IdeographicBaseline - line),
        })
            Js.Value(measured, name, Js.Num(value == 0 ? 0 : value));

        return measured;
    }

    private (int Start, int End) RangeArguments(JsValue[] args, int firstIndex, int length, bool clusters)
    {
        var start = args.Length > firstIndex && !args[firstIndex].IsUndefined() ? ULong(args[firstIndex], "start") : 0u;
        var end = args.Length > firstIndex + 1 && !args[firstIndex + 1].IsUndefined() ? ULong(args[firstIndex + 1], "end") : (uint)length;

        // Both are checked as numbers before either is looked at.
        if (end > length || start > length || clusters && start > end)
            throw DomError("IndexSizeError", "The range is outside the text.");

        return ((int)start, (int)end);
    }

    private void InstallRangeMethods(ObjectInstance prototype, bool shaped)
    {
        MeasuredTextObject This(JsValue self) => self as MeasuredTextObject ?? throw TypeError("Illegal invocation");
        TextLayout LayoutOf(JsValue self) => (TextLayout)This(self).Layout;

        Js.StrictMethod(Engine, prototype, shaped ? "actualBoundingBox" : "getActualBoundingBox", 2, (self, args) =>
        {
            var layout = LayoutOf(self);
            var (start, end) = RangeArguments(args, 0, layout.Text.Length, clusters: true);
            var (left, right, ascent, descent) = InkBox(layout, start, end);
            return Rect(-left, -ascent, left + right, ascent + descent);
        });

        Js.StrictMethod(Engine, prototype, shaped ? "selectionRects" : "getSelectionRects", 2, (self, args) =>
        {
            var layout = LayoutOf(self);
            var (start, end) = RangeArguments(args, 0, layout.Text.Length, clusters: false);
            var rects = new List<JsValue>();
            if (layout.Run is { } run && start != end)
            {
                foreach (var (x, width) in run.SelectionBoxes(start, end))
                    rects.Add(Rect(x - layout.Anchor, -(layout.Metrics.FontAscent - layout.LineOffset), width, layout.Metrics.FontAscent + layout.Metrics.FontDescent));
            }

            return Js.Array(Engine, rects);
        });

        Js.StrictMethod(Engine, prototype, shaped ? "advanceToPosition" : "getIndexFromOffset", 1, (self, args) =>
        {
            var layout = LayoutOf(self);
            var offset = Num(args, 0);
            if (double.IsNaN(offset))
                offset = 0;

            return Js.Num(layout.Run?.IndexFromOffset(layout.Anchor + offset) ?? 0);
        });

        if (shaped)
        {
            Js.StrictMethod(Engine, prototype, "positionToAdvance", 1, (self, args) =>
            {
                var layout = LayoutOf(self);
                var index = ULong(Js.Arg(args, 0), "position");
                if (index > layout.Text.Length)
                    throw DomError("IndexSizeError", "The position is outside the text.");

                return Js.Num((layout.Run?.CaretX((int)index) ?? 0) - layout.Anchor);
            });

            Js.StrictMethod(Engine, prototype, "glyphClusters", 0, (self, args) =>
            {
                var layout = LayoutOf(self);
                var (start, end) = RangeArguments(args, 0, layout.Text.Length, clusters: true);
                var result = new List<JsValue>();
                foreach (var cluster in Clusters(layout, start, end, align: null))
                {
                    var run = new GlyphRunObject(Engine, GlyphRunPrototype, cluster.Text, layout.Baseline, cluster.X, 0);
                    Js.Value(run, "x", Js.Num(cluster.X));
                    Js.Value(run, "y", Js.Num(0));
                    Js.Value(run, "width", Js.Num(cluster.Width));
                    Js.Value(run, "start", Js.Num(cluster.Start));
                    Js.Value(run, "end", Js.Num(cluster.End));
                    var context = This(self).Context;
                    var text = cluster.Text;
                    Js.Method(Engine, run, "textMetrics", 0, (_, _) => MeasureWith(context, text, layout.Align, layout.Baseline));
                    result.Add(run);
                }

                return Js.Array(Engine, result);
            });

            Js.StrictMethod(Engine, prototype, "glyphRuns", 0, (self, _) =>
            {
                var layout = LayoutOf(self);
                var result = new List<JsValue>();
                foreach (var segment in layout.Run?.Runs ?? [])
                {
                    var x = segment.X - layout.Anchor;
                    var run = new GlyphRunObject(Engine, GlyphRunPrototype, layout.Text[segment.Start..segment.End], layout.Baseline, x, 0);
                    Js.Value(run, "x", Js.Num(x));
                    Js.Value(run, "y", Js.Num(0));
                    Js.Value(run, "width", Js.Num(segment.Width));
                    Js.Value(run, "start", Js.Num(segment.Start));
                    Js.Value(run, "end", Js.Num(segment.End));
                    result.Add(run);
                }

                return Js.Array(Engine, result);
            });
        }
        else
        {
            Js.StrictMethod(Engine, prototype, "getTextClusters", 0, (self, args) =>
            {
                var layout = LayoutOf(self);

                // The options may come first (getTextClusters({align: 'left'})), or after the range.
                ObjectInstance? options = null;
                var rangeArgs = args;
                if (args.Length > 0 && args[0] is ObjectInstance first)
                {
                    options = first;
                    rangeArgs = [];
                }
                else if (args.Length > 2 && args[2] is ObjectInstance third)
                {
                    options = third;
                }

                var (start, end) = RangeArguments(rangeArgs, 0, layout.Text.Length, clusters: true);
                var align = ClusterWord(options, "align", AlignWords) ?? layout.Align;
                var baseline = ClusterWord(options, "baseline", BaselineWords) ?? layout.Baseline;
                var y = layout.LineOffset - LineOffsetOf(baseline, layout.Metrics);

                var result = new List<JsValue>();
                foreach (var cluster in Clusters(layout, start, end, align))
                {
                    var item = Js.NewObject(Engine);
                    Js.Value(item, "text", Js.Str(cluster.Text));
                    Js.Value(item, "start", Js.Num(cluster.Start));
                    Js.Value(item, "end", Js.Num(cluster.End));
                    Js.Value(item, "x", Js.Num(cluster.X));
                    Js.Value(item, "y", Js.Num(y == 0 ? 0 : y));
                    Js.Value(item, "align", Js.Str(align));
                    Js.Value(item, "baseline", Js.Str(baseline));
                    Js.Value(item, "fontIndex", Js.Num(0));
                    result.Add(item);
                }

                return Js.Array(Engine, result);
            });
        }
    }

    // TextMetrics for a piece of text under the alignment and baseline a shaped text had.
    private JsValue MeasureWith(ContextObject context, string text, string align, string baseline)
    {
        var state = context.Bitmap.State;
        var (savedAlign, savedBaseline) = (state.TextAlign, state.TextBaseline);
        state.TextAlign = align;
        state.TextBaseline = baseline;
        try
        {
            return Measure(context, text, shaped: false);
        }
        finally
        {
            state.TextAlign = savedAlign;
            state.TextBaseline = savedBaseline;
        }
    }

    private sealed record ClusterBox(string Text, int Start, int End, double X, double Width);

    // The characters of a range, each as the box it takes, where the alignment point is the origin; with an alignment, the point of the box that alignment names.
    private IEnumerable<ClusterBox> Clusters(TextLayout layout, int start, int end, string? align)
    {
        if (layout.Run is not { } run)
            yield break;

        var bounds = run.Boundaries;
        var rtl = layout.Run is not null && IsRightToLeftText(layout);
        for (var i = 0; i + 1 < bounds.Count; i++)
        {
            var (clusterStart, clusterEnd) = (bounds[i], bounds[i + 1]);
            if (clusterEnd <= start || clusterStart >= end)
                continue;

            var boxes = run.SelectionBoxes(clusterStart, clusterEnd);
            double left, width;
            if (boxes.Count == 0)
            {
                left = run.CaretX(clusterStart);
                width = 0;
            }
            else
            {
                left = boxes.Min(b => b.X);
                width = boxes.Max(b => b.X + b.Width) - left;
            }

            var x = left - layout.Anchor + (align is null ? 0 : AlignOffsetOf(align, width, rtl));
            yield return new ClusterBox(layout.Text[clusterStart..clusterEnd], clusterStart, clusterEnd, x, width);
        }
    }

    // Whether the text of a laid-out run reads right to left as a whole (its first run does).
    private static bool IsRightToLeftText(TextLayout layout) => layout.Run?.Runs is { Count: > 0 } runs && runs[0].IsRightToLeft && runs.All(r => r.IsRightToLeft);
}
