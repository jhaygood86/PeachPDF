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
/// The signature is an invisible field on the first page unless the options draw it. An encrypted file (AES-128 or AES-256) is signed
/// when its password is given: the new objects are written encrypted with the file's own key. A file encrypted with RC4 and one whose
/// cross-reference data is damaged are refused. Each call adds one signature or timestamp; call again on the result to add another.
/// </remarks>
public static class PdfFileSigner
{
    /// <summary>Signs a PDF.</summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="options">how to sign; <see cref="PdfSigningOptions.Timestamp"/> must be unset (use <see cref="SignAsync"/> for a timestamp)</param>
    /// <param name="password">the password of an encrypted file (the user password, or for an AES-256 file the owner password too), or <see langword="null"/> for a file that opens without one</param>
    /// <returns>the file with the signature appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, or ask for a timestamp, or the signature does not fit in its reserved room</exception>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="NotSupportedException">the file is encrypted with RC4 or a security handler this does not know</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static byte[] Sign(byte[] pdf, PdfSigningOptions options, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = Prepare(options);
        if (copy.Timestamp is not null)
            throw new InvalidOperationException("The signature asks for a timestamp, whose authority is asked asynchronously: call SignAsync.");

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, encrypt => PdfDocumentSigner.SignatureValueText(copy, encrypt), "Signature", Visible(copy), password);
        PdfDocumentSigner.Seal(file, copy);
        return file;
    }

    /// <summary>Signs a PDF, asking the time stamping authority of <see cref="PdfSigningOptions.Timestamp"/> for its token when there is one.</summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="options">how to sign</param>
    /// <param name="cancellationToken">cancels the request to the authority</param>
    /// <param name="password">the password of an encrypted file (the user password, or for an AES-256 file the owner password too), or <see langword="null"/> for a file that opens without one</param>
    /// <returns>the file with the signature appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, the authority's answer is refused, or the signature does not fit in its reserved room</exception>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="NotSupportedException">the file is encrypted with RC4 or a security handler this does not know</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static async Task<byte[]> SignAsync(byte[] pdf, PdfSigningOptions options, string? password = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = Prepare(options);

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, encrypt => PdfDocumentSigner.SignatureValueText(copy, encrypt), "Signature", Visible(copy), password);
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
    /// <param name="password">the password of an encrypted file (the user password, or for an AES-256 file the owner password too), or <see langword="null"/> for a file that opens without one</param>
    /// <returns>the file with the timestamp appended</returns>
    /// <exception cref="InvalidOperationException">the options are not valid, the authority's answer is refused, or the token does not fit in the reserved room</exception>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="NotSupportedException">the file is encrypted with RC4 or a security handler this does not know</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static async Task<byte[]> AddDocumentTimestampAsync(byte[] pdf, PdfTimestampOptions options, int reservedBytes = 16384, string? password = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var copy = options.Clone();
        copy.Validate();
        if (reservedBytes is < 2048 or > 1_000_000)
            throw new InvalidOperationException("The reserved size must be from 2048 to 1000000 bytes.");

        var file = PdfIncrementalUpdate.AppendSignatureField(pdf, _ => PdfDocumentSigner.TimestampValueText(reservedBytes), "Timestamp", password: password);
        await PdfDocumentSigner.SealTimestampAsync(file, copy, reservedBytes, cancellationToken).ConfigureAwait(false);
        return file;
    }

    /// <summary>
    /// Adds validation data (certificates, OCSP responses, CRLs) to the file's document security store, as an incremental update, so a
    /// signature can be checked long after it was made. Data the store already holds is not stored twice. Add a document timestamp after it
    /// (<see cref="AddDocumentTimestampAsync"/>) to cover the store as well, which is the PAdES B-LTA form.
    /// </summary>
    /// <param name="pdf">the bytes of the file</param>
    /// <param name="data">what to add; see <see cref="PdfValidationData.CollectAsync"/></param>
    /// <param name="password">the password of an encrypted file, or <see langword="null"/> for a file that opens without one</param>
    /// <returns>the file with the data appended</returns>
    /// <exception cref="InvalidOperationException">there is nothing to add</exception>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="NotSupportedException">the file is encrypted with RC4 or a security handler this does not know</exception>
    /// <exception cref="InvalidDataException">the file is not a readable PDF, or its cross-reference data is damaged</exception>
    public static byte[] AddValidationData(byte[] pdf, PdfValidationData data, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(data);
        if (data.IsEmpty)
            throw new InvalidOperationException("There is no validation data to add.");

        return PdfIncrementalUpdate.AppendValidationData(pdf, data, password);
    }

    private static PdfIncrementalUpdate.VisibleSignature? Visible(PdfSigningOptions options) =>
        options.Box is { } a
            ? new PdfIncrementalUpdate.VisibleSignature(a.PageNumber, a.Bounds, PdfSignatureBoxContent.Lines(options, PeachDrawing.PDF.Engine.Internal.PdfEntropy.UtcNow()), a.FontSize)
            : null;

    private static PdfSigningOptions Prepare(PdfSigningOptions options)
    {
        options.Validate();
        return options.Clone();
    }
}
