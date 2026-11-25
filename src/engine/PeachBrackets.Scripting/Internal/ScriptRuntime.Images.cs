using Jint;
using Jint.Native;
using Jint.Native.Object;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

// Images a script makes (new Image()) or points at (an img element's src): loaded through the document's resource loader, answered like a network
// request (before the virtual clock moves on), with load and error events and the properties that report the result.
internal sealed partial class ScriptRuntime
{
    private sealed class ImageState
    {
        /// <summary>0: nothing to load, 1: loading, 2: loaded, 3: broken.</summary>
        public int Status { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public string CurrentSource { get; set; } = string.Empty;

        public int Token { get; set; }

        public List<(JsValue Resolve, JsValue Reject)> Waiters { get; } = [];
    }

    private readonly Dictionary<ScriptNode, ImageState> _images = [];
    private readonly List<(Task<ScriptImageInfo?> Task, ScriptNode Node, int Token)> _pendingImages = [];
    private bool _initialImagesStarted;

    private static bool IsImage(ScriptNode node) => node.Kind == ScriptNodeKind.Element && string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase);

    private const double ViewportWidth = 800;

    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/svg+xml", "image/bmp", "image/avif", "image/apng", "image/x-icon"];

    // A media query the image sources use, as far as the width of the viewport answers it.
    private static bool MediaMatches(string? media)
    {
        if (string.IsNullOrWhiteSpace(media) || media.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        var matches = System.Text.RegularExpressions.Regex.Matches(media, @"\(\s*(min|max)-width\s*:\s*([0-9.]+)px\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (matches.Count == 0 || media.Contains(',') || media.Contains("not ", StringComparison.OrdinalIgnoreCase))
            return false;

        return matches.All(m =>
        {
            var limit = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return m.Groups[1].Value.Equals("min", StringComparison.OrdinalIgnoreCase) ? ViewportWidth >= limit : ViewportWidth <= limit;
        });
    }

    // The width the image is laid out at, from a sizes attribute: px and vw lengths, 100vw when there is none.
    private static double SourceSize(string? sizes)
    {
        foreach (var entry in (sizes ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = System.Text.RegularExpressions.Regex.Match(entry, @"^(?:(\(.*\))\s*)?([0-9.]+)(px|vw)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success || (match.Groups[1].Success && !MediaMatches(match.Groups[1].Value)))
                continue;

            var number = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return match.Groups[3].Value.Equals("vw", StringComparison.OrdinalIgnoreCase) ? number / 100 * ViewportWidth : number;
        }

        return ViewportWidth;
    }

    // The candidate a srcset gives for a one-to-one screen: the smallest that is at least as dense as the screen, else the densest.
    private static string? PickCandidate(string srcset, string? sizes)
    {
        var candidates = new List<(string Url, double Density)>();
        var width = SourceSize(sizes);
        var text = srcset.Trim();
        var position = 0;
        while (position < text.Length)
        {
            while (position < text.Length && (char.IsWhiteSpace(text[position]) || text[position] == ','))
                position++;

            var start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]))
                position++;

            var url = text[start..position];
            var descriptor = string.Empty;
            if (url.EndsWith(','))
            {
                url = url.TrimEnd(',');
            }
            else
            {
                var end = text.IndexOf(',', position);
                descriptor = (end < 0 ? text[position..] : text[position..end]).Trim();
                position = end < 0 ? text.Length : end + 1;
            }

            var density = 1.0;
            if (descriptor.Length > 0)
            {
                var d = descriptor.ToLowerInvariant();
                if (d.EndsWith('w') && double.TryParse(d[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0) density = w / width;
                else if (d.EndsWith('x') && double.TryParse(d[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && x > 0) density = x;
                else continue;
            }

            if (url.Length > 0)
                candidates.Add((url, density));
        }

        if (candidates.Count == 0)
            return null;

        var denser = candidates.Where(c => c.Density >= 1).OrderBy(c => c.Density).ToList();
        return (denser.Count > 0 ? denser[0] : candidates.OrderByDescending(c => c.Density).First()).Url;
    }

    // The address an img element loads: from a source element of its picture, else its own srcset, else src.
    private static string? SelectImageSource(ScriptNode image)
    {
        if (image.Parent is { Kind: ScriptNodeKind.Element } parent && string.Equals(parent.TagName, "picture", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sibling in parent.ChildElements.TakeWhile(c => !ReferenceEquals(c, image)))
            {
                if (!string.Equals(sibling.TagName, "source", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(sibling.GetAttribute("srcset")))
                    continue;
                if (sibling.GetAttribute("type") is { } type && !ImageTypes.Contains(type.Trim().ToLowerInvariant()))
                    continue;
                if (!MediaMatches(sibling.GetAttribute("media")))
                    continue;

                if (PickCandidate(sibling.GetAttribute("srcset")!, sibling.GetAttribute("sizes")) is { } fromSource)
                    return fromSource;
            }
        }

        if (!string.IsNullOrWhiteSpace(image.GetAttribute("srcset")) && PickCandidate(image.GetAttribute("srcset")!, image.GetAttribute("sizes")) is { } picked)
            return picked;

        return image.GetAttribute("src");
    }

    private void StartImageLoad(ScriptNode image)
    {
        if (!_images.TryGetValue(image, out var state))
            _images[image] = state = new ImageState();

        state.Token++;
        state.Width = state.Height = 0;
        state.CurrentSource = string.Empty;
        var source = SelectImageSource(image);
        if (string.IsNullOrEmpty(source))
        {
            state.Status = 0;
            return;
        }

        state.Status = 1;
        _pendingImages.Add((image.OwnerDocument.LoadImageAsync(source).AsTask(), image, state.Token));
    }

    private void StartInitialImages()
    {
        if (_initialImagesStarted)
            return;

        _initialImagesStarted = true;
        foreach (var image in Document.GetElementsByTagName("img").Where(i => !string.IsNullOrEmpty(i.GetAttribute("src"))))
            StartImageLoad(image);
    }

    // Waits for the images the scripts asked for and queues each result as a zero-delay timer, like a network answer.
    private async ValueTask DeliverImagesAsync()
    {
        var batch = _pendingImages.ToArray();
        _pendingImages.Clear();
        foreach (var (task, node, token) in batch)
        {
            ScriptImageInfo? info = null;
            var source = SelectImageSource(node) ?? string.Empty;
            try
            {
                info = await task;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
            }

            AddTimer(Js.Function(Engine, "imageLoaded", 0, (_, _) =>
            {
                if (!_images.TryGetValue(node, out var state) || state.Token != token)
                    return JsValue.Undefined;

                state.Status = info is null ? 3 : 2;
                state.Width = info?.Width ?? 0;
                state.Height = info?.Height ?? 0;
                state.CurrentSource = info?.Address ?? ResolveAddress(node, source);
                var waiters = state.Waiters.ToArray();
                state.Waiters.Clear();
                foreach (var (resolve, reject) in waiters)
                {
                    if (info is null)
                        reject.Call(JsValue.Undefined, [DomError("EncodingError", "The image could not be decoded.").Error]);
                    else
                        resolve.Call(JsValue.Undefined, []);
                }

                FireEvent(Wrap(node), info is null ? "error" : "load", bubbles: false, cancelable: false);
                return JsValue.Undefined;
            }), 0, [], repeat: false, frame: false);
        }
    }

    private string ResolveAddress(ScriptNode node, string address) =>
        node.OwnerDocument.BaseUri is { } baseUri && Uri.TryCreate(baseUri, address, out var absolute) ? absolute.AbsoluteUri : address;

    private void InstallImages()
    {
        var prototype = ElementPrototypeFor("img");
        ImageState State(JsValue self)
        {
            StartInitialImages();
            return _images.TryGetValue(Sn(self), out var state) ? state : new ImageState();
        }

        Js.Accessor(Engine, prototype, "complete", self => Js.Bool(State(self).Status != 1));
        Js.Accessor(Engine, prototype, "naturalWidth", self => Js.Num(Math.Round(State(self).Width)));
        Js.Accessor(Engine, prototype, "naturalHeight", self => Js.Num(Math.Round(State(self).Height)));
        Js.Accessor(Engine, prototype, "currentSrc", self => Js.Str(State(self).CurrentSource));
        foreach (var (name, natural) in new (string, Func<ImageState, double>)[] { ("width", s => s.Width), ("height", s => s.Height) })
        {
            var (attribute, size) = (name, natural);
            Js.Accessor(Engine, prototype, attribute,
                self => Sn(self).GetAttribute(attribute) is { } text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? Js.Num(Math.Max(0, Math.Truncate(number)))
                    : Js.Num(Math.Round(size(State(self)))),
                (self, value) => Sn(self).SetAttribute(attribute, Js.AsText(value)));
        }

        Js.Method(Engine, prototype, "decode", 0, (self, _) =>
        {
            var state = State(self);
            var deferred = Engine.Evaluate("(function () { var d = {}; d.promise = new Promise(function (a, b) { d.resolve = a; d.reject = b; }); return d; })()").AsObject();
            switch (state.Status)
            {
                case 2:
                    deferred.Get("resolve").Call(JsValue.Undefined, []);
                    break;
                case 1:
                    state.Waiters.Add((deferred.Get("resolve"), deferred.Get("reject")));
                    break;
                default:
                    deferred.Get("reject").Call(JsValue.Undefined, [DomError("EncodingError", "The image could not be decoded.").Error]);
                    break;
            }

            return deferred.Get("promise");
        });

        // new Image(width, height) makes an img element; Image.prototype is the img element prototype.
        var interfaceObject = Engine.Global.Get("HTMLImageElement");
        var constructor = Js.Constructor(Engine, "Image", prototype, 0, (_, args, _) =>
        {
            var element = Document.CreateElement("img");
            if (args.Length > 0 && !args[0].IsUndefined())
                element.SetAttribute("width", Js.AsText(args[0]));
            if (args.Length > 1 && !args[1].IsUndefined())
                element.SetAttribute("height", Js.AsText(args[1]));

            return Wrap(element);
        });
        if (interfaceObject is ObjectInstance)
            prototype.FastSetProperty("constructor", new Jint.Runtime.Descriptors.PropertyDescriptor(interfaceObject, Jint.Runtime.Descriptors.PropertyFlag.Writable | Jint.Runtime.Descriptors.PropertyFlag.Configurable));

        Define(Engine.Global, "Image", constructor);
    }
}
