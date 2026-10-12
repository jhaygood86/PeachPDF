// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using PeachPDF.PDF.Advanced;
using PeachPDF.PDF.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a PDF array object.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay}")]
internal class PdfArray : PdfObject, IEnumerable<PdfItem>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfArray"/> class.
    /// </summary>
    public PdfArray()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfArray"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfArray(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfArray"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="items">The items.</param>
    public PdfArray(PdfDocument document, params PdfItem[] items)
        : base(document)
    {
        foreach (PdfItem item in items)
            Elements.Add(item);
    }

    /// <summary>
    /// Initializes a new instance from an existing dictionary. Used for object type transformation.
    /// </summary>
    /// <param name="array">The array.</param>
    protected PdfArray(PdfArray array)
        : base(array)
    {
        if (array._elements is not null)
            array._elements.ChangeOwner(this);
    }

    /// <summary>
    /// Creates a copy of this array. Direct elements are deep copied.
    /// Indirect references are not modified.
    /// </summary>
    public new PdfArray Clone()
    {
        return (PdfArray)Copy();
    }

    /// <summary>
    /// Implements the copy mechanism.
    /// </summary>
    protected override object Copy()
    {
        PdfArray array = (PdfArray)base.Copy();
        if (array._elements is not null)
        {
            array._elements = array._elements.Clone();
            int count = array._elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                PdfItem item = array._elements[idx];
                if (item is PdfObject)
                    array._elements[idx] = item.Clone();
            }
        }
        return array;
    }

    /// <summary>
    /// Gets the collection containing the elements of this object.
    /// </summary>
    public ArrayElements Elements => _elements ?? (_elements = new ArrayElements(this));

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    public virtual IEnumerator<PdfItem> GetEnumerator()
    {
        return Elements.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Returns a string with the content of this object in a readable form. Useful for debugging purposes only.
    /// </summary>
    public override string ToString()
    {
        StringBuilder pdf = new StringBuilder();
        pdf.Append("[ ");
        int count = Elements.Count;
        for (int idx = 0; idx < count; idx++)
            pdf.Append(Elements[idx] + " ");
        pdf.Append("]");
        return pdf.ToString();
    }

    internal override void WriteObject(PdfWriter writer)
    {
        writer.WriteBeginObject(this);
        int count = Elements.Count;
        for (int idx = 0; idx < count; idx++)
        {
            PdfItem value = Elements[idx];
            value.WriteObject(writer);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Represents the elements of an PdfArray.
    /// </summary>
    internal sealed class ArrayElements : IList<PdfItem>
    {
        internal ArrayElements(PdfArray array)
        {
            _elements = new List<PdfItem>();
            _ownerArray = array;
        }

        /// <summary>
        /// Creates a shallow copy of this object.
        /// </summary>
        public ArrayElements Clone()
        {
            ArrayElements elements = (ArrayElements)MemberwiseClone();
            elements._elements = new List<PdfItem>(elements._elements);
            elements._ownerArray = null;
            return elements;
        }

        /// <summary>
        /// Moves this instance to another array during object type transformation.
        /// </summary>
        internal void ChangeOwner(PdfArray array)
        {
            if (_ownerArray is not null)
            {
                // ???
            }

            // Set new owner.
            _ownerArray = array;

            // Set owners elements to this.
            array._elements = this;
        }

        /// <summary>
        /// Converts the specified value to boolean.
        /// If the value does not exist, the function returns false.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// If the index is out of range, the function throws an ArgumentOutOfRangeException.
        /// </summary>
        public bool GetBoolean(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            object obj = this[index];
            if (obj is null || obj is PdfNull)
                return false;

            PdfBoolean boolean = obj as PdfBoolean;
            if (boolean is not null)
                return boolean.Value;

            PdfBooleanObject booleanObject = obj as PdfBooleanObject;
            if (booleanObject is not null)
                return booleanObject.Value;

            throw new InvalidCastException("GetBoolean: Object is not a boolean.");
        }

        /// <summary>
        /// Converts the specified value to integer.
        /// If the value does not exist, the function returns 0.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// If the index is out of range, the function throws an ArgumentOutOfRangeException.
        /// </summary>
        public int GetInteger(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            object obj = this[index];
            if (obj is null || obj is PdfNull)
                return 0;

            PdfInteger integer = obj as PdfInteger;
            if (integer is not null)
                return integer.Value;

            PdfIntegerObject integerObject = obj as PdfIntegerObject;
            if (integerObject is not null)
                return integerObject.Value;

            throw new InvalidCastException("GetInteger: Object is not an integer.");
        }

        /// <summary>
        /// Converts the specified value to double.
        /// If the value does not exist, the function returns 0.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// If the index is out of range, the function throws an ArgumentOutOfRangeException.
        /// </summary>
        public double GetReal(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            object obj = this[index];
            if (obj is null || obj is PdfNull)
                return 0;

            PdfReal real = obj as PdfReal;
            if (real is not null)
                return real.Value;

            PdfRealObject realObject = obj as PdfRealObject;
            if (realObject is not null)
                return realObject.Value;

            PdfInteger integer = obj as PdfInteger;
            if (integer is not null)
                return integer.Value;

            PdfIntegerObject integerObject = obj as PdfIntegerObject;
            if (integerObject is not null)
                return integerObject.Value;

            throw new InvalidCastException("GetReal: Object is not a number.");
        }

        /// <summary>
        /// Converts the specified value to string.
        /// If the value does not exist, the function returns the empty string.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// If the index is out of range, the function throws an ArgumentOutOfRangeException.
        /// </summary>
        public string GetString(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            object obj = this[index];
            if (obj is null || obj is PdfNull)
                return string.Empty;

            PdfString str = obj as PdfString;
            if (str is not null)
                return str.Value;

            PdfStringObject strObject = obj as PdfStringObject;
            if (strObject is not null)
                return strObject.Value;

            throw new InvalidCastException("GetString: Object is not a string.");
        }

        /// <summary>
        /// Converts the specified value to a name.
        /// If the value does not exist, the function returns the empty string.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// If the index is out of range, the function throws an ArgumentOutOfRangeException.
        /// </summary>
        public string GetName(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            object obj = this[index];
            if (obj is null || obj is PdfNull)
                return string.Empty;

            PdfName name = obj as PdfName;
            if (name is not null)
                return name.Value;

            PdfNameObject nameObject = obj as PdfNameObject;
            if (nameObject is not null)
                return nameObject.Value;

            throw new InvalidCastException("GetName: Object is not a name.");
        }

        /// <summary>
        /// Gets the PdfObject with the specified index, or null, if no such object exists. If the index refers to
        /// a reference, the referenced PdfObject is returned.
        /// </summary>
        public PdfObject GetObject(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", index, "The index is out of range.");

            PdfItem item = this[index];
            PdfReference reference = item as PdfReference;
            if (reference is not null)
                return reference.Value;

            return item as PdfObject;
        }

        /// <summary>
        /// Gets the PdfArray with the specified index, or null, if no such object exists. If the index refers to
        /// a reference, the referenced PdfArray is returned.
        /// </summary>
        public PdfDictionary GetDictionary(int index)
        {
            return GetObject(index) as PdfDictionary;
        }

        /// <summary>
        /// Gets the PdfArray with the specified index, or null, if no such object exists. If the index refers to
        /// a reference, the referenced PdfArray is returned.
        /// </summary>
        public PdfArray GetArray(int index)
        {
            return GetObject(index) as PdfArray;
        }

        /// <summary>
        /// Gets the PdfReference with the specified index, or null, if no such object exists.
        /// </summary>
        public PdfReference GetReference(int index)
        {
            PdfItem item = this[index];
            return item as PdfReference;
        }

        /// <summary>
        /// Gets all items of this array.
        /// </summary>
        public PdfItem[] Items => _elements.ToArray();

        /// <summary>
        /// Returns false.
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets or sets an item at the specified index.
        /// </summary>
        /// <value></value>
        public PdfItem this[int index]
        {
            get => _elements[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _elements[index] = value;
            }
        }

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        public void RemoveAt(int index)
        {
            _elements.RemoveAt(index);
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the array/>.
        /// </summary>
        public bool Remove(PdfItem item)
        {
            return _elements.Remove(item);
        }

        /// <summary>
        /// Inserts the item the specified index.
        /// </summary>
        public void Insert(int index, PdfItem value)
        {
            _elements.Insert(index, value);
        }

        /// <summary>
        /// Determines whether the specified value is in the array.
        /// </summary>
        public bool Contains(PdfItem value)
        {
            return _elements.Contains(value);
        }

        /// <summary>
        /// Removes all items from the array.
        /// </summary>
        public void Clear()
        {
            _elements.Clear();
        }

        /// <summary>
        /// Gets the index of the specified item.
        /// </summary>
        public int IndexOf(PdfItem value)
        {
            return _elements.IndexOf(value);
        }

        /// <summary>
        /// Appends the specified object to the array.
        /// </summary>
        public void Add(PdfItem value)
        {
            // TODO: ??? 
            //Debug.Assert((value is PdfObject && ((PdfObject)value).Reference == null) | !(value is PdfObject),
            //  "You try to set an indirect object directly into an array.");

            PdfObject obj = value as PdfObject;
            if (obj is not null && obj.IsIndirect)
                _elements.Add(obj.Reference);
            else
                _elements.Add(value);
        }

        /// <summary>
        /// Returns false.
        /// </summary>
        public bool IsFixedSize => false;

        /// <summary>
        /// Returns false.
        /// </summary>
        public bool IsSynchronized => false;

        /// <summary>
        /// Gets the number of elements in the array.
        /// </summary>
        public int Count => _elements.Count;

        /// <summary>
        /// Copies the elements of the array to the specified array.
        /// </summary>
        public void CopyTo(PdfItem[] array, int index)
        {
            _elements.CopyTo(array, index);
        }

        /// <summary>
        /// The current implementation return null.
        /// </summary>
        public object SyncRoot => null;

        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        public IEnumerator<PdfItem> GetEnumerator()
        {
            return _elements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _elements.GetEnumerator();
        }

        /// <summary>
        /// The elements of the array.
        /// </summary>
        List<PdfItem> _elements = null!;

        /// <summary>
        /// The array this objects belongs to.
        /// </summary>
        PdfArray _ownerArray;
    }

    ArrayElements _elements = null!;

    /// <summary>
    /// Gets the DebuggerDisplayAttribute text.
    /// </summary>
    // ReSharper disable UnusedMember.Local
    string DebuggerDisplay
    // ReSharper restore UnusedMember.Local
    {
        get
        {
            return string.Format(CultureInfo.InvariantCulture, "array({0},[{1}])", $"id=({ObjectId})", _elements is null ? 0 : _elements.Count);

        }
    }
}
