using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

// audio and video elements. There is no media decoder here, so a source never plays: the element's resource selection fails the way it does for a type the
// browser cannot play (networkState NETWORK_NO_SOURCE, an error event, a MediaError of code 4), which is what a script that waits for canplaythrough or error sees.
internal sealed partial class ScriptRuntime
{
    private readonly Dictionary<ScriptNode, int> _mediaState = [];

    private static bool IsMedia(ScriptNode node) =>
        node.Kind == ScriptNodeKind.Element && (string.Equals(node.TagName, "video", StringComparison.OrdinalIgnoreCase) || string.Equals(node.TagName, "audio", StringComparison.OrdinalIgnoreCase));

    // A media element whose source is set or changed starts selecting its resource, which fails in a task of its own.
    private void HandleMediaMutation(ScriptMutation mutation)
    {
        if (mutation.Kind == ScriptMutationKind.Attributes && mutation.AttributeName is { } name && name.Equals("src", StringComparison.OrdinalIgnoreCase) && IsMedia(mutation.Target))
            FailMedia(mutation.Target);
    }

    private void FailMedia(ScriptNode media)
    {
        if (string.IsNullOrEmpty(media.GetAttribute("src")))
            return;

        _mediaState[media] = 3;
        AddTimer(Js.Function(Engine, "mediaError", 0, (_, _) =>
        {
            if (media.GetAttribute("src") is { Length: > 0 })
                FireEvent(Wrap(media), "error", bubbles: false, cancelable: false);

            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: false);
    }

    private void InstallMedia()
    {
        foreach (var tag in new[] { "video", "audio" })
        {
            var prototype = ElementPrototypeFor(tag);
            int NetworkState(JsValue self) => _mediaState.TryGetValue(Sn(self), out var state) ? state : (string.IsNullOrEmpty(Sn(self).GetAttribute("src")) ? 0 : 3);

            Js.Accessor(Engine, prototype, "networkState", self => Js.Num(NetworkState(self)));
            Js.Accessor(Engine, prototype, "readyState", _ => Js.Num(0));
            Js.Accessor(Engine, prototype, "paused", _ => JsBoolean.True);
            Js.Accessor(Engine, prototype, "ended", _ => JsBoolean.False);
            Js.Accessor(Engine, prototype, "seeking", _ => JsBoolean.False);
            Js.Accessor(Engine, prototype, "duration", _ => Js.Num(double.NaN));
            Js.Accessor(Engine, prototype, "currentSrc", self => Js.Str(NetworkState(self) == 3 ? Sn(self).GetAttribute("src") ?? "" : ""));
            Js.Accessor(Engine, prototype, "error", self => NetworkState(self) == 3
                ? Engine.Evaluate("(function () { var e = Object.create((typeof MediaError === 'function' ? MediaError : Object).prototype); Object.defineProperty(e, 'code', { value: 4 }); Object.defineProperty(e, 'message', { value: 'The source is not supported.' }); return e; })").Call(JsValue.Undefined, [])
                : JsValue.Null);
            Js.Accessor(Engine, prototype, "currentTime", _ => Js.Num(0), (_, _) => { });
            Js.Accessor(Engine, prototype, "muted", _ => JsBoolean.False, (_, _) => { });
            Js.Accessor(Engine, prototype, "volume", _ => Js.Num(1), (_, _) => { });
            Js.Method(Engine, prototype, "canPlayType", 1, (_, _) => Js.Str(""));
            Js.Method(Engine, prototype, "load", 0, (self, _) =>
            {
                FailMedia(Sn(self));
                return JsValue.Undefined;
            });
            Js.Method(Engine, prototype, "pause", 0, (_, _) => JsValue.Undefined);
            Js.Method(Engine, prototype, "play", 0, (_, _) =>
                Engine.Evaluate("(function () { return Promise.reject(new DOMException('The source is not supported.', 'NotSupportedError')); })").Call(JsValue.Undefined, []));

            if (tag == "video")
            {
                Js.Accessor(Engine, prototype, "videoWidth", _ => Js.Num(0));
                Js.Accessor(Engine, prototype, "videoHeight", _ => Js.Num(0));
            }
        }
    }
}
