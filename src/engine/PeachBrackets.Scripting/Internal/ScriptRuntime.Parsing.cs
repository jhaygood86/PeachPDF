using Jint;
using System.Collections.Generic;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Internal;

// The HTML parsing methods of the HTML standard that put markup into a tree without a sanitizer: setHTMLUnsafe, the positional
// {append, prepend, before, after, replaceWith}HTMLUnsafe and Document.parseHTMLUnsafe. The sanitizing variants need the Sanitizer API, which is not provided.
internal sealed partial class ScriptRuntime
{
    // Documents made by DOMParser or new Document(): a Document, not an XMLDocument, whatever their media type.
    private readonly HashSet<ScriptDocument> _plainDocuments = [];

    private void InstallParsingMethods()
    {
        void Positional(ObjectInstance prototype, string position)
        {
            Js.Method(Engine, prototype, position + "HTMLUnsafe", 1, (self, args) =>
            {
                var target = Sn(self);
                var html = Js.AsText(Js.Arg(args, 0));
                switch (position)
                {
                    case "append":
                        target.AppendChild(target.OwnerDocument.CreateFragment(html));
                        break;
                    case "prepend":
                        target.InsertBefore(target.OwnerDocument.CreateFragment(html), target.FirstChild);
                        break;
                    default:
                        var parent = target.Parent;
                        if (parent is null)
                            break;
                        if (parent.Kind == ScriptNodeKind.Document)
                            throw DomError("NoModificationAllowedError", "The parent of the node is a document.");
                        var fragment = target.OwnerDocument.CreateFragment(html);
                        if (position == "before")
                            parent.InsertBefore(fragment, target);
                        else if (position == "after")
                            parent.InsertBefore(fragment, target.NextSibling);
                        else
                            parent.ReplaceChild(fragment, target);
                        break;
                }

                return JsValue.Undefined;
            });
        }

        foreach (var position in new[] { "append", "prepend" })
        {
            Positional(ElementPrototype, position);
            Positional(FragmentPrototype, position);
        }

        foreach (var position in new[] { "before", "after", "replaceWith" })
        {
            Positional(ElementPrototype, position);
            Positional(CharacterDataPrototype, position);
        }

        foreach (var (name, position) in new[]
        {
            ("streamHTMLUnsafe", "replace"), ("streamAppendHTMLUnsafe", "append"), ("streamPrependHTMLUnsafe", "prepend"),
            ("streamBeforeHTMLUnsafe", "before"), ("streamAfterHTMLUnsafe", "after"), ("streamReplaceWithHTMLUnsafe", "replaceWith"),
        })
        {
            var where = position;
            Js.Method(Engine, ElementPrototype, name, 0, (self, _) =>
            {
                LoadWebBundle();
                if (_htmlStream is null)
                    throw TypeError("Streaming is not available.");
                return _htmlStream.Call(JsValue.Undefined, [self, Js.Str(where)]);
            });
        }

        Js.Method(Engine, ElementPrototype, "setHTMLUnsafe", 1, (self, args) =>
        {
            Sn(self).InnerHtml = Js.AsText(Js.Arg(args, 0));
            return JsValue.Undefined;
        });

        if (Engine.Global.Get("Document") is ObjectInstance constructor)
            Js.Method(Engine, constructor, "parseHTMLUnsafe", 1, (_, args) => Wrap(Document.ParseDocument(Js.AsText(Js.Arg(args, 0))).Root));
    }
}
