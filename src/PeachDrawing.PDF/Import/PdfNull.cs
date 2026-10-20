using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.IO;

namespace PeachDrawing.PDF.Import;

/// <summary>The PDF null object, kept as an item where position matters (an array element, a function argument).</summary>
internal sealed class PdfNull : PdfItem
{
    /// <summary>The one instance.</summary>
    public static readonly PdfNull Instance = new();

    PdfNull() { }

    /// <inheritdoc />
    internal override void WriteObject(PdfWriter writer) => writer.WriteRaw("null");

    /// <inheritdoc />
    public override string ToString() => "null";
}
