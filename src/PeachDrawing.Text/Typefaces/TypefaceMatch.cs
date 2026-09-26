namespace PeachDrawing.Text
{
    /// <summary>
    /// The face a family matched a <see cref="TypefaceQuery"/> with, and what is still missing from it.
    /// </summary>
    /// <param name="Typeface">The face that matched.</param>
    /// <param name="Synthesis">What the caller has to fake because the face is not bold or italic enough for the query.</param>
    public readonly record struct TypefaceMatch(Typeface Typeface, SyntheticStyle Synthesis);
}
