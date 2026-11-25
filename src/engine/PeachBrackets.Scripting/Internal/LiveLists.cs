using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>NodeList</c> or <c>HTMLCollection</c> that follows the document: what it holds is worked out again when the tree has changed since it was last read.</summary>
internal sealed class LiveListObject : ObjectInstance
{
    private readonly ScriptRuntime _runtime;
    private readonly ScriptDocument _document;
    private readonly Func<IEnumerable<ScriptNode>> _compute;
    private List<ScriptNode> _items = [];
    private int _version = -1;

    public LiveListObject(ScriptRuntime runtime, ObjectInstance prototype, ScriptDocument document, Func<IEnumerable<ScriptNode>> compute) : base(runtime.Engine)
    {
        _runtime = runtime;
        Prototype = prototype;
        _document = document;
        _compute = compute;
    }

    private List<ScriptNode> Items
    {
        get
        {
            if (_version != _document.Version)
            {
                _items = [.. _compute()];
                _version = _document.Version;
            }

            return _items;
        }
    }

    private bool TryIndex(JsValue property, out int index)
    {
        index = -1;
        return property.IsString() && uint.TryParse(property.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && (index = (int)Math.Min(parsed, int.MaxValue)) >= 0
            && parsed.ToString(CultureInfo.InvariantCulture) == property.AsString();
    }

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (TryIndex(property, out var index))
            return index < Items.Count ? _runtime.Wrap(Items[index]) : JsValue.Undefined;

        if (property.IsString() && property.AsString() == "length")
            return JsNumber.Create(Items.Count);

        return base.Get(property, receiver);
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
    {
        if (TryIndex(property, out var index))
            return index < Items.Count ? new PropertyDescriptor(_runtime.Wrap(Items[index]), PropertyFlag.Enumerable | PropertyFlag.Configurable) : PropertyDescriptor.Undefined;

        return base.GetOwnProperty(property);
    }

    public override bool HasProperty(JsValue property)
    {
        if (TryIndex(property, out var index))
            return index < Items.Count;

        return property.IsString() && property.AsString() == "length" || base.HasProperty(property);
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol)
    {
        var keys = new List<JsValue>();
        if ((types & Types.String) != 0)
        {
            for (var i = 0; i < Items.Count; i++)
                keys.Add(i.ToString(CultureInfo.InvariantCulture));
        }

        keys.AddRange(base.GetOwnPropertyKeys(types));
        return keys;
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver) =>
        TryIndex(property, out _) ? false : base.Set(property, value, receiver);
}

internal sealed partial class ScriptRuntime
{
    /// <summary>A list that stays current with the document: a <c>NodeList</c>, or an <c>HTMLCollection</c> when <paramref name="collection"/> is true.</summary>
    public JsValue WrapLive(ScriptDocument document, Func<IEnumerable<ScriptNode>> compute, bool collection) =>
        new LiveListObject(this, collection ? HtmlCollectionPrototype : NodeListPrototype, document, compute);

    // The descendant elements with a qualified name; "*" is all of them.
    private IEnumerable<ScriptNode> ElementsByTagName(ScriptNode root, string name)
    {
        var all = root.QuerySelectorAll("*");
        if (name == "*")
            return all;

        var lower = name.ToLowerInvariant();
        return all.Where(element =>
        {
            var wrapper = Wrap(element);
            var qualified = wrapper.NamespaceInfo is { } info ? (info.Prefix is null ? info.LocalName : info.Prefix + ":" + info.LocalName) : element.TagName!;
            var html = element.OwnerDocument.IsHtml && (wrapper.NamespaceInfo is null || wrapper.NamespaceInfo.Namespace == HtmlNamespace);
            return html ? qualified.ToLowerInvariant() == lower : qualified == name;
        });
    }

    private static IEnumerable<ScriptNode> ElementsByClassName(ScriptNode root, string classes)
    {
        var wanted = classes.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return wanted.Length == 0 ? [] : root.QuerySelectorAll("*").Where(e => wanted.All(w => e.ClassNames.Contains(w)));
    }
}
