// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Provides access to the internal PDF object data structures. This class prevents the public
/// interfaces from pollution with to much internal functions.
/// </summary>
internal sealed class PdfObjectInternals
{
    internal PdfObjectInternals(PdfObject obj)
    {
        _obj = obj;
    }
    readonly PdfObject _obj;

    /// <summary>
    /// Gets the object identifier. Returns PdfObjectId.Empty for direct objects.
    /// </summary>
    public PdfObjectId ObjectId => _obj.ObjectId;

    /// <summary>
    /// Gets the object number.
    /// </summary>
    public int ObjectNumber => _obj.ObjectId.ObjectNumber;

    /// <summary>
    /// Gets the generation number.
    /// </summary>
    public int GenerationNumber => _obj.ObjectId.GenerationNumber;

    /// <summary>
    /// Gets the name of the current type.
    /// Not a very useful property, but can be used for data binding.
    /// </summary>
    public string TypeId
    {
        get
        {
            if (_obj is PdfArray)
                return "array";
            if (_obj is PdfDictionary)
                return "dictionary";
            return _obj.GetType().Name;
        }
    }
}
