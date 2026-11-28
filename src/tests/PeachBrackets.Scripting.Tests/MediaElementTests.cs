using PeachBrackets.Media;
using PeachBrackets.Scripting;
using PeachDrawing.Core;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The <c>audio</c> and <c>video</c> element API over a media provider, on the script's virtual clock.</summary>
public class MediaElementTests
{
    /// <summary>A picture of one color.</summary>
    internal sealed class SolidImage(int width, int height, (byte R, byte G, byte B) color) : Image
    {
        public override double Width => width;

        public override double Height => height;

        public override bool Interpolate { get; set; }

        public override void Dispose()
        {
        }

        public override PixelBuffer? GetPixels()
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (color.R, color.G, color.B, 255);

            return new PixelBuffer(width, height, pixels);
        }
    }

    /// <summary>A session that plays by the clock the document gives it, as a deterministic provider's does.</summary>
    internal sealed class FakeSession(double duration, int width, int height, MediaKind kind) : IMediaSession
    {
        private TimeSpan? _last;

        private static int s_disposals;

        public bool Disposed { get; private set; }

        /// <summary>The order sessions were disposed in, across all of them; 0 until this one is.</summary>
        public int DisposedOrder { get; private set; }

        public MediaReadyState Ready { get; set; } = MediaReadyState.EnoughData;

        /// <summary>The color of the picture at a position.</summary>
        public Func<TimeSpan, (byte R, byte G, byte B)> Color { get; set; } = _ => (255, 0, 0);

        public MediaColorSpace Space { get; set; }

        public int FramesLeased { get; private set; }

        public int FramesReleased { get; private set; }

        public MediaKind Kind => kind;

        public MediaReadyState ReadyState => Ready;

        public TimeSpan? Duration => TimeSpan.FromSeconds(duration);

        public bool HasVideo => kind == MediaKind.Video;

        public int VideoWidth => width;

        public int VideoHeight => height;

        public TimeSpan Position { get; private set; }

        public bool IsPlaying { get; private set; }

        public bool Ended { get; private set; }

        public MediaErrorCode Error { get; set; }

        public double PlaybackRate { get; set; } = 1;

        public double Volume { get; set; } = 1;

        public bool Muted { get; set; }

        public bool Loop { get; set; }

        public event EventHandler<MediaChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Play() => IsPlaying = true;

        public void Pause() => IsPlaying = false;

        public void Seek(TimeSpan position)
        {
            Position = position > TimeSpan.FromSeconds(duration) ? TimeSpan.FromSeconds(duration) : position;
            Ended = false;
        }

        public void Update(TimeSpan documentTime)
        {
            if (_last is { } last && IsPlaying && !Ended)
            {
                Position += TimeSpan.FromTicks((long)((documentTime - last).Ticks * PlaybackRate));
                if (Position >= TimeSpan.FromSeconds(duration))
                {
                    if (Loop)
                    {
                        Position = TimeSpan.Zero;
                    }
                    else
                    {
                        Position = TimeSpan.FromSeconds(duration);
                        Ended = true;
                        IsPlaying = false;
                    }
                }
            }

            _last = documentTime;
        }

        public MediaFrame? AcquireFrame()
        {
            FramesLeased++;
            return new MediaFrame(new SolidImage(width, height, Color(Position)), Position, () => FramesReleased++) { ColorSpace = Space };
        }

        public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            LastRequestedPosition = position;
            return new(new MediaFrame(new SolidImage(width, height, Color(position)), position) { ColorSpace = Space });
        }

        /// <summary>Where the last still of the media was asked for.</summary>
        public TimeSpan? LastRequestedPosition { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            DisposedOrder = Interlocked.Increment(ref s_disposals);
        }
    }

    internal sealed class FakeProvider(double duration = 10) : IMediaProvider
    {
        public List<MediaRequest> Requests { get; } = [];

        public List<FakeSession> Sessions { get; } = [];

        public Func<TimeSpan, (byte R, byte G, byte B)> Color { get; set; } = _ => (255, 0, 0);

        public MediaColorSpace Space { get; set; }

        public string CanPlayType(string mimeType) => mimeType.StartsWith("video/webm", StringComparison.OrdinalIgnoreCase) ? "probably" : mimeType.StartsWith("video/ogg", StringComparison.OrdinalIgnoreCase) ? "maybe" : string.Empty;

        public ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Address.AbsolutePath.EndsWith(".bad", StringComparison.Ordinal))
                return new ValueTask<IMediaSession?>((IMediaSession?)null);

            var session = new FakeSession(request.Address.AbsolutePath.Contains("short", StringComparison.Ordinal) ? 2 : duration, 40, 20, request.Kind) { Color = Color, Space = Space };
            Sessions.Add(session);
            return new ValueTask<IMediaSession?>(session);
        }
    }

    internal static async Task<(List<string> Log, HtmlDocument Document)> RunAsync(string body, IMediaProvider? provider)
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1), MediaProvider = provider };
        options.SetMargins(0);
        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap($"<script>{body}</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return (logger.Texts, document);
    }

    private const string Events = "var events = []; ['loadstart','durationchange','loadedmetadata','resize','loadeddata','canplay','canplaythrough','play','playing','pause','ended','seeking','seeked','error','emptied','waiting','volumechange','ratechange'].forEach(function (t) { v.addEventListener(t, function () { events.push(t); }); });";

    [Fact]
    public async Task AVideoInTheMarkup_StillLoads_WhenAScriptReadSomethingOfItFirst()
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1), MediaProvider = new FakeProvider() };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap("""
            <video id="v" src="https://media.test/a.webm"></video>
            <script>
            var before = v.readyState;
            v.oncanplay = function () { Log.info('canplay after readyState ' + before + ', now ' + v.readyState); };
            setTimeout(function () {}, 500);
            </script>
            """), context, options);

        Assert.Empty(logger.Errors);
        Assert.Equal(["canplay after readyState 0, now 3"], logger.Texts);
    }

    [Fact]
    public async Task APictureOfAnElement_ShowsAVideoInsideItAtTheLivePosition()
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        var provider = new FakeProvider { Color = t => t >= TimeSpan.FromSeconds(3) ? ((byte)0, (byte)255, (byte)0) : ((byte)255, (byte)0, (byte)0) };
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1), MediaProvider = provider };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap("""
            <canvas id="k" width="60" height="40" content="drawable"><div id="kid" drawable style="width:40px;height:20px"><video id="v" src="https://media.test/a.webm" style="width:40px;height:20px"></video></div></canvas>
            <script>
            var k = document.getElementById('k'), kg = k.getContext('2d');
            v.oncanplay = function () { v.currentTime = 3; };
            v.onseeked = function () {
                k.addEventListener('paint', function () {
                    kg.drawElementImage(kid, 0, 0);
                    Log.info(Array.from(kg.getImageData(10, 10, 1, 1).data).join(','));
                }, { once: true });
                k.requestPaint();
            };
            setTimeout(function () {}, 500);
            </script>
            """), context, options);

        Assert.Empty(logger.Errors);
        // The copy of the document the picture is made from plays the video from where the live one is (3 s), so the picture shows that moment, not the poster or the start.
        Assert.Equal(["0,255,0,255"], logger.Texts);
        Assert.Contains(provider.Sessions, session => session.LastRequestedPosition == TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ASourceThatFailsToOpen_FiresErrorAtItself_AndTheNextOneIsTried()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video'); var seen = [];
            v.addEventListener('error', function () { seen.push('video-error'); });
            ['a.bad', 'b.bad', 'c.webm'].forEach(function (name) {
                var s = document.createElement('source'); s.src = 'https://media.test/' + name;
                s.addEventListener('error', function () { seen.push('error@' + name); });
                v.appendChild(s);
            });
            setTimeout(function () { Log.info(seen.join(',') + ' | ' + v.currentSrc + ' ' + v.readyState + ' ' + v.networkState); }, 300);
            """, new FakeProvider());
        using var _ = document;

        // Each failing source reports at itself, the element reports nothing, and the one that opens is the one it plays.
        Assert.Equal(["error@a.bad,error@b.bad | https://media.test/c.webm 4 1"], log);
    }

    [Fact]
    public async Task WhenEverySourceFails_TheElementHasNoSource_WithoutAnErrorOfItsOwn_AndPlayRejects()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video'); var seen = [];
            v.addEventListener('error', function () { seen.push('video-error'); });
            ['a.bad', 'b.bad'].forEach(function (name) {
                var s = document.createElement('source'); s.src = 'https://media.test/' + name;
                s.addEventListener('error', function () { seen.push('error@' + name); });
                v.appendChild(s);
            });
            v.play().catch(function (e) { seen.push('play:' + e.name); });
            setTimeout(function () { Log.info(seen.join(',') + ' | ' + v.networkState + ' ' + (v.error === null)); }, 300);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["error@a.bad,error@b.bad,play:NotSupportedError | 3 true"], log);
    }

    [Fact]
    public async Task TheMediaEventHandlerAttributes_AreCalledLikeListeners()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video'); var seen = [];
            ['loadstart', 'loadedmetadata', 'loadeddata', 'canplay', 'canplaythrough', 'play', 'playing', 'pause', 'ended', 'seeked', 'timeupdate'].forEach(function (t) {
                v['on' + t] = function () { seen.push(t); };
            });
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { v.play(); }, 100);
            setTimeout(function () { v.pause(); Log.info(seen.filter(function (t, i) { return seen.indexOf(t) === i; }).join(',')); }, 700);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["loadstart,loadedmetadata,loadeddata,canplay,canplaythrough,play,playing,timeupdate"], log);
    }

    [Fact]
    public async Task ALoadedVideo_FiresItsEventsInTheStandardOrder_AndReportsWhatTheSessionHas()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.networkState + ' ' + v.readyState + ' ' + v.duration + ' ' + v.videoWidth + 'x' + v.videoHeight + ' ' + v.paused + ' ' + v.currentSrc); }, 200);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["loadstart,durationchange,loadedmetadata,resize,loadeddata,canplay,canplaythrough | 1 4 10 40x20 true https://media.test/a.webm"], log);
    }

    [Fact]
    public async Task Play_StartsPlayback_ResolvesItsPromise_AndTheClockFollowsTheVirtualTime()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            setTimeout(function () {
                events.length = 0;
                v.play().then(function () { events.push('resolved'); });
            }, 100);
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.paused + ' ' + v.currentTime.toFixed(1)); }, 1100);
            """, new FakeProvider());
        using var _ = document;

        // One virtual second after play(), a second of the media has played.
        Assert.Equal(["play,playing,resolved | false 1.0"], log);
    }

    [Fact]
    public async Task Pause_StopsTheClock_AndFiresPause()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { v.play(); }, 100);
            setTimeout(function () { v.pause(); events.push('|'); }, 600);
            setTimeout(function () { Log.info(events.slice(events.indexOf('play')).join(',') + ' | ' + v.paused + ' ' + v.currentTime.toFixed(1)); }, 2000);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["play,playing,|,pause | true 0.5"], log);
    }

    [Fact]
    public async Task SettingTheCurrentTime_Seeks_AndFiresSeekingThenSeeked()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { events.length = 0; v.currentTime = 5; Log.info('now ' + v.currentTime + ' ' + v.seeking); }, 100);
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.currentTime + ' ' + v.seeking); }, 500);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["now 5 true", "seeking,seeked | 5 false"], log);
    }

    [Fact]
    public async Task APlayingVideoThatReachesItsEnd_FiresEnded_AndIsPaused()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/short.webm';
            setTimeout(function () { v.play(); }, 100);
            setTimeout(function () { Log.info(events.slice(events.indexOf('play')).join(',') + ' | ' + v.ended + ' ' + v.paused + ' ' + v.currentTime); }, 3000);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["play,playing,pause,ended | true true 2"], log);
    }

    [Fact]
    public async Task AVideoWithAutoplay_PlaysWhenItCanPlayThrough()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.autoplay = true;
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.paused + ' ' + v.currentTime.toFixed(1)); }, 1000);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["loadstart,durationchange,loadedmetadata,resize,loadeddata,canplay,canplaythrough,play,playing | false 1.0"], log);
    }

    [Fact]
    public async Task ASourceChild_IsChosenByWhatTheProviderCanPlay_AndCanPlayTypeAnswersForIt()
    {
        var provider = new FakeProvider();
        var (log, document) = await RunAsync("""
            var v = document.createElement('video');
            var a = document.createElement('source'); a.src = 'https://media.test/a.mp4'; a.type = 'video/mp4';
            var b = document.createElement('source'); b.src = 'https://media.test/b.webm'; b.type = 'video/webm; codecs=vp9';
            v.appendChild(a); v.appendChild(b);
            document.body.appendChild(v);
            Log.info([v.canPlayType('video/webm; codecs="vp9, opus"'), v.canPlayType('video/ogg'), v.canPlayType('video/mp4')].join('|'));
            setTimeout(function () { Log.info(v.currentSrc + ' ' + v.readyState); }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal(["probably|maybe|", "https://media.test/b.webm 4"], log);
        Assert.Equal("video/webm; codecs=vp9", Assert.Single(provider.Requests).ContentType);
    }

    [Fact]
    public async Task ASourceTheProviderCannotOpen_IsAnErrorWithCode4_AndPlayRejects()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.bad';
            var rejected = '';
            v.play().catch(function (e) { rejected = e.name; });
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.networkState + ' ' + v.error.code + ' ' + (v.error instanceof MediaError) + ' ' + rejected); }, 100);
            """, new FakeProvider());
        using var _ = document;

        // play() ran before the source failed, so the element was no longer paused: play comes before error, as in a browser.
        Assert.Equal(["loadstart,play,error | 3 4 true NotSupportedError"], log);
    }

    [Fact]
    public async Task WithNoProvider_AMediaElementFailsAsItAlwaysDid()
    {
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            var rejected = '';
            v.play().catch(function (e) { rejected = e.name; });
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.networkState + ' ' + v.readyState + ' ' + v.error.code + ' ' + v.canPlayType('video/webm') + '|' + rejected); }, 100);
            """, null);
        using var _ = document;

        Assert.Equal(["error | 3 0 4 |NotSupportedError"], log);
    }

    [Fact]
    public async Task RequestVideoFrameCallback_IsCalledWhenAPictureIsPresented_WithItsMediaTime()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { v.play(); v.requestVideoFrameCallback(function (now, meta) { Log.info(meta.width + 'x' + meta.height + ' ' + meta.mediaTime.toFixed(1)); }); }, 100);
            var id = 0;
            setTimeout(function () { id = v.requestVideoFrameCallback(function () { Log.info('cancelled ran'); }); v.cancelVideoFrameCallback(id); }, 150);
            """, new FakeProvider());
        using var _ = document;

        Assert.Single(log);
        Assert.StartsWith("40x20 0.", log[0]);
    }

    [Fact]
    public async Task TheRangesAndTheSettings_AreValidated()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video');
            v.src = 'https://media.test/a.webm';
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            setTimeout(function () {
                Log.info(v.seekable.length + ' ' + v.seekable.start(0) + ' ' + v.seekable.end(0) + ' ' + v.buffered.length + ' ' + v.played.length + ' ' + (v.seekable instanceof TimeRanges));
                Log.info([name(function () { v.volume = 2; }), name(function () { v.volume = 0.5; }), name(function () { v.currentTime = Infinity; }), name(function () { v.seekable.start(1); }), name(function () { v.canPlayType(); })].join(','));
                v.volume = 0.25; v.muted = true; v.playbackRate = 2;
                Log.info(v.volume + ' ' + v.muted + ' ' + v.playbackRate + ' ' + v.HAVE_ENOUGH_DATA + ' ' + v.NETWORK_NO_SOURCE);
            }, 100);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["1 0 10 1 0 true", "IndexSizeError,none,TypeError,IndexSizeError,TypeError", "0.25 true 2 4 3"], log);
    }

    [Fact]
    public async Task PlayAfterTheEnd_StartsFromTheBeginning()
    {
        var (log, document) = await RunAsync("""
            var v = document.createElement('video');
            v.src = 'https://media.test/short.webm';
            setTimeout(function () { v.play(); }, 100);
            setTimeout(function () { var at = v.ended; v.play(); Log.info(at + ' ' + v.currentTime + ' ' + v.paused); }, 3000);
            """, new FakeProvider());
        using var _ = document;

        Assert.Equal(["true 0 false"], log);
    }

    [Fact]
    public async Task ChangingTheSource_StartsOver_AndTheOldSessionIsDisposedFirst()
    {
        var provider = new FakeProvider();
        var (log, document) = await RunAsync($$"""
            var v = document.createElement('video'); {{Events}}
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { events.length = 0; v.src = 'https://media.test/b.webm'; }, 100);
            setTimeout(function () { Log.info(events.join(',') + ' | ' + v.currentSrc); }, 300);
            """, provider);
        using var _ = document;

        Assert.Equal(["emptied,loadstart,durationchange,loadedmetadata,resize,loadeddata,canplay,canplaythrough | https://media.test/b.webm"], log);
        // The scripts are done, so both are closed now; the one the new source replaced was closed first, when it was replaced.
        Assert.Equal(2, provider.Sessions.Count);
        Assert.True(provider.Sessions[0].DisposedOrder > 0 && provider.Sessions[0].DisposedOrder < provider.Sessions[1].DisposedOrder);
    }

    [Fact]
    public async Task WhenTheScriptsAreDone_TheSessionsAreDisposed()
    {
        var provider = new FakeProvider();
        var (_, document) = await RunAsync("""
            var v = document.createElement('video'); v.src = 'https://media.test/a.webm';
            var a = document.createElement('audio'); a.src = 'https://media.test/b.webm';
            """, provider);

        // What a script does with media is over when its run is, and the document is parsed again from what it left.
        using var _ = document;
        Assert.Equal(2, provider.Sessions.Count);
        Assert.Equal([MediaKind.Video, MediaKind.Audio], provider.Requests.Select(r => r.Kind));
        Assert.All(provider.Sessions, s => Assert.True(s.Disposed));
    }
}
