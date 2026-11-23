namespace PeachDrawing.PDF;

/// <summary>What a <see cref="PdfOperation"/> does when a file it draws or copies pages from is digitally signed.</summary>
/// <remarks>
/// A signature covers the exact bytes of its file, so an operation that writes a new file cannot carry it. See
/// <see cref="PdfPageSource.HasSignatures"/>.
/// </remarks>
public enum PdfSignedSourcePolicy
{
    /// <summary>Write the output anyway, without the signature, and name the loss in <see cref="PdfOperation.Warnings"/>. The default.</summary>
    Warn,

    /// <summary>Refuse: the save (or <see cref="PdfOperation.AppendTo"/>) throws <see cref="System.InvalidOperationException"/> and writes nothing.</summary>
    Reject,
}
