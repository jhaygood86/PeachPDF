using PeachBrackets.Html.Utils;
using PeachBrackets.Media;
using PeachDrawing.Core;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS box for a <c>&lt;video&gt;</c> element. PeachBrackets decodes no video itself, so what it shows depends on the host:
    /// with an <see cref="IMediaProvider"/> (<see cref="HtmlDocumentOptions.MediaProvider"/>) the box shows a picture of the media
    /// (the first one, or the one at the address's <c>#t=</c> fragment, and then, while the host steps animations and the
    /// session plays, the current one), at the size of the video and laid out as replaced content like an image; without one, or
    /// when the provider cannot play the resource, it renders the element's <c>poster</c> image (the frame browsers show before
    /// playback) as <see cref="CssBoxObject"/>'s replaced/fallback behavior, honoring <c>object-fit</c>/<c>object-position</c>
    /// exactly like an image; with no <c>poster</c> either the box falls back to being an ordinary container for any fallback DOM
    /// content.
    /// </summary>
    internal sealed class CssBoxVideo : CssBoxObject
    {
        private IMediaSession? _session;
        private MediaFrame? _shown;

        // The frame shown before this one stays held for a step: a host may still be painting from it on another thread.
        private MediaFrame? _previous;

        public CssBoxVideo(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
        }

        /// <summary>The session playing this element's media, or null when there is none.</summary>
        internal IMediaSession? Session => _session;

        protected override string? ResolveReplacedSource() => GetAttribute("poster", null);

        protected override async ValueTask<Image?> ResolveLiveImageAsync()
        {
            if (HtmlContainer is not { MediaProvider: { } provider } container)
                return null;

            var (address, type) = ChooseSource(container, provider);
            if (address is null)
                return null;

            var request = new MediaRequest(address, MediaKind.Video, container.RenderContext, _ => container.RenderContext.GetResourceStream(address))
            {
                ContentType = type,
                CrossOrigin = HtmlTag?.HasAttribute("crossorigin") != true ? null : GetAttribute("crossorigin", string.Empty).Equals("use-credentials", StringComparison.OrdinalIgnoreCase) ? "use-credentials" : "anonymous",
                Muted = HtmlTag?.HasAttribute("muted") == true,
                Loop = HtmlTag?.HasAttribute("loop") == true,
                Preload = GetAttribute("preload", null)?.ToLowerInvariant() switch { "none" => MediaPreload.None, "auto" => MediaPreload.Auto, _ => MediaPreload.Metadata },
            };

            IMediaSession? session = null;
            try
            {
                session = await provider.OpenAsync(request);
                if (session is null)
                    return null;

                var frame = await session.AcquireFrameAtAsync(StartOf(address));
                if (frame is null)
                {
                    session.Dispose();
                    return null;
                }

                // A host that steps animations plays the video; a paged document shows the picture it asked for and no more.
                if (container.AnimationsEnabled && HtmlTag?.HasAttribute("autoplay") == true)
                    session.Play();

                session.Changed += OnSessionChanged;
                _session = session;
                _shown = frame;
                return Painted(frame);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                // A provider is the host's code: one that fails to open or decode leaves the poster, as a resource that cannot be played does.
                session?.Dispose();
                return null;
            }
        }

        // The resource to play: the src attribute, else the first source child the provider says it can play.
        private (Uri? Address, string? Type) ChooseSource(HtmlContainerInt container, IMediaProvider provider)
        {
            var src = GetAttribute("src", null);
            if (!string.IsNullOrWhiteSpace(src))
                return (Absolute(container, src), null);

            foreach (var child in Boxes)
            {
                if (child.HtmlTag is not { } tag || !string.Equals(tag.Name, "source", StringComparison.OrdinalIgnoreCase))
                    continue;

                var childSrc = child.GetAttribute("src", null);
                if (string.IsNullOrWhiteSpace(childSrc))
                    continue;

                var type = child.GetAttribute("type", null);
                if (!string.IsNullOrWhiteSpace(type) && provider.CanPlayType(type).Length == 0)
                    continue;

                if (Absolute(container, childSrc) is { } address)
                    return (address, string.IsNullOrWhiteSpace(type) ? null : type);
            }

            return (null, null);
        }

        private static Uri? Absolute(HtmlContainerInt container, string src) =>
            CommonUtils.ResolveAgainstDocumentBase(container, src.Trim()) is { IsAbsoluteUri: true } uri ? uri : null;

        /// <summary>Where a media fragment (<c>#t=10</c>, <c>#t=npt:1:20</c>, <c>#t=10,20</c>) starts the media, or the beginning.</summary>
        internal static TimeSpan StartOf(Uri address)
        {
            var fragment = address.Fragment;
            if (fragment.Length < 4 || !fragment.StartsWith("#t=", StringComparison.Ordinal))
                return TimeSpan.Zero;

            var spec = fragment[3..];
            if (spec.StartsWith("npt:", StringComparison.Ordinal))
                spec = spec[4..];

            var start = spec.Split(',')[0];
            if (start.Length == 0)
                return TimeSpan.Zero;

            double seconds = 0;
            foreach (var part in start.Split(':'))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
                    return TimeSpan.Zero;

                seconds = (seconds * 60) + value;
            }

            return double.IsFinite(seconds) && seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }

        // A page and a screen are sRGB: a picture in another space is converted, once per picture; one in sRGB is painted as it is (a texture stays one).
        private static Image Painted(MediaFrame frame)
        {
            if (frame.ColorSpace == MediaColorSpace.Srgb || frame.Image.GetPixels() is not { } pixels)
                return frame.Image;

            var rgba = pixels.PremultipliedRgba.ToArray();
            var opaque = true;
            for (var i = 3; i < rgba.Length && opaque; i += 4)
                opaque = rgba[i] == 255;

            // A video is opaque, so its premultiplied and straight bytes are the same; anything else is left as it is rather than converted wrongly.
            if (!opaque || ImageColorSources.ToSrgb(rgba, pixels.Width, pixels.Height, frame.ColorSpace) is not { } srgb)
                return frame.Image;

            return new ConvertedFrameImage(pixels.Width, pixels.Height, srgb);
        }

        private sealed class ConvertedFrameImage(int width, int height, byte[] premultipliedRgba) : Image
        {
            public override double Width => width;

            public override double Height => height;

            public override bool Interpolate { get; set; } = true;

            public override void Dispose()
            {
            }

            public override PixelBuffer? GetPixels() => new PixelBuffer(width, height, premultipliedRgba);
        }

        // May be raised from the provider's thread: only asks the clock to run again, which a host that stopped its frame loop has to be told.
        private void OnSessionChanged(object? sender, MediaChangedEventArgs e) => HtmlContainer?.AnimationClock?.Wake();

        /// <summary>
        /// Brings the shown picture up to the session's current one, telling it what time the document is at first.
        /// </summary>
        /// <param name="documentTime">the time on the document's animation clock</param>
        /// <param name="running">whether the session is playing, so that another step is due</param>
        /// <returns>whether the picture changed and the box has to be painted again</returns>
        internal bool Advance(TimeSpan documentTime, out bool running)
        {
            running = false;
            if (_session is not { } session || ReplacedWord is not { } word)
                return false;

            session.Update(documentTime);
            running = session.IsPlaying;

            var frame = session.AcquireFrame();
            if (frame is null)
                return false;

            if (_shown is not null && _shown.Time == frame.Time)
            {
                frame.Dispose();
                return false;
            }

            _previous?.Dispose();
            _previous = _shown;
            _shown = frame;
            word.Image = Painted(frame);
            return true;
        }

        public override void Dispose()
        {
            // The frames go back before the session that made them goes away.
            _previous?.Dispose();
            _shown?.Dispose();
            _previous = _shown = null;
            if (_session is { } session)
            {
                session.Changed -= OnSessionChanged;
                _session = null;
                session.Dispose();
            }

            base.Dispose();
        }
    }
}
