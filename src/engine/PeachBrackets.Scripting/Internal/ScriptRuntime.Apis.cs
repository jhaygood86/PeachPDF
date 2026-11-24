using Acornima;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Microsoft.Extensions.Logging;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

// The objects the host adds to the scripts (ScriptApi), and the conversion of values between the script engine and the host.
internal sealed partial class ScriptRuntime
{
    private const int MaxValueDepth = 8;
    private readonly List<(ScriptApi Api, Action<string, ScriptValue> Handler)> _apiSubscriptions = [];

    /// <summary>Installs the objects the host of the document asked for.</summary>
    private void InstallHostApis()
    {
        foreach (var api in Document.Apis)
            InstallApi(api);
    }

    /// <summary>Stops delivering the host objects' events: the document is done with.</summary>
    public void DetachApis()
    {
        foreach (var (api, handler) in _apiSubscriptions)
            api.EventRaised -= handler;

        _apiSubscriptions.Clear();
    }

    /// <summary>The <c>Log</c> object: the levels of the logger as methods, and <c>data</c>, which hands a named value to whatever runs the document.</summary>
    private ScriptApi BuildLogApi()
    {
        var log = new ScriptApi("Log");
        foreach (var (name, level) in new[] { ("debug", LogLevel.Debug), ("info", LogLevel.Information), ("warning", LogLevel.Warning), ("error", LogLevel.Error) })
        {
            var captured = level;
            log.AddMethod(name, args =>
            {
                WriteLog(captured, ScriptLogEvents.Message, args.Count > 0 ? args[0].AsString() ?? string.Empty : "undefined");
                return ScriptValue.Undefined;
            });
        }

        log.AddMethod("data", args =>
        {
            var entry = new ScriptDataEntry(args.Count > 0 ? args[0].AsString() ?? string.Empty : "undefined", args.Count > 1 ? args[1].AsString() ?? string.Empty : "undefined");
            _options.Logger?.Log(LogLevel.Information, ScriptLogEvents.Data, entry, null, static (state, _) => state.ToString());
            return ScriptValue.Undefined;
        });
        return log;
    }

    /// <summary>Makes a global of a host object: its properties as accessors, its methods as functions, its events as listenable events.</summary>
    private void InstallApi(ScriptApi api)
    {
        var target = new TargetObject(Engine, EventTargetPrototype);

        foreach (var name in api.PropertyNames)
        {
            var captured = name;
            Js.Accessor(Engine, target, captured,
                _ => FromScriptValue(HostCall(() => api.GetProperty(captured))),
                api.IsWritable(captured) ? (_, value) => HostCall(() =>
                {
                    api.SetProperty(captured, ToScriptValue(value));
                    return ScriptValue.Undefined;
                }) : null);
        }

        foreach (var name in api.MethodNames)
        {
            var captured = name;
            Js.Method(Engine, target, captured, 0, (_, args) =>
                FromScriptValue(HostCall(() => api.Call(captured, [.. args.Select(a => ToScriptValue(a))]))));
        }

        foreach (var name in api.EventNames)
        {
            var captured = name;
            Js.Accessor(Engine, target, "on" + captured,
                _ => target.Handlers.TryGetValue(captured, out var handler) ? handler : JsValue.Null,
                (_, value) =>
                {
                    if (Js.IsCallable(value)) target.Handlers[captured] = value;
                    else target.Handlers.Remove(captured);
                });
        }

        if (api.EventNames.Count > 0)
        {
            void Deliver(string type, ScriptValue detail)
            {
                var ev = new EventObject(Engine, _eventPrototypes["CustomEvent"], type, false, false) { TimeStamp = NowMs };
                ev.FastSetProperty("detail", new PropertyDescriptor(FromScriptValue(detail), PropertyFlag.ConfigurableEnumerableWritable));
                DispatchEvent(target, ev);
                RunMicrotasks();
            }

            api.EventRaised += Deliver;
            _apiSubscriptions.Add((api, Deliver));
        }

        Engine.Global.FastSetProperty(api.Name, new PropertyDescriptor(target, PropertyFlag.Writable | PropertyFlag.Configurable));
    }

    // Runs host code a script called; what it throws (other than what the script engine itself throws) is the script's to catch as an Error.
    private T HostCall<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (Exception error) when (error is not (JavaScriptException or OperationCanceledException or ParseErrorException or TimeoutException
            or RecursionDepthOverflowException or StatementsCountOverflowException or MemoryLimitExceededException))
        {
            throw new JavaScriptException(Engine.Intrinsics.Error, error.Message);
        }
    }

    /// <summary>What a script's value is to the host: text, numbers, booleans, arrays, plain objects, nodes and functions.</summary>
    public ScriptValue ToScriptValue(JsValue value, int depth = 0)
    {
        if (value.IsUndefined())
            return ScriptValue.Undefined;
        if (value.IsNull())
            return ScriptValue.Null;
        if (value.IsBoolean())
            return value.AsBoolean();
        if (value.IsNumber())
            return value.AsNumber();
        if (value.IsString())
            return value.AsString();
        if (value is NodeObject node)
            return ScriptValue.From(node.Node);
        if (Js.IsCallable(value))
        {
            return ScriptValue.From(new ScriptCallback(args =>
                Call(value, JsValue.Undefined, [.. args.Select(FromScriptValue)]) is { } result ? ToScriptValue(result) : ScriptValue.Undefined));
        }

        if (depth >= MaxValueDepth || !value.IsObject())
            return ScriptValue.Null;

        var obj = value.AsObject();
        if (obj.IsArray())
        {
            var length = (int)TypeConverter.ToNumber(obj.Get("length"));
            var items = new List<ScriptValue>(length);
            for (var i = 0; i < length; i++)
                items.Add(ToScriptValue(obj.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), depth + 1));

            return ScriptValue.From(items);
        }

        var properties = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
        foreach (var key in obj.GetOwnPropertyKeys())
        {
            if (key.IsString() && obj.GetOwnProperty(key).Enumerable)
                properties[key.AsString()] = ToScriptValue(obj.Get(key), depth + 1);
        }

        return ScriptValue.From(properties);
    }

    /// <summary>What the host's value is to a script.</summary>
    public JsValue FromScriptValue(ScriptValue value)
    {
        switch (value.Kind)
        {
            case ScriptValueKind.Null:
                return JsValue.Null;
            case ScriptValueKind.Boolean:
                return Js.Bool(value.AsBoolean());
            case ScriptValueKind.Number:
                return Js.Num(value.AsNumber());
            case ScriptValueKind.String:
                return Js.Str(value.AsString());
            case ScriptValueKind.Array:
                return Js.Array(Engine, [.. value.AsArray().Select(FromScriptValue)]);
            case ScriptValueKind.Object:
                {
                    var obj = Js.NewObject(Engine);
                    foreach (var (name, item) in value.AsObject())
                        obj.FastSetProperty(name, new PropertyDescriptor(FromScriptValue(item), PropertyFlag.ConfigurableEnumerableWritable));

                    return obj;
                }

            case ScriptValueKind.Function:
                {
                    var callback = value.AsCallback()!;
                    return Js.Function(Engine, "callback", 0, (_, args) => FromScriptValue(HostCall(() => callback.Invoke([.. args.Select(a => ToScriptValue(a))]))));
                }

            case ScriptValueKind.Node:
                return Wrap(value.AsNode()!);
            default:
                return JsValue.Undefined;
        }
    }
}
