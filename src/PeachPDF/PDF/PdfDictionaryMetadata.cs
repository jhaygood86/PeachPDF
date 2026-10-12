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
    /// Contains meta information about all keys of a PDF dictionary.
    /// </summary>
    internal class PdfDictionaryMetadata
    {
        public PdfDictionaryMetadata([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            foreach (FieldInfo field in fields)
            {
                var attributes = field.GetCustomAttributes<PdfKeyAttribute>(false);
                foreach (var attribute in attributes)
                {
                    PdfKeyDescriptor descriptor = new PdfKeyDescriptor(attribute);
                    descriptor.KeyValue = (string)field.GetValue(null);
                    _keyDescriptors[descriptor.KeyValue] = descriptor;
                }
            }
        }

        /// <summary>
        /// Gets the PdfKeyDescriptor of the specified key, or null if no such descriptor exits.
        /// </summary>
        public PdfKeyDescriptor this[string key]
        {
            get
            {
                PdfKeyDescriptor keyDescriptor;
                _keyDescriptors.TryGetValue(key, out keyDescriptor);
                return keyDescriptor;
            }
        }

        readonly Dictionary<string, PdfKeyDescriptor> _keyDescriptors = new Dictionary<string, PdfKeyDescriptor>();
    }
}