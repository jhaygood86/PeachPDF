using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachBrackets.ScriptHosting;

/// <summary>What a <see cref="ScriptValue"/> holds.</summary>
public enum ScriptValueKind
{
    /// <summary>No value: <c>undefined</c>.</summary>
    Undefined,

    /// <summary><c>null</c>.</summary>
    Null,

    /// <summary>A true or false.</summary>
    Boolean,

    /// <summary>A number.</summary>
    Number,

    /// <summary>Text.</summary>
    String,

    /// <summary>A list of values: a script's array.</summary>
    Array,

    /// <summary>Named values: a script's plain object.</summary>
    Object,

    /// <summary>A function a script gave (a <see cref="ScriptCallback"/>), or one the host gave to a script.</summary>
    Function,

    /// <summary>A node of the script's document.</summary>
    Node,
}

/// <summary>
/// A value that crosses between a script and the host, with no reference to any script engine: the type a <see cref="ScriptApi"/> member takes
/// and returns. Numbers, text and booleans convert to and from it implicitly.
/// </summary>
public readonly struct ScriptValue : IEquatable<ScriptValue>
{
    private readonly object? _reference;
    private readonly double _number;

    private ScriptValue(ScriptValueKind kind, object? reference, double number)
    {
        Kind = kind;
        _reference = reference;
        _number = number;
    }

    /// <summary>What the value is.</summary>
    public ScriptValueKind Kind { get; }

    /// <summary><c>undefined</c>, which is also what a default <see cref="ScriptValue"/> is.</summary>
    public static ScriptValue Undefined => default;

    /// <summary><c>null</c>.</summary>
    public static ScriptValue Null { get; } = new(ScriptValueKind.Null, null, 0);

    /// <summary>Makes a boolean value.</summary>
    /// <param name="value">the boolean</param>
    /// <returns>the value</returns>
    public static ScriptValue From(bool value) => new(ScriptValueKind.Boolean, null, value ? 1 : 0);

    /// <summary>Makes a number value.</summary>
    /// <param name="value">the number</param>
    /// <returns>the value</returns>
    public static ScriptValue From(double value) => new(ScriptValueKind.Number, null, value);

    /// <summary>Makes a text value; a null string is <c>null</c>.</summary>
    /// <param name="value">the text</param>
    /// <returns>the value</returns>
    public static ScriptValue From(string? value) => value is null ? Null : new(ScriptValueKind.String, value, 0);

    /// <summary>Makes an array value.</summary>
    /// <param name="items">the items, in order</param>
    /// <returns>the value</returns>
    public static ScriptValue From(IReadOnlyList<ScriptValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(ScriptValueKind.Array, items, 0);
    }

    /// <summary>Makes an object value.</summary>
    /// <param name="properties">the properties and their values</param>
    /// <returns>the value</returns>
    public static ScriptValue From(IReadOnlyDictionary<string, ScriptValue> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return new(ScriptValueKind.Object, properties, 0);
    }

    /// <summary>Makes a function value a script can call.</summary>
    /// <param name="callback">the function</param>
    /// <returns>the value</returns>
    public static ScriptValue From(ScriptCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new(ScriptValueKind.Function, callback, 0);
    }

    /// <summary>Makes a node value.</summary>
    /// <param name="node">the node</param>
    /// <returns>the value</returns>
    public static ScriptValue From(ScriptNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(ScriptValueKind.Node, node, 0);
    }

    /// <summary>A boolean converts to a value.</summary>
    /// <param name="value">the boolean</param>
    public static implicit operator ScriptValue(bool value) => From(value);

    /// <summary>A number converts to a value.</summary>
    /// <param name="value">the number</param>
    public static implicit operator ScriptValue(double value) => From(value);

    /// <summary>An integer converts to a value.</summary>
    /// <param name="value">the integer</param>
    public static implicit operator ScriptValue(int value) => From(value);

    /// <summary>Text converts to a value; null text is <c>null</c>.</summary>
    /// <param name="value">the text</param>
    public static implicit operator ScriptValue(string? value) => From(value);

    /// <summary>Whether the value is <c>null</c> or <c>undefined</c>.</summary>
    public bool IsNullish => Kind is ScriptValueKind.Undefined or ScriptValueKind.Null;

    /// <summary>The boolean a boolean value holds; for any other value, what a script would take it to mean (non-empty text and non-zero numbers are true).</summary>
    public bool AsBoolean() => Kind switch
    {
        ScriptValueKind.Boolean => _number != 0,
        ScriptValueKind.Number => _number != 0 && !double.IsNaN(_number),
        ScriptValueKind.String => ((string)_reference!).Length > 0,
        ScriptValueKind.Undefined or ScriptValueKind.Null => false,
        _ => true,
    };

    /// <summary>The number a number or boolean value holds, or what text parses to; NaN for anything else.</summary>
    public double AsNumber() => Kind switch
    {
        ScriptValueKind.Number or ScriptValueKind.Boolean => _number,
        ScriptValueKind.String => double.TryParse((string)_reference!, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
        _ => double.NaN,
    };

    /// <summary>The text of a text value, the usual text of a number or boolean, and null for <c>null</c> and <c>undefined</c>.</summary>
    public string? AsString() => Kind switch
    {
        ScriptValueKind.String => (string)_reference!,
        ScriptValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        ScriptValueKind.Boolean => _number != 0 ? "true" : "false",
        ScriptValueKind.Undefined or ScriptValueKind.Null => null,
        ScriptValueKind.Array => string.Join(',', AsArray()),
        _ => _reference?.ToString(),
    };

    /// <summary>The items of an array value, or an empty list for any other kind.</summary>
    public IReadOnlyList<ScriptValue> AsArray() => _reference as IReadOnlyList<ScriptValue> ?? [];

    /// <summary>The properties of an object value, or an empty set for any other kind.</summary>
    public IReadOnlyDictionary<string, ScriptValue> AsObject() => _reference as IReadOnlyDictionary<string, ScriptValue> ?? new Dictionary<string, ScriptValue>();

    /// <summary>The function a function value holds, or null.</summary>
    public ScriptCallback? AsCallback() => _reference as ScriptCallback;

    /// <summary>The node a node value holds, or null.</summary>
    public ScriptNode? AsNode() => _reference as ScriptNode;

    /// <inheritdoc />
    public bool Equals(ScriptValue other) =>
        Kind == other.Kind && (Kind is ScriptValueKind.Number or ScriptValueKind.Boolean ? _number.Equals(other._number) : Equals(_reference, other._reference));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ScriptValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, _number, _reference);

    /// <summary>Whether two values are the same kind and hold the same thing (arrays, objects, functions and nodes by reference).</summary>
    /// <param name="left">the first value</param>
    /// <param name="right">the second value</param>
    public static bool operator ==(ScriptValue left, ScriptValue right) => left.Equals(right);

    /// <summary>Whether two values differ.</summary>
    /// <param name="left">the first value</param>
    /// <param name="right">the second value</param>
    public static bool operator !=(ScriptValue left, ScriptValue right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ScriptValueKind.Undefined => "undefined",
        ScriptValueKind.Null => "null",
        ScriptValueKind.Array => "[" + string.Join(", ", AsArray()) + "]",
        ScriptValueKind.Object => "{object}",
        ScriptValueKind.Function => "{function}",
        ScriptValueKind.Node => "{node}",
        _ => AsString() ?? string.Empty,
    };
}

/// <summary>A function a script gave the host, which the host can call, or one the host makes for a script to call.</summary>
public sealed class ScriptCallback
{
    private readonly Func<IReadOnlyList<ScriptValue>, ScriptValue> _invoke;

    /// <summary>Makes a function from a delegate. A script that calls it runs the delegate, and what it returns is the call's result.</summary>
    /// <param name="invoke">the function's body</param>
    public ScriptCallback(Func<IReadOnlyList<ScriptValue>, ScriptValue> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        _invoke = invoke;
    }

    /// <summary>Calls the function.</summary>
    /// <param name="arguments">the arguments</param>
    /// <returns>what the function returned; <c>undefined</c> when it threw (the script engine reports the error)</returns>
    public ScriptValue Invoke(params ScriptValue[] arguments) => _invoke(arguments);
}
