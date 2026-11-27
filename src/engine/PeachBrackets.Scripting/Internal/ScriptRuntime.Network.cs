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
using System.Text;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

// The network a script reaches with XMLHttpRequest and fetch. The classes themselves are script (WebPlatformScripts), installed the first time one is named;
// what they stand on is here: a few functions that send a request through the document's network, text encodings, and host name normalization.
internal sealed partial class ScriptRuntime
{
    private readonly List<(Task<ScriptNetworkResponse?> Task, JsValue Callback, Uri Url)> _pendingRequests = [];
    private bool _webLoaded;
    private ObjectInstance? _hyperlink;
    private ObjectInstance? _htmlStream;

    // a and area elements have the parts of their href as properties, worked out by the script bundle's URL parser (loaded when first asked).
    private void InstallHyperlinks()
    {
        foreach (var tag in new[] { "a", "area" })
        {
            var prototype = ElementPrototypeFor(tag);
            foreach (var part in new[] { "href", "origin", "protocol", "username", "password", "host", "hostname", "port", "pathname", "search", "hash" })
            {
                var name = part;
                Js.Accessor(Engine, prototype, name,
                    self =>
                    {
                        LoadWebBundle();
                        return _hyperlink is null ? Js.Str(string.Empty) : _hyperlink.Get("get").Call(_hyperlink, [self, Js.Str(name)]);
                    },
                    name == "origin" ? null : (self, value) =>
                    {
                        LoadWebBundle();
                        _hyperlink?.Get("set").Call(_hyperlink, [self, Js.Str(name), value]);
                    });
            }

            Js.Method(Engine, prototype, "toString", 0, (self, _) =>
            {
                LoadWebBundle();
                return _hyperlink is null ? Js.Str(string.Empty) : _hyperlink.Get("get").Call(_hyperlink, [self, Js.Str("href")]);
            });
        }
    }

    // The globals the bundle defines. Each is an accessor until the first read, which runs the bundle and replaces them all with the real values.
    private static readonly string[] WebGlobals =
    [
        "URL", "URLSearchParams", "Headers", "Request", "Response", "fetch", "XMLHttpRequest", "XMLHttpRequestEventTarget", "XMLHttpRequestUpload",
        "Blob", "File", "FormData", "TextEncoder", "TextDecoder", "FileReader",
        "ReadableStream", "ReadableStreamDefaultReader", "ReadableStreamDefaultController", "WritableStream", "WritableStreamDefaultWriter", "WritableStreamDefaultController",
        "TransformStream", "TransformStreamDefaultController", "TextEncoderStream", "TextDecoderStream", "CountQueuingStrategy", "ByteLengthQueuingStrategy", "XMLSerializer", "structuredClone", "ReadableStreamBYOBReader", "ReadableStreamBYOBRequest", "ReadableByteStreamController", "MessageChannel", "MessagePort", "BroadcastChannel", "postMessage", "Worker",
    ];

    private void InstallLazyWebGlobals()
    {
        foreach (var name in WebGlobals)
        {
            var captured = name;
            var getter = Js.Function(Engine, "get " + captured, 0, (_, _) =>
            {
                LoadWebBundle();
                var value = Engine.Global.GetOwnProperty(captured);
                return value.IsAccessorDescriptor() ? JsValue.Undefined : value.Value;
            });
            var setter = Js.Function(Engine, "set " + captured, 1, (_, args) =>
            {
                Engine.Global.FastSetProperty(captured, new PropertyDescriptor(Js.Arg(args, 0), PropertyFlag.Writable | PropertyFlag.Configurable));
                return JsValue.Undefined;
            });
            Engine.Global.FastSetProperty(captured, new GetSetPropertyDescriptor(getter, setter, enumerable: false, configurable: true));
        }
    }

    // An address resolved against the document's must be a URL; if not it is a SyntaxError, as a location assignment or window.open reports it.
    private void RequireValidUrl(string address)
    {
        LoadWebBundle();
        var check = Engine.Evaluate("(function (a, b) { try { new URL(a, b); return true; } catch (e) { return false; } })");
        if (!TypeConverter.ToBoolean(check.Call(JsValue.Undefined, [Js.Str(address), Js.Str(BaseOf(Document))])))
            throw DomError("SyntaxError", $"'{address}' is not a valid URL.");
    }

    private void LoadWebBundle()
    {
        if (_webLoaded)
            return;

        _webLoaded = true;
        try
        {
            // The bundle is defined and run in one script: the engine runs the promise jobs that are waiting when a script ends, and one of them can name a
            // global this bundle is still making (a test's first promise_test callback does).
            Engine.Global.FastSetProperty("__peachNet", new PropertyDescriptor(NetObject(), PropertyFlag.Writable | PropertyFlag.Configurable));
            var bundle = WebPlatformScripts.Bundle.Replace(WebPlatformScripts.StreamsMarker, WebStreamsScripts.Source, StringComparison.Ordinal).Replace(WebPlatformScripts.XmlSerializerMarker, XmlSerializerScripts.Source, StringComparison.Ordinal).Replace(WebPlatformScripts.MessagingMarker, MessagingScripts.Source, StringComparison.Ordinal).Replace(WebPlatformScripts.WorkersMarker, WorkerScripts.Source, StringComparison.Ordinal);
            Engine.Execute(bundle + "(globalThis.__peachNet, globalThis); delete globalThis.__peachNet;");
        }
        catch (Exception error) when (IsScriptFailure(error))
        {
            ReportError(error);
        }
    }

    private ObjectInstance NetObject()
    {
        var net = Js.NewObject(Engine);

        Js.Method(Engine, net, "registerHtmlStream", 1, (_, args) =>
        {
            _htmlStream = Js.Arg(args, 0) as ObjectInstance;
            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "registerHyperlink", 1, (_, args) =>
        {
            _hyperlink = Js.Arg(args, 0) as ObjectInstance;
            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "documentUrl", 0, (_, _) => Js.Str(Document.BaseUri?.AbsoluteUri ?? "about:blank"));
        Js.Method(Engine, net, "utf8Encode", 1, (_, args) => Js.Str(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(Js.AsText(Js.Arg(args, 0))))));
        Js.Method(Engine, net, "utf8Decode", 2, (_, args) =>
        {
            var bytes = Encoding.Latin1.GetBytes(Js.AsText(Js.Arg(args, 0)));
            var strip = TypeConverter.ToBoolean(Js.Arg(args, 1));
            var start = strip && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Js.Str(new UTF8Encoding(false, false).GetString(bytes, start, bytes.Length - start));
        });

        // A text in the encoding a label names, or null when the label is not one this knows.
        Js.Method(Engine, net, "decode", 2, (_, args) =>
        {
            var bytes = Encoding.Latin1.GetBytes(Js.AsText(Js.Arg(args, 0)));
            return Js.Str(DecoderFor(Js.AsText(Js.Arg(args, 1)))?.GetString(bytes));
        });
        // Decoders with a state, for TextDecoder: decoderNew(name, fatal) is a handle or -1; decoderRun(handle, bytes, flush) is the text, or null for an invalid input of a fatal decoder.
        Js.Method(Engine, net, "decoderNew", 2, (_, args) => (double)TextEncodings.Create(Js.AsText(Js.Arg(args, 0)), TypeConverter.ToBoolean(Js.Arg(args, 1))));
        Js.Method(Engine, net, "decoderRun", 3, (_, args) =>
            Js.Str(TextEncodings.Run((int)TypeConverter.ToNumber(Js.Arg(args, 0)), Encoding.Latin1.GetBytes(Js.AsText(Js.Arg(args, 1))), TypeConverter.ToBoolean(Js.Arg(args, 2)))));
        // The lower-case name of the encoding a label stands for in the Encoding Standard ("replacement" for the replacement encodings), or null.
        Js.Method(Engine, net, "encodingName", 1, (_, args) => Js.Str(EncodingLabels.NameOf(Js.AsText(Js.Arg(args, 0)))?.ToLowerInvariant()));
        // Domain to ASCII (UTS #46); null on an error.
        Js.Method(Engine, net, "idna", 1, (_, args) => Js.Str(Idna.Uts46.ToAscii(Js.AsText(Js.Arg(args, 0)))));

        // send(method, url, headers, body, followRedirects, callback): the callback gets the answer, or null for a network error, after the clock has
        // stopped for it (see PlayTimersAsync). sendSync answers at once.
        Js.Method(Engine, net, "send", 8, (_, args) =>
        {
            var (request, url) = BuildRequest(args, 6);
            if (!Js.IsCallable(Js.Arg(args, 5)))
                throw TypeError("send needs a callback.");

            _pendingRequests.Add((SendAsync(request), args[5], url));
            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "sendSync", 7, (_, args) =>
        {
            var (request, url) = BuildRequest(args, 5);
            // The script is waiting for the answer, so this waits for it, and can deadlock a host that holds a context the answer needs; the documented cost of a synchronous request.
            var response = SendAsync(request).GetAwaiter().GetResult();
            return Result(response, url);
        });

        InstallWorkerNet(net);
        return net;
    }

    private (ScriptNetworkRequest Request, Uri Url) BuildRequest(JsValue[] args, int referrerIndex)
    {
        var method = Js.AsText(Js.Arg(args, 0));
        if (!Uri.TryCreate(Js.AsText(Js.Arg(args, 1)), UriKind.Absolute, out var url))
            throw TypeError("The address is not an absolute URL.");

        var headers = new List<KeyValuePair<string, string>>();
        if (Js.Arg(args, 2) is ObjectInstance list && list.IsArray())
        {
            var length = (int)TypeConverter.ToNumber(list.Get("length"));
            for (var i = 0; i < length; i++)
            {
                if (list.Get(i.ToString(CultureInfo.InvariantCulture)) is ObjectInstance pair)
                    headers.Add(new KeyValuePair<string, string>(Js.AsText(pair.Get("0")), Js.AsText(pair.Get("1"))));
            }
        }

        var body = Js.Arg(args, 3);
        byte[]? bytes = Js.IsNullish(body) ? null : Encoding.Latin1.GetBytes(Js.AsText(body));
        var referrer = Js.Arg(args, referrerIndex);
        var policy = Js.Arg(args, referrerIndex + 1);
        return (new ScriptNetworkRequest(method, url, headers, bytes, TypeConverter.ToBoolean(Js.Arg(args, 4)),
            Js.IsNullish(referrer) ? null : Js.AsText(referrer), Js.IsNullish(policy) ? string.Empty : Js.AsText(policy)), url);
    }

    // The answer to a request: a data: address is read here, anything else is the document's network's to answer. A failure is a network error, not an exception.
    private async Task<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request)
    {
        try
        {
            if (request.Url.Scheme == "data")
                return ReadDataUrl(request.Url);

            return await Document.SendAsync(request);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }

    // data:[<type>][;base64],<data>
    private static ScriptNetworkResponse? ReadDataUrl(Uri url)
    {
        var text = url.OriginalString["data:".Length..];
        var comma = text.IndexOf(',');
        if (comma < 0)
            return null;

        var meta = text[..comma];
        var payload = text[(comma + 1)..];
        var hash = payload.IndexOf('#');
        if (hash >= 0)
            payload = payload[..hash];

        var isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        var mime = isBase64 ? meta[..^";base64".Length] : meta;
        if (mime.Length == 0 || mime.StartsWith(';'))
            mime = "text/plain" + mime;

        byte[] body;
        try
        {
            body = isBase64
                ? Convert.FromBase64String(Uri.UnescapeDataString(payload).Replace(" ", string.Empty))
                : Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload))));
        }
        catch (FormatException)
        {
            return null;
        }

        return new ScriptNetworkResponse(200, "OK", [new KeyValuePair<string, string>("Content-Type", mime)], body, url);
    }

    // [status, statusText, [[name, value]...], body as a string of bytes, url, redirected], or null.
    private JsValue Result(ScriptNetworkResponse? response, Uri requested)
    {
        if (response is null)
            return JsValue.Null;

        var headers = Js.Array(Engine, [.. response.Headers.Select(h => Js.Array(Engine, [Js.Str(h.Key), Js.Str(h.Value)]))]);
        var redirected = !string.Equals(response.Url.GetLeftPart(UriPartial.Query), requested.GetLeftPart(UriPartial.Query), StringComparison.Ordinal);
        return Js.Array(Engine,
        [
            Js.Num(response.Status), Js.Str(response.StatusText), headers, Js.Str(Encoding.Latin1.GetString(response.Body)),
            Js.Str(response.Url.AbsoluteUri), Js.Bool(redirected),
        ]);
    }

    /// <summary>Waits for the requests the scripts have sent and queues each answer as a zero-delay timer, so it runs after what was already due and before time moves on.</summary>
    private async ValueTask DeliverRequestsAsync()
    {
        StartInitialImages();
        while (_pendingRequests.Count > 0 || _pendingImages.Count > 0 || _pendingFonts.Count > 0)
        {
            // Fonts a script asked to load after the page loaded are waited for as images and requests are.
            if (_pendingFonts.Count > 0)
                await RunFontLoadsAsync();

            await DeliverImagesAsync();
            if (_pendingRequests.Count == 0)
                continue;

            var batch = _pendingRequests.ToArray();
            _pendingRequests.Clear();
            foreach (var (task, callback, url) in batch)
            {
                ScriptNetworkResponse? response = null;
                try
                {
                    response = await task;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                }

                var result = Result(response, url);
                AddTimer(Js.Function(Engine, "deliver", 0, (_, _) =>
                {
                    Call(callback, JsValue.Undefined, result);
                    return JsValue.Undefined;
                }), 0, [], repeat: false, frame: false);
            }
        }
    }

    // The decoder for an encoding label (the ones the framework has without a code-page provider), or null.
    private static Encoding? DecoderFor(string label) => label.Trim().ToLowerInvariant() switch
    {
        "utf-8" or "utf8" or "unicode-1-1-utf-8" => new UTF8Encoding(false, false),
        "utf-16le" or "utf-16" or "ucs-2" or "unicode" => new UnicodeEncoding(false, false),
        "utf-16be" => new UnicodeEncoding(true, false),
        "iso-8859-1" or "latin1" or "l1" or "windows-1252" or "us-ascii" or "ascii" or "cp1252" => Encoding.Latin1,
        _ => null,
    };
}
