using System;

namespace PeachDrawing.Text
{
    /// <summary>
    /// The parts of a requested style that the face a match found does not have, and that a renderer therefore
    /// has to fake: bold by emboldening the outlines, italic by shearing them.
    /// </summary>
    /// <remarks>
    /// CSS calls this <c>font-synthesis</c>. A match asks for synthetic bold when the request is bold (weight 600
    /// or more) and the best face is lighter than that, and for synthetic italic when the request is italic and
    /// the best face is upright.
    /// </remarks>
    [Flags]
    public enum SyntheticStyle
    {
        /// <summary>The face has everything that was asked for.</summary>
        None = 0,

        /// <summary>The outlines have to be emboldened.</summary>
        Bold = 1,

        /// <summary>The outlines have to be sheared.</summary>
        Italic = 2,

        /// <summary>Both <see cref="Bold"/> and <see cref="Italic"/> have to be faked.</summary>
        BoldItalic = Bold | Italic,
    }
}
