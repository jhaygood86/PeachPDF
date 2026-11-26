using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
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

    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/svg+xml", "image/bmp", "image/avif", "image/apng", "image/x-icon", "image/vnd.microsoft.icon"];

    // The viewport each document's images are chosen for: the page's is the window's; a frame's is its frame's size.
    private readonly Dictionary<ScriptDocument, ImageViewport> _viewports = [];

    private ImageViewport ViewportOf(ScriptDocument document) => _viewports.TryGetValue(document, out var viewport) ? viewport : new ImageViewport(800, 600);

    // The address an img element loads, by the HTML algorithm: a picture's first matching source, else the element's own srcset with its src.
    private string? SelectImageSource(ScriptNode image)
    {
        var viewport = ViewportOf(image.OwnerDocument);
        if (image.Parent is { Kind: ScriptNodeKind.Element } parent && string.Equals(parent.TagName, "picture", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sibling in parent.ChildElements.TakeWhile(c => !ReferenceEquals(c, image)))
            {
                if (!string.Equals(sibling.TagName, "source", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(sibling.GetAttribute("srcset")))
                    continue;
                if (sibling.GetAttribute("type") is { } type && !ImageTypes.Contains(type.Trim().ToLowerInvariant()))
                    continue;
                if (!ImageSources.MediaMatches(sibling.GetAttribute("media"), viewport))
                    continue;

                var fromSource = ImageSources.ParseSrcset(sibling.GetAttribute("srcset")!);
                if (fromSource.Count == 0)
                    continue;

                return ImageSources.Select(fromSource, null, SizeFor(sibling, image, viewport), viewport.DevicePixelRatio);
            }
        }

        var candidates = ImageSources.ParseSrcset(image.GetAttribute("srcset") ?? string.Empty);
        return ImageSources.Select(candidates, image.GetAttribute("src"), SizeFor(image, image, viewport), viewport.DevicePixelRatio);
    }

    // The source size of an img or picture source: its sizes attribute, where auto means the width the image is laid out at.
    private static double SizeFor(ScriptNode holder, ScriptNode image, ImageViewport viewport)
    {
        var size = ImageSources.SourceSize(holder.GetAttribute("sizes"), viewport, out var auto);
        if (auto)
        {
            var lazy = string.Equals(image.GetAttribute("loading")?.Trim(), "lazy", StringComparison.OrdinalIgnoreCase);
            size = lazy && image.OwnerDocument.GetLayoutBox(image) is { Width: > 0 } box ? box.Width : viewport.Width;
        }

        return size;
    }

    private static bool IsPicture(ScriptNode? node) => node is { Kind: ScriptNodeKind.Element } && string.Equals(node.TagName, "picture", StringComparison.OrdinalIgnoreCase);

    private static bool IsSource(ScriptNode node) => node.Kind == ScriptNodeKind.Element && string.Equals(node.TagName, "source", StringComparison.OrdinalIgnoreCase);

    private static bool HasSource(ScriptNode image) => image.GetAttribute("src") is not null || image.GetAttribute("srcset") is not null;

    // The crossorigin attribute's state: none, anonymous (empty, anonymous or anything else), use-credentials.
    private static string CorsState(string? value) => value is null ? "none" : string.Equals(value, "use-credentials", StringComparison.OrdinalIgnoreCase) ? "credentials" : "anonymous";

    private static readonly string[] ReferrerPolicies = ["no-referrer", "no-referrer-when-downgrade", "same-origin", "origin", "strict-origin", "origin-when-cross-origin", "strict-origin-when-cross-origin", "unsafe-url"];

    private static string ReferrerState(string? value) => value is not null && ReferrerPolicies.Contains(value.ToLowerInvariant()) ? value.ToLowerInvariant() : string.Empty;

    // The img elements that come after a node among its parent's children, which a picture's sources before them change the choice of.
    private static IEnumerable<ScriptNode> ImagesAfter(ScriptNode parent, ScriptNode? previous)
    {
        var children = parent.ChildElements.ToList();
        var from = previous is null ? 0 : children.IndexOf(previous) + 1;
        return children.Skip(Math.Max(0, from)).Where(IsImage);
    }

    // The mutations that count as relevant to an img element (HTML, "relevant mutations"): its src, srcset and sizes; a crossorigin or referrerpolicy
    // change of state while it has a source; a picture's sources before it; the img entering or leaving a picture.
    private void HandleImageMutation(ScriptMutation mutation)
    {
        HandleMediaMutation(mutation);
        HandleSvgImageMutation(mutation);
        HandleCanvasSubtreeMutation(mutation);
        var target = mutation.Target;
        if (mutation.Kind == ScriptMutationKind.Attributes && mutation.AttributeName is { } name)
        {
            var lower = name.ToLowerInvariant();
            if (IsImage(target))
            {
                switch (lower)
                {
                    case "src" or "srcset" or "sizes":
                        QueueImageUpdate(target);
                        break;
                    case "crossorigin" when HasSource(target) && CorsState(mutation.OldValue) != CorsState(target.GetAttribute("crossorigin")):
                    case "referrerpolicy" when HasSource(target) && ReferrerState(mutation.OldValue) != ReferrerState(target.GetAttribute("referrerpolicy")):
                        QueueImageUpdate(target);
                        break;
                }
            }
            else if (IsSource(target) && target.Parent is { } holder && IsPicture(holder) && lower is "srcset" or "sizes" or "media" or "type")
            {
                foreach (var following in ImagesAfter(holder, target))
                    QueueImageUpdate(following);
            }

            return;
        }

        if (mutation.Kind != ScriptMutationKind.ChildList)
            return;

        foreach (var added in mutation.Added ?? [])
        {
            if (IsPicture(target) && IsImage(added))
                QueueImageUpdate(added);
            else if (IsPicture(target) && IsSource(added))
                foreach (var following in ImagesAfter(target, added)) QueueImageUpdate(following);
            else if (IsPicture(added))
                foreach (var inside in added.ChildElements.Where(IsImage)) QueueImageUpdate(inside);
        }

        foreach (var removed in mutation.Removed ?? [])
        {
            if (IsPicture(target) && IsImage(removed))
                QueueImageUpdate(removed);
            else if (IsPicture(target) && IsSource(removed))
                foreach (var following in ImagesAfter(target, mutation.Previous)) QueueImageUpdate(following);
            else if (IsPicture(removed))
                foreach (var inside in removed.ChildElements.Where(IsImage)) QueueImageUpdate(inside);
        }
    }

    // An SVG image element (<image> inside an svg): it loads the picture its href names when it is in the document, and says so with a load or an error event.
    private static bool IsSvgImage(ScriptNode node) =>
        node.Kind == ScriptNodeKind.Element && string.Equals(node.TagName, "image", StringComparison.OrdinalIgnoreCase);

    // The address an image element loads: an img's src, an SVG image's href (plain or in the xlink namespace).
    private static string? ImageAddressOf(ScriptNode node) =>
        IsSvgImage(node) ? node.GetAttribute("href") ?? node.GetAttribute("xlink:href") : node.GetAttribute("src");

    // The scheme, host and port a script's page or a resource is of, or null for an address with no origin of its own (data:, blob:, a file).
    private static string? OriginOf(Uri? address) =>
        address is { IsAbsoluteUri: true } && address.Scheme is "http" or "https" ? address.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : null;

    // Whether reading what a source shows is not allowed: it is from another origin and was not asked for with a crossorigin attribute (which the server must then have allowed),
    // or it is a canvas or a bitmap that has been tainted already.
    private bool SourceTainted(JsValue source)
    {
        switch (source)
        {
            case BitmapObject bitmap:
                return !bitmap.OriginClean;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } element } when string.Equals(element.TagName, "canvas", StringComparison.OrdinalIgnoreCase):
                return _contexts.TryGetValue(element, out var context) && !context.Bitmap.OriginClean;
            case NodeObject { Node: { Kind: ScriptNodeKind.Element } image } when IsImage(image) || IsSvgImage(image):
                if (image.GetAttribute("crossorigin") is not null || !_images.TryGetValue(image, out var state) || state.Status != 2)
                    return false;

                return OriginOf(Uri.TryCreate(state.CurrentSource, UriKind.Absolute, out var resource) ? resource : null) is { } resourceOrigin
                    && !string.Equals(resourceOrigin, OriginOf(image.OwnerDocument.BaseUri), StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private void HandleSvgImageMutation(ScriptMutation mutation)
    {
        if (mutation.Kind == ScriptMutationKind.Attributes && mutation.AttributeName is { } name && name.EndsWith("href", StringComparison.OrdinalIgnoreCase) && IsSvgImage(mutation.Target))
        {
            StartSvgImageLoad(mutation.Target);
        }
        else if (mutation.Kind == ScriptMutationKind.ChildList)
        {
            foreach (var added in mutation.Added ?? [])
            {
                foreach (var image in added.Kind == ScriptNodeKind.Element ? new[] { added }.Concat(added.QuerySelectorAll("image")).Where(IsSvgImage) : [])
                    StartSvgImageLoad(image);
            }
        }
    }

    private void StartSvgImageLoad(ScriptNode image)
    {
        if (!image.IsConnected || string.IsNullOrEmpty(ImageAddressOf(image)))
            return;

        if (!_images.TryGetValue(image, out var state))
            _images[image] = state = new ImageState();

        state.Token++;
        state.Status = 1;
        state.Width = state.Height = 0;
        state.CurrentSource = ResolveAddress(image, ImageAddressOf(image)!);
        _pendingImages.Add((image.OwnerDocument.LoadImageAsync(ImageAddressOf(image)!).AsTask(), image, state.Token));
    }

    private readonly HashSet<ScriptNode> _updateQueue = [];
    private bool _updateScheduled;

    // The relevant mutations ask for "update the image data", which runs once the current script is done (a stable state), in the order they were asked.
    private void QueueImageUpdate(ScriptNode image)
    {
        if (!_updateQueue.Add(image) || _updateScheduled)
            return;

        _updateScheduled = true;
        Engine.Evaluate("(function (f) { Promise.resolve().then(f); })").Call(JsValue.Undefined, [Js.Function(Engine, "updateImages", 0, (_, _) =>
        {
            _updateScheduled = false;
            var queued = _updateQueue.ToArray();
            _updateQueue.Clear();
            foreach (var node in queued)
                StartImageLoad(node);

            return JsValue.Undefined;
        })]);
    }

    // The first time an img's state is asked for it is made, as the update that ran when the parser made the element would have.
    private ImageState StateOf(ScriptNode image)
    {
        if (!_images.TryGetValue(image, out var state))
        {
            StartImageLoad(image);
            state = _images[image];
        }

        return state;
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
            // An img whose src is present but empty has no image to load, and says so.
            if (image.GetAttribute("src") is { Length: 0 } && string.IsNullOrWhiteSpace(image.GetAttribute("srcset")))
            {
                var token = state.Token;
                AddTimer(Js.Function(Engine, "emptySource", 0, (_, _) =>
                {
                    if (_images.TryGetValue(image, out var current) && current.Token == token)
                        FireEvent(Wrap(image), "error", bubbles: false, cancelable: false);

                    return JsValue.Undefined;
                }), 0, [], repeat: false, frame: false);
            }

            return;
        }

        state.Status = 1;
        state.CurrentSource = ResolveAddress(image, source);
        _pendingImages.Add((image.OwnerDocument.LoadImageAsync(source).AsTask(), image, state.Token));
    }

    private void StartInitialImages()
    {
        if (_initialImagesStarted)
            return;

        _initialImagesStarted = true;
        foreach (var image in Document.GetElementsByTagName("img").Where(i => !string.IsNullOrEmpty(i.GetAttribute("src"))))
            StartImageLoad(image);

        foreach (var image in Document.GetElementsByTagName("image").Where(IsSvgImage))
            StartSvgImageLoad(image);
    }

    // Waits for the images the scripts asked for and queues each result as a zero-delay timer, like a network answer.
    // Waits for the images asked for. Normally each result is queued as a timer (it arrives after what is already due); the images the page began with are
    // applied at once, so their load events come before the window's, as a page's load waits for its images.
    private async ValueTask DeliverImagesAsync(bool immediate = false)
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

            JsValue Apply(JsValue self, JsValue[] arguments)
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
            }

            if (immediate)
                Apply(JsValue.Undefined, []);
            else
                AddTimer(Js.Function(Engine, "imageLoaded", 0, Apply), 0, [], repeat: false, frame: false);
        }
    }

    /// <summary>Loads the images the page began with and delivers their load and error events.</summary>
    private async ValueTask LoadInitialImagesAsync()
    {
        StartInitialImages();
        while (_pendingImages.Count > 0)
            await DeliverImagesAsync(immediate: true);
    }

    private string ResolveAddress(ScriptNode node, string address) =>
        node.OwnerDocument.BaseUri is { } baseUri && Uri.TryCreate(baseUri, address, out var absolute) ? absolute.AbsoluteUri : address;

    private void InstallImages()
    {
        var prototype = ElementPrototypeFor("img");
        ImageState State(JsValue self)
        {
            StartInitialImages();
            return StateOf(Sn(self));
        }

        // complete: nothing to load, or the current request has finished (loaded or broken); a source just set and not yet updated is still to be loaded.
        Js.Accessor(Engine, prototype, "complete", self =>
        {
            var image = Sn(self);
            var state = State(self);
            if (string.IsNullOrEmpty(image.GetAttribute("src")) && string.IsNullOrWhiteSpace(image.GetAttribute("srcset")))
                return JsBoolean.True;

            return Js.Bool(state.Status is 2 or 3 && !_updateQueue.Contains(image));
        });
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

        // Reflected attributes with a limited set of values: an unknown value reads as the attribute's default.
        void Enumerated(string property, string attribute, string[] values, string missing, string invalid)
        {
            Js.Accessor(Engine, prototype, property,
                self => Js.Str(Sn(self).GetAttribute(attribute) is { } raw ? (values.FirstOrDefault(v => string.Equals(v, raw, StringComparison.OrdinalIgnoreCase)) ?? invalid) : missing),
                (self, value) => Sn(self).SetAttribute(attribute, Js.AsText(value)));
        }

        Enumerated("loading", "loading", ["eager", "lazy"], "eager", "eager");
        Enumerated("decoding", "decoding", ["sync", "async", "auto"], "auto", "auto");
        Enumerated("fetchPriority", "fetchpriority", ["high", "low", "auto"], "auto", "auto");
        Enumerated("referrerPolicy", "referrerpolicy", ReferrerPolicies, string.Empty, string.Empty);
        Js.Accessor(Engine, prototype, "crossOrigin",
            self => Sn(self).GetAttribute("crossorigin") is { } cors ? Js.Str(string.Equals(cors, "use-credentials", StringComparison.OrdinalIgnoreCase) ? "use-credentials" : "anonymous") : JsValue.Null,
            (self, value) =>
            {
                if (value.IsNull()) Sn(self).RemoveAttribute("crossorigin");
                else Sn(self).SetAttribute("crossorigin", Js.AsText(value));
            });
        Js.Accessor(Engine, prototype, "useMap", self => Js.Str(Sn(self).GetAttribute("usemap") ?? string.Empty), (self, value) => Sn(self).SetAttribute("usemap", Js.AsText(value)));
        Js.Accessor(Engine, prototype, "isMap", self => Js.Bool(Sn(self).HasAttribute("ismap")), (self, value) =>
        {
            if (TypeConverter.ToBoolean(value)) Sn(self).SetAttribute("ismap", string.Empty);
            else Sn(self).RemoveAttribute("ismap");
        });
        Js.Accessor(Engine, prototype, "x", _ => Js.Num(0));
        Js.Accessor(Engine, prototype, "y", _ => Js.Num(0));

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
