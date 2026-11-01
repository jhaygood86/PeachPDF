using System;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>
/// Raised inside the hinting engine when a font cannot be hinted (its font or CVT program fails, a glyph's data is
/// malformed, an execution limit is reached). Fonts are untrusted input: the public API catches it and answers with the
/// unhinted outline, so nothing outside the engine ever sees it.
/// </summary>
internal sealed class HintingException : Exception
{
    public HintingException(string message)
        : base(message)
    {
    }

    public HintingException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
