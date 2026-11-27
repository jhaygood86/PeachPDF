using System;

namespace PeachBrackets.Media;

/// <summary>Whether a media element plays sound alone or pictures with it.</summary>
public enum MediaKind
{
    /// <summary>An <c>audio</c> element.</summary>
    Audio,

    /// <summary>A <c>video</c> element.</summary>
    Video,
}

/// <summary>How much of a resource a session has, the values of <c>HTMLMediaElement.readyState</c>.</summary>
public enum MediaReadyState
{
    /// <summary>Nothing is known about the resource yet.</summary>
    Nothing = 0,

    /// <summary>The duration and, for video, the size are known.</summary>
    Metadata = 1,

    /// <summary>The data at the current position is there, but not what follows it.</summary>
    CurrentData = 2,

    /// <summary>The current position and some of what follows are there.</summary>
    FutureData = 3,

    /// <summary>Enough is there to play on to the end without stopping.</summary>
    EnoughData = 4,
}

/// <summary>Why a session cannot play, the values of <c>MediaError.code</c>.</summary>
public enum MediaErrorCode
{
    /// <summary>There is no error.</summary>
    None = 0,

    /// <summary>Loading was stopped at the user's request.</summary>
    Aborted = 1,

    /// <summary>The resource could not be fetched.</summary>
    Network = 2,

    /// <summary>The resource was fetched but could not be decoded.</summary>
    Decode = 3,

    /// <summary>The resource is of a kind the provider does not play.</summary>
    SourceNotSupported = 4,
}

/// <summary>What changed in a session, so the document can react to only that.</summary>
[Flags]
public enum MediaChange
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The duration or the picture size became known or changed.</summary>
    Metadata = 1,

    /// <summary>More data arrived, or the ready state changed.</summary>
    Data = 2,

    /// <summary>The session started or stopped playing, or its rate, volume or mute changed.</summary>
    State = 4,

    /// <summary>The position changed other than by playing (a seek finished).</summary>
    Position = 8,

    /// <summary>A new picture is ready to be shown.</summary>
    Frame = 16,

    /// <summary>Playback reached the end.</summary>
    Ended = 32,

    /// <summary>The session failed.</summary>
    Error = 64,
}

/// <summary>What a <see cref="IMediaSession.Changed"/> notification says changed.</summary>
public sealed class MediaChangedEventArgs(MediaChange changes) : EventArgs
{
    /// <summary>What changed.</summary>
    public MediaChange Changes { get; } = changes;
}

/// <summary>How much of a resource a media element loads before it is played (the <c>preload</c> attribute).</summary>
public enum MediaPreload
{
    /// <summary>Nothing until the element is played.</summary>
    None,

    /// <summary>The metadata (the default for most elements).</summary>
    Metadata,

    /// <summary>Whatever the provider sees fit.</summary>
    Auto,
}

/// <summary>Settings every media provider honors.</summary>
public sealed class MediaProviderOptions
{
    /// <summary>
    /// Whether playback follows the document's own clock instead of the player's. The position then advances only as the time given to
    /// <see cref="IMediaSession.Update"/> does, and each picture is the one at that position (found by seeking, not by letting the player run), so a
    /// document that plays media behaves the same however fast the machine is. For tests; an app that shows video leaves it off.
    /// </summary>
    public bool DeterministicClock { get; init; }
}
