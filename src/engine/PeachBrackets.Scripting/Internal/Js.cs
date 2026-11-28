using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Scripting.Internal;

/// <summary>Small helpers for building the objects a page sees, without the reflection-based interop Jint also has.</summary>
internal static class Js
{
    public static JsValue Str(string? value) => value is null ? JsValue.Null : JsString.Create(value);

    public static JsValue Num(double value) => JsNumber.Create(value);

    public static JsValue Bool(bool value) => value ? JsBoolean.True : JsBoolean.False;

    public static string AsText(JsValue value) => TypeConverter.ToString(value);

    /// <summary>Whether the value can be called: a function, a bound function or any other callable object.</summary>
    public static bool IsCallable(JsValue value) => value.IsObject() && value.AsObject().IsCallable();

    public static JsValue Arg(JsValue[] args, int index) => index < args.Length ? args[index] : JsValue.Undefined;

    public static bool IsNullish(JsValue value) => value.IsNull() || value.IsUndefined();

    public static JsValue Function(Engine engine, string name, int length, Func<JsValue, JsValue[], JsValue> body) =>
        new ClrFunction(engine, name, body, length);

    /// <summary>Defines a method on an object: writable and configurable, but not enumerable, as the built-ins are.</summary>
    public static void Method(Engine engine, ObjectInstance target, string name, int length, Func<JsValue, JsValue[], JsValue> body) =>
        target.FastSetProperty(name, new PropertyDescriptor(Function(engine, name, length, body), PropertyFlag.Writable | PropertyFlag.Configurable));

    /// <summary>
    /// <see cref="Method"/> for an operation WebIDL gives required arguments: <paramref name="length"/> is the number of them, and a call with fewer is a <c>TypeError</c>
    /// before the body runs.
    /// </summary>
    public static void StrictMethod(Engine engine, ObjectInstance target, string name, int length, Func<JsValue, JsValue[], JsValue> body) =>
        Method(engine, target, name, length, (self, args) =>
            args.Length < length
                ? throw new JavaScriptException(engine.Intrinsics.TypeError, $"Failed to execute '{name}': {length} argument{(length == 1 ? "" : "s")} required, but only {args.Length} present.")
                : body(self, args));

    /// <summary>Defines an accessor property; a null setter makes it read-only.</summary>
    public static void Accessor(Engine engine, ObjectInstance target, string name, Func<JsValue, JsValue> get, Action<JsValue, JsValue>? set = null)
    {
        var getter = Function(engine, "get " + name, 0, (self, _) => get(self));
        JsValue setter = set is null ? JsValue.Undefined : Function(engine, "set " + name, 1, (self, args) =>
        {
            set(self, Arg(args, 0));
            return JsValue.Undefined;
        });

        target.FastSetProperty(name, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: true));
    }

    public static void Value(ObjectInstance target, string name, JsValue value) =>
        target.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.Writable | PropertyFlag.Configurable));

    public static JsValue Array(Engine engine, IReadOnlyList<JsValue> items)
    {
        var values = new JsValue[items.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = items[i];

        return new JsArray(engine, values);
    }

    public static ObjectInstance NewObject(Engine engine) => new JsObject(engine);

    /// <summary>
    /// A constructor function with a prototype: <c>new Name(...)</c> and <c>instanceof Name</c> work. It is a script function that hands its arguments
    /// to <paramref name="construct"/>, because a native function cannot be called with <c>new</c>; the object <paramref name="construct"/> returns
    /// replaces the one <c>new</c> made.
    /// </summary>
    public static ObjectInstance Constructor(Engine engine, string name, ObjectInstance prototype, int length, Func<JsValue, JsValue[], JsValue, JsValue> construct)
    {
        var make = Function(engine, name, length, (self, args) => construct(self, args, JsValue.Undefined));
        var function = engine.Evaluate("(function (make) { return function " + name + "() { if (new.target === undefined) throw new TypeError(\"Failed to construct '" + name + "': Please use the 'new' operator, this DOM object constructor cannot be called as a function.\"); return make.apply(this, arguments); }; })").Call(make).AsObject();
        // Set, not FastSetProperty: a script function keeps its prototype in a slot of its own that only a normal write reaches.
        function.Set("prototype", prototype);
        prototype.FastSetProperty("constructor", new PropertyDescriptor(function, PropertyFlag.Writable | PropertyFlag.Configurable));
        return function;
    }
}
