using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Decodes a <see cref="Stream"/> (an <c>@import</c>/externally-fetched stylesheet) into a fully
    /// decoded <see cref="ReadOnlyMemory{T}"/> of <see langword="char"/> - BOM sniffing plus decode, using
    /// <see cref="PipeReader"/> for pooled buffering instead of a hand-rolled <c>byte[]</c>/<c>MemoryStream</c>
    /// pair. This is the entire stream-facing surface: <see cref="TextSource"/> never touches a
    /// <see cref="Stream"/>/<see cref="PipeReader"/> itself, only the memory this hands back.
    /// <para>
    /// No encoding-correction/replay support: the old <c>TextSource.CurrentEncoding</c> setter's "peek
    /// without consuming, replay from raw bytes if the encoding turns out wrong" mechanism was confirmed
    /// dead code (nothing outside <c>TextSource</c> itself ever set it) before this loader was written, so
    /// there is nothing here to replicate - the encoding decided from the first chunk's BOM (or the
    /// explicitly-supplied encoding, or UTF-8) is used for the whole stream.
    /// </para>
    /// </summary>
    internal static class CssStreamLoader
    {
        public static ReadOnlyMemory<char> Load(Stream stream, Encoding? encoding = null)
        {
            return LoadAsync(stream, encoding, CancellationToken.None).GetAwaiter().GetResult();
        }

        public static async Task<ReadOnlyMemory<char>> LoadAsync(Stream stream, Encoding? encoding,
            CancellationToken cancellationToken)
        {
            var reader = PipeReader.Create(stream);

            try
            {
                return await LoadCoreAsync(reader, encoding, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await reader.CompleteAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ReadOnlyMemory<char>> LoadCoreAsync(PipeReader reader, Encoding? explicitEncoding,
            CancellationToken cancellationToken)
        {
            var sb = Pool.NewStringBuilder();
            var scratch = ArrayPool<char>.Shared.Rent(4096);

            try
            {
                Decoder? decoder = null;

                while (true)
                {
                    var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    var buffer = result.Buffer;

                    if (decoder is null)
                    {
                        // Not enough buffered yet to reliably classify the BOM, and more may still
                        // arrive - examine without consuming, so the next ReadAsync sees these bytes
                        // again alongside whatever follows.
                        if (buffer.Length < 4 && !result.IsCompleted)
                        {
                            reader.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }

                        var (detected, bomLength) = DetectEncoding(buffer, explicitEncoding);
                        decoder = detected.GetDecoder();
                        buffer = buffer.Slice(bomLength);
                    }

                    DecodeInto(decoder, buffer, result.IsCompleted, sb, scratch);
                    reader.AdvanceTo(buffer.End);

                    if (result.IsCompleted) break;
                }

                return sb.ToPool().AsMemory();
            }
            finally
            {
                ArrayPool<char>.Shared.Return(scratch);
            }
        }

        /// <summary>
        /// Feeds every byte segment of <paramref name="buffer"/> through <paramref name="decoder"/>,
        /// appending decoded characters to <paramref name="sb"/>. <paramref name="decoder"/> is stateful
        /// across calls (one instance for the whole stream), so a multi-byte character split across a
        /// segment or a read boundary decodes correctly without this method needing to know about it -
        /// <see cref="Decoder"/> defers an incomplete trailing sequence internally until the bytes that
        /// complete it arrive in a later <c>Convert</c> call.
        /// </summary>
        private static void DecodeInto(Decoder decoder, ReadOnlySequence<byte> buffer, bool isFinalRead,
            StringBuilder sb, char[] scratch)
        {
            foreach (var segment in buffer)
            {
                var bytes = segment.Span;

                while (true)
                {
                    decoder.Convert(bytes, scratch, false, out var bytesUsed, out var charsUsed, out var completed);
                    sb.Append(scratch, 0, charsUsed);

                    // `completed` (not "bytes is now empty") is the correct loop-exit signal: a trailing
                    // incomplete multi-byte sequence is deferred inside `decoder`'s own state without
                    // being reflected in `bytesUsed`, so re-slicing and re-looping on it would spin
                    // forever re-offering the same undecodable tail.
                    if (completed) break;

                    bytes = bytes[bytesUsed..];
                }
            }

            if (isFinalRead)
            {
                // Flushes any still-deferred trailing partial sequence (a genuinely malformed/truncated
                // stream) via the encoding's own DecoderFallback (replacement character by default) -
                // degrades gracefully rather than throwing, matching this codebase's error-handling
                // convention elsewhere in CSS parsing.
                decoder.Convert(ReadOnlySpan<byte>.Empty, scratch, true, out _, out var finalChars, out _);
                if (finalChars > 0) sb.Append(scratch, 0, finalChars);
            }
        }

        /// <summary>Same BOM signature table <c>TextSource.DetectByteOrderMarkAsync</c> used, expressed
        /// against a <see cref="ReadOnlySequence{T}"/> instead of a flat <c>byte[]</c>.</summary>
        private static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySequence<byte> buffer,
            Encoding? explicitEncoding)
        {
            Span<byte> head = stackalloc byte[4];
            var headLength = (int)Math.Min(4, buffer.Length);
            buffer.Slice(0, headLength).CopyTo(head);

            if (headLength > 2 && head[0] == 0xef && head[1] == 0xbb && head[2] == 0xbf)
                return (TextEncoding.Utf8, 3);

            if (headLength > 3 && head[0] == 0xff && head[1] == 0xfe && head[2] == 0x0 && head[3] == 0x0)
                return (TextEncoding.Utf32Le, 4);

            if (headLength > 3 && head[0] == 0x0 && head[1] == 0x0 && head[2] == 0xfe && head[3] == 0xff)
                return (TextEncoding.Utf32Be, 4);

            if (headLength > 1 && head[0] == 0xfe && head[1] == 0xff)
                return (TextEncoding.Utf16Be, 2);

            if (headLength > 1 && head[0] == 0xff && head[1] == 0xfe)
                return (TextEncoding.Utf16Le, 2);

            if (headLength > 3 && head[0] == 0x84 && head[1] == 0x31 && head[2] == 0x95 && head[3] == 0x33)
                return (TextEncoding.Gb18030, 4);

            return (explicitEncoding ?? TextEncoding.Utf8, 0);
        }
    }
}
