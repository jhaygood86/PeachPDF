using Jint;
using Jint.Native;
using Jint.Native.Object;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Scripting.Internal;

/// <summary>The declarations of a <c>style</c> attribute, in order.</summary>
internal sealed class InlineStyle
{
    private readonly List<(string Name, string Value, bool Important)> _declarations = [];

    public static InlineStyle Parse(string? text)
    {
        var style = new InlineStyle();
        if (string.IsNullOrWhiteSpace(text))
            return style;

        var depth = 0;
        char quote = '\0';
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ';';
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == ';' && depth == 0)
            {
                style.AddDeclaration(text[start..i]);
                start = i + 1;
            }
        }

        return style;
    }

    private void AddDeclaration(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0)
            return;

        var name = text[..colon].Trim();
        var value = text[(colon + 1)..].Trim();
        var important = false;
        if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
        {
            important = true;
            value = value[..^"!important".Length].TrimEnd();
        }

        if (name.Length > 0 && value.Length > 0)
            Set(name, value, important);
    }

    public int Count => _declarations.Count;

    public string NameAt(int index) => _declarations[index].Name;

    public string? Get(string name)
    {
        for (var i = _declarations.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_declarations[i].Name, name, StringComparison.OrdinalIgnoreCase) || _declarations[i].Name == name)
                return _declarations[i].Value;
        }

        return null;
    }

    public bool IsImportant(string name) =>
        _declarations.Exists(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && d.Important);

    public void Set(string name, string value, bool important)
    {
        Remove(name);
        _declarations.Add((name, value, important));
    }

    public string? Remove(string name)
    {
        var previous = Get(name);
        _declarations.RemoveAll(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return previous;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var (name, value, important) in _declarations)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(name).Append(": ").Append(value).Append(important ? " !important;" : ";");
        }

        return text.ToString();
    }
}

/// <summary>An element's <c>style</c> (or a computed style): property access by camel-case name and by CSS name.</summary>
internal sealed class StyleObject : ObjectInstance
{
    private readonly ScriptNode? _node;
    private readonly Func<string, string?>? _computed;

    public StyleObject(Engine engine, ObjectInstance prototype, ScriptNode node) : base(engine)
    {
        Prototype = prototype;
        _node = node;
    }

    public StyleObject(Engine engine, ObjectInstance prototype, Func<string, string?> computed) : base(engine)
    {
        Prototype = prototype;
        _computed = computed;
    }

    public bool ReadOnly => _computed is not null;

    private InlineStyle Declarations => InlineStyle.Parse(_node?.GetAttribute("style"));

    private void Save(InlineStyle style)
    {
        if (_node is null) return;
        var text = style.ToString();
        if (text.Length == 0) _node.RemoveAttribute("style");
        else _node.SetAttribute("style", text);
    }

    public int Length => _computed is not null ? 0 : Declarations.Count;

    public string NameAt(int index) => _computed is not null || index < 0 || index >= Declarations.Count ? string.Empty : Declarations.NameAt(index);

    public string CssText
    {
        get => _computed is not null ? string.Empty : Declarations.ToString();
        set
        {
            if (_computed is not null || _node is null) return;
            if (string.IsNullOrWhiteSpace(value)) _node.RemoveAttribute("style");
            else _node.SetAttribute("style", value);
        }
    }

    public string GetValue(string name) =>
        _computed is not null ? _computed(name.ToLowerInvariant()) ?? string.Empty : Declarations.Get(name) ?? string.Empty;

    public string GetPriority(string name) => _computed is null && Declarations.IsImportant(name) ? "important" : string.Empty;

    public void SetValue(string name, string value, string priority)
    {
        if (_computed is not null) return;
        var style = Declarations;
        if (value.Length == 0) style.Remove(name);
        else style.Set(name, value, priority.Equals("important", StringComparison.OrdinalIgnoreCase));
        Save(style);
    }

    public string RemoveValue(string name)
    {
        if (_computed is not null) return string.Empty;
        var style = Declarations;
        var previous = style.Remove(name);
        Save(style);
        return previous ?? string.Empty;
    }

    /// <summary>Turns <c>backgroundColor</c> into <c>background-color</c>, <c>cssFloat</c> into <c>float</c> and <c>webkitFoo</c> into <c>-webkit-foo</c>.</summary>
    public static string ToCssName(string key)
    {
        if (key == "cssFloat") return "float";
        var text = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsUpper(c)) text.Append('-').Append(char.ToLowerInvariant(c));
            else text.Append(c);
        }

        var name = text.ToString();
        return name.StartsWith("webkit-", StringComparison.Ordinal) || name.StartsWith("moz-", StringComparison.Ordinal) || name.StartsWith("ms-", StringComparison.Ordinal) ? "-" + name : name;
    }

    private static bool LooksLikeProperty(string key) => key.Length > 0 && char.IsLetter(key[0]) && !key.Contains('-') || key.StartsWith('-');

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        var inherited = base.Get(property, receiver);
        if (!inherited.IsUndefined() || !property.IsString())
            return inherited;

        var key = property.AsString();
        return LooksLikeProperty(key) ? Js.Str(GetValue(ToCssName(key))) : inherited;
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (property.IsString() && base.Get(property, receiver).IsUndefined() && LooksLikeProperty(property.AsString()))
        {
            SetValue(ToCssName(property.AsString()), Js.IsNullish(value) ? string.Empty : Js.AsText(value), string.Empty);
            return true;
        }

        return base.Set(property, value, receiver);
    }
}
