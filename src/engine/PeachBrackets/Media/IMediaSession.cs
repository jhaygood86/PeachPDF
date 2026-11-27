using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Media;

/// <summary>
/// One opened media resource: what a media element plays. The provider owns decoding, the clock a player keeps while it plays, and any sound; the document
/// asks it for state, tells it what to do and takes the pictures. Members may be called from the document's thread only, but <see cref="Changed"/> may be
/// raised from any thread.
/// </summary>
public interface IMediaSession : IDisposable
{
    /// <summary>Whether the resource was opened for an <c>audio</c> or a <c>video</c> element.</summary>
    MediaKind Kind { get; }

    /// <summary>How much of the resource the session has.</summary>
    MediaReadyState ReadyState { get; }

    /// <summary>The length of the media, or null while it is not known or when it has none (a live stream).</summary>
    TimeSpan? Duration { get; }

    /// <summary>Whether the resource has a picture.</summary>
    bool HasVideo { get; }

    /// <summary>The width of the picture in pixels once the rotation is applied, or 0 while it is not known.</summary>
    int VideoWidth { get; }

    /// <summary>The height of the picture in pixels once the rotation is applied, or 0 while it is not known.</summary>
    int VideoHeight { get; }

    /// <summary>Where playback is.</summary>
    TimeSpan Position { get; }

    /// <summary>Whether the session is playing, so that its position moves and its pictures change.</summary>
    bool IsPlaying { get; }

    /// <summary>Whether playback reached the end and is not looping.</summary>
    bool Ended { get; }

    /// <summary>Why the session cannot play, or <see cref="MediaErrorCode.None"/>.</summary>
    MediaErrorCode Error { get; }

    /// <summary>How fast playback goes, 1 being normal.</summary>
    double PlaybackRate { get; set; }

    /// <summary>The volume from 0 to 1.</summary>
    double Volume { get; set; }

    /// <summary>Whether the sound is off.</summary>
    bool Muted { get; set; }

    /// <summary>Whether playback starts again from the beginning when it reaches the end.</summary>
    bool Loop { get; set; }

    /// <summary>Raised when something about the session changed; may be raised from any thread, and may be raised more than once for one change.</summary>
    event EventHandler<MediaChangedEventArgs>? Changed;

    /// <summary>Starts or resumes playback.</summary>
    void Play();

    /// <summary>Stops playback where it is.</summary>
    void Pause();

    /// <summary>Moves playback to a position.</summary>
    /// <param name="position">where to go; a position past the end is the end</param>
    void Seek(TimeSpan position);

    /// <summary>
    /// Tells the session what time the document is at. A session made with <see cref="MediaProviderOptions.DeterministicClock"/> moves its position by
    /// the difference from the time it was last told; one that plays by its player's clock ignores it. Called before the pictures are asked for in each
    /// step the document takes, and before each <see cref="Play"/>, <see cref="Pause"/>, <see cref="Seek"/> and change of <see cref="PlaybackRate"/> a script
    /// asks for, so that the time since the last call is played at the old setting.
    /// </summary>
    /// <param name="documentTime">the time on the document's clock</param>
    void Update(TimeSpan documentTime);

    /// <summary>The picture at the current position, or null when there is none yet. The caller disposes it. Called again, it gives a frame with the same <see cref="MediaFrame.Time"/> until the picture changes.</summary>
    MediaFrame? AcquireFrame();

    /// <summary>The picture at a position, found by seeking and waiting for it: for a document that is laid out or printed once rather than played.</summary>
    /// <param name="position">where in the media</param>
    /// <param name="cancellationToken">stops waiting</param>
    /// <returns>the picture, which the caller disposes, or null when the resource has none there</returns>
    ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default);
}
