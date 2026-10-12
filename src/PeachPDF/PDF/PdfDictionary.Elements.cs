// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using System.Numerics;
using PeachPDF.PDF.Drawing;
using PeachPDF.PDF.Advanced;
using PeachPDF.PDF.Filters;
using PeachPDF.PDF.Internal;
using PeachPDF.PDF.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using static System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes;

namespace PeachPDF.PDF;

internal partial class PdfDictionary
{
    /// <summary>
    /// Represents the interface to the elements of a PDF dictionary.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal sealed class DictionaryElements : IDictionary<string, PdfItem>, ICloneable
    {
        internal DictionaryElements(PdfDictionary ownerDictionary)
        {
            _elements = new Dictionary<string, PdfItem>();
            _ownerDictionary = ownerDictionary;
        }

        object ICloneable.Clone()
        {
            DictionaryElements dictionaryElements = (DictionaryElements)MemberwiseClone();
            dictionaryElements._elements = new Dictionary<string, PdfItem>(dictionaryElements._elements);
            dictionaryElements._ownerDictionary = null;
            return dictionaryElements;
        }

        /// <summary>
        /// Creates a shallow copy of this object. The clone is not owned by a dictionary anymore.
        /// </summary>
        public DictionaryElements Clone()
        {
            return (DictionaryElements)((ICloneable)this).Clone();
        }

        /// <summary>
        /// Moves this instance to another dictionary during object type transformation.
        /// </summary>
        internal void ChangeOwner(PdfDictionary ownerDictionary)
        {
            if (_ownerDictionary is not null)
            {
                // ???
            }

            // Set new owner.
            _ownerDictionary = ownerDictionary;

            // Set owners elements to this.
            ownerDictionary._elements = this;
        }

        /// <summary>
        /// Gets the dictionary to which this elements object belongs to.
        /// </summary>
        internal PdfDictionary Owner => _ownerDictionary;

        /// <summary>
        /// Follows an indirect reference to the object it points at; any other item is returned as it is.
        /// </summary>
        private static PdfItem? Unwrapped(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        private static bool TryReadBoolean(PdfItem? item, out bool value)
        {
            switch (item)
            {
                case PdfBoolean boolean:
                    value = boolean.Value;
                    return true;
                case PdfBooleanObject booleanObject:
                    value = booleanObject.Value;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private static bool TryReadInteger(PdfItem? item, out int value)
        {
            switch (item)
            {
                case PdfInteger integer:
                    value = integer.Value;
                    return true;
                case PdfIntegerObject integerObject:
                    value = integerObject.Value;
                    return true;
                case PdfUInteger unsignedInteger:
                    value = (int)unsignedInteger.Value;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryReadReal(PdfItem? item, out double value)
        {
            switch (item)
            {
                case PdfReal real:
                    value = real.Value;
                    return true;
                case PdfRealObject realObject:
                    value = realObject.Value;
                    return true;
                case PdfInteger integer:
                    value = integer.Value;
                    return true;
                case PdfIntegerObject integerObject:
                    value = integerObject.Value;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryReadString(PdfItem? item, [NotNullWhen(true)] out string? value)
        {
            switch (item)
            {
                case PdfString text:
                    value = text.Value;
                    return true;
                case PdfStringObject textObject:
                    value = textObject.Value;
                    return true;
                case PdfName name:
                    value = name.Value;
                    return true;
                case PdfNameObject nameObject:
                    value = nameObject.Value;
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        /// <summary>
        /// Converts the specified value to boolean.
        /// If the value does not exist, the function returns false.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public bool GetBoolean(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                if (create)
                    this[key] = new PdfBoolean();
                return false;
            }

            return TryReadBoolean(Unwrapped(item), out bool value)
                ? value
                : throw new InvalidCastException("GetBoolean: Object is not a boolean.");
        }

        /// <summary>
        /// Tries to convert the specified value to boolean. Returns false if the key is missing or the value has the wrong type.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value) => TryReadBoolean(Unwrapped(this[key]), out value);

        /// <summary>
        /// Sets the entry to a direct boolean value.
        /// </summary>
        public void SetBoolean(string key, bool value)
        {
            this[key] = new PdfBoolean(value);
        }

        /// <summary>
        /// Converts the specified value to integer.
        /// If the value does not exist, the function returns 0.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public int GetInteger(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                if (create)
                    this[key] = new PdfInteger();
                return 0;
            }

            if (item is PdfNull)
                return 0;

            return TryReadInteger(Unwrapped(item), out int value)
                ? value
                : throw new InvalidCastException("GetInteger: Object is not an integer.");
        }

        /// <summary>
        /// Tries to convert the specified value to integer. Returns false if the key is missing or the value has the wrong type.
        /// </summary>
        public bool TryGetInteger(string key, out int value)
        {
            PdfItem? item = this[key];
            if (item is null or PdfNull)
            {
                value = 0;
                return false;
            }

            return TryReadInteger(Unwrapped(item), out value);
        }

        /// <summary>
        /// Sets the entry to a direct integer value.
        /// </summary>
        public void SetInteger(string key, int value)
        {
            this[key] = new PdfInteger(value);
        }

        /// <summary>
        /// Converts the specified value to double.
        /// If the value does not exist, the function returns 0.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public double GetReal(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                if (create)
                    this[key] = new PdfReal();
                return 0;
            }

            return TryReadReal(Unwrapped(item), out double value)
                ? value
                : throw new InvalidCastException("GetReal: Object is not a number.");
        }

        /// <summary>
        /// Tries to convert the specified value to double. Returns false if the key is missing or the value has the wrong type.
        /// </summary>
        public bool TryGetReal(string key, out double value) => TryReadReal(Unwrapped(this[key]), out value);

        /// <summary>
        /// Sets the entry to a direct real value.
        /// </summary>
        public void SetReal(string key, double value)
        {
            this[key] = new PdfReal(value);
        }

        /// <summary>
        /// Converts the specified value to string.
        /// If the value does not exist, the function returns the empty string.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public string GetString(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                if (create)
                    this[key] = new PdfString();
                return "";
            }

            return TryReadString(Unwrapped(item), out string? value)
                ? value
                : throw new InvalidCastException("GetString: Object is not a string.");
        }

        /// <summary>
        /// Tries to convert the specified value to string. Returns false if the key is missing or the value has the wrong type.
        /// </summary>
        public bool TryGetString(string key, [NotNullWhen(true)] out string? value) => TryReadString(Unwrapped(this[key]), out value);

        /// <summary>
        /// Sets the entry to a direct string value.
        /// </summary>
        public void SetString(string key, string value)
        {
            this[key] = new PdfString(value);
        }

        /// <summary>
        /// Sets the entry to a direct string value with the specified encoding.
        /// </summary>
        public void SetString(string key, string value, PdfStringEncoding encoding)
        {
            this[key] = new PdfString(value, encoding);
        }

        /// <summary>
        /// Converts the specified value to a name.
        /// If the value does not exist, the function returns the empty string.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public string GetName(string key)
        {
            PdfItem? item = Unwrapped(this[key]);
            return item switch
            {
                null => string.Empty,
                PdfName name => name.Value,
                PdfNameObject nameObject => nameObject.Value,
                _ => throw new InvalidCastException("GetName: Object is not a name."),
            };
        }

        /// <summary>
        /// Sets the specified name value.
        /// If the value doesn't start with a slash, it is added automatically.
        /// </summary>
        public void SetName(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Length == 0 || value[0] != '/')
                value = "/" + value;

            this[key] = new PdfName(value);
        }

        /// <summary>
        /// Converts the specified value to PdfRectangle.
        /// If the value does not exist, the function returns an empty rectangle.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public PdfRectangle GetRectangle(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                var empty = new PdfRectangle();
                if (create)
                    this[key] = empty;
                return empty;
            }

            item = Unwrapped(item);
            if (item is PdfArray { Elements.Count: 4 } array)
            {
                var rectangle = new PdfRectangle(array.Elements.GetReal(0), array.Elements.GetReal(1),
                    array.Elements.GetReal(2), array.Elements.GetReal(3));
                this[key] = rectangle;
                return rectangle;
            }

            return (PdfRectangle)item!;
        }

        /// <summary>
        /// Tries to convert the specified value to PdfRectangle. Returns false if the key is missing or the value has the wrong type.
        /// </summary>
        public bool TryGetRectangle(string key, out PdfRectangle value)
        {
            value = new PdfRectangle();
            PdfItem? item = Unwrapped(this[key]);
            switch (item)
            {
                case PdfArray { Elements.Count: 4 } array:
                    value = new PdfRectangle(array.Elements.GetReal(0), array.Elements.GetReal(1),
                        array.Elements.GetReal(2), array.Elements.GetReal(3));
                    return true;
                case PdfRectangle rectangle:
                    value = rectangle;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Sets the entry to a direct rectangle value.
        /// </summary>
        public void SetRectangle(string key, PdfRectangle rect)
        {
            _elements[key] = rect;
        }

        /// <summary>
        /// Converts the specified value to a matrix.
        /// If the value does not exist, the function returns the identity matrix.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public Matrix3x2 GetMatrix(string key, bool create = false)
        {
            PdfItem? item = this[key];
            if (item is null)
            {
                if (create)
                    this[key] = new PdfLiteral("[1 0 0 1 0 0]");  // cannot be parsed, implement a PdfMatrix...
                return Matrix3x2.Identity;
            }

            return Unwrapped(item) switch
            {
                PdfArray { Elements.Count: 6 } array => PdfMatrix.Create(array.Elements.GetReal(0), array.Elements.GetReal(1),
                    array.Elements.GetReal(2), array.Elements.GetReal(3), array.Elements.GetReal(4), array.Elements.GetReal(5)),
                PdfLiteral => throw new NotImplementedException("Parsing matrix from literal."),
                _ => throw new InvalidCastException("Element is not an array with 6 values."),
            };
        }

        /// <summary>
        /// Sets the entry to a direct matrix value.
        /// </summary>
        public void SetMatrix(string key, Matrix3x2 matrix)
        {
            _elements[key] = PdfLiteral.FromMatrix(matrix);
        }

        /// <summary>
        /// Converts the specified value to DateTime.
        /// If the value does not exist, or is a date string (which is not parsed), the function returns the default value.
        /// If the value is not convertible, the function throws an InvalidCastException.
        /// </summary>
        public DateTime GetDateTime(string key, DateTime defaultValue)
        {
            return Unwrapped(this[key]) switch
            {
                null => defaultValue,
                PdfDate date => date.Value,
                PdfString or PdfStringObject => defaultValue,
                _ => throw new InvalidCastException("GetDateTime: Object is not a string or date."),
            };
        }

        /// <summary>
        /// Sets the entry to a direct date value.
        /// </summary>
        public void SetDateTime(string key, DateTime value)
        {
            _elements[key] = new PdfDate(value);
        }

        /// <summary>
        /// Reads an enumeration value stored as a name; the default is returned when the key is missing.
        /// </summary>
        internal TEnum GetEnumFromName<TEnum>(string key, TEnum defaultValue) where TEnum : struct, Enum
        {
            PdfItem? item = this[key];
            return item is null ? defaultValue : Enum.Parse<TEnum>(item.ToString()!.AsSpan(1), false);
        }

        /// <summary>
        /// Stores an enumeration value as a name.
        /// </summary>
        internal void SetEnumAsName<TEnum>(string key, TEnum value) where TEnum : struct, Enum
        {
            _elements[key] = new PdfName("/" + value);
        }

        /// <summary>
        /// Gets the value for the specified key. If the value does not exist, it is optionally created.
        /// </summary>
        public PdfItem GetValue(string key, ValueCreationFlags options)
        {
            PdfItem value = this[key];

            if (value is null)
                return options == ValueCreationFlags.None ? null : CreateMissingValue(key, options);

            if (value is PdfReference iref)
                return ResolveIndirectValue(key, iref);

            // The value exists and can be returned as-is unless it needs object-type coercion.
            if (value is PdfDictionary dict)
                return CoerceDictionary(key, dict);

            if (value is PdfArray array)
                return CoerceArray(key, array);

            return value;
        }

        /// <summary>
        /// Creates and stores the value for a key that has no value yet, per GetValue's options.
        /// </summary>
        PdfItem CreateMissingValue(string key, ValueCreationFlags options)
        {
            Type type = GetValueType(key) ?? throw new NotImplementedException("Cannot create value for key: " + key);

            Debug.Assert(typeof(PdfItem).IsAssignableFrom(type), "Type not allowed.");

            PdfObject obj = typeof(PdfDictionary).IsAssignableFrom(type) ? CreateDictionary(type, null)
                : typeof(PdfArray).IsAssignableFrom(type) ? CreateArray(type, null)
                : throw new NotImplementedException("Type other than array or dictionary.");

            if (options == ValueCreationFlags.CreateIndirect)
            {
                _ownerDictionary.Owner._irefTable.Add(obj);
                this[key] = obj.Reference;
            }
            else
                this[key] = obj;

            return obj;
        }

        /// <summary>
        /// Resolves an indirect reference value, coercing it to the key's declared type if necessary.
        /// </summary>
        PdfItem ResolveIndirectValue(string key, PdfReference iref)
        {
            // If we come here PDF file is corrupted.
            PdfItem value = iref.Value ?? throw new InvalidOperationException("Indirect reference without value.");

            Type type = GetValueType(key);
            Debug.Assert(type is not null, "No value type specified in meta information. Please send this file to PDFsharp support.");

            if (type is null || type == value.GetType())
                return value;

            if (typeof(PdfDictionary).IsAssignableFrom(type))
            {
                Debug.Assert(value is PdfDictionary, "Bug in PeachPDF.PDF. Please send this file to PDFsharp support.");
                return CreateDictionary(type, (PdfDictionary)value);
            }

            if (typeof(PdfArray).IsAssignableFrom(type))
            {
                Debug.Assert(value is PdfArray, "Bug in PeachPDF.PDF. Please send this file to PDFsharp support.");
                return CreateArray(type, (PdfArray)value);
            }

            throw new NotImplementedException("Type other than array or dictionary.");
        }

        PdfDictionary CoerceDictionary(string key, PdfDictionary dict)
        {
            Debug.Assert(!dict.IsIndirect);

            Type type = GetValueType(key);
            Debug.Assert(type is not null, "No value type specified in meta information. Please send this file to PDFsharp support.");
            return dict.GetType() != type ? CreateDictionary(type, dict) : dict;
        }

        PdfArray CoerceArray(string key, PdfArray array)
        {
            Debug.Assert(!array.IsIndirect);

            // This is more complicated. If type is null do nothing.
            Type type = GetValueType(key);
            return type is not null && type != array.GetType() ? CreateArray(type, array) : array;
        }

        /// <summary>
        /// Short cut for GetValue(key, ValueCreationFlags.None).
        /// </summary>
        public PdfItem GetValue(string key)
        {
            return GetValue(key, ValueCreationFlags.None);
        }

        /// <summary>
        /// Returns the type of the object to be created as value of the specified key.
        /// </summary>
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type GetValueType(string key)  // TODO: move to PdfObject
        {
            Type type = null;
            PdfDictionaryMetadata meta = _ownerDictionary.Meta;
            if (meta is not null)
            {
                PdfKeyDescriptor kd = meta[key];
                if (kd is not null)
                    type = kd.GetValueType();
                //else
                //    Debug.WriteLine("Warning: Key not descriptor table: " + key);  // TODO: check what this means...
            }
            //else
            //    Debug.WriteLine("Warning: No meta provided for type: " + _owner.GetType().Name);  // TODO: check what this means...
            return type;
        }

        PdfArray CreateArray([DynamicallyAccessedMembers(PublicConstructors | NonPublicConstructors)] Type type, PdfArray? oldArray) =>
            oldArray is null
                ? Instantiate<PdfArray>(type, _ownerDictionary.Owner, typeof(PdfDocument))
                : Instantiate<PdfArray>(type, oldArray, typeof(PdfArray));

        PdfDictionary CreateDictionary([DynamicallyAccessedMembers(PublicConstructors | NonPublicConstructors)] Type type, PdfDictionary? oldDictionary) =>
            oldDictionary is null
                ? Instantiate<PdfDictionary>(type, _ownerDictionary.Owner, typeof(PdfDocument))
                : Instantiate<PdfDictionary>(type, oldDictionary, typeof(PdfDictionary));

        /// <summary>
        /// Creates the value type a key is declared with, through its (possibly non-public) constructor taking one argument.
        /// </summary>
        static TItem Instantiate<TItem>([DynamicallyAccessedMembers(PublicConstructors | NonPublicConstructors)] Type type, object argument, Type argumentType)
            where TItem : PdfObject
        {
            ConstructorInfo? constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [argumentType], null);
            Debug.Assert(constructor is not null, "No appropriate constructor found for type: " + type.Name);
            return (TItem)(constructor ?? throw new InvalidOperationException("No appropriate constructor found for type: " + type.Name))
                .Invoke([argument]);
        }

        /// <summary>
        /// Sets the entry with the specified value. DON'T USE THIS FUNCTION - IT MAY BE REMOVED.
        /// </summary>
        public void SetValue(string key, PdfItem value)
        {
            Debug.Assert((value is PdfObject && ((PdfObject)value).Reference is null) | !(value is PdfObject),
                "You try to set an indirect object directly into a dictionary.");

            // HACK?
            _elements[key] = value;
        }

        ///// <summary>
        ///// Returns the indirect object if the value of the specified key is a PdfReference.
        ///// </summary>

        /// <summary>
        /// Gets the PdfObject with the specified key, or null, if no such object exists. If the key refers to
        /// a reference, the referenced PdfObject is returned.
        /// </summary>
        public PdfObject GetObject(string key)
        {
            PdfItem item = this[key];
            PdfReference reference = item as PdfReference;
            if (reference is not null)
                return reference.Value;
            return item as PdfObject;
        }

        /// <summary>
        /// Gets the PdfDictionary with the specified key, or null, if no such object exists. If the key refers to
        /// a reference, the referenced PdfDictionary is returned.
        /// </summary>
        public PdfDictionary GetDictionary(string key)
        {
            return GetObject(key) as PdfDictionary;
        }

        /// <summary>
        /// Gets the PdfArray with the specified key, or null, if no such object exists. If the key refers to
        /// a reference, the referenced PdfArray is returned.
        /// </summary>
        public PdfArray GetArray(string key)
        {
            return GetObject(key) as PdfArray;
        }

        /// <summary>
        /// Gets the PdfReference with the specified key, or null, if no such object exists.
        /// </summary>
        public PdfReference GetReference(string key)
        {
            PdfItem item = this[key];
            return item as PdfReference;
        }

        /// <summary>
        /// Sets the entry to the specified object. The object must not be an indirect object,
        /// otherwise an exception is raised.
        /// </summary>
        public void SetObject(string key, PdfObject obj)
        {
            if (obj.Reference is not null)
                throw new ArgumentException("PdfObject must not be an indirect object.", "obj");
            this[key] = obj;
        }

        /// <summary>
        /// Sets the entry as a reference to the specified object. The object must be an indirect object,
        /// otherwise an exception is raised.
        /// </summary>
        public void SetReference(string key, PdfObject obj)
        {
            if (obj.Reference is null)
                throw new ArgumentException("PdfObject must be an indirect object.", "obj");
            this[key] = obj.Reference;
        }

        /// <summary>
        /// Sets the entry as a reference to the specified iref.
        /// </summary>
        public void SetReference(string key, PdfReference iref)
        {
            ArgumentNullException.ThrowIfNull(iref);
            this[key] = iref;
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.IDictionary"></see> object is read-only.
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Returns an <see cref="T:System.Collections.IDictionaryEnumerator"></see> object for the <see cref="T:System.Collections.IDictionary"></see> object.
        /// </summary>
        public IEnumerator<KeyValuePair<string, PdfItem>> GetEnumerator()
        {
            return _elements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((ICollection)_elements).GetEnumerator();
        }

        /// <summary>
        /// Gets or sets an entry in the dictionary. The specified key must be a valid PDF name
        /// starting with a slash '/'. This property provides full access to the elements of the
        /// PDF dictionary. Wrong use can lead to errors or corrupt PDF files.
        /// </summary>
        public PdfItem this[string key]
        {
            get
            {
                PdfItem item;
                _elements.TryGetValue(key, out item);
                return item;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                PdfObject obj = value as PdfObject;
                if (obj is not null && obj.IsIndirect)
                    value = obj.Reference;
                _elements[key] = value;
            }
        }

        /// <summary>
        /// Gets or sets an entry in the dictionary identified by a PdfName object.
        /// </summary>
        public PdfItem this[PdfName key]
        {
            get => this[key.Value];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                PdfObject obj = value as PdfObject;
                if (obj is not null && obj.IsIndirect)
                    value = obj.Reference;
                _elements[key.Value] = value;
            }
        }

        /// <summary>
        /// Removes the value with the specified key.
        /// </summary>
        public bool Remove(string key)
        {
            return _elements.Remove(key);
        }

        /// <summary>
        /// Removes the value with the specified key.
        /// </summary>
        public bool Remove(KeyValuePair<string, PdfItem> item)
        {
            throw new NotImplementedException();
        }

        ///// <summary>
        ///// Determines whether the dictionary contains the specified name.
        ///// </summary>

        /// <summary>
        /// Determines whether the dictionary contains the specified name.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return _elements.ContainsKey(key);
        }

        /// <summary>
        /// Determines whether the dictionary contains a specific value.
        /// </summary>
        public bool Contains(KeyValuePair<string, PdfItem> item)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all elements from the dictionary.
        /// </summary>
        public void Clear()
        {
            _elements.Clear();
        }

        /// <summary>
        /// Adds the specified value to the dictionary.
        /// </summary>
        public void Add(string key, PdfItem value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException("key");

            if (key[0] != '/')
                throw new ArgumentException("The key must start with a slash '/'.");

            // If object is indirect automatically convert value to reference.
            PdfObject obj = value as PdfObject;
            if (obj is not null && obj.IsIndirect)
                value = obj.Reference;

            _elements.Add(key, value);
        }

        /// <summary>
        /// Adds an item to the dictionary.
        /// </summary>
        public void Add(KeyValuePair<string, PdfItem> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary>
        /// Gets all keys currently in use in this dictionary as an array of PdfName objects.
        /// </summary>
        public PdfName[] KeyNames
        {
            get
            {
                ICollection values = _elements.Keys;
                int count = values.Count;
                string[] strings = new string[count];
                values.CopyTo(strings, 0);
                PdfName[] names = new PdfName[count];
                for (int idx = 0; idx < count; idx++)
                    names[idx] = new PdfName(strings[idx]);
                return names;
            }
        }

        /// <summary>
        /// Get all keys currently in use in this dictionary as an array of string objects.
        /// </summary>
        public ICollection<string> Keys
        {
            // It is by design not to return _elements.Keys, but a copy.
            get
            {
                ICollection values = _elements.Keys;
                int count = values.Count;
                string[] keys = new string[count];
                values.CopyTo(keys, 0);
                return keys;
            }
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetValue(string key, out PdfItem value)
        {
            return _elements.TryGetValue(key, out value);
        }

        /// <summary>
        /// Gets all values currently in use in this dictionary as an array of PdfItem objects.
        /// </summary>
        //public ICollection<PdfItem> Values
        public ICollection<PdfItem> Values
        {
            // It is by design not to return _elements.Values, but a copy.
            get
            {
                ICollection values = _elements.Values;
                PdfItem[] items = new PdfItem[values.Count];
                values.CopyTo(items, 0);
                return items;
            }
        }

        /// <summary>
        /// Return false.
        /// </summary>
        public bool IsFixedSize => false;

        /// <summary>
        /// Return false.
        /// </summary>
        public bool IsSynchronized => false;

        /// <summary>
        /// Gets the number of elements contained in the dictionary.
        /// </summary>
        public int Count => _elements.Count;

        /// <summary>
        /// Copies the elements of the dictionary to an array, starting at a particular index.
        /// </summary>
        public void CopyTo(KeyValuePair<string, PdfItem>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The current implementation returns null.
        /// </summary>
        public object SyncRoot => null;

        /// <summary>
        /// Gets the DebuggerDisplayAttribute text.
        /// </summary>
        // ReSharper disable UnusedMember.Local
        internal string DebuggerDisplay
        // ReSharper restore UnusedMember.Local
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "key={0}:(", _elements.Count);
                bool addSpace = false;
                ICollection<string> keys = _elements.Keys;
                foreach (string key in keys)
                {
                    if (addSpace)
                        sb.Append(' ');
                    addSpace = true;
                    sb.Append(key);
                }
                sb.Append(")");
                return sb.ToString();
            }
        }

        /// <summary>
        /// The elements of the dictionary with a string as key.
        /// Because the string is a name it starts always with a '/'.
        /// </summary>
        Dictionary<string, PdfItem> _elements;

        /// <summary>
        /// The dictionary this objects belongs to.
        /// </summary>
        PdfDictionary _ownerDictionary;
    }
}
