using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>MutationObserver</c>: the callback, the records waiting for it, and what it is watching.</summary>
internal sealed class ObserverObject : ObjectInstance
{
    public ObserverObject(Engine engine, ObjectInstance prototype, JsValue callback) : base(engine)
    {
        Prototype = prototype;
        Callback = callback;
    }

    public JsValue Callback { get; }

    public List<JsValue> Queue { get; } = [];

    public List<ObserverRegistration> Registrations { get; } = [];
}

/// <summary>What an observer was asked to watch on one node.</summary>
internal sealed class ObserverRegistration(ObserverObject observer, ScriptNode target, ObserverOptions options, ObserverRegistration? source = null)
{
    public ObserverObject Observer { get; } = observer;

    public ScriptNode Target { get; } = target;

    public ObserverOptions Options { get; set; } = options;

    /// <summary>For a registration made so a removed subtree stays watched until the records are delivered: the one it came from.</summary>
    public ObserverRegistration? Source { get; } = source;
}

internal sealed record ObserverOptions(bool ChildList, bool Attributes, bool CharacterData, bool Subtree, bool AttributeOldValue, bool CharacterDataOldValue, IReadOnlyList<string>? AttributeFilter);

internal sealed partial class ScriptRuntime
{
    private ObjectInstance MutationObserverPrototype = null!;
    private ObjectInstance MutationRecordPrototype = null!;
    private readonly List<ObserverRegistration> _registrations = [];
    private readonly List<ObserverObject> _notify = [];
    private readonly HashSet<ScriptDocument> _watched = [];
    private bool _deliveryQueued;

    private void WatchDocument(ScriptDocument document)
    {
        if (_watched.Add(document))
            document.Mutated += OnMutated;
    }

    private void InstallMutationObserver()
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;
        MutationRecordPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        Tag(MutationRecordPrototype, "MutationRecord");
        Define(Engine.Global, "MutationRecord", Js.Constructor(Engine, "MutationRecord", MutationRecordPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

        MutationObserverPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        Tag(MutationObserverPrototype, "MutationObserver");
        Define(Engine.Global, "MutationObserver", Js.Constructor(Engine, "MutationObserver", MutationObserverPrototype, 1, (_, args, _) =>
        {
            if (!Js.IsCallable(Js.Arg(args, 0)))
                throw TypeError("The callback provided as parameter 1 is not a function.");

            return new ObserverObject(Engine, MutationObserverPrototype, args[0]);
        }));

        ObserverObject Observer(JsValue self) => self as ObserverObject ?? throw TypeError("Illegal invocation");

        Js.Method(Engine, MutationObserverPrototype, "observe", 1, (self, args) =>
        {
            var observer = Observer(self);
            if (Js.Arg(args, 0) is not NodeObject target)
                throw TypeError("The parameter 1 is not of type 'Node'.");

            var options = ReadOptions(Js.Arg(args, 1));
            var existing = observer.Registrations.FirstOrDefault(r => r.Source is null && ReferenceEquals(r.Target, target.Node));
            if (existing is not null)
            {
                foreach (var transient in observer.Registrations.Where(r => ReferenceEquals(r.Source, existing)).ToArray())
                {
                    observer.Registrations.Remove(transient);
                    _registrations.Remove(transient);
                }

                existing.Options = options;
            }
            else
            {
                var registration = new ObserverRegistration(observer, target.Node, options);
                observer.Registrations.Add(registration);
                _registrations.Add(registration);
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, MutationObserverPrototype, "disconnect", 0, (self, _) =>
        {
            var observer = Observer(self);
            foreach (var registration in observer.Registrations)
                _registrations.Remove(registration);

            observer.Registrations.Clear();
            observer.Queue.Clear();
            return JsValue.Undefined;
        });
        Js.Method(Engine, MutationObserverPrototype, "takeRecords", 0, (self, _) =>
        {
            var observer = Observer(self);
            var taken = Js.Array(Engine, [.. observer.Queue]);
            observer.Queue.Clear();
            return taken;
        });
    }

    private ObserverOptions ReadOptions(JsValue value)
    {
        if (value is not ObjectInstance bag)
        {
            if (value.IsUndefined())
                throw TypeError("The options must have childList, attributes or characterData set.");

            throw TypeError("The options are not an object.");
        }

        bool? Flag(string name) => bag.HasProperty(name) && !bag.Get(name).IsUndefined() ? TypeConverter.ToBoolean(bag.Get(name)) : null;

        var childList = Flag("childList") ?? false;
        var attributes = Flag("attributes");
        var characterData = Flag("characterData");
        var subtree = Flag("subtree") ?? false;
        var attributeOldValue = Flag("attributeOldValue");
        var characterDataOldValue = Flag("characterDataOldValue");
        IReadOnlyList<string>? filter = null;
        if (bag.HasProperty("attributeFilter") && bag.Get("attributeFilter") is { } list && !list.IsUndefined())
        {
            var array = list as ObjectInstance ?? throw TypeError("The attribute filter is not a sequence.");
            var length = (int)TypeConverter.ToNumber(array.Get("length"));
            filter = [.. Enumerable.Range(0, length).Select(i => Js.AsText(array.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture))))];
        }

        if (attributes is null && (attributeOldValue is not null || filter is not null))
            attributes = true;
        if (characterData is null && characterDataOldValue is not null)
            characterData = true;

        if (!childList && attributes != true && characterData != true)
            throw TypeError("The options must have childList, attributes or characterData set.");
        if (attributeOldValue == true && attributes == false)
            throw TypeError("attributeOldValue needs attributes.");
        if (filter is not null && attributes == false)
            throw TypeError("attributeFilter needs attributes.");
        if (characterDataOldValue == true && characterData == false)
            throw TypeError("characterDataOldValue needs characterData.");

        return new ObserverOptions(childList, attributes == true, characterData == true, subtree, attributeOldValue == true, characterDataOldValue == true, filter);
    }

    // A change was made: queue a record for each observer watching the node or an ancestor that watches its subtree.
    private void OnMutated(ScriptMutation mutation)
    {
        if (_ranges.Count > 0)
            AdjustRanges(mutation);

        if (_registrations.Count == 0)
            return;

        var chain = new List<ScriptNode>();
        for (var node = mutation.Target; node is not null; node = node.Parent)
            chain.Add(node);

        var interested = new List<(ObserverObject Observer, bool OldValue)>();
        foreach (var registration in _registrations.ToArray())
        {
            var depth = chain.IndexOf(registration.Target);
            if (depth < 0 || (depth > 0 && !registration.Options.Subtree))
                continue;

            var o = registration.Options;
            var matches = mutation.Kind switch
            {
                ScriptMutationKind.ChildList => o.ChildList,
                ScriptMutationKind.Attributes => o.Attributes && (o.AttributeFilter is null || (AttributeNamespaceOf(mutation) is null && o.AttributeFilter.Contains(mutation.AttributeName!))),
                _ => o.CharacterData,
            };

            if (!matches)
                continue;

            var wantsOld = mutation.Kind == ScriptMutationKind.Attributes ? o.AttributeOldValue : mutation.Kind == ScriptMutationKind.CharacterData && o.CharacterDataOldValue;
            var at = interested.FindIndex(i => ReferenceEquals(i.Observer, registration.Observer));
            if (at < 0)
                interested.Add((registration.Observer, wantsOld));
            else if (wantsOld)
                interested[at] = (registration.Observer, true);
        }

        // A removed node keeps being watched by whoever watched it, until the records are delivered.
        if (mutation.Kind == ScriptMutationKind.ChildList && mutation.Removed is { Count: > 0 } removed)
        {
            foreach (var registration in _registrations.ToArray())
            {
                var depth = chain.IndexOf(registration.Target);
                if (depth < 0 || (depth > 0 && !registration.Options.Subtree) || !registration.Options.Subtree)
                    continue;

                foreach (var node in removed)
                {
                    var transient = new ObserverRegistration(registration.Observer, node, registration.Options, registration.Source ?? registration);
                    registration.Observer.Registrations.Add(transient);
                    _registrations.Add(transient);
                }
            }
        }

        foreach (var (observer, oldValue) in interested)
        {
            observer.Queue.Add(MakeRecord(mutation, oldValue));
            if (!_notify.Contains(observer))
                _notify.Add(observer);
        }

        if (interested.Count > 0 && !_deliveryQueued)
        {
            _deliveryQueued = true;
            Engine.Evaluate("(function (f) { Promise.resolve().then(f); })").Call(JsValue.Undefined, [Js.Function(Engine, "notify", 0, (_, _) =>
            {
                DeliverRecords();
                return JsValue.Undefined;
            })]);
        }
    }

    private string? AttributeNamespaceOf(ScriptMutation mutation)
    {
        var element = Wrap(mutation.Target);
        return element.AttributeNames.TryGetValue(mutation.AttributeName!, out var info) ? info.Namespace : null;
    }

    private JsValue MakeRecord(ScriptMutation mutation, bool withOldValue)
    {
        var record = new JsObject(Engine) { Prototype = MutationRecordPrototype };
        void Set(string name, JsValue value) => record.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.ConfigurableEnumerableWritable));

        Set("type", Js.Str(mutation.Kind switch
        {
            ScriptMutationKind.ChildList => "childList",
            ScriptMutationKind.Attributes => "attributes",
            _ => "characterData",
        }));
        Set("target", Wrap(mutation.Target));
        Set("addedNodes", WrapList(mutation.Added ?? []));
        Set("removedNodes", WrapList(mutation.Removed ?? []));
        Set("previousSibling", WrapOrNull(mutation.Previous));
        Set("nextSibling", WrapOrNull(mutation.Next));
        Set("attributeName", mutation.AttributeName is null ? JsValue.Null : Js.Str(mutation.AttributeName));
        Set("attributeNamespace", mutation.Kind == ScriptMutationKind.Attributes ? Js.Str(AttributeNamespaceOf(mutation)) : JsValue.Null);
        Set("oldValue", withOldValue && mutation.OldValue is not null ? Js.Str(mutation.OldValue) : JsValue.Null);
        return record;
    }

    // The microtask after a change: each observer with records gets them in one call.
    private void DeliverRecords()
    {
        _deliveryQueued = false;
        var observers = _notify.ToArray();
        _notify.Clear();
        foreach (var observer in observers)
        {
            foreach (var transient in observer.Registrations.Where(r => r.Source is not null).ToArray())
            {
                observer.Registrations.Remove(transient);
                _registrations.Remove(transient);
            }

            if (observer.Queue.Count == 0)
                continue;

            var records = Js.Array(Engine, [.. observer.Queue]);
            observer.Queue.Clear();
            try
            {
                Call(observer.Callback, observer, records, observer);
            }
            catch (Exception error) when (IsScriptFailure(error))
            {
                ReportError(error);
            }
        }
    }
}
