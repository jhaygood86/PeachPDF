namespace PeachDrawing.PDF.Engine;

/// <summary>
/// A dictionary class that <see cref="PdfDictionary.DictionaryElements.GetOrCreate{TItem}"/> can create when a key has no value yet,
/// and wrap around a plain dictionary a key already holds. It states the two constructors the class offers for that, so no
/// reflection is needed to find them.
/// </summary>
internal interface IPdfDictionaryValue<TSelf> where TSelf : PdfDictionary
{
    /// <summary>Creates an empty value owned by <paramref name="document"/>.</summary>
    static abstract TSelf Create(PdfDocument document);

    /// <summary>Takes over <paramref name="existing"/> (an object of a more general class) as a value of this class.</summary>
    static abstract TSelf Wrap(PdfDictionary existing);
}

/// <summary>
/// The array counterpart of <see cref="IPdfDictionaryValue{TSelf}"/>, used by
/// <see cref="PdfDictionary.DictionaryElements.GetOrCreateArray{TItem}"/>.
/// </summary>
internal interface IPdfArrayValue<TSelf> where TSelf : PdfArray
{
    /// <summary>Creates an empty value owned by <paramref name="document"/>.</summary>
    static abstract TSelf Create(PdfDocument document);

    /// <summary>Takes over <paramref name="existing"/> (an object of a more general class) as a value of this class.</summary>
    static abstract TSelf Wrap(PdfArray existing);
}
