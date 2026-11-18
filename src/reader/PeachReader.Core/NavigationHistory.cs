namespace PeachReader.Core;

/// <summary>The back and forward lists of a reader: where it was before it followed a link or jumped to a contents entry.</summary>
public sealed class NavigationHistory
{
    private const int Limit = 100;
    private readonly List<ReaderLocation> _back = [];
    private readonly List<ReaderLocation> _forward = [];

    /// <summary>Whether <see cref="GoBack"/> has somewhere to go.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>Whether <see cref="GoForward"/> has somewhere to go.</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Records that the reader is leaving <paramref name="from"/> by a jump; what could have been reached by going forward is forgotten.</summary>
    /// <param name="from">where the reader was</param>
    public void Navigated(ReaderLocation from)
    {
        if (_back.Count > 0 && _back[^1] == from) return;
        _back.Add(from);
        if (_back.Count > Limit) _back.RemoveAt(0);
        _forward.Clear();
    }

    /// <summary>Goes back one place.</summary>
    /// <param name="current">where the reader is now, kept so it can come forward again</param>
    /// <returns>the place to show, or <see langword="null"/> when there is none</returns>
    public ReaderLocation? GoBack(ReaderLocation current)
    {
        if (_back.Count == 0) return null;
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
        return target;
    }

    /// <summary>Goes forward one place, after going back.</summary>
    /// <param name="current">where the reader is now, kept so it can go back again</param>
    /// <returns>the place to show, or <see langword="null"/> when there is none</returns>
    public ReaderLocation? GoForward(ReaderLocation current)
    {
        if (_forward.Count == 0) return null;
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
        return target;
    }

    /// <summary>Forgets both lists, as when another document is opened.</summary>
    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
    }
}
