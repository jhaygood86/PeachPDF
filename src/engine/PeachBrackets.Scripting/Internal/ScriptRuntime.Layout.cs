using Jint.Native;
using Jint.Native.Object;
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
        if (Engine.Global.Get("Log") is ObjectInstance log)
            Js.Value(prince, "Log", log);

        return prince;
    }

    /// <summary>Runs the functions registered for after layout, each once.</summary>
    /// <returns>true when a function registered another, which asks for another pass</returns>
    public bool RunPostLayout(ScriptLayout layout)
    {
        PageCount = layout.PageCount;
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
        PageCount = layout.PageCount;
        if (_prince is { } prince)
            FireEvent(prince, "complete", bubbles: false, cancelable: false);

        RunMicrotasks();
        PlayTimers();
    }
}
