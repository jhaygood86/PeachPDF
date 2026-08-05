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
using PeachPDF.PdfSharpCore.Pdf.IO;
using System;
using System.Diagnostics;

namespace PeachPDF.PdfSharpCore.Pdf
{
    /// <summary>
    /// Base class of all composite PDF objects.
    /// </summary>
    internal abstract class PdfObject : PdfItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfObject"/> class.
        /// </summary>
        protected PdfObject()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfObject"/> class.
        /// </summary>
        protected PdfObject(PdfDocument document)
        {
            // Calling a virtual member in a constructor is dangerous.
            // In PDFsharp Document is overridden in PdfPage and the code is checked to be save
            // when called for a not completely initialized object.
            Document = document;
        }

        /// <summary>
        /// Initializes a new instance from an existing object. Used for object type transformation.
        /// </summary>
        protected PdfObject(PdfObject obj)
            : this(obj.Owner)
        {
            // If the object that was transformed to an instance of a derived class was an indirect object
            // set the value of the reference to this.
            if (obj._iref != null)
                obj._iref.Value = this;
#if DEBUG_  // BUG
            else
            {
                // If this occurs it is an internal error
                Debug.Assert(false, "Object type transformation must not be done with direct objects");
            }
#endif
        }

        /// <summary>
        /// Creates a copy of this object. The clone does not belong to a document, i.e. its owner and its iref are null.
        /// </summary>
        public new PdfObject Clone()
        {
            return (PdfObject)Copy();
        }

        /// <summary>
        /// Implements the copy mechanism. Must be overridden in derived classes.
        /// </summary>
        protected override object Copy()
        {
            PdfObject obj = (PdfObject)base.Copy();
            obj._document = null;
            obj._iref = null;
            return obj;
        }

#if true_  // works, but may lead to other problems that I cannot assess
        /// <summary>
        /// Determines whether the specified object is equal to the current PdfObject.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is PdfObject)
            {
                PdfObject other = (PdfObject)obj;
                // Take object type transformation into account
                if (_iref != null && other._iref != null)
                {
                    Debug.Assert(_iref.Value != null, "iref without value.");
                    Debug.Assert(other.iref.Value != null, "iref without value.");
                    return Object.ReferenceEquals(_iref.Value, other.iref.Value);
                }
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            if (_iref != null)
            {
                Debug.Assert(_iref.Value != null, "iref without value.");
                return _iref.GetHashCode();
            }
            return base.GetHashCode();
        }
#endif

        /// <summary>
        /// Sets the object and generation number.
        /// Setting the object identifier makes this object an indirect object, i.e. the object gets
        /// a PdfReference entry in the PdfReferenceTable.
        /// </summary>
        internal void SetObjectID(int objectNumber, int generationNumber)
        {
            PdfObjectID objectID = new PdfObjectID(objectNumber, generationNumber);

            // TODO: check imported
            if (_iref == null)
                _iref = _document._irefTable[objectID];
            if (_iref == null)
            {
                // ReSharper disable once ObjectCreationAsStatement because the new object is set to this object
                // in the constructor of PdfReference.
                new PdfReference(this);
                Debug.Assert(_iref != null);
                _iref.ObjectID = objectID;
            }
            _iref.Value = this;
            _iref.Document = _document;
        }

        /// <summary>
        /// Gets the PdfDocument this object belongs to.
        /// </summary>
        public virtual PdfDocument Owner
        {
            get { return _document; }
        }

        /// <summary>
        /// Sets the PdfDocument this object belongs to.
        /// </summary>
        internal virtual PdfDocument Document
        {
            set
            {
                if (!ReferenceEquals(_document, value))
                {
                    if (_document != null)
                        throw new InvalidOperationException("Cannot change document.");
                    _document = value;
                    if (_iref != null)
                        _iref.Document = value;
                }
            }
        }
        internal PdfDocument _document = null!;

        /// <summary>
        /// Indicates whether the object is an indirect object.
        /// </summary>
        public bool IsIndirect
        {
            // An object is an indirect object if and only if is has an indirect reference value.
            get { return _iref != null; }
        }

        /// <summary>
        /// Gets the PdfInternals object of this document, that grants access to some internal structures
        /// which are not part of the public interface of PdfDocument.
        /// </summary>
        public PdfObjectInternals Internals
        {
            get { return _internals ?? (_internals = new PdfObjectInternals(this)); }
        }
        PdfObjectInternals _internals = null!;

        /// <summary>
        /// When overridden in a derived class, prepares the object to get saved.
        /// </summary>
        internal virtual void PrepareForSave()
        { }

        /// <summary>
        /// Saves the stream position. 2nd Edition.
        /// </summary>
        internal override void WriteObject(PdfWriter writer)
        {
            Debug.Assert(false, "Must not come here!");
            //Debug.Assert(_inStreamOffset <= 0);
            //if (_inStreamOffset == 0)
            //{
            //    //_InStreamOffset = stream.Position;
            //    _document.xrefTable.AddObject(this);
            //    return Format("{0} {1} obj\n", _objectID, _generation);
            //}
            //else if (_inStreamOffset == -1)
            //{
            //}
            //return null;
        }

        /// <summary>
        /// Gets the object identifier. Returns PdfObjectID.Empty for direct objects,
        /// i.e. never returns null.
        /// </summary>
        internal PdfObjectID ObjectID
        {
            get { return _iref != null ? _iref.ObjectID : PdfObjectID.Empty; }
        }

        /// <summary>
        /// Gets the object number.
        /// </summary>
        internal int ObjectNumber
        {
            get { return ObjectID.ObjectNumber; }
        }

        /// <summary>
        /// Gets the generation number.
        /// </summary>
        internal int GenerationNumber
        {
            get { return ObjectID.GenerationNumber; }
        }

        /// <summary>
        /// Gets the indirect reference of this object. If the value is null, this object is a direct object.
        /// </summary>
        public PdfReference Reference
        {
            get { return _iref; }

            // Setting the reference outside PDFsharp is not considered as a valid operation.
            internal set { _iref = value; }
        }
        PdfReference _iref = null!;
    }
}