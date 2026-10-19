// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Contains meta information about all keys of a PDF dictionary.
/// </summary>
internal sealed class PdfDictionaryMetadata
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
                descriptor.KeyValue = (string)field.GetValue(null)!;
                _keyDescriptors[descriptor.KeyValue] = descriptor;
            }
        }
    }

    /// <summary>
    /// Gets the PdfKeyDescriptor of the specified key, or null if no such descriptor exits.
    /// </summary>
    public PdfKeyDescriptor? this[string key]
    {
        get
        {
            _keyDescriptors.TryGetValue(key, out PdfKeyDescriptor? keyDescriptor);
            return keyDescriptor;
        }
    }

    readonly Dictionary<string, PdfKeyDescriptor> _keyDescriptors = new Dictionary<string, PdfKeyDescriptor>();
}
