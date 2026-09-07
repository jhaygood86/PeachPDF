#nullable disable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeachPDF.CSS
{
    internal sealed class TextSource : IDisposable
    {
        private const int BufferSize = 4096;
        private readonly Stream _baseStream;
        private readonly MemoryStream _raw;
        private readonly byte[] _buffer;
        private readonly char[] _chars;

        // Non-null only for a string-backed instance (the constructor every hot tokenizer call site
        // uses). Indexes directly into the caller's own immutable string instead of copying it into a
        // second StringBuilder-backed buffer, and needs none of the stream-oriented machinery below
        // (no BOM sniffing, no incremental decode, no raw-bytes replay on an encoding switch) - a
        // string source is already fully decoded and never re-sniffed (see CurrentEncoding below).
        private readonly string _sourceText;

        private StringBuilder _content;
        private EncodingConfidence _confidence;
        private bool _finished;
        private Encoding _encoding;
        private Decoder _decoder;

        public void Dispose()
        {
            if (_sourceText != null) return;

            var isDisposed = _content == null;

            if (isDisposed) return;

            _raw.Dispose();
            _content.Clear().ToPool();
            _content = null;
        }

        private enum EncodingConfidence : byte
        {
            Tentative,
            Certain,
            Irrelevant
        }

        private TextSource(Encoding encoding)
        {
            _buffer = new byte[BufferSize];
            _chars = new char[BufferSize + 1];
            _raw = new MemoryStream();
            Index = 0;
            _encoding = encoding ?? TextEncoding.Utf8;
            _decoder = _encoding.GetDecoder();
        }

        public TextSource(string source)
        {
            _sourceText = source;
            Index = 0;
            _finished = true;
            _confidence = EncodingConfidence.Irrelevant;
        }

        public TextSource(Stream baseStream, Encoding encoding = null) : this(encoding)
        {
            _baseStream = baseStream;
            _content = Pool.NewStringBuilder();
            _confidence = EncodingConfidence.Tentative;
        }

        public string Text => _sourceText ?? _content.ToString();
        public char this[int index] => _sourceText != null ? _sourceText[index] : _content[index];
        public int Index { get; set; }
        public int Length => _sourceText?.Length ?? _content.Length;

        public Encoding CurrentEncoding
        {
            get => _encoding;
            set
            {
                // A string source is already fully decoded text - it is never re-sniffed for encoding,
                // and _confidence starts Irrelevant (never Tentative) for exactly this reason, but guard
                // explicitly rather than relying on that alone.
                if (_sourceText != null) return;
                if (_confidence != EncodingConfidence.Tentative) return;

                if (_encoding.IsUnicode())
                {
                    _confidence = EncodingConfidence.Certain;
                    return;
                }

                if (value.IsUnicode()) value = TextEncoding.Utf8;

                if (value == _encoding)
                {
                    _confidence = EncodingConfidence.Certain;
                    return;
                }

                _encoding = value;
                _decoder = value.GetDecoder();

                var raw = _raw.ToArray();
                var rawChars = new char[_encoding.GetMaxCharCount(raw.Length)];
                var charLength = _decoder.GetChars(raw, 0, raw.Length, rawChars, 0);
                var content = new string(rawChars, 0, charLength);
                var index = Math.Min(Index, content.Length);

                if (content.Substring(0, index).Is(_content.ToString(0, index)))
                {
                    // If everything seems to fit up to this point, do an
                    // instant switch
                    _confidence = EncodingConfidence.Certain;
                    _content.Remove(index, _content.Length - index);
                    _content.Append(content.Substring(index));
                }
                else
                {
                    // Otherwise consider restart from beginning ...
                    Index = 0;
                    _content.Clear().Append(content);
                    throw new NotSupportedException();
                }
            }
        }

        public char ReadCharacter()
        {
            if (_sourceText != null)
            {
                // Index must advance on every call, even past the end - matching the stream-backed
                // branch below exactly. LexerBase.NormalizeForward relies on this: after reading a
                // trailing '\r' with nothing after it, it peeks one more character and backs Index up by
                // one when the peek isn't '\n'; if Index hadn't advanced on the EOF peek, that decrement
                // would leave Index one short of the true end.
                var sourceIndex = Index++;
                return sourceIndex < _sourceText.Length ? _sourceText[sourceIndex] : Symbols.EndOfFile;
            }

            if (Index < _content.Length) return _content[Index++];

            ExpandBuffer(BufferSize);
            var index = Index++;
            return index < _content.Length ? _content[index] : Symbols.EndOfFile;
        }

        public string ReadCharacters(int characters)
        {
            var start = Index;

            if (_sourceText != null)
            {
                // Matches the stream-backed branch below: the cursor always advances by the full
                // requested count (even past the end of the source), while the returned string is
                // clamped to what's actually available.
                Index += characters;
                var available = Math.Max(0, Math.Min(characters, _sourceText.Length - start));
                return available > 0 ? _sourceText.Substring(start, available) : string.Empty;
            }

            var end = start + characters;

            if (end <= _content.Length)
            {
                Index += characters;
                return _content.ToString(start, characters);
            }

            ExpandBuffer(Math.Max(BufferSize, characters));
            Index += characters;
            characters = Math.Min(characters, _content.Length - start);
            return _content.ToString(start, characters);
        }

        public async Task PrefetchAllAsync(CancellationToken cancellationToken)
        {
            // A string source is always _finished already - nothing to prefetch, and _content is never
            // allocated for it, so the Length check below must not run against it.
            if (_sourceText != null) return;

            if (_content.Length == 0) await DetectByteOrderMarkAsync(cancellationToken).ConfigureAwait(false);

            while (!_finished) await ReadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable IDE0060 // Remove unused parameter
        private async Task DetectByteOrderMarkAsync(CancellationToken cancellationToken)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            var count = await _baseStream.ReadAsync(_buffer, 0, BufferSize, cancellationToken).ConfigureAwait(false);
            var offset = 0;

            //TODO readable hex values
            if (count > 2 && _buffer[0] == 0xef && _buffer[1] == 0xbb && _buffer[2] == 0xbf)
            {
                _encoding = TextEncoding.Utf8;
                offset = 3;
            }
            else if (count > 3 && _buffer[0] == 0xff && _buffer[1] == 0xfe && _buffer[2] == 0x0 && _buffer[3] == 0x0)
            {
                _encoding = TextEncoding.Utf32Le;
                offset = 4;
            }
            else if (count > 3 && _buffer[0] == 0x0 && _buffer[1] == 0x0 && _buffer[2] == 0xfe && _buffer[3] == 0xff)
            {
                _encoding = TextEncoding.Utf32Be;
                offset = 4;
            }
            else if (count > 1 && _buffer[0] == 0xfe && _buffer[1] == 0xff)
            {
                _encoding = TextEncoding.Utf16Be;
                offset = 2;
            }
            else if (count > 1 && _buffer[0] == 0xff && _buffer[1] == 0xfe)
            {
                _encoding = TextEncoding.Utf16Le;
                offset = 2;
            }
            else if (count > 3 && _buffer[0] == 0x84 && _buffer[1] == 0x31 && _buffer[2] == 0x95 && _buffer[3] == 0x33)
            {
                _encoding = TextEncoding.Gb18030;
                offset = 4;
            }

            if (offset > 0)
            {
                count -= offset;
                Array.Copy(_buffer, offset, _buffer, 0, count);
                _decoder = _encoding.GetDecoder();
                _confidence = EncodingConfidence.Certain;
            }

            AppendContentFromBuffer(count);
        }

        private async Task ReadIntoBufferAsync(CancellationToken cancellationToken)
        {
            var returned = await _baseStream.ReadAsync(_buffer, 0, BufferSize, cancellationToken).ConfigureAwait(false);
            AppendContentFromBuffer(returned);
        }

        private void ExpandBuffer(long size)
        {
            if (!_finished && _content.Length == 0) DetectByteOrderMarkAsync(CancellationToken.None).Wait();

            while (size + Index > _content.Length && !_finished) ReadIntoBuffer();
        }

        private void ReadIntoBuffer()
        {
            var returned = _baseStream.Read(_buffer, 0, BufferSize);
            AppendContentFromBuffer(returned);
        }

        private void AppendContentFromBuffer(int size)
        {
            _finished = size == 0;
            var charLength = _decoder.GetChars(_buffer, 0, size, _chars, 0);

            if (_confidence != EncodingConfidence.Certain) _raw.Write(_buffer, 0, size);

            _content.Append(_chars, 0, charLength);
        }
    }
}