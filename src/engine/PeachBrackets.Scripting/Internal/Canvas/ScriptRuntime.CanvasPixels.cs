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
        Js.Method(Engine, BitmapPrototype, "close", 0, (self, _) =>
        {
            ((BitmapObject)self).Image = null;
            return JsValue.Undefined;
        });
        Define(Engine.Global, "ImageBitmap", Js.Constructor(Engine, "ImageBitmap", BitmapPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

        var p = ContextPrototype;
        CanvasBitmap B(JsValue self) => Cx(self).Bitmap;

        Js.Method(Engine, p, "isPointInPath", 2, (self, args) =>
        {
            var index = 0;
            PathData path = B(self).Path;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path.Transformed(B(self).State.Transform);
                index = 1;
            }

            var (x, y) = (Num(args, index), Num(args, index + 1));
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return JsBoolean.False;

            var rule = Js.Arg(args, index + 2) is { } text && text.IsString() && text.AsString() == "evenodd" ? FillMode.EvenOdd : FillMode.Nonzero;
            return Js.Bool(path.Contains(x, y, rule));
        });
        Js.Method(Engine, p, "isPointInStroke", 2, (self, args) =>
        {
            var index = 0;
            PathData path = B(self).Path;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path.Transformed(B(self).State.Transform);
                index = 1;
            }

            var (x, y) = (Num(args, index), Num(args, index + 1));
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return JsBoolean.False;

            var scale = Math.Sqrt(Math.Abs(B(self).State.Transform.GetDeterminant()));
            return Js.Bool(path.Touches(x, y, B(self).State.LineWidth * scale));
        });

        Js.Method(Engine, p, "createImageData", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is ObjectInstance source && source.HasProperty("data"))
                return MakeImageData(TypeConverter.ToInt32(source.Get("width")), TypeConverter.ToInt32(source.Get("height")), null, Js.AsText(source.Get("colorSpace")));

            var settingsIndex = Js.Arg(args, 0) is ObjectInstance ? 1 : 2;
            var space = ImageDataSpace(Js.Arg(args, settingsIndex), Cx(self).Settings.ColorSpace);
            var (w, h) = (Num(args, 0), Num(args, 1));
            if (!double.IsFinite(w) || !double.IsFinite(h))
                throw TypeError("The size is not finite.");
            if (w == 0 || h == 0)
                throw DomError("IndexSizeError", "The size is zero.");

            return MakeImageData((int)Math.Abs(Math.Truncate(w)), (int)Math.Abs(Math.Truncate(h)), null, space);
        });
        Js.Method(Engine, p, "getImageData", 4, (self, args) =>
        {
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
            ConvertColorSpace(pixels, "srgb", space);
            return MakeImageData(sw, sh, pixels, space);
        });
        Js.Method(Engine, p, "putImageData", 3, (self, args) =>
        {
            if (Js.Arg(args, 0) is not ObjectInstance data || !data.HasProperty("data"))
                throw TypeError("The first argument is not an ImageData.");

            var (dx, dy) = (Num(args, 1), Num(args, 2));
            if (!double.IsFinite(dx) || !double.IsFinite(dy))
                throw TypeError("The position is not finite.");

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
                rx = Math.Max(rx, 0);
                ry = Math.Max(ry, 0);
                rw = Math.Min(rw, w - rx);
                rh = Math.Min(rh, h - ry);
            }

            if (rw <= 0 || rh <= 0)
                return JsValue.Undefined;

            var array = data.Get("data") as ObjectInstance ?? throw TypeError("The data is not an array.");
            var bytes = new byte[PixelBytes(w, h)];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)TypeConverter.ToInt32(array.Get(i.ToString(CultureInfo.InvariantCulture)));

            ConvertColorSpace(bytes, data.Get("colorSpace") is { } dataSpace && !dataSpace.IsUndefined() ? Js.AsText(dataSpace) : "srgb", "srgb");
            if (Cx(self).Canvas.OwnerDocument.DecodeImage(PngWriter.Encode(w, h, bytes)) is { } image)
                B(self).PutPixels(image, new Rect(rx, ry, rw, rh), new Rect(dx + rx, dy + ry, rw, rh));

            return JsValue.Undefined;
        });

        var element = ElementPrototypeFor("canvas");
        Js.Method(Engine, element, "toDataURL", 0, (self, _) => Js.Str(DataUrlOf(Sn(self))));
        Js.Method(Engine, element, "toBlob", 1, (self, args) =>
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
            if (Js.Arg(args, 0) is ObjectInstance { } array && !array.IsArray() && array.HasProperty("length"))
            {
                var length = (int)TypeConverter.ToNumber(array.Get("length"));
                if (length % 4 != 0)
                    throw DomError("InvalidStateError", "The data length is not a multiple of 4.");

                var width = TypeConverter.ToInt32(Js.Arg(args, 1));
                if (width <= 0)
                    throw DomError("IndexSizeError", "The width is zero.");
                var pixels = length / 4;
                if (pixels % width != 0)
                    throw DomError("IndexSizeError", "The data length does not match the width.");

                var height = args.Length > 2 && !args[2].IsUndefined() ? TypeConverter.ToInt32(args[2]) : pixels / width;
                if (height <= 0 || (long)width * height != pixels)
                    throw DomError("IndexSizeError", "The data length does not match the size.");

                return BuildImageData(width, height, array, ImageDataSpace(Js.Arg(args, 3), "srgb"));
            }

            var (w, h) = (TypeConverter.ToInt32(Js.Arg(args, 0)), TypeConverter.ToInt32(Js.Arg(args, 1)));
            if (w <= 0 || h <= 0)
                throw DomError("IndexSizeError", "The size is zero.");

            return MakeImageData(w, h, null, ImageDataSpace(Js.Arg(args, 2), "srgb"));
        }));

        InstallBitmaps();
        InstallOffscreenCanvas();

        foreach (var named in Document.QuerySelectorAll("[id]"))
            NameElement(named);

        Js.Method(Engine, ContextPrototype, "getContextAttributes", 0, (self, _) =>
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
        foreach (var name in new[] { "width", "height", "getContext" })
            OffscreenPrototype.FastSetProperty(name, element.GetOwnProperty(name));

        Js.Method(Engine, OffscreenPrototype, "transferToImageBitmap", 0, (self, _) =>
            BitmapFrom(Wrap(Sn(self)), []));
        Js.Method(Engine, OffscreenPrototype, "convertToBlob", 0, (self, _) =>
        {
            var url = DataUrlOf(Sn(self));
            return Engine.Evaluate("(function (url) { return new Promise(function (resolve) { var s = atob(url.slice(url.indexOf(',') + 1)); var b = new Uint8Array(s.length); for (var i = 0; i < s.length; i++) b[i] = s.charCodeAt(i); resolve(new Blob([b], { type: 'image/png' })); }); })")
                .Call(JsValue.Undefined, [Js.Str(url)]);
        });
        Define(Engine.Global, "OffscreenCanvas", Js.Constructor(Engine, "OffscreenCanvas", OffscreenPrototype, 2, (_, args, _) =>
        {
            if (args.Length < 2)
                throw TypeError("OffscreenCanvas needs a width and a height.");

            var node = Document.CreateElement("canvas");
            foreach (var (attribute, value) in new[] { ("width", Num(args, 0)), ("height", Num(args, 1)) })
                node.SetAttribute(attribute, (double.IsFinite(value) && value >= 0 ? Math.Truncate(value) : 0).ToString("0", CultureInfo.InvariantCulture));

            var wrapper = Wrap(node);
            wrapper.Prototype = OffscreenPrototype;
            return wrapper;
        }));
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

    private JsValue BuildImageData(int width, int height, ObjectInstance data, string colorSpace = "srgb")
    {
        var result = new JsObject(Engine) { Prototype = ImageDataPrototype() };
        void Set(string name, JsValue value) => result.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.Configurable | PropertyFlag.Enumerable));
        Set("width", Js.Num(width));
        Set("height", Js.Num(height));
        Set("data", data);
        Set("colorSpace", Js.Str(colorSpace));
        Set("pixelFormat", Js.Str("rgba-unorm8"));
        return result;
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

    // Converts straight RGBA bytes between the predefined color spaces (the canvas keeps its pixels as sRGB).
    private static void ConvertColorSpace(byte[] pixels, string from, string to)
    {
        if (from == to)
            return;

        var fromP3 = from.StartsWith("display-p3", StringComparison.Ordinal);
        var toP3 = to.StartsWith("display-p3", StringComparison.Ordinal);
        var fromLinear = from.EndsWith("-linear", StringComparison.Ordinal);
        var toLinear = to.EndsWith("-linear", StringComparison.Ordinal);
        var rgb = new double[3];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                rgb[c] = fromLinear ? pixels[i + c] / 255.0 : Decode(pixels[i + c] / 255.0);

            if (fromP3 != toP3)
            {
                var m = fromP3 ? P3ToSrgb : SrgbToP3;
                (rgb[0], rgb[1], rgb[2]) = ((m[0] * rgb[0]) + (m[1] * rgb[1]) + (m[2] * rgb[2]), (m[3] * rgb[0]) + (m[4] * rgb[1]) + (m[5] * rgb[2]), (m[6] * rgb[0]) + (m[7] * rgb[1]) + (m[8] * rgb[2]));
            }

            for (var c = 0; c < 3; c++)
            {
                var value = Math.Clamp(rgb[c], 0, 1);
                pixels[i + c] = (byte)Math.Round((toLinear ? value : Encode(value)) * 255);
            }
        }
    }

    private string DataUrlOf(ScriptNode element)
    {
        if (!_contexts.TryGetValue(element, out var context))
        {
            var (w, h) = CanvasSize(element);
            return w == 0 || h == 0 ? "data:," : "data:image/png;base64," + Convert.ToBase64String(PngWriter.Encode((int)w, (int)h, new byte[(int)w * (int)h * 4]));
        }

        var bitmap = context.Bitmap;
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return "data:,";

        var pixels = bitmap.Pixels();
        return pixels.Length == 0 ? "data:," : "data:image/png;base64," + Convert.ToBase64String(PngWriter.Encode((int)bitmap.Width, (int)bitmap.Height, pixels));
    }

    // ---- ImageBitmap and OffscreenCanvas

    private void InstallBitmaps()
    {
        var native = Js.NewObject(Engine);
        Js.Method(Engine, native, "bitmapFrom", 5, (_, args) => BitmapFrom(Js.Arg(args, 0), args.Skip(1).Select(TypeConverter.ToNumber).ToArray()));
        Js.Method(Engine, native, "bitmapFromEncoded", 1, (_, args) =>
            Document.DecodeImage(Encoding.Latin1.GetBytes(Js.AsText(Js.Arg(args, 0)))) is { } image ? new BitmapObject(Engine, BitmapPrototype, image) : JsValue.Null);

        Engine.Evaluate("""
            (function (native, global) {
                var define = function (name, value) { Object.defineProperty(global, name, { value: value, writable: true, configurable: true }); };
                define('createImageBitmap', function createImageBitmap(source) {
                    var crop = Array.prototype.slice.call(arguments, 1);
                    return new Promise(function (resolve, reject) {
                        if (arguments.length === 0) { reject(new TypeError('Not enough arguments')); return; }
                        if (typeof Blob !== 'undefined' && source instanceof Blob) {
                            source.arrayBuffer().then(function (buffer) {
                                var bytes = new Uint8Array(buffer), s = '';
                                for (var i = 0; i < bytes.length; i += 8192) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
                                var bitmap = native.bitmapFromEncoded(s);
                                if (bitmap) resolve(bitmap); else reject(new DOMException('The image could not be decoded.', 'InvalidStateError'));
                            }, reject);
                            return;
                        }
                        try {
                            var made = native.bitmapFrom.apply(native, [source].concat(crop));
                            if (made) resolve(made); else reject(new DOMException('The source cannot be decoded.', 'InvalidStateError'));
                        } catch (e) { reject(e); }
                    });
                });
            })
            """).Call(JsValue.Undefined, [native, Engine.Global]);
    }

    // A bitmap of an image element, a canvas, an ImageData or another bitmap (the last four numbers crop it).
    private JsValue BitmapFrom(JsValue source, double[] crop)
    {
        Image? image = null;
        switch (source)
        {
            case BitmapObject bitmap when bitmap.Image is { } existing:
                image = existing;
                break;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } element } when IsImage(element):
                image = ImageOf(source) ?? throw DomError("InvalidStateError", "The image is not loaded.");
                break;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } canvas } when _contexts.TryGetValue(canvas, out var context):
                if (context.Bitmap.Width <= 0 || context.Bitmap.Height <= 0)
                    throw DomError("InvalidStateError", "The canvas is empty.");
                var read = context.Bitmap.Pixels();
                if (read.Length == 0)
                    throw DomError("InvalidStateError", "The canvas is too large.");
                image = canvas.OwnerDocument.DecodeImage(PngWriter.Encode((int)context.Bitmap.Width, (int)context.Bitmap.Height, read));
                break;
            case ObjectInstance data when data.HasProperty("data") && data.HasProperty("width"):
            {
                var (w, h) = (TypeConverter.ToInt32(data.Get("width")), TypeConverter.ToInt32(data.Get("height")));
                var array = data.Get("data") as ObjectInstance ?? throw TypeError("The data is not an array.");
                var bytes = new byte[PixelBytes(w, h)];
                for (var i = 0; i < bytes.Length; i++)
                    bytes[i] = (byte)TypeConverter.ToInt32(array.Get(i.ToString(CultureInfo.InvariantCulture)));

                image = Document.DecodeImage(PngWriter.Encode(w, h, bytes));
                break;
            }

            default:
                throw TypeError("The source is not a supported image source.");
        }

        if (image is null)
            return JsValue.Null;

        if (crop.Length >= 4 && crop.Take(4).All(double.IsFinite) && (crop[2] != 0 && crop[3] != 0) && (crop[0] != 0 || crop[1] != 0 || Math.Abs(crop[2]) != image.Width || Math.Abs(crop[3]) != image.Height))
        {
            // A cropped bitmap is a picture of the part: the part is drawn onto a canvas and read back.
            var (cx, cy, cw, ch) = (crop[0], crop[1], Math.Abs(crop[2]), Math.Abs(crop[3]));
            var piece = new CanvasBitmap(Document, cw, ch);
            piece.DrawImage(image, new Rect(cx, cy, cw, ch), new Rect(0, 0, cw, ch));
            image = piece.Pixels() is { Length: > 0 } cropped ? Document.DecodeImage(PngWriter.Encode((int)cw, (int)ch, cropped)) ?? image : image;
        }

        return new BitmapObject(Engine, BitmapPrototype, image);
    }
}
