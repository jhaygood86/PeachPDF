// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Filters;
using PeachDrawing.PDF.Engine.Internal;
using PeachDrawing.PDF.Engine.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Represents a PDF dictionary object.
/// </summary>
internal partial class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<string, PdfItem>>
{
    // Reference: 3.2.6  Dictionary Objects / Page 59

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionary"/> class.
    /// </summary>
    public PdfDictionary()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionary"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfDictionary(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Initializes a new instance from an existing dictionary. Used for object type transformation.
    /// </summary>
    protected PdfDictionary(PdfDictionary dict)
        : base(dict)
    {
        if (dict._elements is not null)
            dict._elements.ChangeOwner(this);
        if (dict._stream is not null)
            dict._stream.ChangeOwner(this);
    }

/// <summary>
    /// Gets the dictionary containing the elements of this dictionary.
    /// </summary>
    public DictionaryElements Elements => _elements ?? (_elements = new DictionaryElements(this));

    /// <summary>
    /// The elements of the dictionary.
    /// </summary>
    internal DictionaryElements _elements = null!;

    /// <summary>
    /// Returns an enumerator that iterates through the dictionary elements.
    /// </summary>
    public IEnumerator<KeyValuePair<string, PdfItem>> GetEnumerator()
    {
        return Elements.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

internal override void WriteObject(PdfWriter writer)
    {
        // /Length precedes the stream bytes and AES changes their count, so a stream is encrypted before anything is
        // written. What is changed on the dictionary for the write is put back afterwards, so saving a second time
        // encrypts the plaintext again rather than the ciphertext.
        PdfItem? plainLength = null;
        bool changedLength = false, addedCrypt = false;
        if (writer.Encryption is { } encryption && _stream is not null && IsIndirect)
        {
            if (this is PdfMetadataStream && encryption.LeavesMetadataPlain)
            {
                // ISO 32000-2, 7.6.6: a cleartext stream in an encrypted document names the Identity crypt filter.
                if (!Elements.ContainsKey(PdfStream.Keys.Filter))
                {
                    Elements.SetName(PdfStream.Keys.Filter, "/Crypt");
                    var parms = new PdfDictionary(_document);
                    parms.Elements.SetName("/Name", "/Identity");
                    Elements["/DecodeParms"] = parms;
                    addedCrypt = true;
                }
            }
            else
            {
                byte[] cipher = encryption.Encrypt(_stream.Value);
                plainLength = Elements[PdfStream.Keys.Length];
                Elements.SetInteger(PdfStream.Keys.Length, cipher.Length);
                changedLength = true;
                writer.EncryptedStreamBytes = cipher;
            }
        }

        try
        {
            writer.WriteBeginObject(this);
            PdfName[] keys = Elements.KeyNames;

            // TODO: automatically set length
            if (_stream is not null)
                Debug.Assert(Elements.ContainsKey(PdfStream.Keys.Length), "Dictionary has a stream but no length is set.");

            foreach (PdfName key in keys)
                WriteDictionaryElement(writer, key);
            if (Stream is not null)
                WriteDictionaryStream(writer);
            writer.WriteEndObject();
        }
        finally
        {
            writer.EncryptedStreamBytes = null;
            if (changedLength && plainLength is not null)
                Elements[PdfStream.Keys.Length] = plainLength;
            if (addedCrypt)
            {
                Elements.Remove(PdfStream.Keys.Filter);
                Elements.Remove("/DecodeParms");
            }
        }
    }

    /// <summary>
    /// Writes a key/value pair of this dictionary. This function is intended to be overridden
    /// in derived classes.
    /// </summary>
    internal virtual void WriteDictionaryElement(PdfWriter writer, PdfName key)
    {
        ArgumentNullException.ThrowIfNull(key);
        PdfItem? item = Elements[key];
        key.WriteObject(writer);
        item!.WriteObject(writer);
        writer.NewLine();
    }

    /// <summary>
    /// Writes the stream of this dictionary. This function is intended to be overridden
    /// in a derived class.
    /// </summary>
    internal virtual void WriteDictionaryStream(PdfWriter writer)
    {
        writer.WriteStream(this, (writer.Options & PdfWriterOptions.OmitStream) == PdfWriterOptions.OmitStream);
    }

    /// <summary>
    /// Gets or sets the PDF stream belonging to this dictionary. Returns null if the dictionary has
    /// no stream. To create the stream, call the CreateStream function.
    /// </summary>
    public PdfStream Stream
    {
        get => _stream;
        set => _stream = value;
    }
    PdfStream _stream = null!;

    /// <summary>
    /// Creates the stream of this dictionary and initializes it with the specified byte array.
    /// The function must not be called if the dictionary already has a stream.
    /// </summary>
    public PdfStream CreateStream(byte[] value)
    {
        if (_stream is not null)
            throw new InvalidOperationException("The dictionary already has a stream.");

        _stream = new PdfStream(value, this);
        // Always set the length.
        Elements[PdfStream.Keys.Length] = new PdfInteger(_stream.Length);
        return _stream;
    }

    /// <summary>
    /// When overridden in a derived class, gets the KeysMeta of this dictionary type.
    /// </summary>
    internal virtual PdfDictionaryMetadata? Meta => null;
}
