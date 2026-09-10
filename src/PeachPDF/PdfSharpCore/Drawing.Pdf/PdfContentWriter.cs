#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Accumulates PDF page/form content directly as raw bytes. PDF content operators
// are ASCII, while encoded text strings use the same byte-to-char identity mapping
// as PdfEncoders.RawEncoding.
//
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed class PdfContentWriter
    {
        private const int InitialChunkSize = 256;
        private const int DefaultChunkSize = 16 * 1024;

        private readonly int _initialChunkSize;
        private readonly int _maximumChunkSize;
        private readonly List<byte[]> _completedChunks = [];
        private StringBuilder? _formatBuffer;
        private byte[]? _currentChunk;
        private int _currentCount;
        private int _length;
        private int _nextChunkSize;

        public PdfContentWriter()
        {
            _initialChunkSize = InitialChunkSize;
            _maximumChunkSize = DefaultChunkSize;
            _nextChunkSize = InitialChunkSize;
        }

        public PdfContentWriter(int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize));

            _initialChunkSize = chunkSize;
            _maximumChunkSize = chunkSize;
            _nextChunkSize = chunkSize;
        }

        public int Length => _length;

        public PdfContentWriter Append(char value)
        {
            EnsureCurrentChunk();
            _currentChunk![_currentCount++] = (byte)value;
            _length++;
            return this;
        }

        public PdfContentWriter Append(string? value)
        {
            if (value is not null)
                Append(value.AsSpan());
            return this;
        }

        public PdfContentWriter Append(ReadOnlySpan<char> value)
        {
            while (!value.IsEmpty)
            {
                EnsureCurrentChunk();
                int count = Math.Min(value.Length, _currentChunk!.Length - _currentCount);
                Span<byte> destination = _currentChunk.AsSpan(_currentCount, count);
                for (int i = 0; i < count; i++)
                    destination[i] = (byte)value[i];

                _currentCount += count;
                _length += count;
                value = value[count..];
            }

            return this;
        }

        public PdfContentWriter Append(ReadOnlySpan<byte> value)
        {
            while (!value.IsEmpty)
            {
                EnsureCurrentChunk();
                int count = Math.Min(value.Length, _currentChunk!.Length - _currentCount);
                value[..count].CopyTo(_currentChunk.AsSpan(_currentCount));
                _currentCount += count;
                _length += count;
                value = value[count..];
            }

            return this;
        }

        public PdfContentWriter AppendFormat(IFormatProvider? provider, string format, params object?[] args)
        {
            StringBuilder buffer = PrepareFormatBuffer();
            try
            {
                buffer.AppendFormat(provider, format, args);
                AppendFormatBuffer(buffer);
                return this;
            }
            finally
            {
                buffer.Clear();
            }
        }

        public PdfContentWriter AppendFormat(IFormatProvider? provider, string format, object? arg0)
        {
            StringBuilder buffer = PrepareFormatBuffer();
            try
            {
                buffer.AppendFormat(provider, format, arg0);
                AppendFormatBuffer(buffer);
                return this;
            }
            finally
            {
                buffer.Clear();
            }
        }

        public PdfContentWriter AppendFormat(
            IFormatProvider? provider,
            string format,
            object? arg0,
            object? arg1)
        {
            StringBuilder buffer = PrepareFormatBuffer();
            try
            {
                buffer.AppendFormat(provider, format, arg0, arg1);
                AppendFormatBuffer(buffer);
                return this;
            }
            finally
            {
                buffer.Clear();
            }
        }

        public PdfContentWriter AppendFormat(
            IFormatProvider? provider,
            string format,
            object? arg0,
            object? arg1,
            object? arg2)
        {
            StringBuilder buffer = PrepareFormatBuffer();
            try
            {
                buffer.AppendFormat(provider, format, arg0, arg1, arg2);
                AppendFormatBuffer(buffer);
                return this;
            }
            finally
            {
                buffer.Clear();
            }
        }

        public byte[] ToArray()
        {
            if (_length == 0)
                return [];

            var result = new byte[_length];
            int offset = 0;
            foreach (byte[] chunk in _completedChunks)
            {
                chunk.CopyTo(result, offset);
                offset += chunk.Length;
            }

            if (_currentCount > 0)
                _currentChunk!.AsSpan(0, _currentCount).CopyTo(result.AsSpan(offset));

            return result;
        }

        public void Clear()
        {
            _completedChunks.Clear();
            _currentChunk = null;
            _currentCount = 0;
            _length = 0;
            _nextChunkSize = _initialChunkSize;
            _formatBuffer?.Clear();
        }

        public override string ToString()
            => PdfEncoders.RawEncoding.GetString(ToArray());

        private void EnsureCurrentChunk()
        {
            if (_currentChunk is not null && _currentCount < _currentChunk.Length)
                return;

            if (_currentChunk is not null)
                _completedChunks.Add(_currentChunk);

            _currentChunk = new byte[_nextChunkSize];
            _currentCount = 0;
            if (_nextChunkSize < _maximumChunkSize)
            {
                _nextChunkSize = _nextChunkSize <= _maximumChunkSize / 4
                    ? _nextChunkSize * 4
                    : _maximumChunkSize;
            }
        }

        private StringBuilder PrepareFormatBuffer()
        {
            _formatBuffer ??= new StringBuilder();
            _formatBuffer.Clear();
            return _formatBuffer;
        }

        private void AppendFormatBuffer(StringBuilder buffer)
        {
            foreach (ReadOnlyMemory<char> chunk in buffer.GetChunks())
                Append(chunk.Span);
        }
    }
}
