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

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PeachPDF.PdfSharpCore.Pdf
{
    /// <summary>
    /// Holds information about the value of a key in a dictionary. This information is used to create
    /// and interpret this value.
    /// </summary>
    internal sealed class KeyDescriptor
    {
        /// <summary>
        /// Initializes a new instance of KeyDescriptor from the specified attribute during a KeysMeta
        /// initializes itself using reflection.
        /// </summary>
        public KeyDescriptor(KeyInfoAttribute attribute)
        {
            _version = attribute.Version;
            _keyType = attribute.KeyType;
            _fixedValue = attribute.FixedValue;
            _objectType = attribute.ObjectType;

            if (_version == "")
                _version = "1.0";
        }

        /// <summary>
        /// Gets or sets the PDF version starting with the availability of the described key.
        /// </summary>
        public string Version
        {
            get { return _version; }
            set { _version = value; }
        }
        string _version;

        public KeyType KeyType
        {
            get { return _keyType; }
            set { _keyType = value; }
        }
        KeyType _keyType;

        public string KeyValue
        {
            get { return _keyValue; }
            set { _keyValue = value; }
        }
        string _keyValue = null!;

        public string FixedValue
        {
            get { return _fixedValue; }
        }
        readonly string _fixedValue;

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        public Type ObjectType
        {
            get { return _objectType; }
            set { _objectType = value; }
        }
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type _objectType;

        public bool CanBeIndirect
        {
            get { return (_keyType & KeyType.MustNotBeIndirect) == 0; }
        }

        // These types are not yet used/implemented.
        static readonly FrozenSet<KeyType> NotYetImplementedKeyTypes = new HashSet<KeyType>
        {
            KeyType.NumberTree,
            KeyType.NameOrArray,
            KeyType.ArrayOrDictionary,
            KeyType.StreamOrArray,
        }.ToFrozenSet();

        /// <summary>
        /// Returns the type of the object to be created as value for the described key.
        /// </summary>
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        public Type GetValueType()
        {
            if (_objectType != null)
                return _objectType;

            // If we have no ObjectType specified, use the KeyType enumeration.
            KeyType keyType = _keyType & KeyType.TypeMask;

            // Each arm below returns typeof(...) directly (rather than through a lookup table) so the
            // trim/AOT analyzer can keep tracking DynamicallyAccessedMembers through to the constructors
            // PdfDictionary.CreateDictionary/CreateArray reflect on - a FrozenDictionary<KeyType, Type>
            // lookup severs that flow (IL2068) since TryGetValue's out Type carries no annotation.
            Type? type = keyType switch
            {
                KeyType.Name => typeof(PdfName),
                KeyType.String => typeof(PdfString),
                KeyType.Boolean => typeof(PdfBoolean),
                KeyType.Integer => typeof(PdfInteger),
                KeyType.Real => typeof(PdfReal),
                KeyType.Date => typeof(PdfDate),
                KeyType.Rectangle => typeof(PdfRectangle),
                KeyType.Array => typeof(PdfArray),
                KeyType.Dictionary or KeyType.Stream => typeof(PdfDictionary),
                _ => null
            };
            if (type != null)
                return type;

            if (NotYetImplementedKeyTypes.Contains(keyType))
                throw new NotImplementedException($"KeyType.{keyType}");

            if (keyType == KeyType.ArrayOrNameOrString)
                return null; // HACK: Make PdfOutline work

            return ReportInvalidKeyType();
        }

        // No caller passes a KeyType this doesn't recognize - excluded because a test driving it would
        // have to call Debug.Assert(false), which xUnit's default trace listener turns into a test
        // failure rather than letting it be asserted against.
        [ExcludeFromCodeCoverage]
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        private Type ReportInvalidKeyType()
        {
            Debug.Assert(false, "Invalid KeyType: " + _keyType);
            return null;
        }
    }

    /// <summary>
    /// Contains meta information about all keys of a PDF dictionary.
    /// </summary>
    internal class DictionaryMeta
    {
        public DictionaryMeta([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            foreach (FieldInfo field in fields)
            {
                var attributes = field.GetCustomAttributes<KeyInfoAttribute>(false);
                foreach (var attribute in attributes)
                {
                    KeyDescriptor descriptor = new KeyDescriptor(attribute);
                    descriptor.KeyValue = (string)field.GetValue(null);
                    _keyDescriptors[descriptor.KeyValue] = descriptor;
                }
            }
        }

        /// <summary>
        /// Gets the KeyDescriptor of the specified key, or null if no such descriptor exits.
        /// </summary>
        public KeyDescriptor this[string key]
        {
            get
            {
                KeyDescriptor keyDescriptor;
                _keyDescriptors.TryGetValue(key, out keyDescriptor);
                return keyDescriptor;
            }
        }

        readonly Dictionary<string, KeyDescriptor> _keyDescriptors = new Dictionary<string, KeyDescriptor>();
    }
}