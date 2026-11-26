using System;
using System.Collections.Generic;

namespace PeachBrackets.ScriptHosting;

/// <summary>
/// A global object the host adds to a document's scripts: a name, and members that are properties, methods and events. It has no reference to any
/// script engine, so the code that makes one (PeachPDF's <c>PDF</c> object, say) does not depend on the package that runs the scripts: add it to
/// <see cref="HtmlDocumentOptions.ScriptApis"/> and the script host installs it.
/// </summary>
/// <remarks>
/// One instance serves one document: it is what the document's scripts talk to, and it is told when the scripts have run and how the layout came out.
/// A method that throws is thrown on to the script as an <c>Error</c> its script can catch.
/// </remarks>
public sealed class ScriptApi
{
    private readonly Dictionary<string, PropertyMember> _properties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<ScriptValue>, ScriptValue>> _methods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);

    private sealed record PropertyMember(Func<ScriptValue> Get, Action<ScriptValue>? Set);

    /// <summary>Makes an object that scripts reach as the global <paramref name="name"/>.</summary>
    /// <param name="name">the global's name, a valid JavaScript identifier</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a JavaScript identifier</exception>
    public ScriptApi(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!IsIdentifier(name))
            throw new ArgumentException($"'{name}' is not a JavaScript identifier.", nameof(name));

        Name = name;
    }

    /// <summary>The name scripts reach the object by.</summary>
    public string Name { get; }

    /// <summary>Adds a property scripts can read, and write when <paramref name="set"/> is given.</summary>
    /// <param name="name">the property name</param>
    /// <param name="get">called for each read</param>
    /// <param name="set">called with the value a script assigns, or null for a read-only property (an assignment is ignored)</param>
    /// <returns>this object, for chaining</returns>
    public ScriptApi AddProperty(string name, Func<ScriptValue> get, Action<ScriptValue>? set = null)
    {
        Check(name);
        ArgumentNullException.ThrowIfNull(get);
        _properties[name] = new PropertyMember(get, set);
        return this;
    }

    /// <summary>Adds a constant property.</summary>
    /// <param name="name">the property name</param>
    /// <param name="value">its value</param>
    /// <returns>this object, for chaining</returns>
    public ScriptApi AddValue(string name, ScriptValue value) => AddProperty(name, () => value);

    /// <summary>Adds a method scripts can call.</summary>
    /// <param name="name">the method name</param>
    /// <param name="body">called with the script's arguments; what it returns is the call's result</param>
    /// <returns>this object, for chaining</returns>
    public ScriptApi AddMethod(string name, Func<IReadOnlyList<ScriptValue>, ScriptValue> body)
    {
        Check(name);
        ArgumentNullException.ThrowIfNull(body);
        _methods[name] = body;
        return this;
    }

    /// <summary>
    /// Declares an event: scripts listen with <c>api.addEventListener(name, fn)</c> or <c>api.on&lt;name&gt; = fn</c>, and <see cref="Raise"/> sends it.
    /// </summary>
    /// <param name="name">the event type</param>
    /// <returns>this object, for chaining</returns>
    public ScriptApi AddEvent(string name)
    {
        Check(name);
        _events.Add(name);
        return this;
    }

    /// <summary>The names of the properties, in the order they were added.</summary>
    public IReadOnlyCollection<string> PropertyNames => _properties.Keys;

    /// <summary>The names of the methods.</summary>
    public IReadOnlyCollection<string> MethodNames => _methods.Keys;

    /// <summary>The event types.</summary>
    public IReadOnlyCollection<string> EventNames => _events;

    /// <summary>Reads a property the way a script does.</summary>
    /// <param name="name">the property name</param>
    /// <returns>its value, or <c>undefined</c> when there is no such property</returns>
    public ScriptValue GetProperty(string name) => _properties.TryGetValue(name, out var member) ? member.Get() : ScriptValue.Undefined;

    /// <summary>Whether a script can assign the property.</summary>
    /// <param name="name">the property name</param>
    /// <returns>true for a property added with a setter</returns>
    public bool IsWritable(string name) => _properties.TryGetValue(name, out var member) && member.Set is not null;

    /// <summary>Assigns a property the way a script does; nothing happens for a read-only or unknown property.</summary>
    /// <param name="name">the property name</param>
    /// <param name="value">the value</param>
    public void SetProperty(string name, ScriptValue value)
    {
        if (_properties.TryGetValue(name, out var member))
            member.Set?.Invoke(value);
    }

    /// <summary>Calls a method the way a script does.</summary>
    /// <param name="name">the method name</param>
    /// <param name="arguments">the arguments</param>
    /// <returns>what the method returned</returns>
    /// <exception cref="InvalidOperationException">there is no such method</exception>
    public ScriptValue Call(string name, params ScriptValue[] arguments) =>
        _methods.TryGetValue(name, out var body) ? body(arguments) : throw new InvalidOperationException($"{Name} has no method '{name}'.");

    /// <summary>Sends an event to the scripts listening for it.</summary>
    /// <param name="name">the event type, which must have been declared with <see cref="AddEvent"/></param>
    /// <param name="detail">the event's <c>detail</c>, or <c>null</c></param>
    /// <exception cref="InvalidOperationException">the event was not declared</exception>
    public void Raise(string name, ScriptValue detail = default)
    {
        if (!_events.Contains(name))
            throw new InvalidOperationException($"{Name} has no event '{name}'.");

        EventRaised?.Invoke(name, detail);
    }

    /// <summary>Raised by <see cref="Raise"/>; the script host listens for it to deliver the event. Not for the code that made the object.</summary>
    public event Action<string, ScriptValue>? EventRaised;

    /// <summary>Raised just before the document's scripts run, with the document they will run in: the first moment the object can reach it.</summary>
    public event Action<ScriptDocument>? ScriptsStarting;

    /// <summary>Raised once the document's scripts have all run, before the document is styled. The object may read and change the document here.</summary>
    public event Action<ScriptDocument>? ScriptsCompleted;

    private Action<ScriptLayout>? _layoutCompleted;
    private Action<ScriptLayout>? _layoutFinished;

    /// <summary>
    /// Raised after each layout of the document. Subscribing keeps the script document live so there is something to be told about, which a
    /// document that nothing subscribed to does not do.
    /// </summary>
    public event Action<ScriptLayout> LayoutCompleted
    {
        add => _layoutCompleted += value;
        remove => _layoutCompleted -= value;
    }

    /// <summary>Raised once, after the last layout, as the document is about to be painted. Subscribing keeps the script document live.</summary>
    public event Action<ScriptLayout> LayoutFinished
    {
        add => _layoutFinished += value;
        remove => _layoutFinished -= value;
    }

    /// <summary>Raised when the document is disposed (or its scripts run again), once for a document whose scripts ran.</summary>
    public event Action? Closed;

    internal bool ObservesLayout => _layoutCompleted is not null || _layoutFinished is not null;

    internal void NoteScriptsStarting(ScriptDocument document) => ScriptsStarting?.Invoke(document);

    internal void NoteScriptsCompleted(ScriptDocument document) => ScriptsCompleted?.Invoke(document);

    internal void NoteLayoutCompleted(ScriptLayout layout) => _layoutCompleted?.Invoke(layout);

    internal void NoteLayoutFinished(ScriptLayout layout) => _layoutFinished?.Invoke(layout);

    internal void NoteClosed() => Closed?.Invoke();

    private void Check(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_properties.ContainsKey(name) || _methods.ContainsKey(name) || _events.Contains(name))
            throw new ArgumentException($"{Name} already has a member '{name}'.", nameof(name));
    }

    private static bool IsIdentifier(string name)
    {
        if (!(char.IsLetter(name[0]) || name[0] is '_' or '$'))
            return false;

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '$'))
                return false;
        }

        return true;
    }
}
