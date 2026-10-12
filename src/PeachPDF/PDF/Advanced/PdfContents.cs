// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using PeachPDF.PDF.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents an array of PDF content streams of a page.
/// </summary>
internal sealed class PdfContents : PdfArray
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfContents"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfContents(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Appends a new content stream and returns it.
    /// </summary>
    public PdfContent AppendContent()
    {
        Debug.Assert(Owner is not null);

        SetModified();
        PdfContent content = new PdfContent(Owner);
        Owner._irefTable.Add(content);
        Debug.Assert(content.Reference is not null);
        Elements.Add(content.Reference);
        return content;
    }

    /// <summary>
    /// Prepends a new content stream and returns it.
    /// </summary>
    public PdfContent PrependContent()
    {
        Debug.Assert(Owner is not null);

        SetModified();
        PdfContent content = new PdfContent(Owner);
        Owner._irefTable.Add(content);
        Debug.Assert(content.Reference is not null);
        Elements.Insert(0, content.Reference);
        return content;
    }

    /// <summary>
    /// Replaces the current content of the page with the specified bytes.
    /// </summary>
    PdfContent ReplaceContent(byte[] contentBytes)
    {
        Debug.Assert(Owner is not null);

        PdfContent content = new PdfContent(Owner);

        content.CreateStream(contentBytes);

        Owner._irefTable.Add(content);
        Elements.Clear();
        Elements.Add(content.Reference);

        return content;
    }

    void SetModified()
    {
        if (!_modified)
        {
            _modified = true;
            int count = Elements.Count;

            if (count == 1)
            {
                PdfContent content = (PdfContent)((PdfReference)Elements[0]).Value;
                content.PreserveGraphicsState();
            }
            else if (count > 1)
            {
                // Surround content streams with q/Q operations
                byte[] value;
                int length;
                PdfContent content = (PdfContent)((PdfReference)Elements[0]).Value;
                if (content is not null && content.Stream is not null)
                {
                    length = content.Stream.Length;
                    value = new byte[length + 2];
                    value[0] = (byte)'q';
                    value[1] = (byte)'\n';
                    Array.Copy(content.Stream.Value, 0, value, 2, length);
                    content.Stream.Value = value;
                    content.Elements.SetInteger("/Length", length + 2);
                }
                content = (PdfContent)((PdfReference)Elements[count - 1]).Value;
                if (content is not null && content.Stream is not null)
                {
                    length = content.Stream.Length;
                    value = new byte[length + 3];
                    Array.Copy(content.Stream.Value, 0, value, 0, length);
                    value[length] = (byte)' ';
                    value[length + 1] = (byte)'Q';
                    value[length + 2] = (byte)'\n';
                    content.Stream.Value = value;
                    content.Elements.SetInteger("/Length", length + 3);
                }
            }
        }
    }
    bool _modified;

    internal override void WriteObject(PdfWriter writer)
    {
        // Save two bytes in PDF stream...
        if (Elements.Count == 1)
            Elements[0].WriteObject(writer);
        else
            base.WriteObject(writer);
    }

    /// <summary>
    /// Gets the enumerator.
    /// </summary>
    public new IEnumerator<PdfContent> GetEnumerator()
    {
        return new PdfPageContentEnumerator(this);
    }

    class PdfPageContentEnumerator : IEnumerator<PdfContent>
    {
        internal PdfPageContentEnumerator(PdfContents list)
        {
            _contents = list;
            _index = -1;
        }

        public bool MoveNext()
        {
            if (_index < _contents.Elements.Count - 1)
            {
                _index++;
                _currentElement = (PdfContent)((PdfReference)_contents.Elements[_index]).Value;
                return true;
            }
            _index = _contents.Elements.Count;
            return false;
        }

        public void Reset()
        {
            _currentElement = null;
            _index = -1;
        }

        object IEnumerator.Current
        {
            get => Current;
        }

        public PdfContent Current
        {
            get
            {
                if (_index == -1 || _index >= _contents.Elements.Count)
                    throw new InvalidOperationException("Enumeration out of range.");
                return _currentElement!;
            }
        }

        public void Dispose()
        {
            // Nothing to do.
        }

        PdfContent _currentElement = null!;
        int _index;
        readonly PdfContents _contents;
    }
}
