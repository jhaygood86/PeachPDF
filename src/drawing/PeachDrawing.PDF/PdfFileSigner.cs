using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PeachDrawing.PDF.Engine.Security;

namespace PeachDrawing.PDF;

/// <summary>
/// Signs or timestamps a PDF that already exists, without changing a byte of it: the signature is appended as an incremental update, so
/// signatures the file already carries stay valid. The result is a new byte array; the input is not modified.
/// </summary>
/// <remarks>
/// The signature is an invisible field on the first page. An encrypted file and one whose cross-reference data is damaged are
/// refused. Each call adds one signature or timestamp; call again on the result to add another.
/// </remarks>
public static class PdfFileSigner
{
    /// <summary>Signs a PDF.</summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="options">how to sign; <see cref="PdfSigningOptions.Timestamp"/> must be unset (use <see cref="SignAsync"/> for a timestamp)</param>
    /// <returns>the file with the signature appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, or ask for a timestamp, or the signature does not fit in its reserved room</exception>
    /// <exception cref="NotSupportedException">the file is encrypted</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static byte[] Sign(byte[] pdf, PdfSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = Prepare(options);
        if (copy.Timestamp is not null)
            throw new InvalidOperationException("The signature asks for a timestamp, whose authority is asked asynchronously: call SignAsync.");

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, PdfDocumentSigner.SignatureValueText(copy), "Signature");
        PdfDocumentSigner.Seal(file, copy);
        return file;
    }

    /// <summary>Signs a PDF, asking the time stamping authority of <see cref="PdfSigningOptions.Timestamp"/> for its token when there is one.</summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="options">how to sign</param>
    /// <param name="cancellationToken">cancels the request to the authority</param>
    /// <returns>the file with the signature appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, the authority's answer is refused, or the signature does not fit in its reserved room</exception>
    /// <exception cref="NotSupportedException">the file is encrypted</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static async Task<byte[]> SignAsync(byte[] pdf, PdfSigningOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = Prepare(options);

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, PdfDocumentSigner.SignatureValueText(copy), "Signature");
        await PdfDocumentSigner.SealAsync(file, copy, cancellationToken).ConfigureAwait(false);
        return file;
    }

    /// <summary>
    /// Adds a document timestamp: the authority vouches that the whole file, signatures included, existed at the time it names. Adding
    /// one after the last signature is the usual way to extend how long a signed file can be trusted.
    /// </summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="options">the authority to ask</param>
    /// <param name="reservedBytes">the room reserved for the token, from 2 048 to 1 000 000 bytes</param>
    /// <param name="cancellationToken">cancels the request to the authority</param>
    /// <returns>the file with the timestamp appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, the authority's answer is refused, or the token does not fit in the reserved room</exception>
    /// <exception cref="NotSupportedException">the file is encrypted</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static async Task<byte[]> AddDocumentTimestampAsync(byte[] pdf, PdfTimestampOptions options, int reservedBytes = 16384, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = options.Clone();
        copy.Validate();
        if (reservedBytes is < 2048 or > 1_000_000)
            throw new InvalidOperationException("The reserved size must be from 2048 to 1000000 bytes.");

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, PdfDocumentSigner.TimestampValueText(reservedBytes), "Timestamp");
        await PdfDocumentSigner.SealTimestampAsync(file, copy, reservedBytes, cancellationToken).ConfigureAwait(false);
        return file;
    }

    private static PdfSigningOptions Prepare(PdfSigningOptions options)
    {
        options.Validate();
        return options.Clone();
    }
}
