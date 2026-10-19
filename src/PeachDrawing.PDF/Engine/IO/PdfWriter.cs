// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Internal;
using PeachDrawing.PDF.Engine.Security;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PeachDrawing.PDF.Engine.IO;

/// <summary>
/// Represents a writer for generation of PDF streams. 
/// </summary>
internal sealed class PdfWriter
{
    public PdfWriter(Stream pdfStream)
    {
        _stream = pdfStream;
    }

    public void Close(bool closeUnderlyingStream)
    {
        if (_stream is not null && closeUnderlyingStream)
            _stream.Dispose();
        _stream = null!;
    }

    public void Close()
    {
        Close(true);
    }

    public long Position => _stream.Position;

    public PdfWriterOptions Options
    {
        get => _options;
        set => _options = value;
    }
    PdfWriterOptions _options;

    // -----------------------------------------------------------

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(bool value)
    {
        WriteSeparator(CharCat.Character);
        WriteRaw(value ? bool.TrueString : bool.FalseString);
        _lastCat = CharCat.Character;
    }

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(PdfBoolean value)
    {
        WriteSeparator(CharCat.Character);
        WriteRaw(value.Value ? "true" : "false");
        _lastCat = CharCat.Character;
    }

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(int value) => WriteNumber(value);

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(long value) => WriteNumber(value);

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(uint value) => WriteNumber(value);

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(PdfInteger value) => WriteNumber(value.Value);



    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(double value) => WriteNumber(value, PdfNumberFormats.SevenDecimals);

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(PdfReal value) => WriteNumber(value.Value, PdfNumberFormats.SevenDecimals);

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(PdfString value)
    {
        WriteSeparator(CharCat.Delimiter);
        PdfStringEncoding encoding = (PdfStringEncoding)(value.Flags & PdfStringFlags.EncodingMask);
        bool hex = (value.Flags & PdfStringFlags.HexLiteral) != 0;
        if (StringsAreEncrypted)
        {
            // A Unicode string's byte-order mark is part of what the reader decrypts, so it goes in before encryption.
            byte[] plain = value.EncryptionValue;
            if (encoding == PdfStringEncoding.Unicode)
                plain = [0xFE, 0xFF, .. plain];
            WriteEncryptedString(plain);
            return;
        }
        Write(PdfEncoders.FormatStringLiteral(value.EncryptionValue, encoding == PdfStringEncoding.Unicode, true, hex));
        _lastCat = CharCat.Delimiter;
    }

    /// <summary>
    /// The handler that encrypts what this writer writes, or null when the document is not encrypted.
    /// </summary>
    public PdfAes256SecurityHandler? Encryption { get; set; }

    // Only strings inside an indirect object are encrypted: the trailer (/ID) and the direct /Encrypt dictionary in it are not.
    bool _inIndirectObject;

    bool StringsAreEncrypted => Encryption is not null && _inIndirectObject;

    // Ciphertext is binary, and hexadecimal is the one literal form that carries it without escaping rules.
    void WriteEncryptedString(byte[] plain)
    {
        Write(PdfEncoders.FormatStringLiteral(Encryption!.Encrypt(plain), false, false, true));
        _lastCat = CharCat.Delimiter;
    }

    /// <summary>
    /// Writes the specified value to the PDF stream.
    /// </summary>
    public void Write(PdfName value)
    {
        WriteSeparator(CharCat.Delimiter, '/');
        string name = value.Value;

        // Every character that needs escaping takes three bytes: '#' and two hexadecimal digits.
        byte[]? rented = null;
        int capacity = 1 + (name.Length * 3);
        Span<byte> buffer = capacity <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(capacity));
        try
        {
            buffer[0] = (byte)'/';
            int length = 1;
            for (int idx = 1; idx < name.Length; idx++)
            {
                char ch = name[idx];
                Debug.Assert(ch < 256);
                // TODO: is this all?
                if (ch > ' ' && ch is not ('%' or '/' or '<' or '>' or '(' or ')' or '#'))
                {
                    buffer[length++] = (byte)ch;
                }
                else
                {
                    buffer[length++] = (byte)'#';
                    buffer[length++] = (byte)"0123456789ABCDEF"[(ch >> 4) & 0xF];
                    buffer[length++] = (byte)"0123456789ABCDEF"[ch & 0xF];
                }
            }
            _stream.Write(buffer[..length]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
        _lastCat = CharCat.Character;
    }

    public void Write(PdfLiteral value)
    {
        WriteSeparator(CharCat.Character);
        WriteRaw(value.Value);
        _lastCat = CharCat.Character;
    }

    public void Write(PdfRectangle rect)
    {
        WriteSeparator(CharCat.Delimiter, '/');
        WriteRaw(string.Create(CultureInfo.InvariantCulture, $"[{rect.X1:0.###} {rect.Y1:0.###} {rect.X2:0.###} {rect.Y2:0.###}]"));
        _lastCat = CharCat.Delimiter;
    }

    public void Write(PdfReference iref)
    {
        WriteSeparator(CharCat.Character);
        WriteRaw(iref.ToString());
        _lastCat = CharCat.Character;
    }

    public void WriteDocString(string text)
    {
        WriteSeparator(CharCat.Delimiter);
        byte[] bytes = PdfEncoders.PdfDocumentEncoding.GetBytes(text);
        if (StringsAreEncrypted)
        {
            WriteEncryptedString(bytes);
            return;
        }
        bytes = PdfEncoders.FormatStringLiteral(bytes, false, false, false);
        Write(bytes);
        _lastCat = CharCat.Delimiter;
    }

    /// <summary>
    /// Begins a direct or indirect dictionary or array.
    /// </summary>
    public void WriteBeginObject(PdfObject obj)
    {
        bool indirect = obj.IsIndirect;
        if (indirect)
        {
            WriteObjectAddress(obj);
            _inIndirectObject = true;
        }
        _stack.Add(new StackItem(obj));
        if (indirect)
        {
            if (obj is PdfArray)
                WriteRaw("[\n");
            else if (obj is PdfDictionary)
                WriteRaw("<<\n");
            _lastCat = CharCat.NewLine;
        }
        else
        {
            if (obj is PdfArray)
            {
                WriteSeparator(CharCat.Delimiter);
                WriteRaw('[');
                _lastCat = CharCat.Delimiter;
            }
            else if (obj is PdfDictionary)
            {
                NewLine();
                WriteSeparator(CharCat.Delimiter);
                WriteRaw("<<\n");
                _lastCat = CharCat.NewLine;
            }
        }
    }

    /// <summary>
    /// Ends a direct or indirect dictionary or array.
    /// </summary>
    public void WriteEndObject()
    {
        int count = _stack.Count;
        Debug.Assert(count > 0, "PdfWriter stack underflow.");

        StackItem stackItem = _stack[count - 1];
        _stack.RemoveAt(count - 1);

        PdfObject value = stackItem.Object;
        bool indirect = value.IsIndirect;
        if (value is PdfArray)
        {
            if (indirect)
            {
                WriteRaw("\n]\n");
                _lastCat = CharCat.NewLine;
            }
            else
            {
                WriteRaw("]");
                _lastCat = CharCat.Delimiter;
            }
        }
        else if (value is PdfDictionary)
        {
            if (indirect)
            {
                if (!stackItem.HasStream)
                    WriteRaw(_lastCat == CharCat.NewLine ? ">>\n" : " >>\n");
            }
            else
            {
                Debug.Assert(!stackItem.HasStream, "Direct object with stream??");
                WriteSeparator(CharCat.NewLine);
                WriteRaw(">>\n");
                _lastCat = CharCat.NewLine;
            }
        }
        if (indirect)
        {
            NewLine();
            WriteRaw("endobj\n");
            _inIndirectObject = false;
        }
    }

    /// <summary>
    /// Writes a number with the invariant culture, without allocating a string.
    /// </summary>
    private void WriteNumber<T>(T value, string? format = null) where T : ISpanFormattable, IUtf8SpanFormattable
    {
        WriteSeparator(CharCat.Character);
        Span<byte> buffer = stackalloc byte[64];
        if (value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture))
            _stream.Write(buffer[..written]);
        else
            WriteRaw(value.ToString(format, CultureInfo.InvariantCulture));
        _lastCat = CharCat.Character;
    }

    /// <summary>
    /// Writes the characters as Latin-1 bytes.
    /// </summary>
    public void WriteRaw(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return;

        byte[]? rented = null;
        Span<byte> bytes = text.Length <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(text.Length));
        try
        {
            int count = PdfEncoders.LatinOneByteEncoding.GetBytes(text, bytes);
            _stream.Write(bytes[..count]);
            _lastCat = GetCategory((char)bytes[count - 1]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// The ciphertext to write in place of the current dictionary's stream bytes, set by the dictionary that encrypted them
    /// (its /Length has to describe the ciphertext, and /Length is written before the bytes).
    /// </summary>
    internal byte[]? EncryptedStreamBytes { get; set; }

    /// <summary>
    /// Writes the stream of the specified dictionary.
    /// </summary>
    public void WriteStream(PdfDictionary value, bool omitStream)
    {
        StackItem stackItem = _stack[_stack.Count - 1];
        Debug.Assert(stackItem.Object is PdfDictionary);
        Debug.Assert(stackItem.Object.IsIndirect);
        stackItem.HasStream = true;

        WriteRaw(_lastCat == CharCat.NewLine ? ">>\nstream\n" : " >>\nstream\n");

        if (omitStream)
        {
            WriteRaw("  ...stream content omitted...\n");  // useful for debugging only
        }
        else
        {
            byte[] bytes = EncryptedStreamBytes ?? value.Stream.Value;
            if (bytes.Length != 0)
            {
                Write(bytes);
                // Unconditional, not "only if the last byte doesn't already look like a newline" -
                // stream content is frequently binary (FlateDecode-compressed data in particular),
                // where a content BYTE can coincidentally equal 0x0A. Skipping the marker in that
                // case leaves nothing but that content byte between the data and "endstream", which
                // a strict reader (ISO 19005 §6.1.7.1 - Length shall match the byte count between
                // the stream's own EOL and the (uncounted) EOL before endstream) then misattributes
                // as the delimiter, not as the last byte of content it actually is - producing a
                // Length/actual-byte-count mismatch of exactly one byte. Always writing our own
                // marker removes the ambiguity entirely, at the cost of an occasional harmless
                // extra blank line when content already ended in one.
                WriteRaw('\n');
            }
        }
        WriteRaw("endstream\n");
    }

    public void WriteRaw(string rawString)
    {
        if (string.IsNullOrEmpty(rawString))
            return;

        WriteRaw(rawString.AsSpan());
    }

    public void WriteRaw(char ch)
    {
        Debug.Assert(ch < 256, "Raw character greater than 255 detected.");

        _stream.WriteByte((byte)ch);
        _lastCat = GetCategory(ch);
    }

    public void Write(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return;

        _stream.Write(bytes, 0, bytes.Length);
        _lastCat = GetCategory((char)bytes[bytes.Length - 1]);
    }

    void WriteObjectAddress(PdfObject value)
    {
        PdfObjectId identity = value.ObjectId;
        WriteRaw(string.Create(CultureInfo.InvariantCulture, $"{identity.ObjectNumber} {identity.GenerationNumber} obj\n"));
    }

    public void WriteFileHeader(PdfDocument document)
    {
        int version = document._version;
        WriteRaw(string.Create(CultureInfo.InvariantCulture, $"%PDF-{version / 10}.{version % 10}\n%\xD3\xF4\xCC\xE1\n"));
    }

    public void WriteEof(PdfDocument document, long startxref)
    {
        WriteRaw("startxref\n");
        WriteRaw(startxref.ToString(CultureInfo.InvariantCulture));
        WriteRaw("\n%%EOF\n");
    }

    void WriteSeparator(CharCat cat, char ch)
    {
        if (_lastCat == CharCat.Character)
            _stream.WriteByte((byte)' ');
    }

    void WriteSeparator(CharCat cat)
    {
        WriteSeparator(cat, '\0');
    }

    public void NewLine()
    {
        if (_lastCat != CharCat.NewLine)
            WriteRaw('\n');
    }

    CharCat GetCategory(char ch)
    {
        if (ch == '(' || ch == ')' || ch == '<' || ch == '>' ||
            ch == '[' || ch == ']' || ch == '{' || ch == '}' ||
            ch == '/' || ch == '%')
            return CharCat.Delimiter;
        if (ch == '\n')
            return CharCat.NewLine;
        return CharCat.Character;
    }

    enum CharCat
    {
        NewLine,
        Character,
        Delimiter,
    };
    CharCat _lastCat;

    /// <summary>
    /// Gets the underlying stream.
    /// </summary>
    internal Stream Stream => _stream;
    Stream _stream;

    class StackItem
    {
        public StackItem(PdfObject value)
        {
            Object = value;
        }

        public readonly PdfObject Object;
        public bool HasStream;
    }

    readonly List<StackItem> _stack = [];
}
