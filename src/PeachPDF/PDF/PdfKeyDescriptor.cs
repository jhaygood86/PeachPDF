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

namespace PeachPDF.PDF
{
    /// <summary>
    /// Holds information about the value of a key in a dictionary. This information is used to create
    /// and interpret this value.
    /// </summary>
    internal sealed class PdfKeyDescriptor
    {
        /// <summary>
        /// Initializes a new instance of PdfKeyDescriptor from the specified attribute during a KeysMeta
        /// initializes itself using reflection.
        /// </summary>
        public PdfKeyDescriptor(PdfKeyAttribute attribute)
        {
            _version = attribute.Version;
            _keyType = attribute.PdfKeyType;
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

        public PdfKeyType PdfKeyType
        {
            get { return _keyType; }
            set { _keyType = value; }
        }
        PdfKeyType _keyType;

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
            get { return (_keyType & PdfKeyType.MustNotBeIndirect) == 0; }
        }

        // These types are not yet used/implemented.
        static readonly FrozenSet<PdfKeyType> NotYetImplementedKeyTypes = new HashSet<PdfKeyType>
        {
            PdfKeyType.NumberTree,
            PdfKeyType.NameOrArray,
            PdfKeyType.ArrayOrDictionary,
            PdfKeyType.StreamOrArray,
        }.ToFrozenSet();

        /// <summary>
        /// Returns the type of the object to be created as value for the described key.
        /// </summary>
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        public Type GetValueType()
        {
            if (_objectType != null)
                return _objectType;

            // If we have no ObjectType specified, use the PdfKeyType enumeration.
            PdfKeyType keyType = _keyType & PdfKeyType.TypeMask;

            // Each arm below returns typeof(...) directly (rather than through a lookup table) so the
            // trim/AOT analyzer can keep tracking DynamicallyAccessedMembers through to the constructors
            // PdfDictionary.CreateDictionary/CreateArray reflect on - a FrozenDictionary<PdfKeyType, Type>
            // lookup severs that flow (IL2068) since TryGetValue's out Type carries no annotation.
            Type? type = keyType switch
            {
                PdfKeyType.Name => typeof(PdfName),
                PdfKeyType.String => typeof(PdfString),
                PdfKeyType.Boolean => typeof(PdfBoolean),
                PdfKeyType.Integer => typeof(PdfInteger),
                PdfKeyType.Real => typeof(PdfReal),
                PdfKeyType.Date => typeof(PdfDate),
                PdfKeyType.Rectangle => typeof(PdfRectangle),
                PdfKeyType.Array => typeof(PdfArray),
                PdfKeyType.Dictionary or PdfKeyType.Stream => typeof(PdfDictionary),
                _ => null
            };
            if (type != null)
                return type;

            if (NotYetImplementedKeyTypes.Contains(keyType))
                throw new NotImplementedException($"PdfKeyType.{keyType}");

            if (keyType == PdfKeyType.ArrayOrNameOrString)
                return null; // HACK: Make PdfOutline work

            return ReportInvalidKeyType();
        }

        // No caller passes a PdfKeyType this doesn't recognize - excluded because a test driving it would
        // have to call Debug.Assert(false), which xUnit's default trace listener turns into a test
        // failure rather than letting it be asserted against.
        [ExcludeFromCodeCoverage]
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        private Type ReportInvalidKeyType()
        {
            Debug.Assert(false, "Invalid PdfKeyType: " + _keyType);
            return null;
        }
    }
}
