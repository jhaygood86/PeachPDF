using Jint;
using Jint.Native;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>The XML name productions the DOM standard validates element and attribute names against.</summary>
internal static class XmlNames
{
    private static bool IsStart(int c) =>
        c == ':' || c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 0xC0 && c <= 0xD6) || (c >= 0xD8 && c <= 0xF6)
        || (c >= 0xF8 && c <= 0x2FF) || (c >= 0x370 && c <= 0x37D) || (c >= 0x37F && c <= 0x1FFF) || (c >= 0x200C && c <= 0x200D)
        || (c >= 0x2070 && c <= 0x218F) || (c >= 0x2C00 && c <= 0x2FEF) || (c >= 0x3001 && c <= 0xD7FF) || (c >= 0xF900 && c <= 0xFDCF)
        || (c >= 0xFDF0 && c <= 0xFFFD) || (c >= 0x10000 && c <= 0xEFFFF);

    private static bool IsPart(int c) =>
        IsStart(c) || c == '-' || c == '.' || (c >= '0' && c <= '9') || c == 0xB7 || (c >= 0x300 && c <= 0x36F) || (c >= 0x203F && c <= 0x2040);

    /// <summary>Whether the text matches the <c>Name</c> production.</summary>
    public static bool IsName(string text)
    {
        if (text.Length == 0)
            return false;

        var first = true;
        for (var i = 0; i < text.Length; i++)
        {
            int c = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                c = char.ConvertToUtf32(text[i], text[++i]);
            else if (char.IsSurrogate(text[i]))
                return false;

            if (!(first ? IsStart(c) : IsPart(c)))
                return false;
            first = false;
        }

        return true;
    }

    /// <summary>Whether the text is a name with no colon (an <c>NCName</c>).</summary>
    public static bool IsNcName(string text) => text.IndexOf(':') < 0 && IsName(text);

    /// <summary>Whether the text matches the <c>QName</c> production: a name, or two names with one colon between.</summary>
    public static bool IsQName(string text)
    {
        var colon = text.IndexOf(':');
        return colon < 0 ? IsNcName(text) : IsNcName(text[..colon]) && IsNcName(text[(colon + 1)..]);
    }
}

internal sealed partial class ScriptRuntime
{
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    // The name an element reports: the one it was made with, upper-cased only in the HTML namespace.
    private static string QualifiedName(NodeObject node)
    {
        var html = node.Node.OwnerDocument.IsHtml;
        if (node.NamespaceInfo is not { } info)
            return html ? node.Node.TagName!.ToUpperInvariant() : node.Node.TagName!;

        var name = info.Prefix is null ? info.LocalName : info.Prefix + ":" + info.LocalName;
        return info.Namespace == HtmlNamespace && html ? name.ToUpperInvariant() : name;
    }

    // A copy of a node keeps the namespace its original was made with.
    private void CopyNamespaces(ScriptNode original, ScriptNode copy, bool deep)
    {
        if (Wrap(original).NamespaceInfo is { } info)
            Wrap(copy).NamespaceInfo = info;

        if (!deep)
            return;

        var from = original.Children;
        var to = copy.Children;
        for (var i = 0; i < from.Count && i < to.Count; i++)
            CopyNamespaces(from[i], to[i], true);
    }

    // createElementNS: validate and extract the namespace, prefix and local name, then make the element.
    private JsValue CreateElementNs(ScriptDocument document, JsValue namespaceArg, string qualifiedName)
    {
        var (ns, prefix, local) = ValidateAndExtract(namespaceArg, qualifiedName);

        var element = Wrap(document.CreateElement(local));
        element.NamespaceInfo = new ElementNamespace(ns, prefix, local);
        // The interface of an HTML element belongs to the HTML namespace only.
        if (ns != HtmlNamespace)
            element.Prototype = ElementPrototype;
        return element;
    }

    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\f', '\r'];

    // The tokens of an attribute value: split on ASCII white space, each kept once, in order.
    private static List<string> ParseTokens(string? value)
    {
        var tokens = new List<string>();
        if (value is null)
            return tokens;

        foreach (var token in value.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!tokens.Contains(token))
                tokens.Add(token);
        }

        return tokens;
    }

    private string ValidToken(JsValue value)
    {
        var token = Js.AsText(value);
        if (token.Length == 0)
            throw DomError("SyntaxError", "The token must not be empty.");
        if (token.IndexOfAny(AsciiWhitespace) >= 0)
            throw DomError("InvalidCharacterError", "The token must not have white space.");
        return token;
    }

    private static List<string> Tokens(JsValue self) => ParseTokens(((TokenListObject)self).Node.GetAttribute(((TokenListObject)self).Attribute));

    // The update steps: the attribute is written back as the tokens, unless there is neither an attribute nor a token.
    private static void SaveTokens(JsValue self, List<string> tokens)
    {
        var list = (TokenListObject)self;
        var node = list.Node;
        if (tokens.Count == 0 && !node.HasAttribute(list.Attribute))
            return;

        node.SetAttribute(list.Attribute, string.Join(' ', tokens));
    }

    private void InstallTokenList()
    {
        var p = TokenListPrototype;
        Js.Accessor(Engine, p, "length", self => Js.Num(Tokens(self).Count));
        Js.Accessor(Engine, p, "value", self => Js.Str(((TokenListObject)self).Node.GetAttribute(((TokenListObject)self).Attribute) ?? string.Empty),
            (self, value) => ((TokenListObject)self).Node.SetAttribute(((TokenListObject)self).Attribute, Js.AsText(value)));
        Js.Method(Engine, p, "item", 1, (self, args) =>
        {
            var tokens = Tokens(self);
            var index = TypeConverter.ToNumber(Js.Arg(args, 0));
            return index >= 0 && index < tokens.Count ? Js.Str(tokens[(int)index]) : JsValue.Null;
        });
        Js.Method(Engine, p, "contains", 1, (self, args) => Js.Bool(Tokens(self).Contains(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "add", 0, (self, args) =>
        {
            var valid = args.Select(ValidToken).ToList();
            var tokens = Tokens(self);
            foreach (var token in valid)
            {
                if (!tokens.Contains(token)) tokens.Add(token);
            }

            SaveTokens(self, tokens);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "remove", 0, (self, args) =>
        {
            var valid = args.Select(ValidToken).ToList();
            var tokens = Tokens(self);
            tokens.RemoveAll(valid.Contains);
            SaveTokens(self, tokens);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "toggle", 1, (self, args) =>
        {
            var token = ValidToken(Js.Arg(args, 0));
            var force = Js.Arg(args, 1);
            var tokens = Tokens(self);
            if (tokens.Contains(token))
            {
                if (!force.IsUndefined() && TypeConverter.ToBoolean(force))
                    return JsBoolean.True;

                tokens.Remove(token);
                SaveTokens(self, tokens);
                return JsBoolean.False;
            }

            if (!force.IsUndefined() && !TypeConverter.ToBoolean(force))
                return JsBoolean.False;

            tokens.Add(token);
            SaveTokens(self, tokens);
            return JsBoolean.True;
        });
        Js.Method(Engine, p, "replace", 2, (self, args) =>
        {
            var token = Js.AsText(Js.Arg(args, 0));
            var replacement = Js.AsText(Js.Arg(args, 1));
            if (token.Length == 0 || replacement.Length == 0)
                throw DomError("SyntaxError", "The token must not be empty.");
            if (token.IndexOfAny(AsciiWhitespace) >= 0 || replacement.IndexOfAny(AsciiWhitespace) >= 0)
                throw DomError("InvalidCharacterError", "The token must not have white space.");

            var tokens = Tokens(self);
            if (!tokens.Contains(token))
                return JsBoolean.False;

            // The first item that is either token takes the new one; any other copy goes.
            var first = tokens.FindIndex(t => t == token || t == replacement);
            tokens[first] = replacement;
            var kept = new List<string>();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (i == first || (tokens[i] != token && tokens[i] != replacement))
                    kept.Add(tokens[i]);
            }

            SaveTokens(self, kept);
            return JsBoolean.True;
        });
        Js.Method(Engine, p, "supports", 1, (_, _) => throw TypeError("The class attribute has no supported tokens."));
        Js.Method(Engine, p, "forEach", 1, (self, args) =>
        {
            var i = 0;
            foreach (var token in Tokens(self)) Call(Js.Arg(args, 0), Js.Arg(args, 1), Js.Str(token), Js.Num(i++), self);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "toString", 0, (self, _) => Js.Str(((TokenListObject)self).Node.GetAttribute(((TokenListObject)self).Attribute) ?? string.Empty));
    }
}
