using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.Media;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

// audio and video elements. The document's media provider (HtmlDocumentOptions.MediaProvider) opens a session for each element's resource and this file
// is the HTMLMediaElement state machine over it: resource selection, the ready-state and event sequence, play, pause and seeking, and the clock. Without a
// provider nothing plays: resource selection fails the way it does for a type the browser cannot play (networkState NETWORK_NO_SOURCE, an error event, a
// MediaError of code 4), which is what a script that waits for canplaythrough or error sees.
//
// A provider's state is read on the script's virtual clock, never when the provider changes it: a recurring timer (while anything is playing or loading) tells each
// session what time the document is at and turns what changed into events. A session made with MediaProviderOptions.DeterministicClock then plays by that clock.
internal sealed partial class ScriptRuntime
{
    private sealed class MediaState
    {
        public IMediaSession? Session { get; set; }

        /// <summary>Where among the element's source children the next candidate begins.</summary>
        public int NextSource { get; set; }

        /// <summary>The source element whose resource is being opened, when the element has source children and no src.</summary>
        public ScriptNode? CurrentSourceNode { get; set; }

        public int Token { get; set; }

        /// <summary>0: empty, 1: idle, 2: loading, 3: no source.</summary>
        public int NetworkState { get; set; }

        public string CurrentSrc { get; set; } = string.Empty;

        public bool Paused { get; set; } = true;

        public int ReadyReported { get; set; }

        public bool MetadataFired { get; set; }

        public bool DataFired { get; set; }

        public bool CanPlayFired { get; set; }

        public bool ThroughFired { get; set; }

        public bool PlayingFired { get; set; }

        /// <summary>Whether play() was called and its <c>play</c> event, queued as a task, has not fired yet: <c>playing</c> comes after it.</summary>
        public bool PlayEventPending { get; set; }

        public bool EndedFired { get; set; }

        public bool AutoplayHandled { get; set; }

        public MediaErrorCode ErrorCode { get; set; }

        public JsValue? ErrorObject { get; set; }

        public double? DurationReported { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public bool Seeking { get; set; }

        /// <summary>The position set before a session existed to take it, in seconds.</summary>
        public double? InitialTime { get; set; }

        public double Rate { get; set; } = 1;

        public double DefaultRate { get; set; } = 1;

        public double Volume { get; set; } = 1;

        public bool Muted { get; set; }

        public double LastPosition { get; set; } = double.NaN;

        public double LastTimeUpdateMs { get; set; } = double.NegativeInfinity;

        public List<(JsValue Resolve, JsValue Reject)> PlayWaiters { get; } = [];

        public Dictionary<int, JsValue> FrameCallbacks { get; } = [];

        public int NextFrameId { get; set; } = 1;

        public double LastFrame { get; set; } = double.NaN;

        public int Presented { get; set; }
    }

    private readonly Dictionary<ScriptNode, MediaState> _media = [];

    private readonly List<(Task<IMediaSession?> Task, ScriptNode Node, int Token)> _pendingMedia = [];

    private bool _mediaTickScheduled;

    private bool _initialMediaStarted;

    private JsValue _makeMediaError = JsValue.Undefined;

    private JsValue _makeTimeRanges = JsValue.Undefined;

    private static bool IsMedia(ScriptNode node) =>
        node.Kind == ScriptNodeKind.Element && (string.Equals(node.TagName, "video", StringComparison.OrdinalIgnoreCase) || string.Equals(node.TagName, "audio", StringComparison.OrdinalIgnoreCase));

    private static MediaKind KindOf(ScriptNode media) => string.Equals(media.TagName, "audio", StringComparison.OrdinalIgnoreCase) ? MediaKind.Audio : MediaKind.Video;

    private static bool IsVideo(ScriptNode node) =>
        node.Kind == ScriptNodeKind.Element && string.Equals(node.TagName, "video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A picture of the video as it is now, for a canvas to draw: a copy, since a provider may reuse what its frame is made of once the frame is given back.
    /// Null while the video has no picture to show (its readyState is below HAVE_CURRENT_DATA, or its provider has no picture there). A picture in a space
    /// other than sRGB is kept as Display P3 floats (colors outside that gamut survive), which a canvas of any space converts from.
    /// </summary>
    private Image? VideoFrameImage(ScriptNode video)
    {
        if (!_media.TryGetValue(video, out var state) || state.Session is not { } session || session.ReadyState < MediaReadyState.CurrentData)
            return null;

        session.Update(TimeSpan.FromMilliseconds(NowMs));
        using var frame = session.AcquireFrame();
        if (frame?.Image.GetPixels() is not { Width: > 0, Height: > 0 } pixels)
            return null;

        var straight = new byte[pixels.PremultipliedRgba.Length];
        var span = pixels.PremultipliedRgba.Span;
        for (var i = 0; i + 3 < straight.Length; i += 4)
        {
            var alpha = span[i + 3];
            straight[i + 3] = alpha;
            for (var c = 0; c < 3; c++)
                straight[i + c] = alpha == 0 ? (byte)0 : alpha == 255 ? span[i + c] : (byte)Math.Min(255, ((span[i + c] * 255) + (alpha / 2)) / alpha);
        }

        if (frame.ColorSpace != MediaColorSpace.Srgb && Document.ConvertMediaPixelsToDisplayP3(straight, pixels.Width, pixels.Height, frame.ColorSpace) is { } floats
            && Document.CreateWideImage(pixels.Width, pixels.Height, floats) is { } wide)
        {
            CanvasColorSpaces.Tag(wide, "display-p3", floats);
            return wide;
        }

        return Document.DecodeImage(PngWriter.Encode(pixels.Width, pixels.Height, straight));
    }

    /// <summary>Whether the video is from another origin than the document and was not asked for with a crossorigin attribute.</summary>
    private bool VideoTainted(ScriptNode video) =>
        video.GetAttribute("crossorigin") is null && _media.TryGetValue(video, out var state) && state.Session is not null
        && AnyOtherOrigin(video.OwnerDocument.BaseUri, state.CurrentSrc, Document.GetMediaFinalAddress(state.Session));

    // Whether any of the addresses a resource was asked of and read from is of another origin than the document's.
    private bool AnyOtherOrigin(Uri? documentAddress, params string?[] addresses)
    {
        var own = OriginOf(documentAddress);
        foreach (var address in addresses)
        {
            if (address is not null && OriginOf(Uri.TryCreate(address, UriKind.Absolute, out var resource) ? resource : null) is { } origin && !string.Equals(origin, own, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // Where the live player of a video is, for the copy of the document a picture of an element is made from.
    private TimeSpan? LiveMediaPosition(ScriptNode node) =>
        _media.TryGetValue(node, out var state) && state.Session is { HasVideo: true } session ? session.Position : null;

    private MediaState MediaStateOf(ScriptNode node)
    {
        if (!_media.TryGetValue(node, out var state))
            _media[node] = state = new MediaState { Muted = node.GetAttribute("muted") is not null };

        return state;
    }

    // The element's own resource: its src, else the first source child (from a position among them) whose type the provider can play. Next is where the one after it begins.
    private (string? Src, string? Type, ScriptNode? Source, int Next) SelectMediaSource(ScriptNode media, int from = 0)
    {
        var src = media.GetAttribute("src");
        if (src is not null)
            return string.IsNullOrWhiteSpace(src) ? (null, null, null, 0) : (src, null, null, int.MaxValue);

        var sources = media.ChildElements.Where(IsSource).ToList();
        for (var i = from; i < sources.Count; i++)
        {
            var child = sources[i];
            var childSrc = child.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(childSrc))
                continue;

            var type = child.GetAttribute("type");
            if (!string.IsNullOrWhiteSpace(type) && Document.CanPlayMediaType(type).Length == 0)
                continue;

            return (childSrc, string.IsNullOrWhiteSpace(type) ? null : type, child, i + 1);
        }

        return (null, null, null, sources.Count);
    }

    // The media elements the page began with start selecting their resource.
    private void StartInitialMedia()
    {
        if (_initialMediaStarted)
            return;

        _initialMediaStarted = true;
        foreach (var media in Document.GetElementsByTagName("video").Concat(Document.GetElementsByTagName("audio")))
        {
            // A script that read a property of the element first (readyState) has made its state already, and the element still loads: it is a token, not the
            // state, that says a load was started.
            if ((!_media.TryGetValue(media, out var state) || state.Token == 0) && SelectMediaSource(media).Src is not null)
                StartMediaLoad(media);
        }
    }

    // The mutations that start (or restart) an element's resource selection: its src, a source child, the element entering the document.
    private void HandleMediaMutation(ScriptMutation mutation)
    {
        var target = mutation.Target;
        if (mutation.Kind == ScriptMutationKind.Attributes && mutation.AttributeName is { } name)
        {
            var lower = name.ToLowerInvariant();
            if (lower == "src" && IsMedia(target))
                StartMediaLoad(target);
            else if (lower is "src" or "type" && IsSource(target) && target.Parent is { } holder && IsMedia(holder) && holder.GetAttribute("src") is null && IdleOrFailed(holder))
                StartMediaLoad(holder);

            return;
        }

        if (mutation.Kind != ScriptMutationKind.ChildList)
            return;

        foreach (var added in mutation.Added ?? [])
        {
            if (IsSource(added) && IsMedia(target) && target.GetAttribute("src") is null && IdleOrFailed(target))
                StartMediaLoad(target);
            else if (IsMedia(added) && SelectMediaSource(added).Src is not null && !_media.ContainsKey(added))
                StartMediaLoad(added);
        }
    }

    private bool IdleOrFailed(ScriptNode media) => !_media.TryGetValue(media, out var state) || (state.Session is null && state.NetworkState is 0 or 3);

    private void DisposeSession(MediaState state)
    {
        var session = state.Session;
        state.Session = null;
        if (session is null)
            return;

        try
        {
            session.Dispose();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The provider's code: a session that fails to close has nothing more to give.
        }
    }

    private void RejectPlayWaiters(MediaState state, string name, string message)
    {
        var waiters = state.PlayWaiters.ToArray();
        state.PlayWaiters.Clear();
        foreach (var (_, reject) in waiters)
            reject.Call(JsValue.Undefined, [DomError(name, message).Error]);
    }

    // The resource selection algorithm, started by a src, a source child or load().
    private void StartMediaLoad(ScriptNode media)
    {
        var state = MediaStateOf(media);
        state.Token++;
        var token = state.Token;
        var hadSession = state.Session is not null;
        DisposeSession(state);

        // load() aborts what was there: it is paused again, and its position and flags are those of a new resource.
        state.Paused = true;
        state.PlayEventPending = false;
        state.ReadyReported = 0;
        state.MetadataFired = state.DataFired = state.CanPlayFired = state.ThroughFired = state.PlayingFired = state.EndedFired = state.AutoplayHandled = false;
        state.ErrorCode = MediaErrorCode.None;
        state.ErrorObject = null;
        state.DurationReported = null;
        state.Width = state.Height = 0;
        state.Seeking = false;
        state.LastPosition = double.NaN;
        state.LastFrame = double.NaN;
        state.CurrentSrc = string.Empty;
        RejectPlayWaiters(state, "AbortError", "The play() request was interrupted by a new load request.");

        if (hadSession)
            QueueMediaEvent(media, token, "emptied");

        var (src, type, firstSource, next) = SelectMediaSource(media);
        if (src is null)
        {
            state.NetworkState = 0;
            return;
        }

        if (!Document.HasMediaProvider)
        {
            FailMedia(media, state, token, src);
            return;
        }

        state.NextSource = next;
        state.CurrentSourceNode = firstSource;
        OpenMediaCandidate(media, state, token, src, type);
    }

    // Asks the provider for one candidate resource of an element.
    private void OpenMediaCandidate(ScriptNode media, MediaState state, int token, string src, string? type)
    {
        state.NetworkState = 2;
        state.CurrentSrc = ResolveAddress(media, src);
        QueueMediaEvent(media, token, "loadstart");
        var crossOrigin = media.GetAttribute("crossorigin") is { } cors ? (string.Equals(cors, "use-credentials", StringComparison.OrdinalIgnoreCase) ? "use-credentials" : "anonymous") : null;
        var preload = media.GetAttribute("preload")?.ToLowerInvariant() switch { "none" => MediaPreload.None, "auto" or "" => MediaPreload.Auto, _ => MediaPreload.Metadata };
        _pendingMedia.Add((Document.OpenMediaAsync(src, KindOf(media), type, crossOrigin, state.Muted, media.GetAttribute("loop") is not null, preload).AsTask(), media, token));
    }

    // The source cannot be played: no provider, or the provider declined it.
    private void FailMedia(ScriptNode media, MediaState state, int token, string src)
    {
        state.NetworkState = 3;
        state.CurrentSrc = src;
        state.ErrorCode = MediaErrorCode.SourceNotSupported;
        RejectPlayWaiters(state, "NotSupportedError", "The element has no supported sources.");
        AddTimer(Js.Function(Engine, "mediaError", 0, (_, _) =>
        {
            if (_media.TryGetValue(media, out var current) && current.Token == token)
                FireEvent(Wrap(media), "error", bubbles: false, cancelable: false);

            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: false);
    }

    // An error event at a source element whose resource could not be opened, queued as a task: it fires unless the media element started over since.
    private void QueueSourceError(ScriptNode source, ScriptNode media, int token) =>
        AddTimer(Js.Function(Engine, "mediaSourceError", 0, (_, _) =>
        {
            if (_media.TryGetValue(media, out var current) && current.Token == token)
                FireEvent(Wrap(source), "error", bubbles: false, cancelable: false);

            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: false);

    // An event of the element's, queued as a task: it fires unless the element started over since.
    private void QueueMediaEvent(ScriptNode media, int token, string type) =>
        AddTimer(Js.Function(Engine, "media-" + type, 0, (_, _) =>
        {
            if (_media.TryGetValue(media, out var current) && current.Token == token)
                FireEvent(Wrap(media), type, bubbles: false, cancelable: false);

            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: false);

    // Waits for the sessions asked for, and queues each answer as a zero-delay timer, like a network answer.
    private async ValueTask DeliverMediaAsync()
    {
        var batch = _pendingMedia.ToArray();
        _pendingMedia.Clear();
        foreach (var (task, node, token) in batch)
        {
            IMediaSession? session = null;
            try
            {
                session = await task;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
            }

            AddTimer(Js.Function(Engine, "mediaOpened", 0, (_, _) =>
            {
                ApplyOpened(node, token, session);
                return JsValue.Undefined;
            }), 0, [], repeat: false, frame: false);
        }
    }

    private void ApplyOpened(ScriptNode media, int token, IMediaSession? session)
    {
        if (!_media.TryGetValue(media, out var state) || state.Token != token)
        {
            session?.Dispose();
            return;
        }

        if (session is null)
        {
            // An element with source children goes on to the next one that fails to open: each failure is an error event on its source, and only when none is left does
            // the element give up (without an error of its own, which the standard keeps for a src that fails).
            if (media.GetAttribute("src") is null)
            {
                var failed = state.CurrentSourceNode;
                if (failed is not null)
                    QueueSourceError(failed, media, token);

                var (src, type, source, next) = SelectMediaSource(media, state.NextSource);
                if (src is not null)
                {
                    state.NextSource = next;
                    state.CurrentSourceNode = source;
                    OpenMediaCandidate(media, state, token, src, type);
                    return;
                }

                state.CurrentSourceNode = null;
                state.NetworkState = 3;

                // After the last source's error event, as a task of its own.
                AddTimer(Js.Function(Engine, "mediaNoSource", 0, (_, _) =>
                {
                    if (_media.TryGetValue(media, out var current) && current.Token == token)
                        RejectPlayWaiters(current, "NotSupportedError", "The element has no supported sources.");

                    return JsValue.Undefined;
                }), 0, [], repeat: false, frame: false);
                return;
            }

            FailMedia(media, state, token, media.GetAttribute("src") ?? string.Empty);
            return;
        }

        state.Session = session;
        state.NetworkState = 1;
        session.Volume = state.Volume;
        session.Muted = state.Muted;
        session.PlaybackRate = state.Rate;
        session.Loop = media.GetAttribute("loop") is not null;
        if (state.InitialTime is { } start)
        {
            state.InitialTime = null;
            session.Seek(TimeSpan.FromSeconds(start));
        }

        SyncMedia(media, state);
        EnsureMediaTick();
    }

    // The session's state as events: what has become true since the last time, in the order the standard fires it.
    private void SyncMedia(ScriptNode media, MediaState state)
    {
        if (state.Session is not { } session)
            return;

        var token = state.Token;
        bool Current() => _media.TryGetValue(media, out var s) && s.Token == token && ReferenceEquals(s.Session, session);
        void Fire(string type) => FireEvent(Wrap(media), type, bubbles: false, cancelable: false);

        session.Update(TimeSpan.FromMilliseconds(NowMs));
        session.Loop = media.GetAttribute("loop") is not null;
        var ready = session.ReadyState;

        if (session.Error != MediaErrorCode.None && state.ErrorCode == MediaErrorCode.None)
        {
            state.ErrorCode = session.Error;
            state.ErrorObject = null;
            state.NetworkState = 3;
            RejectPlayWaiters(state, session.Error == MediaErrorCode.SourceNotSupported ? "NotSupportedError" : "AbortError", "The media could not be played.");
            Fire("error");
            return;
        }

        if (ready >= MediaReadyState.Metadata && !state.MetadataFired)
        {
            state.MetadataFired = true;
            state.ReadyReported = Math.Max(state.ReadyReported, 1);
            state.DurationReported = session.Duration?.TotalSeconds ?? double.PositiveInfinity;
            Fire("durationchange");
            if (!Current()) return;
            Fire("loadedmetadata");
            if (!Current()) return;
            if (KindOf(media) == MediaKind.Video)
            {
                (state.Width, state.Height) = (session.VideoWidth, session.VideoHeight);
                Fire("resize");
                if (!Current()) return;
            }
        }
        else if (state.MetadataFired)
        {
            var duration = session.Duration?.TotalSeconds ?? double.PositiveInfinity;
            if (state.DurationReported != duration)
            {
                state.DurationReported = duration;
                Fire("durationchange");
                if (!Current()) return;
            }

            if (KindOf(media) == MediaKind.Video && (state.Width, state.Height) != (session.VideoWidth, session.VideoHeight))
            {
                (state.Width, state.Height) = (session.VideoWidth, session.VideoHeight);
                Fire("resize");
                if (!Current()) return;
            }
        }

        if (ready >= MediaReadyState.CurrentData && !state.DataFired)
        {
            state.DataFired = true;
            state.ReadyReported = Math.Max(state.ReadyReported, 2);
            Fire("loadeddata");
            if (!Current()) return;
        }

        if (ready >= MediaReadyState.FutureData && !state.CanPlayFired)
        {
            state.CanPlayFired = true;
            state.ReadyReported = Math.Max(state.ReadyReported, 3);
            Fire("canplay");
            if (!Current()) return;
        }

        if (ready >= MediaReadyState.FutureData && !state.Paused && !state.PlayingFired && !state.PlayEventPending)
        {
            state.PlayingFired = true;
            ResolvePlayWaiters(state);
            Fire("playing");
            if (!Current()) return;
        }

        if (ready >= MediaReadyState.EnoughData && !state.ThroughFired)
        {
            state.ThroughFired = true;
            state.ReadyReported = 4;
            Fire("canplaythrough");
            if (!Current()) return;

            if (state.Paused && !state.AutoplayHandled && media.GetAttribute("autoplay") is not null)
            {
                state.AutoplayHandled = true;
                BeginPlaying(media, state, promise: false);
                if (!Current()) return;
            }
        }

        state.ReadyReported = (int)ready;

        if (state.Seeking && ready >= MediaReadyState.CurrentData)
        {
            state.Seeking = false;
            Fire("timeupdate");
            if (!Current()) return;
            Fire("seeked");
            if (!Current()) return;
        }

        var position = session.Position.TotalSeconds;
        if (!state.Paused && position != state.LastPosition && NowMs - state.LastTimeUpdateMs >= 250)
        {
            state.LastTimeUpdateMs = NowMs;
            Fire("timeupdate");
            if (!Current()) return;
        }

        state.LastPosition = position;

        if (session.Ended)
        {
            if (!state.EndedFired && !state.Paused)
            {
                state.EndedFired = true;
                state.Paused = true;
                Fire("timeupdate");
                if (!Current()) return;
                Fire("pause");
                if (!Current()) return;
                RejectPlayWaiters(state, "AbortError", "The play() request was interrupted because playback ended.");
                Fire("ended");
                if (!Current()) return;
            }
        }
        else
        {
            state.EndedFired = false;
        }

        if (state.FrameCallbacks.Count > 0 && ready >= MediaReadyState.CurrentData && position != state.LastFrame)
        {
            state.LastFrame = position;
            state.Presented++;
            var callbacks = state.FrameCallbacks.Values.ToArray();
            state.FrameCallbacks.Clear();
            var metadata = Js.NewObject(Engine);
            metadata.Set("presentationTime", Js.Num(NowMs));
            metadata.Set("expectedDisplayTime", Js.Num(NowMs));
            metadata.Set("width", Js.Num(session.VideoWidth));
            metadata.Set("height", Js.Num(session.VideoHeight));
            metadata.Set("mediaTime", Js.Num(position));
            metadata.Set("presentedFrames", Js.Num(state.Presented));
            foreach (var callback in callbacks)
                Call(callback, JsValue.Undefined, Js.Num(NowMs), metadata);
        }
    }

    private void ResolvePlayWaiters(MediaState state)
    {
        var waiters = state.PlayWaiters.ToArray();
        state.PlayWaiters.Clear();
        foreach (var (resolve, _) in waiters)
            resolve.Call(JsValue.Undefined, []);
    }

    private bool MediaNeedsTick() =>
        _media.Values.Any(s => s.Session is { Error: MediaErrorCode.None } session && (!s.Paused || s.ReadyReported < 4 || s.Seeking || s.FrameCallbacks.Count > 0 || s.PlayWaiters.Count > 0 || s.Rate != session.PlaybackRate));

    // While anything is playing or loading the document's clock ticks the sessions, so state that changes becomes events at the right virtual time.
    private void EnsureMediaTick()
    {
        if (_mediaTickScheduled || !MediaNeedsTick())
            return;

        _mediaTickScheduled = true;
        AddTimer(Js.Function(Engine, "mediaTick", 0, (_, _) =>
        {
            _mediaTickScheduled = false;

            // The clock's own work is not the script's: it does not count against the callback budget.
            _callbacks--;
            foreach (var (node, state) in _media.ToArray())
                SyncMedia(node, state);

            EnsureMediaTick();
            return JsValue.Undefined;
        }), 20, [], repeat: false, frame: false);
    }

    internal void DisposeMedia()
    {
        foreach (var state in _media.Values)
        {
            state.Token++;
            DisposeSession(state);
        }

        _pendingMedia.Clear();
    }

    // play(): the element is no longer paused; its play event is queued, playing follows it, and a pending promise settles when playback has started.
    private void BeginPlaying(ScriptNode media, MediaState state, bool promise)
    {
        var token = state.Token;
        if (state.Session is { } ended)
        {
            ended.Update(TimeSpan.FromMilliseconds(NowMs));
            if (ended.Ended)
                ended.Seek(TimeSpan.Zero);
        }

        if (state.Paused)
        {
            state.Paused = false;
            state.PlayingFired = false;
            state.PlayEventPending = true;
            state.Session?.Play();
            AddTimer(Js.Function(Engine, "media-play", 0, (_, _) =>
            {
                if (!_media.TryGetValue(media, out var current) || current.Token != token)
                    return JsValue.Undefined;

                current.PlayEventPending = false;
                FireEvent(Wrap(media), "play", bubbles: false, cancelable: false);
                if (!_media.TryGetValue(media, out current) || current.Token != token)
                    return JsValue.Undefined;

                if (current.Session is { } session && session.ReadyState < MediaReadyState.FutureData)
                    FireEvent(Wrap(media), "waiting", bubbles: false, cancelable: false);

                SyncMedia(media, current);
                EnsureMediaTick();
                return JsValue.Undefined;
            }), 0, [], repeat: false, frame: false);
        }

        EnsureMediaTick();
    }

    private JsValue MakeMediaError(int code, string message) =>
        ((ObjectInstance)_makeMediaError).Call(JsValue.Undefined, [Js.Num(code), Js.Str(message)]);

    private JsValue MakeTimeRanges(IEnumerable<(double Start, double End)> ranges) =>
        ((ObjectInstance)_makeTimeRanges).Call(JsValue.Undefined, [Js.Array(Engine, [.. ranges.Select(r => Js.Array(Engine, [Js.Num(r.Start), Js.Num(r.End)]))])]);

    private static string MediaErrorMessage(MediaErrorCode code) => code switch
    {
        MediaErrorCode.Aborted => "The fetching process for the media resource was aborted by the user agent at the user's request.",
        MediaErrorCode.Network => "A network error occurred while fetching the media resource.",
        MediaErrorCode.Decode => "An error occurred while decoding the media resource.",
        _ => "The source is not supported.",
    };

    private void InstallMedia()
    {
        _makeMediaError = Engine.Evaluate("""
            (function (global) {
                function MediaError() { throw new TypeError("Illegal constructor"); }
                var codes = { MEDIA_ERR_ABORTED: 1, MEDIA_ERR_NETWORK: 2, MEDIA_ERR_DECODE: 3, MEDIA_ERR_SRC_NOT_SUPPORTED: 4 };
                var states = new WeakMap();
                Object.keys(codes).forEach(function (name) {
                    Object.defineProperty(MediaError, name, { value: codes[name], enumerable: true });
                    Object.defineProperty(MediaError.prototype, name, { value: codes[name], enumerable: true });
                });
                function read(error, field) {
                    var state = states.get(error);
                    if (!state) throw new TypeError("Illegal invocation");
                    return state[field];
                }
                Object.defineProperty(MediaError.prototype, "code", { get: function () { return read(this, "code"); }, enumerable: true, configurable: true });
                Object.defineProperty(MediaError.prototype, "message", { get: function () { return read(this, "message"); }, enumerable: true, configurable: true });
                Object.defineProperty(MediaError.prototype, Symbol.toStringTag, { value: "MediaError", configurable: true });
                Object.defineProperty(global, "MediaError", { value: MediaError, writable: true, configurable: true });
                return function make(code, message) {
                    var error = Object.create(MediaError.prototype);
                    states.set(error, { code: code, message: message });
                    return error;
                };
            })
            """).Call(JsValue.Undefined, [Engine.Global]);

        _makeTimeRanges = Engine.Evaluate("""
            (function (global) {
                function TimeRanges() { throw new TypeError("Illegal constructor"); }
                var states = new WeakMap();
                function ranges(object) {
                    var pairs = states.get(object);
                    if (!pairs) throw new TypeError("Illegal invocation");
                    return pairs;
                }
                function check(object, index, name, arity) {
                    var pairs = ranges(object);
                    if (arity < 1) throw new TypeError("Failed to execute '" + name + "' on 'TimeRanges': 1 argument required, but only 0 present.");
                    var i = index >>> 0;
                    if (i >= pairs.length) throw new DOMException("The index provided (" + i + ") is greater than or equal to the maximum bound (" + pairs.length + ").", "IndexSizeError");
                    return pairs[i];
                }
                Object.defineProperty(TimeRanges.prototype, "length", { get: function () { return ranges(this).length; }, enumerable: true, configurable: true });
                Object.defineProperty(TimeRanges.prototype, "start", { value: function start(index) { return check(this, index, "start", arguments.length)[0]; }, writable: true, enumerable: true, configurable: true });
                Object.defineProperty(TimeRanges.prototype, "end", { value: function end(index) { return check(this, index, "end", arguments.length)[1]; }, writable: true, enumerable: true, configurable: true });
                Object.defineProperty(TimeRanges.prototype, Symbol.toStringTag, { value: "TimeRanges", configurable: true });
                Object.defineProperty(global, "TimeRanges", { value: TimeRanges, writable: true, configurable: true });
                return function make(pairs) {
                    var object = Object.create(TimeRanges.prototype);
                    states.set(object, pairs);
                    return object;
                };
            })
            """).Call(JsValue.Undefined, [Engine.Global]);

        foreach (var tag in new[] { "video", "audio" })
        {
            var prototype = ElementPrototypeFor(tag);
            MediaState St(JsValue self) => MediaStateOf(Sn(self));

            // What a session reports is read as of the document's time now.
            IMediaSession? Live(MediaState state)
            {
                state.Session?.Update(TimeSpan.FromMilliseconds(NowMs));
                return state.Session;
            }

            foreach (var (name, value) in new (string, int)[]
            {
                ("NETWORK_EMPTY", 0), ("NETWORK_IDLE", 1), ("NETWORK_LOADING", 2), ("NETWORK_NO_SOURCE", 3),
                ("HAVE_NOTHING", 0), ("HAVE_METADATA", 1), ("HAVE_CURRENT_DATA", 2), ("HAVE_FUTURE_DATA", 3), ("HAVE_ENOUGH_DATA", 4),
            })
            {
                prototype.DefineOwnProperty(name, new PropertyDescriptor(Js.Num(value), writable: false, enumerable: true, configurable: false));
            }

            Js.Accessor(Engine, prototype, "networkState", self => Js.Num(St(self).NetworkState));
            Js.Accessor(Engine, prototype, "readyState", self => Js.Num(St(self).ReadyReported));
            Js.Accessor(Engine, prototype, "paused", self => Js.Bool(St(self).Paused));
            Js.Accessor(Engine, prototype, "ended", self =>
            {
                var state = St(self);
                return Js.Bool(Live(state) is { Ended: true, Loop: false } && state.Seeking == false);
            });
            Js.Accessor(Engine, prototype, "seeking", self => Js.Bool(St(self).Seeking));
            Js.Accessor(Engine, prototype, "duration", self =>
            {
                var state = St(self);
                return state.MetadataFired && Live(state) is { } session ? Js.Num(session.Duration?.TotalSeconds ?? double.PositiveInfinity) : Js.Num(double.NaN);
            });
            Js.Accessor(Engine, prototype, "currentSrc", self => Js.Str(St(self).CurrentSrc));
            Js.Accessor(Engine, prototype, "error", self =>
            {
                var state = St(self);
                if (state.ErrorCode == MediaErrorCode.None)
                    return JsValue.Null;

                return state.ErrorObject ??= MakeMediaError((int)state.ErrorCode, MediaErrorMessage(state.ErrorCode));
            });
            Js.Accessor(Engine, prototype, "currentTime", self =>
            {
                var state = St(self);
                return Js.Num(Live(state) is { } session ? session.Position.TotalSeconds : state.InitialTime ?? 0);
            }, (self, value) =>
            {
                var seconds = TypeConverter.ToNumber(value);
                if (!double.IsFinite(seconds))
                    throw TypeError("The provided double value is non-finite.");

                var media = Sn(self);
                var state = St(self);
                if (state.Session is not { } session)
                {
                    state.InitialTime = Math.Max(0, seconds);
                    return;
                }

                state.Seeking = true;
                session.Update(TimeSpan.FromMilliseconds(NowMs));
                session.Seek(TimeSpan.FromSeconds(Math.Max(0, seconds)));
                state.EndedFired = false;
                QueueMediaEvent(media, state.Token, "seeking");
                EnsureMediaTick();
            });
            Js.Accessor(Engine, prototype, "muted", self => Js.Bool(St(self).Muted), (self, value) =>
            {
                var state = St(self);
                var muted = TypeConverter.ToBoolean(value);
                if (state.Muted == muted)
                    return;

                state.Muted = muted;
                if (state.Session is { } session)
                    session.Muted = muted;

                QueueMediaEvent(Sn(self), state.Token, "volumechange");
            });
            Js.Accessor(Engine, prototype, "volume", self => Js.Num(St(self).Volume), (self, value) =>
            {
                var volume = TypeConverter.ToNumber(value);
                if (!double.IsFinite(volume))
                    throw TypeError("The provided double value is non-finite.");
                if (volume < 0 || volume > 1)
                    throw DomError("IndexSizeError", "The volume provided is outside the range [0, 1].");

                var state = St(self);
                if (state.Volume == volume)
                    return;

                state.Volume = volume;
                if (state.Session is { } session)
                    session.Volume = volume;

                QueueMediaEvent(Sn(self), state.Token, "volumechange");
            });
            Js.Accessor(Engine, prototype, "playbackRate", self => Js.Num(St(self).Rate), (self, value) =>
            {
                var rate = TypeConverter.ToNumber(value);
                if (!double.IsFinite(rate))
                    throw TypeError("The provided double value is non-finite.");

                var state = St(self);
                if (state.Rate == rate)
                    return;

                state.Rate = rate;
                if (state.Session is { } session)
                {
                    session.Update(TimeSpan.FromMilliseconds(NowMs));
                    session.PlaybackRate = rate;
                }

                QueueMediaEvent(Sn(self), state.Token, "ratechange");
            });
            Js.Accessor(Engine, prototype, "defaultPlaybackRate", self => Js.Num(St(self).DefaultRate), (self, value) =>
            {
                var rate = TypeConverter.ToNumber(value);
                if (!double.IsFinite(rate))
                    throw TypeError("The provided double value is non-finite.");

                var state = St(self);
                if (state.DefaultRate == rate)
                    return;

                state.DefaultRate = rate;
                QueueMediaEvent(Sn(self), state.Token, "ratechange");
            });
            Js.Accessor(Engine, prototype, "seekable", self => St(self) is { MetadataFired: true } state && Live(state) is { Duration: { } length } ? MakeTimeRanges([(0, length.TotalSeconds)]) : MakeTimeRanges([]));
            Js.Accessor(Engine, prototype, "buffered", self => St(self) is { MetadataFired: true } state && Live(state) is { Duration: { } length } ? MakeTimeRanges([(0, length.TotalSeconds)]) : MakeTimeRanges([]));
            Js.Accessor(Engine, prototype, "played", self => St(self) is { MetadataFired: true, Paused: false } state && Live(state) is { } session && session.Position > TimeSpan.Zero ? MakeTimeRanges([(0, session.Position.TotalSeconds)]) : MakeTimeRanges([]));
            Js.Accessor(Engine, prototype, "srcObject", _ => JsValue.Null, (_, _) => { });
            Js.Accessor(Engine, prototype, "preservesPitch", _ => JsBoolean.True, (_, _) => { });
            Js.Method(Engine, prototype, "canPlayType", 1, (_, args) =>
            {
                if (args.Length == 0)
                    throw TypeError("Failed to execute 'canPlayType' on 'HTMLMediaElement': 1 argument required, but only 0 present.");

                var answer = Document.CanPlayMediaType(Js.AsText(args[0]));
                return Js.Str(answer is "probably" or "maybe" ? answer : "");
            });
            Js.Method(Engine, prototype, "load", 0, (self, _) =>
            {
                StartMediaLoad(Sn(self));
                return JsValue.Undefined;
            });
            Js.Method(Engine, prototype, "pause", 0, (self, _) =>
            {
                var media = Sn(self);
                var state = St(self);
                if (state.Paused)
                    return JsValue.Undefined;

                state.Paused = true;
                state.PlayingFired = false;
                state.Session?.Update(TimeSpan.FromMilliseconds(NowMs));
                state.Session?.Pause();
                RejectPlayWaiters(state, "AbortError", "The play() request was interrupted by a call to pause().");
                QueueMediaEvent(media, state.Token, "timeupdate");
                QueueMediaEvent(media, state.Token, "pause");
                return JsValue.Undefined;
            });
            Js.Method(Engine, prototype, "play", 0, (self, _) =>
            {
                var media = Sn(self);
                var state = St(self);
                if (state.ErrorCode == MediaErrorCode.SourceNotSupported || (state.Session is null && state.NetworkState is 0 or 3))
                    return Engine.Evaluate("(function () { return Promise.reject(new DOMException('The element has no supported sources.', 'NotSupportedError')); })").Call(JsValue.Undefined, []);

                JsValue resolveFn = JsValue.Undefined, rejectFn = JsValue.Undefined;
                var promise = Engine.Evaluate("(function (store) { return new Promise(function (resolve, reject) { store(resolve, reject); }); })")
                    .Call(JsValue.Undefined, [Js.Function(Engine, "store", 2, (_, a) =>
                    {
                        (resolveFn, rejectFn) = (a[0], a[1]);
                        return JsValue.Undefined;
                    })]);
                state.PlayWaiters.Add((resolveFn, rejectFn));
                BeginPlaying(media, state, promise: true);
                return promise;
            });
            Js.Method(Engine, prototype, "getVideoPlaybackQuality", 0, (self, _) =>
            {
                var quality = Js.NewObject(Engine);
                quality.Set("creationTime", Js.Num(NowMs));
                quality.Set("totalVideoFrames", Js.Num(St(self).Presented));
                quality.Set("droppedVideoFrames", Js.Num(0));
                quality.Set("corruptedVideoFrames", Js.Num(0));
                return quality;
            });

            if (tag == "video")
            {
                Js.Accessor(Engine, prototype, "videoWidth", self => Js.Num(St(self).MetadataFired ? Live(St(self))?.VideoWidth ?? 0 : 0));
                Js.Accessor(Engine, prototype, "videoHeight", self => Js.Num(St(self).MetadataFired ? Live(St(self))?.VideoHeight ?? 0 : 0));
                Js.Method(Engine, prototype, "requestVideoFrameCallback", 1, (self, args) =>
                {
                    if (!Js.IsCallable(Js.Arg(args, 0)))
                        throw TypeError("Failed to execute 'requestVideoFrameCallback' on 'HTMLVideoElement': The callback provided as parameter 1 is not a function.");

                    var state = St(self);
                    var id = state.NextFrameId++;
                    state.FrameCallbacks[id] = args[0];
                    EnsureMediaTick();
                    return Js.Num(id);
                });
                Js.Method(Engine, prototype, "cancelVideoFrameCallback", 1, (self, args) =>
                {
                    St(self).FrameCallbacks.Remove((int)TypeConverter.ToNumber(Js.Arg(args, 0)));
                    return JsValue.Undefined;
                });
            }
        }
    }
}
