using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

// The part of the script environment that exists because a document is laid out after its scripts have run: the Prince object, whose
// functions are called after each layout and whose complete event fires once the layout is final.
internal sealed partial class ScriptRuntime
{
    private TargetObject? _prince;
    private readonly List<JsValue> _postLayoutFunctions = [];

    private ScriptLayout? _layout;
    private readonly Dictionary<BoxReport, ObjectInstance> _boxObjects = new(ReferenceEqualityComparer.Instance as IEqualityComparer<BoxReport>);

    /// <summary>Whether a script asked for the boxes of the layout (<c>Prince.trackBoxes = true</c>), which <c>getPrinceBoxes</c> answers only for.</summary>
    public bool TrackBoxes { get; private set; }

    /// <summary>The number of pages the last layout came to; 0 until the document has been laid out.</summary>
    public int PageCount { get; private set; }

    /// <summary>Whether the scripts asked to be called after layout (a post-layout function or a <c>complete</c> listener), so the document must be kept live.</summary>
    public bool WantsLayoutCallbacks =>
        _postLayoutFunctions.Count > 0 || (_prince is { } prince && (prince.Handlers.ContainsKey("complete") || (prince.Listeners.TryGetValue("complete", out var list) && list.Count > 0)));

    private ObjectInstance PrinceObject()
    {
        var prince = new TargetObject(Engine, EventTargetPrototype);
        _prince = prince;

        Js.Method(Engine, prince, "registerPostLayoutFunc", 1, (_, args) =>
        {
            if (!Js.IsCallable(Js.Arg(args, 0)))
                throw TypeError("registerPostLayoutFunc needs a function.");

            _postLayoutFunctions.Add(args[0]);
            return JsValue.Undefined;
        });
        Js.Accessor(Engine, prince, "pageCount", _ => Js.Num(PageCount));
        Js.Accessor(Engine, prince, "trackBoxes", _ => Js.Bool(TrackBoxes), (_, value) => TrackBoxes = TypeConverter.ToBoolean(value));
        Js.Method(Engine, ElementPrototype, "getPrinceBoxes", 0, (self, _) =>
        {
            if (!TrackBoxes || _layout is not { } layout)
                return Js.Array(Engine, []);

            return Js.Array(Engine, [.. layout.GetBoxes(Sn(self)).Select(b => (JsValue)BoxObject(b))]);
        });
        if (Engine.Global.Get("Log") is ObjectInstance log)
            Js.Value(prince, "Log", log);

        return prince;
    }

    private void UseLayout(ScriptLayout layout)
    {
        PageCount = layout.PageCount;
        if (!ReferenceEquals(_layout, layout))
        {
            _layout = layout;
            _boxObjects.Clear();
        }
    }

    // The box a script sees: the properties other print engines give a box (type, pageNum, x, y, w, h with the origin at the lower left of the page,
    // the used margins, padding and borders), with the children, parent and element read when asked for.
    private ObjectInstance BoxObject(BoxReport box)
    {
        if (_boxObjects.TryGetValue(box, out var existing))
            return existing;

        var obj = Js.NewObject(Engine);
        _boxObjects[box] = obj;
        void Set(string name, JsValue value) => obj.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.ConfigurableEnumerableWritable));

        Set("type", Js.Str(box.Kind switch
        {
            BoxReportKind.Page => "BODY",
            BoxReportKind.Footnotes => "FOOTNOTES",
            BoxReportKind.Span => "SPAN",
            BoxReportKind.Text => "TEXT",
            BoxReportKind.Image => "IMAGE",
            BoxReportKind.Svg => "SVG",
            _ => "BOX",
        }));
        Set("pageNum", Js.Num(box.PageNumber));
        Set("x", Js.Num(box.X));
        Set("y", Js.Num(box.PageHeight - (box.Y + box.Height)));
        Set("w", Js.Num(box.Width));
        Set("h", Js.Num(box.Height));
        foreach (var (prefix, edges) in new[] { ("margin", box.Margin), ("padding", box.Padding), ("border", box.Border) })
        {
            Set(prefix + "Top", Js.Num(edges.Top));
            Set(prefix + "Right", Js.Num(edges.Right));
            Set(prefix + "Bottom", Js.Num(edges.Bottom));
            Set(prefix + "Left", Js.Num(edges.Left));
        }

        Set("pseudo", Js.Str(box.PseudoElement));
        Set("text", Js.Str(box.Text ?? string.Empty));
        Set("src", Js.Str(box.ImageSource));
        Js.Accessor(Engine, obj, "element", _ => box.Element is { } element ? Wrap(element) : JsValue.Null);
        Js.Accessor(Engine, obj, "parent", _ => box.Parent is { } parent ? BoxObject(parent) : JsValue.Null);
        Js.Accessor(Engine, obj, "children", _ => Js.Array(Engine, [.. box.Children.Select(c => (JsValue)BoxObject(c))]));
        return obj;
    }

    /// <summary>Runs the functions registered for after layout, each once.</summary>
    /// <returns>true when a function registered another, which asks for another pass</returns>
    public bool RunPostLayout(ScriptLayout layout)
    {
        UseLayout(layout);
        var functions = _postLayoutFunctions.ToArray();
        _postLayoutFunctions.Clear();
        foreach (var function in functions)
            Call(function, JsValue.Undefined);

        RunMicrotasks();
        PlayTimers();
        return _postLayoutFunctions.Count > 0;
    }

    /// <summary>Fires <c>complete</c>: the layout is final.</summary>
    public void RunComplete(ScriptLayout layout)
    {
        UseLayout(layout);
        if (_prince is { } prince)
            FireEvent(prince, "complete", bubbles: false, cancelable: false);

        RunMicrotasks();
        PlayTimers();
    }
}
