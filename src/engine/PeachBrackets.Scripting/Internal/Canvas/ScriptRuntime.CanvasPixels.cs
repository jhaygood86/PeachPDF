using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace PeachBrackets.Scripting.Internal;

/// <summary>An <c>ImageBitmap</c>: a decoded picture.</summary>
internal sealed class BitmapObject : ObjectInstance
{
    public BitmapObject(Engine engine, ObjectInstance prototype, Image image) : base(engine)
    {
        Prototype = prototype;
        Image = image;
    }

    public Image? Image { get; set; }

    /// <summary>Whether what it shows may be read: false when it came from another origin the page was not allowed to read.</summary>
    public bool OriginClean { get; set; } = true;
}

// Reading a canvas back (getImageData, toDataURL), putting pixels on it, and the objects that carry pixels: ImageData, ImageBitmap, OffscreenCanvas.
internal sealed partial class ScriptRuntime
{
    private ObjectInstance BitmapPrototype = null!;
    private ObjectInstance OffscreenPrototype = null!;

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // The byte count of a width by height RGBA picture; one too big to hold is a RangeError, as a browser's allocation failure is.
    private int PixelBytes(long width, long height)
    {
        var bytes = Math.Abs(width) * Math.Abs(height) * 4;
        if (bytes > (1L << 28))
            throw new JavaScriptException(Engine.Construct(Engine.Evaluate("RangeError"), [Js.Str("The image is too large.")]));

        return (int)bytes;
    }

    private void InstallCanvasPixels()
    {
        BitmapPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(BitmapPrototype, "ImageBitmap");
        Js.Accessor(Engine, BitmapPrototype, "width", self => Js.Num(((BitmapObject)self).Image?.Width ?? 0));
        Js.Accessor(Engine, BitmapPrototype, "height", self => Js.Num(((BitmapObject)self).Image?.Height ?? 0));
        Js.StrictMethod(Engine, BitmapPrototype, "close", 0, (self, _) =>
        {
            ((BitmapObject)self).Image = null;
            return JsValue.Undefined;
        });
        // structuredClone of an ImageBitmap: a copy of it showing the same picture; null for one that is closed.
        Define(Engine.Global, "__cloneImageBitmap", Js.Function(Engine, "__cloneImageBitmap", 1, (_, args) =>
            Js.Arg(args, 0) is BitmapObject { Image: { } picture } original ? new BitmapObject(Engine, BitmapPrototype, picture) { OriginClean = original.OriginClean } : JsValue.Null));
        Define(Engine.Global, "ImageBitmap", Js.Constructor(Engine, "ImageBitmap", BitmapPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

        var p = ContextPrototype;
        CanvasBitmap B(JsValue self) => Cx(self).Bitmap;

        Js.StrictMethod(Engine, p, "isPointInPath", 2, (self, args) =>
        {
            var index = 0;
            PathData path = B(self).Path;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path;
                index = 1;
            }

            var (x, y) = (Num(args, index), Num(args, index + 1));

            // The fill rule is an enumeration: anything but "nonzero" or "evenodd" (undefined is the default) is a TypeError.
            var rule = FillMode.Nonzero;
            if (args.Length > index + 2 && !args[index + 2].IsUndefined())
            {
                var name = TypeConverter.ToString(args[index + 2]);
                if (name == "evenodd")
                    rule = FillMode.EvenOdd;
                else if (name != "nonzero")
                    throw TypeError($"The provided value '{name}' is not a valid enum value of type CanvasFillRule.");
            }

            if (!double.IsFinite(x) || !double.IsFinite(y) || !B(self).State.Transform.TryInvert(out var inverse))
                return JsBoolean.False;

            // The point is on the canvas; the path is in the space the transform makes.
            var (ux, uy) = inverse.Apply(x, y);
            return Js.Bool(path.Contains(ux, uy, rule));
        });
        Js.StrictMethod(Engine, p, "isPointInStroke", 2, (self, args) =>
        {
            var index = 0;
            PathData path = B(self).Path;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path;
                index = 1;
            }

            var (x, y) = (Num(args, index), Num(args, index + 1));
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return JsBoolean.False;

            return Js.Bool(B(self).StrokeContains(path, x, y));
        });

        Js.StrictMethod(Engine, p, "createImageData", 1, (self, args) =>
        {
            var context = Cx(self);
            if (Js.Arg(args, 0) is ObjectInstance source)
            {
                if (!IsImageData(source))
                    throw TypeError("The first argument is not an ImageData.");

                return MakeImageData(TypeConverter.ToInt32(source.Get("width")), TypeConverter.ToInt32(source.Get("height")), null, Js.AsText(source.Get("colorSpace")));
            }

            if (args.Length < 2)
                throw TypeError("Failed to execute 'createImageData': 2 arguments required, but only 1 present.");

            var space = ImageDataSpace(Js.Arg(args, 2), context.Settings.ColorSpace);

            // [EnforceRange] long: not finite, or too large for a long, is a TypeError; a fraction is cut off before it is asked whether it is zero (0.99 is 0), and a negative size is its magnitude.
            foreach (var number in new[] { Num(args, 0), Num(args, 1) })
            {
                if (!double.IsFinite(number) || Math.Truncate(number) is < int.MinValue or > int.MaxValue)
                    throw TypeError("Failed to execute 'createImageData': The provided value is outside the range of a long.");
            }

            var (w, h) = (TypeConverter.ToInt32(Js.Arg(args, 0)), TypeConverter.ToInt32(Js.Arg(args, 1)));
            if (w == 0 || h == 0)
                throw DomError("IndexSizeError", "The size is zero.");

            return MakeImageData(Math.Abs(w), Math.Abs(h), null, space);
        });
        Js.StrictMethod(Engine, p, "getImageData", 4, (self, args) =>
        {
            RequireNoOpenLayers(B(self));
            if (!B(self).OriginClean)
                throw DomError("SecurityError", "The canvas has been tainted by cross-origin data.");

            // [EnforceRange] long: a number that is not finite, or that does not fit a long once its fraction is cut off, is a TypeError rather than wrapping round.
            for (var i = 0; i < 4; i++)
            {
                var number = Num(args, i);
                if (!double.IsFinite(number) || Math.Truncate(number) is < int.MinValue or > int.MaxValue)
                    throw TypeError("Failed to execute 'getImageData': The provided value is outside the range of a long.");
            }

            if (!Finite(args, 4))
                throw TypeError("The rectangle is not finite.");

            var (sx, sy, sw, sh) = ((int)Math.Truncate(Num(args, 0)), (int)Math.Truncate(Num(args, 1)), (int)Math.Truncate(Num(args, 2)), (int)Math.Truncate(Num(args, 3)));
            if (sw == 0 || sh == 0)
                throw DomError("IndexSizeError", "The size is zero.");

            if (sw < 0) { sx += sw; sw = -sw; }
            if (sh < 0) { sy += sh; sh = -sh; }

            var bitmap = B(self);
            var source = bitmap.Pixels();
            var (cw, ch) = ((int)bitmap.Width, (int)bitmap.Height);
            var pixels = new byte[PixelBytes(sw, sh)];
            for (var y = 0; y < sh; y++)
            {
                for (var x = 0; x < sw; x++)
                {
                    int ox = sx + x, oy = sy + y;
                    if (ox < 0 || oy < 0 || ox >= cw || oy >= ch || (oy * cw + ox) * 4 + 4 > source.Length)
                        continue;

                    Buffer.BlockCopy(source, (oy * cw + ox) * 4, pixels, (y * sw + x) * 4, 4);
                }
            }

            var space = ImageDataSpace(Js.Arg(args, 4), Cx(self).Settings.ColorSpace);
            if (ImageDataFormat(Js.Arg(args, 4)) == "rgba-float16")
            {
                // Floats: a floating-point canvas gives its own, outside 0 to 1 too; any other gives its bytes over 255.
                var floats = new float[PixelBytes(sw, sh)];
                var wideSource = bitmap.Wide ? bitmap.WidePixels() : null;
                for (var y = 0; y < sh; y++)
                {
                    for (var x = 0; x < sw; x++)
                    {
                        int ox = sx + x, oy = sy + y;
                        if (ox < 0 || oy < 0 || ox >= cw || oy >= ch)
                            continue;

                        var from = (oy * cw + ox) * 4;
                        var to = (y * sw + x) * 4;
                        for (var c = 0; c < 4; c++)
                            floats[to + c] = wideSource is not null ? wideSource[from + c] : source[from + c] / 255f;
                    }
                }

                CanvasColorSpaces.ConvertFloats(floats, bitmap.ColorSpace, space, clip: false);
                return BuildImageData(sw, sh, Float16Of(floats), space, "rgba-float16");
            }

            if (bitmap.Wide)
            {
                // A floating-point canvas is converted from its floats, and rounded to eight bits once, at the end.
                var wideSource = bitmap.WidePixels();
                var rgb = new double[3];
                for (var y = 0; y < sh; y++)
                {
                    for (var x = 0; x < sw; x++)
                    {
                        int ox = sx + x, oy = sy + y;
                        if (ox < 0 || oy < 0 || ox >= cw || oy >= ch)
                            continue;

                        var from = (oy * cw + ox) * 4;
                        var to = (y * sw + x) * 4;
                        for (var c = 0; c < 3; c++)
                            rgb[c] = wideSource[from + c];

                        CanvasColorSpaces.Convert(rgb, bitmap.ColorSpace, space, clip: true);
                        for (var c = 0; c < 3; c++)
                            pixels[to + c] = (byte)Math.Round(rgb[c] * 255);

                        pixels[to + 3] = (byte)Math.Round(Math.Clamp(wideSource[from + 3], 0f, 1f) * 255f);
                    }
                }

                return MakeImageData(sw, sh, pixels, space);
            }

            CanvasColorSpaces.ConvertBytes(pixels, bitmap.ColorSpace, space);
            return MakeImageData(sw, sh, pixels, space);
        });
        Js.StrictMethod(Engine, p, "putImageData", 3, (self, args) =>
        {
            RequireNoOpenLayers(B(self));
            if (Js.Arg(args, 0) is not ObjectInstance data || !IsImageData(data))
                throw TypeError("The first argument is not an ImageData.");

            var (dx, dy) = (Num(args, 1), Num(args, 2));
            if (!double.IsFinite(dx) || !double.IsFinite(dy))
                throw TypeError("The position is not finite.");

            // The position is a long: its fraction is dropped, so the pixels land on whole pixels.
            (dx, dy) = (Math.Truncate(dx), Math.Truncate(dy));
            var (w, h) = (TypeConverter.ToInt32(data.Get("width")), TypeConverter.ToInt32(data.Get("height")));
            var (rx, ry, rw, rh) = (0, 0, w, h);
            if (args.Length >= 7)
            {
                // Argument 0 is the ImageData: only the four dirty-rectangle numbers are checked here (the position was above).
                if (!Enumerable.Range(3, 4).All(i => double.IsFinite(Num(args, i))))
                    throw TypeError("The dirty rectangle is not finite.");

                (rx, ry, rw, rh) = ((int)Num(args, 3), (int)Num(args, 4), (int)Num(args, 5), (int)Num(args, 6));
                if (rw < 0) { rx += rw; rw = -rw; }
                if (rh < 0) { ry += rh; rh = -rh; }
                // The part of it left of or above the image is not in the image: it shortens the rectangle, it does not slide it.
                if (rx < 0) { rw += rx; rx = 0; }
                if (ry < 0) { rh += ry; ry = 0; }
                rw = Math.Min(rw, w - rx);
                rh = Math.Min(rh, h - ry);
            }

            if (rw <= 0 || rh <= 0)
                return JsValue.Undefined;

            var target = B(self);
            Image? image;
            if (target.Wide)
            {
                // A floating-point canvas takes the pixels as floats, in its own space, whole.
                var floats = ImageDataFloats(data, w, h, out var dataSpace);
                CanvasColorSpaces.ConvertFloats(floats, dataSpace, target.ColorSpace, clip: false);
                image = Cx(self).Canvas.OwnerDocument.CreateWideImage(w, h, floats);
            }
            else
            {
                // Any other keeps them as bytes of its own space.
                var bytes = ImageDataFloats(data, w, h, out var dataSpace) is var straight && dataSpace == target.ColorSpace && !IsFloatData(data)
                    ? SrgbBytesOf(data, w, h, dataSpace)
                    : StraightBytes(straight, dataSpace, target.ColorSpace);
                image = Cx(self).Canvas.OwnerDocument.DecodeImage(PngWriter.Encode(w, h, bytes));
            }

            if (image is not null)
                target.PutPixels(image, new Rect(rx, ry, rw, rh), new Rect(dx + rx, dy + ry, rw, rh));

            return JsValue.Undefined;
        });

        var element = ElementPrototypeFor("canvas");
        Js.StrictMethod(Engine, element, "toDataURL", 0, (self, _) => Js.Str(DataUrlOf(Sn(self))));
        Js.StrictMethod(Engine, element, "toBlob", 1, (self, args) =>
        {
            if (!Js.IsCallable(Js.Arg(args, 0)))
                throw TypeError("The callback is not a function.");

            var url = DataUrlOf(Sn(self));
            var callback = args[0];
            var blobMaker = Engine.Evaluate("(function (url) { var s = atob(url.slice(url.indexOf(',') + 1)); var b = new Uint8Array(s.length); for (var i = 0; i < s.length; i++) b[i] = s.charCodeAt(i); return new Blob([b], { type: 'image/png' }); })");
            AddTimer(Js.Function(Engine, "toBlob", 0, (_, _) =>
            {
                Call(callback, JsValue.Undefined, blobMaker.Call(JsValue.Undefined, [Js.Str(url)]));
                return JsValue.Undefined;
            }), 0, [], repeat: false, frame: false);
            return JsValue.Undefined;
        });

        Define(Engine.Global, "ImageData", Js.Constructor(Engine, "ImageData", ImageDataPrototype(), 2, (_, args, _) =>
        {
            if (args.Length < 2)
                throw TypeError($"Failed to construct 'ImageData': 2 arguments required, but only {args.Length} present.");

            // (data, width [, height [, settings]]) when the first argument is a Uint8ClampedArray or a Float16Array, else (width, height [, settings]): a Uint8Array has the same bytes and is
            // read as a number, which is no width.
            var kind = Js.Arg(args, 0) is ObjectInstance { } candidate ? Js.AsText(Engine.Evaluate("(function (a) { return Object.prototype.toString.call(a); })").Call(JsValue.Undefined, [candidate])) : "";
            if (Js.Arg(args, 0) is ObjectInstance array && kind is "[object Uint8ClampedArray]" or "[object Float16Array]")
            {
                var wanted = kind == "[object Float16Array]" ? "rgba-float16" : "rgba-unorm8";
                var length = (int)TypeConverter.ToNumber(array.Get("length"));
                var requested = ImageDataFormat(Js.Arg(args, 3));
                if (length == 0 || length % 4 != 0)
                    throw DomError("InvalidStateError", "The data length is zero or not a multiple of 4.");
                if (requested != wanted)
                    throw DomError("InvalidStateError", "The data is not of the pixel format asked for.");

                var width = TypeConverter.ToInt32(Js.Arg(args, 1));
                if (width <= 0)
                    throw DomError("IndexSizeError", "The width is zero.");
                var pixels = length / 4;
                if (pixels % width != 0)
                    throw DomError("IndexSizeError", "The data length does not match the width.");

                var height = args.Length > 2 && !args[2].IsUndefined() ? TypeConverter.ToInt32(args[2]) : pixels / width;
                if (height <= 0 || (long)width * height != pixels)
                    throw DomError("IndexSizeError", "The data length does not match the size.");

                return BuildImageData(width, height, array, ImageDataSpace(Js.Arg(args, 3), "srgb"), wanted);
            }

            // The third argument is the settings dictionary: an object, or nothing.
            if (args.Length > 2 && !args[2].IsUndefined() && !args[2].IsNull() && args[2] is not ObjectInstance)
                throw TypeError("Failed to construct 'ImageData': The settings are not a dictionary.");

            var (w, h) = (TypeConverter.ToInt32(Js.Arg(args, 0)), TypeConverter.ToInt32(Js.Arg(args, 1)));
            if (w <= 0 || h <= 0)
                throw DomError("IndexSizeError", "The size is zero.");

            if (ImageDataFormat(Js.Arg(args, 2)) == "rgba-float16")
                return BuildImageData(w, h, (ObjectInstance)Engine.Evaluate("(function (n) { return new Float16Array(n); })").Call(JsValue.Undefined, [Js.Num(PixelBytes(w, h))]), ImageDataSpace(Js.Arg(args, 2), "srgb"), "rgba-float16");

            return MakeImageData(w, h, null, ImageDataSpace(Js.Arg(args, 2), "srgb"));
        }));

        InstallBitmaps();
        InstallOffscreenCanvas();

        foreach (var named in Document.QuerySelectorAll("[id]"))
            NameElement(named);

        Js.StrictMethod(Engine, ContextPrototype, "getContextAttributes", 0, (self, _) =>
        {
            var settings = Cx(self).Settings;
            var attributes = Js.NewObject(Engine);
            Visible(attributes, "alpha", Js.Bool(settings.Alpha));
            Visible(attributes, "desynchronized", Js.Bool(settings.Desynchronized));
            Visible(attributes, "willReadFrequently", Js.Bool(settings.WillReadFrequently));
            Visible(attributes, "colorSpace", Js.Str(settings.ColorSpace));
            Visible(attributes, "colorType", Js.Str(settings.ColorType));
            var tone = Js.NewObject(Engine);
            Visible(tone, "mode", Js.Str(settings.ToneMapping));
            Visible(attributes, "toneMapping", tone);
            return attributes;
        });
    }

    // OffscreenCanvas: a canvas element nothing shows, with the surface of an offscreen canvas.
    private void InstallOffscreenCanvas()
    {
        OffscreenPrototype = new JsObject(Engine) { Prototype = EventTargetPrototype };
        Tag(OffscreenPrototype, "OffscreenCanvas");
        var element = ElementPrototypeFor("canvas");
        // OffscreenCanvas takes its sizes as [EnforceRange] unsigned longs: a number that is not one is an error, where a canvas element's attribute wraps it.
        foreach (var attribute in new[] { "width", "height" })
        {
            var name = attribute;
            Js.Accessor(Engine, OffscreenPrototype, name,
                self => Js.Num(name == "width" ? CanvasSize(Sn(self)).Width : CanvasSize(Sn(self)).Height),
                (self, value) =>
                {
                    var number = TypeConverter.ToNumber(value);
                    if (!double.IsFinite(number) || Math.Truncate(number) is < 0 or > uint.MaxValue)
                        throw TypeError($"The value is outside the range of an unsigned long.");

                    Sn(self).SetAttribute(name, Math.Truncate(number).ToString("0", CultureInfo.InvariantCulture));
                    ResetCanvas(Sn(self));
                });
        }

        Js.StrictMethod(Engine, OffscreenPrototype, "getContext", 1, (self, args) => GetContext(Sn(self), args, offscreen: true));

        // OffscreenCanvasRenderingContext2D has its own interface object, whose prototype carries the members CanvasRenderingContext2D's does; InstallOffscreenContext fills it once they all exist.
        OffscreenContextPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(OffscreenContextPrototype, "OffscreenCanvasRenderingContext2D");
        Define(Engine.Global, "OffscreenCanvasRenderingContext2D", Js.Constructor(Engine, "OffscreenCanvasRenderingContext2D", OffscreenContextPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        ((ObjectInstance)Engine.Global.Get("OffscreenCanvasRenderingContext2D")).DefineOwnProperty("prototype", new PropertyDescriptor(OffscreenContextPrototype, PropertyFlag.AllForbidden));

        Js.StrictMethod(Engine, OffscreenPrototype, "transferToImageBitmap", 0, (self, _) =>
        {
            // The canvas hands its picture over and starts again blank (its state is kept); with no 2D context there is nothing to hand over.
            if (!_contexts.TryGetValue(Sn(self), out var context) || context.IsBitmapRenderer)
                throw DomError("InvalidStateError", "The canvas has no context to transfer from.");

            var made = BitmapFrom(Wrap(Sn(self)), []);
            context.Bitmap.ClearDrawing();
            return made;
        });
        Js.StrictMethod(Engine, OffscreenPrototype, "convertToBlob", 0, (self, _) =>
        {
            // A promise-returning method does not throw: what would have been thrown is what the promise is rejected with.
            string url;
            try
            {
                // A canvas that was transferred away is detached, and one with no area has nothing to encode.
                if (_transferred.Contains(Sn(self)))
                    throw DomError("InvalidStateError", "The OffscreenCanvas is detached.");

                if (CanvasSize(Sn(self)) is var (width, height) && (width == 0 || height == 0))
                    throw DomError("IndexSizeError", "The OffscreenCanvas has no area to encode.");

                url = DataUrlOf(Sn(self));
            }
            catch (JavaScriptException error)
            {
                return Engine.Evaluate("(function (reason) { return Promise.reject(reason); })").Call(JsValue.Undefined, [error.Error]);
            }

            return Engine.Evaluate("(function (url) { return new Promise(function (resolve) { var s = atob(url.slice(url.indexOf(',') + 1)); var b = new Uint8Array(s.length); for (var i = 0; i < s.length; i++) b[i] = s.charCodeAt(i); resolve(new Blob([b], { type: 'image/png' })); }); })")
                .Call(JsValue.Undefined, [Js.Str(url)]);
        });
        Define(Engine.Global, "OffscreenCanvas", Js.Constructor(Engine, "OffscreenCanvas", OffscreenPrototype, 2, (_, args, _) =>
        {
            if (args.Length < 2)
                throw TypeError("OffscreenCanvas needs a width and a height.");

            var node = Document.CreateElement("canvas");
            // [EnforceRange] unsigned long: null is 0, a negative number, a NaN or an object is a TypeError.
            foreach (var (attribute, value) in new[] { ("width", Num(args, 0)), ("height", Num(args, 1)) })
            {
                if (!double.IsFinite(value) || Math.Truncate(value) is < 0 or > uint.MaxValue)
                    throw TypeError("The size is outside the range of an unsigned long.");

                node.SetAttribute(attribute, Math.Truncate(value).ToString("0", CultureInfo.InvariantCulture));
            }

            return OffscreenOf(node);
        }));

        // transferControlToOffscreen: the canvas becomes a placeholder, which no longer has a context or a size of its own, and an OffscreenCanvas of its size takes its place.
        Js.StrictMethod(Engine, element, "transferControlToOffscreen", 0, (self, _) =>
        {
            var placeholder = Sn(self);
            if (_contexts.ContainsKey(placeholder) || _transferred.Contains(placeholder))
                throw DomError("InvalidStateError", "The canvas has a context, or its control has been transferred.");

            var (width, height) = CanvasSize(placeholder);
            var node = Document.CreateElement("canvas");
            node.SetAttribute("width", width.ToString("0", CultureInfo.InvariantCulture));
            node.SetAttribute("height", height.ToString("0", CultureInfo.InvariantCulture));
            _transferred.Add(placeholder);
            var offscreen = OffscreenOf(node);
            _offscreenPlaceholders[node] = (this, placeholder);
            return offscreen;
        });
    }

    // The members of the 2D context, as an OffscreenCanvasRenderingContext2D has them: the same functions on a prototype of its own.
    private void InstallOffscreenContext()
    {
        foreach (var key in ContextPrototype.GetOwnPropertyKeys(Types.String))
        {
            if (key.ToString() is var name && name != "constructor")
                OffscreenContextPrototype.FastSetProperty(name, ContextPrototype.GetOwnProperty(key));
        }
    }

    private readonly System.Collections.Generic.HashSet<ScriptNode> _transferred = [];

    // An OffscreenCanvas made of a canvas element nothing shows.
    private ObjectInstance OffscreenOf(ScriptNode node)
    {
        var wrapper = Wrap(node);
        wrapper.Prototype = OffscreenPrototype;
        return wrapper;
    }

    private ObjectInstance? _imageDataPrototype;

    private ObjectInstance ImageDataPrototype()
    {
        if (_imageDataPrototype is not null)
            return _imageDataPrototype;

        var prototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(prototype, "ImageData");
        return _imageDataPrototype = prototype;
    }

    // An ImageData of a size, filled from straight RGBA bytes (or transparent black).
    private JsValue MakeImageData(int width, int height, byte[]? pixels, string colorSpace = "srgb")
    {
        var array = (ObjectInstance)Engine.Evaluate("(function (n) { return new Uint8ClampedArray(n); })").Call(JsValue.Undefined, [Js.Num(PixelBytes(width, height))]);
        if (pixels is not null)
        {
            Engine.Evaluate("(function (a, s) { for (var i = 0; i < s.length; i++) a[i] = s.charCodeAt(i); })").Call(JsValue.Undefined, [array, Js.Str(Latin1(pixels))]);
        }

        return BuildImageData(width, height, array, colorSpace);
    }

    private JsValue BuildImageData(int width, int height, ObjectInstance data, string colorSpace = "srgb", string pixelFormat = "rgba-unorm8")
    {
        var result = new JsObject(Engine) { Prototype = ImageDataPrototype() };
        void Set(string name, JsValue value) => result.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.Configurable | PropertyFlag.Enumerable));
        Set("width", Js.Num(width));
        Set("height", Js.Num(height));
        Set("data", data);
        Set("colorSpace", Js.Str(colorSpace));
        Set("pixelFormat", Js.Str(pixelFormat));
        return result;
    }

    // Whether the object is an ImageData: made by the constructor or the context, or inheriting from it.
    private bool IsImageData(ObjectInstance candidate)
    {
        var prototype = ImageDataPrototype();
        for (var p = candidate.Prototype; p is not null; p = p.Prototype)
        {
            if (ReferenceEquals(p, prototype))
                return true;
        }

        return false;
    }

    private static void Visible(ObjectInstance target, string name, JsValue value) =>
        target.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.ConfigurableEnumerableWritable));

    // The color space an ImageData settings dictionary names (the fallback when it names none).
    private string ImageDataSpace(JsValue settings, string fallback)
    {
        if (settings is not ObjectInstance dictionary || dictionary.Get("colorSpace") is not { } value || value.IsUndefined())
            return fallback;

        var text = Js.AsText(value);
        return Array.IndexOf(ColorSpaces, text) >= 0 ? text : throw TypeError($"The provided value '{text}' is not a valid enum value of type PredefinedColorSpace.");
    }

    private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Encode(double v) => v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    private static readonly double[] SrgbToP3 = [0.8224621, 0.1775380, 0.0000000, 0.0331941, 0.9668058, 0.0000000, 0.0170827, 0.0723974, 0.9105199];

    private static readonly double[] P3ToSrgb = [1.2249401, -0.2249404, 0.0000000, -0.0420569, 1.0420571, 0.0000000, -0.0196376, -0.0786361, 1.0982735];

    // One color between the predefined color spaces (the canvas keeps its pixels as sRGB); the result is clipped to the gamut when asked, else left where the math puts it.
    private static void ConvertRgb(double[] rgb, string from, string to, bool clip)
    {
        var fromP3 = from.StartsWith("display-p3", StringComparison.Ordinal);
        var toP3 = to.StartsWith("display-p3", StringComparison.Ordinal);
        var fromLinear = from.EndsWith("-linear", StringComparison.Ordinal);
        var toLinear = to.EndsWith("-linear", StringComparison.Ordinal);
        if (from != to)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = fromLinear ? rgb[c] : SignedDecode(rgb[c]);

            if (fromP3 != toP3)
            {
                var m = fromP3 ? P3ToSrgb : SrgbToP3;
                (rgb[0], rgb[1], rgb[2]) = ((m[0] * rgb[0]) + (m[1] * rgb[1]) + (m[2] * rgb[2]), (m[3] * rgb[0]) + (m[4] * rgb[1]) + (m[5] * rgb[2]), (m[6] * rgb[0]) + (m[7] * rgb[1]) + (m[8] * rgb[2]));
            }

            for (var c = 0; c < 3; c++)
                rgb[c] = toLinear ? rgb[c] : SignedEncode(rgb[c]);
        }

        if (clip)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = Math.Clamp(rgb[c], 0, 1);
        }
    }

    // The transfer function continued as an odd function, so a color outside the gamut keeps its sign.
    private static double SignedDecode(double v) => v < 0 ? -Decode(-v) : Decode(v);

    private static double SignedEncode(double v) => v < 0 ? -Encode(-v) : Encode(v);

    // Converts straight RGBA bytes between the predefined color spaces.
    private static void ConvertColorSpace(byte[] pixels, string from, string to)
    {
        if (from == to)
            return;

        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = pixels[i + c] / 255.0;

            ConvertRgb(rgb, from, to, clip: true);
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Round(rgb[c] * 255);
        }
    }

    // The straight sRGB bytes an ImageData stands for: its own bytes, or its floats clipped to the 8-bit gamut, converted from the color space it is in.
    // The bytes of an eight-bit ImageData as they are (it is in <paramref name="space"/>, which is where the caller wants them).
    private byte[] SrgbBytesOf(ObjectInstance data, int width, int height, string space)
    {
        var array = data.Get("data") as ObjectInstance ?? throw TypeError("The data is not an array.");
        var bytes = new byte[PixelBytes(width, height)];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)TypeConverter.ToInt32(array.Get(i.ToString(CultureInfo.InvariantCulture)));

        _ = space;
        return bytes;
    }

    private byte[] SrgbBytesOf(ObjectInstance data, int width, int height)
    {
        var space = data.Get("colorSpace") is { } dataSpace && !dataSpace.IsUndefined() ? Js.AsText(dataSpace) : "srgb";
        var floats = data.Get("pixelFormat") is { } format && !format.IsUndefined() && Js.AsText(format) == "rgba-float16";
        var array = data.Get("data") as ObjectInstance ?? throw TypeError("The data is not an array.");
        var bytes = new byte[PixelBytes(width, height)];
        if (!floats)
        {
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)TypeConverter.ToInt32(array.Get(i.ToString(CultureInfo.InvariantCulture)));

            ConvertColorSpace(bytes, space, "srgb");
            return bytes;
        }

        var rgb = new double[3];
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            double Read(int k) => TypeConverter.ToNumber(array.Get((i + k).ToString(CultureInfo.InvariantCulture)));
            for (var c = 0; c < 3; c++)
                rgb[c] = Read(c);

            var alpha = Read(3);
            ConvertRgb(rgb, space, "srgb", clip: true);
            for (var c = 0; c < 3; c++)
                bytes[i + c] = (byte)Math.Round(double.IsNaN(rgb[c]) ? 0 : rgb[c] * 255);
            bytes[i + 3] = (byte)Math.Round(double.IsNaN(alpha) ? 0 : Math.Clamp(alpha, 0, 1) * 255);
        }

        return bytes;
    }

    private static bool IsFloatData(ObjectInstance data) =>
        data.Get("pixelFormat") is { } format && !format.IsUndefined() && Js.AsText(format) == "rgba-float16";

    // The pixels of an ImageData as straight floats (bytes over 255, or its own floats), and the color space they are in.
    private float[] ImageDataFloats(ObjectInstance data, int width, int height, out string space)
    {
        space = data.Get("colorSpace") is { } dataSpace && !dataSpace.IsUndefined() ? Js.AsText(dataSpace) : "srgb";
        var array = data.Get("data") as ObjectInstance ?? throw TypeError("The data is not an array.");
        var floats = new float[PixelBytes(width, height)];
        var scale = IsFloatData(data) ? 1.0 : 1 / 255.0;
        for (var i = 0; i < floats.Length; i++)
            floats[i] = (float)(TypeConverter.ToNumber(array.Get(i.ToString(CultureInfo.InvariantCulture))) * scale);

        return floats;
    }

    // Straight floats in one space as eight-bit bytes in another (brought into the gamut).
    private static byte[] StraightBytes(float[] straight, string from, string to)
    {
        var copy = (float[])straight.Clone();
        CanvasColorSpaces.ConvertFloats(copy, from, to, clip: true);
        var bytes = new byte[copy.Length];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Math.Round(Math.Clamp(copy[i], 0f, 1f) * 255f);

        return bytes;
    }

    // A Float16Array of straight RGBA floats.
    private ObjectInstance Float16Of(float[] straight)
    {
        var values = new JsValue[straight.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = Js.Num(straight[i]);

        return (ObjectInstance)Engine.Evaluate("(function (values) { return new Float16Array(values); })").Call(JsValue.Undefined, [Js.Array(Engine, values)]);
    }

    // A Float16Array of straight RGBA floats in a color space, from the sRGB bytes the canvas keeps.
    private ObjectInstance FloatDataOf(byte[] pixels, string space)
    {
        var values = new JsValue[pixels.Length];
        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = pixels[i + c] / 255.0;

            ConvertRgb(rgb, "srgb", space, clip: false);
            for (var c = 0; c < 3; c++)
                values[i + c] = Js.Num(rgb[c]);
            values[i + 3] = Js.Num(pixels[i + 3] / 255.0);
        }

        return (ObjectInstance)Engine.Evaluate("(function (values) { return new Float16Array(values); })").Call(JsValue.Undefined, [Js.Array(Engine, values)]);
    }

    // The pixel format an ImageData settings dictionary names (unorm8 when it names none).
    private string ImageDataFormat(JsValue settings)
    {
        if (settings is not ObjectInstance dictionary || dictionary.Get("pixelFormat") is not { } value || value.IsUndefined())
            return "rgba-unorm8";

        var text = Js.AsText(value);
        return text is "rgba-unorm8" or "rgba-float16" ? text : throw TypeError($"The provided value '{text}' is not a valid enum value of type ImageDataPixelFormat.");
    }

    private string DataUrlOf(ScriptNode element)
    {
        if (_contexts.TryGetValue(element, out var tainted) && !tainted.Bitmap.OriginClean)
            throw DomError("SecurityError", "The canvas has been tainted by cross-origin data.");

        if (!_contexts.TryGetValue(element, out var context))
        {
            var (w, h) = CanvasSize(element);
            return w == 0 || h == 0 ? "data:," : "data:image/png;base64," + Convert.ToBase64String(PngWriter.Encode((int)w, (int)h, new byte[(int)w * (int)h * 4]));
        }

        var bitmap = context.Bitmap;
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return "data:,";

        RequireNoOpenLayers(bitmap);
        var pixels = bitmap.Pixels();
        if (pixels.Length > 0 && bitmap.ColorSpace != "srgb")
        {
            // A PNG with no color profile is read as sRGB.
            pixels = (byte[])pixels.Clone();
            CanvasColorSpaces.ConvertBytes(pixels, bitmap.ColorSpace, "srgb");
        }

        return pixels.Length == 0 ? "data:," : "data:image/png;base64," + Convert.ToBase64String(PngWriter.Encode((int)bitmap.Width, (int)bitmap.Height, pixels));
    }

    // ---- ImageBitmap and OffscreenCanvas

    private void InstallBitmaps()
    {
        var native = Js.NewObject(Engine);
        // bitmapFrom(source, crop, options): crop is an array of four numbers or null, options a dictionary or undefined.
        Js.Method(Engine, native, "bitmapFrom", 3, (_, args) =>
            BitmapFrom(Js.Arg(args, 0), Js.Arg(args, 1) is ObjectInstance crop ? Enumerable.Range(0, 4).Select(i => TypeConverter.ToNumber(crop.Get(i.ToString(CultureInfo.InvariantCulture)))).ToArray() : [], Js.Arg(args, 2) as ObjectInstance));
        Js.Method(Engine, native, "bitmapFromEncoded", 2, (_, args) =>
        {
            if (Document.DecodeImage(Encoding.Latin1.GetBytes(Js.AsText(Js.Arg(args, 0)))) is not { } image)
                return JsValue.Null;

            return ApplyBitmapOptions(image, [], Js.Arg(args, 1) as ObjectInstance);
        });

        Engine.Evaluate("""
            (function (native, global) {
                var define = function (name, value) { Object.defineProperty(global, name, { value: value, writable: true, configurable: true }); };
                define('createImageBitmap', function createImageBitmap(source) {
                    var count = arguments.length, crop = null, options;
                    if (count === 2) options = arguments[1];
                    else if (count >= 5) { crop = [arguments[1], arguments[2], arguments[3], arguments[4]]; options = arguments[5]; }
                    return new Promise(function (resolve, reject) {
                        if (count === 0 || count === 3 || count === 4) { reject(new TypeError('Not enough arguments')); return; }
                        if (options !== undefined && options !== null && (typeof options !== 'object' && typeof options !== 'function')) { reject(new TypeError('The options are not a dictionary.')); return; }
                        if (typeof Blob !== 'undefined' && source instanceof Blob) {
                            source.arrayBuffer().then(function (buffer) {
                                var bytes = new Uint8Array(buffer), s = '';
                                for (var i = 0; i < bytes.length; i += 8192) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
                                try {
                                    var bitmap = native.bitmapFromEncoded(s, options);
                                    if (bitmap) resolve(bitmap); else reject(new DOMException('The image could not be decoded.', 'InvalidStateError'));
                                } catch (e) { reject(e); }
                            }, reject);
                            return;
                        }
                        try {
                            var made = native.bitmapFrom(source, crop, options);
                            if (made) resolve(made); else reject(new DOMException('The source cannot be decoded.', 'InvalidStateError'));
                        } catch (e) { reject(e); }
                    });
                });
            })
            """).Call(JsValue.Undefined, [native, Engine.Global]);
    }

    private JavaScriptException RangeErrorOf(string message) => new JavaScriptException(Engine.Construct(Engine.Evaluate("RangeError"), [Js.Str(message)]));

    // The crop, flip and resize createImageBitmap's arguments ask for, made of the decoded image: a picture of the part, in the size asked for.
    private JsValue ApplyBitmapOptions(Image image, double[] crop, ObjectInstance? options)
    {
        string Choice(string name, string[] allowed, string fallback)
        {
            if (options is null || options.Get(name) is not { } value || value.IsUndefined())
                return fallback;

            var text = Js.AsText(value);
            return Array.IndexOf(allowed, text) >= 0 ? text : throw TypeError($"The provided value '{text}' is not a valid enum value of type {name}.");
        }

        var orientation = Choice("imageOrientation", ["from-image", "flipY", "none"], "from-image");
        Choice("premultiplyAlpha", ["none", "premultiply", "default"], "default");
        Choice("colorSpaceConversion", ["none", "default"], "default");
        var quality = Choice("resizeQuality", ["pixelated", "low", "medium", "high"], "low");

        // [EnforceRange] unsigned long: a number that is not finite or does not fit is a TypeError; a size that comes to nothing is an InvalidStateError.
        double? Size(string name)
        {
            if (options is null || options.Get(name) is not { } value || value.IsUndefined())
                return null;

            var number = TypeConverter.ToNumber(value);
            if (!double.IsFinite(number) || Math.Truncate(number) is < 0 or > uint.MaxValue)
                throw TypeError($"The value of {name} is outside the range of an unsigned long.");

            return Math.Truncate(number);
        }

        var (resizeWidth, resizeHeight) = (Size("resizeWidth"), Size("resizeHeight"));

        double cx = 0, cy = 0, cw = image.Width, ch = image.Height;
        if (crop.Length >= 4)
        {
            // The rectangle is four longs: a number wraps modulo 2^32 (a NaN is 0), and a width or height of 0 is a RangeError.
            crop = [.. crop.Take(4).Select(v => (double)(double.IsFinite(v) ? (int)(uint)(long)(Math.Truncate(v) % 4294967296.0) : 0))];
            if (crop[2] == 0 || crop[3] == 0)
                throw RangeErrorOf("The crop rectangle has no width or height.");

            (cx, cy, cw, ch) = (crop[0], crop[1], Math.Abs(crop[2]), Math.Abs(crop[3]));
            if (crop[2] < 0) cx += crop[2];
            if (crop[3] < 0) cy += crop[3];
        }

        var (outWidth, outHeight) = (cw, ch);
        if (resizeWidth is not null || resizeHeight is not null)
        {
            outWidth = resizeWidth ?? Math.Round(cw * resizeHeight!.Value / ch);
            outHeight = resizeHeight ?? Math.Round(ch * resizeWidth!.Value / cw);
        }

        if (outWidth < 1 || outHeight < 1 || outWidth * outHeight > (1 << 28) || cw * ch > (1L << 40))
            throw DomError("InvalidStateError", "The bitmap cannot be made at that size.");

        var plain = crop.Length < 4 && resizeWidth is null && resizeHeight is null && orientation != "flipY";
        if (plain)
            return new BitmapObject(Engine, BitmapPrototype, image);

        // The part of the crop that lies in the image is drawn where it falls in the result; what lies outside it stays transparent.
        var pictureSpace = CanvasColorSpaces.SpaceOf(image);
        var pieceIsWide = CanvasColorSpaces.FloatsOf(image) is not null;
        var piece = new CanvasBitmap(Document, outWidth, outHeight);
        piece.ConfigureColor(pictureSpace, pieceIsWide);
        piece.State.ImageSmoothing = quality != "pixelated";
        if (orientation == "flipY")
            piece.SetTransform(new Affine2D(1, 0, 0, -1, 0, outHeight));

        var (scaleX, scaleY) = (outWidth / cw, outHeight / ch);
        var (x0, y0, x1, y1) = (Math.Max(cx, 0), Math.Max(cy, 0), Math.Min(cx + cw, image.Width), Math.Min(cy + ch, image.Height));
        if (x1 > x0 && y1 > y0)
            piece.DrawImage(image, new Rect(x0, y0, x1 - x0, y1 - y0), new Rect((x0 - cx) * scaleX, (y0 - cy) * scaleY, (x1 - x0) * scaleX, (y1 - y0) * scaleY));

        Image? made;
        if (pieceIsWide)
        {
            var floats = (float[])piece.WidePixels().Clone();
            made = Document.CreateWideImage((int)outWidth, (int)outHeight, floats);
            if (made is not null)
                CanvasColorSpaces.Tag(made, pictureSpace, floats);
        }
        else
        {
            var pixels = piece.Pixels();
            made = pixels.Length == 0 ? null : Document.DecodeImage(PngWriter.Encode((int)outWidth, (int)outHeight, pixels));
            if (made is not null)
                CanvasColorSpaces.Tag(made, pictureSpace);
        }

        return made is null ? throw DomError("InvalidStateError", "The bitmap cannot be made.") : new BitmapObject(Engine, BitmapPrototype, made);
    }

    // A loaded SVG image drawn at its natural size, as the picture a bitmap is made of; null when the element is not one, or has no size.
    private Image? RasterizeSvgImage(ScriptNode element)
    {
        if (!_images.TryGetValue(element, out var state) || state.Status != 2 || ImageAddressOf(element) is not { } address || !Document.IsSvgImage(address))
            return null;

        var (width, height) = (Math.Round(state.Width), Math.Round(state.Height));
        if (!(width >= 1) || !(height >= 1) || width * height > (1 << 26))
            return null;

        var piece = new CanvasBitmap(Document, width, height);
        piece.DrawSvg(address, state.Width, state.Height, new Rect(0, 0, state.Width, state.Height), new Rect(0, 0, width, height));
        var pixels = piece.Pixels();
        return pixels.Length == 0 ? null : Document.DecodeImage(PngWriter.Encode((int)width, (int)height, pixels));
    }

    // A bitmap of an image element, a canvas, an ImageData or another bitmap (the last four numbers crop it).
    private JsValue BitmapFrom(JsValue source, double[] crop, ObjectInstance? options = null)
    {
        var made = BitmapFromSource(source, crop, options);
        if (made is BitmapObject bitmap && SourceTainted(source))
            bitmap.OriginClean = false;

        return made;
    }

    private JsValue BitmapFromSource(JsValue source, double[] crop, ObjectInstance? options)
    {
        Image? image = null;
        switch (source)
        {
            case BitmapObject bitmap when bitmap.Image is { } existing:
                image = existing;
                break;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } element } when IsImage(element) || IsSvgImage(element):
                image = ImageOf(source) ?? RasterizeSvgImage(element) ?? throw DomError("InvalidStateError", "The image is not loaded.");
                break;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } bare } when string.Equals(bare.TagName, "canvas", StringComparison.OrdinalIgnoreCase) && !_contexts.ContainsKey(bare):
            {
                // A canvas nothing was drawn on is a transparent picture of its size, if it has a size and that is one that can be held.
                var (bareWidth, bareHeight) = CanvasSize(bare);
                if (bareWidth <= 0 || bareHeight <= 0 || bareWidth * bareHeight > (1 << 26))
                    throw DomError("InvalidStateError", "The canvas is empty or too large.");

                image = bare.OwnerDocument.DecodeImage(PngWriter.Encode((int)bareWidth, (int)bareHeight, new byte[(int)bareWidth * (int)bareHeight * 4]));
                break;
            }

            case NodeObject { Node: { Kind: ScriptNodeKind.Element } canvas } when _contexts.TryGetValue(canvas, out var context):
                if (context.Bitmap.Width <= 0 || context.Bitmap.Height <= 0 || context.Bitmap.Width * context.Bitmap.Height > (1 << 26))
                    throw DomError("InvalidStateError", "The canvas is empty.");
                RequireNoOpenLayers(context.Bitmap);
                var read = context.Bitmap.Pixels();
                if (read.Length == 0)
                    throw DomError("InvalidStateError", "The canvas is too large.");
                image = SnapshotOf(canvas);
                break;
            case ObjectInstance data when data.HasProperty("data") && data.HasProperty("width"):
            {
                var (w, h) = (TypeConverter.ToInt32(data.Get("width")), TypeConverter.ToInt32(data.Get("height")));
                var floats = ImageDataFloats(data, w, h, out var dataSpace);
                if (IsFloatData(data) && Document.CreateWideImage(w, h, floats) is { } wideImage)
                {
                    CanvasColorSpaces.Tag(wideImage, dataSpace, floats);
                    image = wideImage;
                }
                else
                {
                    image = Document.DecodeImage(PngWriter.Encode(w, h, SrgbBytesOf(data, w, h, dataSpace)));
                    if (image is not null)
                        CanvasColorSpaces.Tag(image, dataSpace);
                }
                break;
            }

            default:
                throw TypeError("The source is not a supported image source.");
        }

        if (image is null)
            return JsValue.Null;

        return ApplyBitmapOptions(image, crop, options);
    }
}
