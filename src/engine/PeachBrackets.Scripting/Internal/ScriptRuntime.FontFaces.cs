using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>FontFace</c>: a font a script made, which loads when asked and is used once it is in <c>document.fonts</c>.</summary>
internal sealed class FontFaceObject : ObjectInstance
{
    public FontFaceObject(Engine engine, ObjectInstance prototype, string family, string? source, byte[]? data) : base(engine)
    {
        Prototype = prototype;
        Family = family;
        Source = source;
        Data = data;
    }

    public string Family { get; set; }

    public string? Source { get; }

    public byte[]? Data { get; }

    public string Status { get; set; } = "unloaded";

    public string? Weight { get; set; }

    public string? Style { get; set; }

    public string? Stretch { get; set; }

    public string? UnicodeRange { get; set; }

    public JsValue Loaded { get; set; } = JsValue.Undefined;

    public JsValue Resolve { get; set; } = JsValue.Undefined;

    public JsValue Reject { get; set; } = JsValue.Undefined;
}

// document.fonts and FontFace.
internal sealed partial class ScriptRuntime
{
    private ObjectInstance FontFacePrototype = null!;
    private ObjectInstance FontFaceSetPrototype = null!;
    private ObjectInstance? _fonts;
    private readonly List<FontFaceObject> _fontSet = [];
    private readonly List<(Task<bool> Task, FontFaceObject Face)> _pendingFonts = [];
    private JsValue _fontsReady = JsValue.Undefined;
    private JsValue _fontsReadyResolve = JsValue.Undefined;

    private JsValue FontsObject()
    {
        if (_fonts is not null)
            return _fonts;

        FontFaceSetPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(FontFaceSetPrototype, "FontFaceSet");
        FontFacePrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(FontFacePrototype, "FontFace");
        InstallFontFace();

        var fonts = new JsObject(Engine) { Prototype = FontFaceSetPrototype };
        _fonts = fonts;
        InstallFontFaceSet(FontFaceSetPrototype);
        return fonts;
    }

    // Whether anything the page is waiting for is not loaded yet: its style sheets' fonts, or a face a script asked to load.
    private bool FontsAreLoading => !Document.FontFacesLoaded || _pendingFonts.Count > 0;

    // A promise settled once no font is loading. A new one is made when fonts start loading after the last one was settled.
    private JsValue FontsReady()
    {
        if (_fontsReady.IsUndefined() || _fontsReadyResolve.IsUndefined() && FontsAreLoading)
        {
            var made = (ObjectInstance)Engine.Evaluate("(function () { var resolve; var promise = new Promise(function (r) { resolve = r; }); return { promise: promise, resolve: resolve }; })()");
            _fontsReady = made.Get("promise");
            _fontsReadyResolve = made.Get("resolve");
            if (!FontsAreLoading)
                SettleFontsReady();
        }

        return _fontsReady;
    }

    private void SettleFontsReady()
    {
        if (!_fontsReadyResolve.IsUndefined())
        {
            _fontsReadyResolve.Call(JsValue.Undefined, []);
            _fontsReadyResolve = JsValue.Undefined;
        }
    }

    /// <summary>Waits for the fonts the page's scripts asked to load, and settles <c>document.fonts.ready</c> when none is loading; what a script does then may ask for more, which is waited for too.</summary>
    private async ValueTask RunFontLoadsAsync()
    {
        _ = FontsObject();
        do
        {
            var batch = _pendingFonts.ToArray();
            _pendingFonts.Clear();
            foreach (var (task, face) in batch)
            {
                var loaded = false;
                try
                {
                    loaded = await task;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                }

                face.Status = loaded ? "loaded" : "error";
                if (loaded)
                    face.Resolve.Call(JsValue.Undefined, [face]);
                else
                    face.Reject.Call(JsValue.Undefined, [DomError("NetworkError", "The font could not be loaded.").Error]);
            }

            if (_pendingFonts.Count == 0 && Document.FontFacesLoaded)
                SettleFontsReady();

            RunMicrotasks();
        }
        while (_pendingFonts.Count > 0);
    }

    // Starts loading a face (once): its loaded promise settles when the font is registered, or cannot be.
    private JsValue LoadFontFace(FontFaceObject face)
    {
        if (face.Status == "unloaded")
        {
            face.Status = "loading";
            _pendingFonts.Add((Document.LoadFontFaceAsync(face.Family, face.Source, face.Data, face.Weight, face.Style, face.Stretch, face.UnicodeRange).AsTask(), face));
        }

        return face.Loaded;
    }

    private void InstallFontFace()
    {
        Define(Engine.Global, "FontFace", Js.Constructor(Engine, "FontFace", FontFacePrototype, 2, (_, args, _) =>
        {
            var family = Js.AsText(Js.Arg(args, 0)).Trim().Trim('"', '\'');
            var source = Js.Arg(args, 1);
            string? css = null;
            byte[]? data = null;
            if (source is JsString text)
            {
                css = text.ToString();
            }
            else if (source is ObjectInstance buffer)
            {
                var binary = Engine.Evaluate("(function (v) { var u; if (v instanceof ArrayBuffer) u = new Uint8Array(v); else if (ArrayBuffer.isView(v)) u = new Uint8Array(v.buffer, v.byteOffset, v.byteLength); else return null; var s = ''; for (var i = 0; i < u.length; i++) s += String.fromCharCode(u[i]); return s; })")
                    .Call(JsValue.Undefined, [buffer]);
                data = binary.IsNull() ? throw TypeError("The font source is not a string or a buffer.") : Encoding.Latin1.GetBytes(binary.ToString());
            }
            else
            {
                css = Js.AsText(source);
            }

            var face = new FontFaceObject(Engine, FontFacePrototype, family, css, data);
            var made = (ObjectInstance)Engine.Evaluate("(function () { var resolve, reject; var promise = new Promise(function (a, b) { resolve = a; reject = b; }); promise.catch(function () {}); return { promise: promise, resolve: resolve, reject: reject }; })()");
            face.Loaded = made.Get("promise");
            face.Resolve = made.Get("resolve");
            face.Reject = made.Get("reject");

            if (Js.Arg(args, 2) is ObjectInstance descriptors)
            {
                string? Member(string name) => descriptors.HasProperty(name) && !descriptors.Get(name).IsUndefined() ? TypeConverter.ToString(descriptors.Get(name)) : null;
                face.Weight = Member("weight");
                face.Style = Member("style");
                face.Stretch = Member("stretch");
                face.UnicodeRange = Member("unicodeRange");
            }

            // A source that is not a CSS src list is an error the face reports when it loads.
            if (data is null && !Document.IsFontSource(css ?? ""))
            {
                face.Status = "error";
                face.Reject.Call(JsValue.Undefined, [DomError("SyntaxError", "The font source is not valid.").Error]);
            }

            return face;
        }));

        Js.Accessor(Engine, FontFacePrototype, "family", self => Js.Str(Face(self).Family), (self, value) => Face(self).Family = Js.AsText(value));
        Js.Accessor(Engine, FontFacePrototype, "status", self => Js.Str(Face(self).Status));
        Js.Accessor(Engine, FontFacePrototype, "loaded", self => Face(self).Loaded);
        Js.Accessor(Engine, FontFacePrototype, "weight", self => Js.Str(Face(self).Weight ?? "normal"), (self, value) => Face(self).Weight = Js.AsText(value));
        Js.Accessor(Engine, FontFacePrototype, "style", self => Js.Str(Face(self).Style ?? "normal"), (self, value) => Face(self).Style = Js.AsText(value));
        Js.Accessor(Engine, FontFacePrototype, "stretch", self => Js.Str(Face(self).Stretch ?? "normal"), (self, value) => Face(self).Stretch = Js.AsText(value));
        Js.Accessor(Engine, FontFacePrototype, "unicodeRange", self => Js.Str(Face(self).UnicodeRange ?? "U+0-10FFFF"), (self, value) => Face(self).UnicodeRange = Js.AsText(value));
        Js.Method(Engine, FontFacePrototype, "load", 0, (self, _) => LoadFontFace(Face(self)));
    }

    private FontFaceObject Face(JsValue self) => self as FontFaceObject ?? throw TypeError("Illegal invocation");

    private void InstallFontFaceSet(ObjectInstance p)
    {
        Define(Engine.Global, "FontFaceSet", Js.Constructor(Engine, "FontFaceSet", p, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Js.Accessor(Engine, p, "ready", _ => FontsReady());
        Js.Accessor(Engine, p, "status", _ => Js.Str(FontsAreLoading ? "loading" : "loaded"));
        Js.Accessor(Engine, p, "size", _ => Js.Num(_fontSet.Count));
        Js.Method(Engine, p, "add", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is not FontFaceObject face)
                throw TypeError("The argument is not a FontFace.");

            if (!_fontSet.Contains(face))
                _fontSet.Add(face);

            return self;
        });
        Js.Method(Engine, p, "delete", 1, (_, args) => Js.Bool(Js.Arg(args, 0) is FontFaceObject face && _fontSet.Remove(face)));
        Js.Method(Engine, p, "has", 1, (_, args) => Js.Bool(Js.Arg(args, 0) is FontFaceObject face && _fontSet.Contains(face)));
        Js.Method(Engine, p, "clear", 0, (_, _) =>
        {
            _fontSet.Clear();
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "forEach", 1, (self, args) =>
        {
            foreach (var face in _fontSet.ToArray())
                Call(Js.Arg(args, 0), Js.Arg(args, 1), face, face, self);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "values", 0, (_, _) => Engine.Evaluate("(function (a) { return a[Symbol.iterator](); })").Call(JsValue.Undefined, [Js.Array(Engine, [.. _fontSet])]));
        Js.Method(Engine, p, "keys", 0, (_, _) => Engine.Evaluate("(function (a) { return a[Symbol.iterator](); })").Call(JsValue.Undefined, [Js.Array(Engine, [.. _fontSet])]));
        Engine.Evaluate("(function (p) { p[Symbol.iterator] = p.values; })").Call(JsValue.Undefined, [p]);

        // load(font) loads the faces of the family the font shorthand names; check(font) says whether they are all loaded.
        Js.Method(Engine, p, "load", 1, (_, args) =>
        {
            var families = FamiliesOf(Js.AsText(Js.Arg(args, 0)));
            var matching = _fontSet.Where(f => families.Contains(f.Family, StringComparer.OrdinalIgnoreCase)).ToList();
            var loads = matching.Select(LoadFontFace).ToArray();
            // Settled once the fonts of the page's style sheets are registered too, so a page font is there when this is.
            return Engine.Evaluate("(function (ready, loads) { return ready.then(function () { return Promise.all(loads); }); })").Call(JsValue.Undefined, [FontsReady(), Js.Array(Engine, loads)]);
        });
        Js.Method(Engine, p, "check", 1, (_, args) =>
        {
            var families = FamiliesOf(Js.AsText(Js.Arg(args, 0)));
            return Js.Bool(_fontSet.Where(f => families.Contains(f.Family, StringComparer.OrdinalIgnoreCase)).All(f => f.Status == "loaded"));
        });
    }

    // The family names of a font shorthand (its size is dropped).
    private static List<string> FamiliesOf(string font)
    {
        var parsed = Canvas.CanvasFont.Parse(font, Canvas.FontEnvironment.Detached);
        return parsed is null ? [] : [.. parsed.Families.Select(f => f.Name)];
    }
}
