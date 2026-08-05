#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.PdfSharpCore.Pdf
{
    /// <summary>
    /// Represents the pages of the document.
    /// </summary>
    [DebuggerDisplay("(PageCount={Count})")]
    internal sealed class PdfPages : PdfDictionary, IEnumerable<PdfPage>
    {
        internal PdfPages(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/Pages");
            Elements[Keys.Count] = new PdfInteger(0);
        }

        internal PdfPages(PdfDictionary dictionary)
            : base(dictionary)
        { }

        /// <summary>
        /// Gets the number of pages.
        /// </summary>
        public int Count
        {
            get { return PagesArray.Elements.Count; }
        }

        /// <summary>
        /// Gets the page with the specified index.
        /// </summary>
        public PdfPage this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException("index", index, PSSR.PageIndexOutOfRange);

                PdfDictionary dict = (PdfDictionary)((PdfReference)PagesArray.Elements[index]).Value;
                if (!(dict is PdfPage))
                    dict = new PdfPage(dict);
                return (PdfPage)dict;
            }
        }

        /// <summary>
        /// Finds a page by its id. Transforms it to PdfPage if necessary.
        /// </summary>
        internal PdfPage FindPage(PdfObjectID id)  // TODO: public?
        {
            PdfPage page = null;
            foreach (PdfItem item in PagesArray)
            {
                PdfReference reference = item as PdfReference;
                if (reference != null)
                {
                    PdfDictionary dictionary = reference.Value as PdfDictionary;
                    if (dictionary != null && dictionary.ObjectID == id)
                    {
                        page = dictionary as PdfPage ?? new PdfPage(dictionary);
                        break;
                    }
                }
            }
            return page;
        }

        /// <summary>
        /// Creates a new PdfPage, adds it to the end of this document, and returns it.
        /// </summary>
        public PdfPage Add()
        {
            PdfPage page = new PdfPage();
            Insert(Count, page);
            return page;
        }

        /// <summary>
        /// Creates a new PdfPage, inserts it at the specified position into this document, and returns it.
        /// </summary>
        public PdfPage Insert(int index)
        {
            PdfPage page = new PdfPage();
            Insert(index, page);
            return page;
        }

        /// <summary>
        /// Inserts the specified PdfPage at the specified position to this document and maybe returns a new PdfPage object.
        /// The value returned is a new object if the inserted page comes from a foreign document.
        /// </summary>
        public PdfPage Insert(int index, PdfPage page)
        {
            if (page == null)
                throw new ArgumentNullException("page");

            // Is the page already owned by this document?
            if (page.Owner == Owner)
            {
                // Case: Page is first removed and than inserted again, maybe at another position.
                int count = Count;
                // Check if page is not already part of the document.
                for (int idx = 0; idx < count; idx++)
                {
                    if (ReferenceEquals(this[idx], page))
                        throw new InvalidOperationException(PSSR.MultiplePageInsert);
                }

                // TODO: check this case
                // Because the owner of the inserted page is this document we assume that the page was former part of it 
                // and it is therefore well-defined.
                Owner._irefTable.Add(page);
                Debug.Assert(page.Owner == Owner);

                // Insert page in array.
                PagesArray.Elements.Insert(index, page.Reference);

                // Update page count.
                Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);

                return page;
            }

            // All new page insertions come here.
            if (page.Owner == null)
            {
                // Case: New page was newly created and inserted now.
                page.Document = Owner;

                Owner._irefTable.Add(page);
                Debug.Assert(page.Owner == Owner);
                PagesArray.Elements.Insert(index, page.Reference);
                Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
            }
            else
            {
                // Case: Page is from an external document. Importing pages requires the source document to
                // have been opened with PdfDocumentOpenMode.Import, which this reader-less fork (no PdfReader)
                // never does - so this path can never succeed and exists only to fail with a clear message.
                throw new InvalidOperationException("A PDF document must be opened with PdfDocumentOpenMode.Import to import pages from it.");
            }
            if (Owner.Settings.TrimMargins.AreSet)
                page.TrimMargins = Owner.Settings.TrimMargins;
            return page;
        }

        /// <summary>
        /// Removes the specified page from the document.
        /// </summary>
        public void Remove(PdfPage page)
        {
            PagesArray.Elements.Remove(page.Reference);
            Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
        }

        /// <summary>
        /// Removes the specified page from the document.
        /// </summary>
        public void RemoveAt(int index)
        {
            PagesArray.Elements.RemoveAt(index);
            Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
        }

        /// <summary>
        /// Moves a page within the page sequence.
        /// </summary>
        /// <param name="oldIndex">The page index before this operation.</param>
        /// <param name="newIndex">The page index after this operation.</param>
        public void MovePage(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Count)
                throw new ArgumentOutOfRangeException("oldIndex");
            if (newIndex < 0 || newIndex >= Count)
                throw new ArgumentOutOfRangeException("newIndex");
            if (oldIndex == newIndex)
                return;

            //PdfPage page = (PdfPage)pagesArray.Elements[oldIndex];
            PdfReference page = (PdfReference)_pagesArray.Elements[oldIndex];
            _pagesArray.Elements.RemoveAt(oldIndex);
            _pagesArray.Elements.Insert(newIndex, page);
        }

        /// <summary>
        /// Gets a PdfArray containing all pages of this document. The array must not be modified.
        /// </summary>
        public PdfArray PagesArray
        {
            get
            {
                if (_pagesArray == null)
                    _pagesArray = (PdfArray)Elements.GetValue(Keys.Kids, VCF.Create);
                return _pagesArray;
            }
        }
        PdfArray _pagesArray = null!;

        /// <summary>
        /// Prepares the document for saving.
        /// </summary>
        internal override void PrepareForSave()
        {
            // TODO: Close all open content streams

            // TODO: Create the page tree.
            // Arrays have a limit of 8192 entries, but I successfully tested documents
            // with 50000 pages and no page tree.
            // ==> wait for bug report.
            int count = _pagesArray.Elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                PdfPage page = this[idx];
                page.PrepareForSave();
            }
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        public new IEnumerator<PdfPage> GetEnumerator()
        {
            return new PdfPagesEnumerator(this);
        }

        class PdfPagesEnumerator : IEnumerator<PdfPage>
        {
            internal PdfPagesEnumerator(PdfPages list)
            {
                _list = list;
                _index = -1;
            }

            public bool MoveNext()
            {
                if (_index < _list.Count - 1)
                {
                    _index++;
                    _currentElement = _list[_index];
                    return true;
                }
                _index = _list.Count;
                return false;
            }

            public void Reset()
            {
                _currentElement = null;
                _index = -1;
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }

            public PdfPage Current
            {
                get
                {
                    if (_index == -1 || _index >= _list.Count)
                        throw new InvalidOperationException(PSSR.ListEnumCurrentOutOfRange);
                    return _currentElement!;
                }
            }

            public void Dispose()
            {
                // Nothing to do.
            }

            PdfPage? _currentElement;
            int _index;
            readonly PdfPages _list;
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : PdfPage.InheritablePageKeys
        {
            /// <summary>
            /// (Required) The type of PDF object that this dictionary describes; 
            /// must be Pages for a page tree node.
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "Pages")]
            public const string Type = "/Type";

            /// <summary>
            /// (Required except in root node; must be an indirect reference)
            /// The page tree node that is the immediate parent of this one.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Parent = "/Parent";

            /// <summary>
            /// (Required) An array of indirect references to the immediate children of this node.
            /// The children may be page objects or other page tree nodes.
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Required)]
            public const string Kids = "/Kids";

            /// <summary>
            /// (Required) The number of leaf nodes (page objects) that are descendants of this node 
            /// within the page tree.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string Count = "/Count";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static DictionaryMeta Meta
            {
                get { return _meta ?? (_meta = CreateMeta(typeof(Keys))); }
            }
            static DictionaryMeta _meta = null!;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
    }
}